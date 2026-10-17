using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class CombatSnapshotChangeLogTests
{
    [Theory]
    [InlineData("combat", 1)]
    [InlineData("combat", 2)]
    [InlineData("combat", 64)]
    [InlineData("mechanic", 1)]
    [InlineData("mechanic", 2)]
    [InlineData("mechanic", 64)]
    [InlineData("resource", 1)]
    [InlineData("resource", 2)]
    [InlineData("resource", 64)]
    public void CopyChanges_ResumesWithoutSkippingOrRepeatingChanges(string domain, int capacity)
    {
        var (feed, append) = CreateFeed(domain);
        var expected = feed.ReadChanges(feed.CreateCursor(0), int.MaxValue).Changes;
        var actual = new List<CombatSnapshotChange>();
        Span<CombatSnapshotChange> buffer = stackalloc CombatSnapshotChange[capacity];
        var cursor = feed.CreateCursor(0);
        while (true)
        {
            var result = feed.CopyChanges(cursor, buffer);
            Assert.InRange(result.Count, 1, capacity);
            actual.AddRange(buffer[..result.Count]);
            cursor = result.Cursor;
            if (!result.HasMore)
                break;
        }

        Assert.Equal(expected, actual);
        Assert.Equal(0, cursor.Offset);
        Assert.Equal(0, feed.CopyChanges(cursor, buffer).Count);

        append();
        var appended = feed.CopyChanges(cursor, buffer);
        Assert.True(appended.Count > 0);
        Assert.All(buffer[..appended.Count].ToArray(), change => Assert.True(change.Revision > cursor.Revision));
    }

    [Theory]
    [InlineData("combat")]
    [InlineData("mechanic")]
    [InlineData("resource")]
    public void CopyChanges_EmptyDestinationPreservesCursorAndReportsPendingChanges(string domain)
    {
        var (feed, _) = CreateFeed(domain);
        var cursor = feed.CreateCursor(0);

        var result = feed.CopyChanges(cursor, []);

        Assert.Equal(0, result.Count);
        Assert.Equal(cursor, result.Cursor);
        Assert.True(result.HasMore);
        var final = feed.ReadChanges(cursor, int.MaxValue);
        Assert.False(feed.CopyChanges(feed.CreateCursor(final.ToRevision), []).HasMore);
    }

    [Fact]
    public void ReadChanges_AfterPartialCopyFinishesTheRevision()
    {
        var (feed, _) = CreateFeed("combat");
        Span<CombatSnapshotChange> buffer = stackalloc CombatSnapshotChange[1];
        var result = feed.CopyChanges(feed.CreateCursor(0), buffer);

        var remaining = feed.ReadChanges(result.Cursor, 1);

        Assert.Equal(2, remaining.Changes.Count);
        Assert.Equal(buffer[0].Revision, remaining.ToRevision);
        Assert.All(remaining.Changes, change => Assert.Equal(CombatSnapshotChangeKind.CombatantUpdated, change.Kind));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void CopyChanges_RejectsOffsetsOutsideNextRevision(int offset)
    {
        var (feed, _) = CreateFeed("combat");
        Assert.Throws<ArgumentOutOfRangeException>(() => feed.CopyChanges(new SnapshotChangeCursor(0, offset), new CombatSnapshotChange[1]));
    }

    private static (ISnapshotChangeFeed<CombatSnapshotChange> Feed, Action Append) CreateFeed(string domain)
    {
        var observation = new CombatWireObservation { SkillCode = 1_001, Damage = 100 };
        var contribution = new CombatContribution(CombatMetricKind.Damage, CombatDeliveryKind.Direct, 100, default);
        var mechanic = new CombatMechanicOccurrence(DamageModifiers.None, 1, 1, 0, 0, 0, 0, default);
        var resource = new CombatResourceOccurrence(CombatResourceKind.Mana, CombatResourceFlowKind.Restore, CombatResourceDeliveryKind.Direct, 100, default);
        var combat = new CombatStore();
        var mechanics = new MechanicStore();
        var resources = new ResourceStore();
        ISnapshotChangeFeed<CombatSnapshotChange> feed = domain switch
        {
            "combat" => combat,
            "mechanic" => mechanics,
            "resource" => resources,
            _ => throw new ArgumentOutOfRangeException(nameof(domain))
        };
        var ordinal = 0;
        void Append()
        {
            var sourceId = ordinal == 0 ? 0 : 100 + ordinal % 2;
            var targetId = ordinal == 1 ? 0 : 200;
            switch (domain)
            {
                case "combat": combat.ApplyCombat(sourceId, targetId, in observation, in contribution, ordinal); break;
                case "mechanic": mechanics.Apply(sourceId, targetId, in observation, in mechanic, ordinal, ordinal, default); break;
                case "resource": resources.Apply(sourceId, targetId, in observation, in resource, ordinal, ordinal, default); break;
            }
            ordinal++;
        }

        for (var i = 0; i < 5; i++)
            Append();
        return (feed, Append);
    }
}
