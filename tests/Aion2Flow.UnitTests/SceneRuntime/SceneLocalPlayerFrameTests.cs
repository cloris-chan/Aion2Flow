using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class SceneLocalPlayerFrameTests
{
    private static readonly DateTimeOffset Started = new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CopyLocalPlayerAurasTo_RefreshesIdentityAndKeepsOnlyActiveLocalTargetsIncludingPartyBuffs()
    {
        var scene = new SceneLiveReadModel(Started, new FixedTimeProvider(Started.AddSeconds(2)));
        var sink = SceneSinkFactory.CreateForLive(scene)();
        var auras = new List<AuraInstanceState> { default };
        OpenAura(sink, 100, 200, 1, durationMilliseconds: 5_000);
        OpenAura(sink, 100, 100, 2, durationMilliseconds: 5_000);
        OpenAura(sink, 200, 200, 3, durationMilliseconds: 5_000);
        OpenAura(sink, 100, 200, 4, durationMilliseconds: 500);

        var unknown = scene.CopyLocalPlayerAurasTo(auras);
        Assert.Equal(0, unknown.EntityId);
        Assert.Empty(auras);

        sink.AppendNickname(Source(), 100, "Local", isLocalPlayer: true);
        var frame = scene.CopyLocalPlayerAurasTo(auras);

        Assert.Equal(scene.SessionId, frame.SessionId);
        Assert.Equal(100, frame.EntityId);
        Assert.Equal(2_000, frame.ObservedAtMilliseconds);
        Assert.Equal(scene.Journal.NextObservationOrdinal, frame.EndObservationOrdinalExclusive);
        Assert.Equal([1, 2], auras.Select(static aura => aura.InstanceSequenceId));
        Assert.Equal([200, 100], auras.Select(static aura => aura.OriginEntityId));
        Assert.All(auras, aura => Assert.Equal(frame.EntityId, aura.TargetEntityId));
    }

    [Fact]
    public void CopyLocalPlayerAurasTo_PreservesJournalBoundaryAndStartsFreshAfterReset()
    {
        var scene = new SceneLiveReadModel(Started, new FixedTimeProvider(Started.AddSeconds(10)));
        var sink = SceneSinkFactory.CreateForLive(scene)();
        sink.AppendNickname(Source(), 100, "Local", isLocalPlayer: true);
        OpenAura(sink, 100, 200, 1);
        var auras = new List<AuraInstanceState>();
        var first = scene.CopyLocalPlayerAurasTo(auras);

        sink.RegisterCooldown4738(Source(), 12_780_000, 5_000);
        var oldEnd = scene.Journal.NextObservationOrdinal;
        scene.Reset(Started.AddSeconds(5));
        sink.AppendNickname(Source(), 200, "NextLocal", isLocalPlayer: true);
        OpenAura(sink, 200, 100, 2);
        var next = scene.CopyLocalPlayerAurasTo(auras);

        Assert.NotEqual(first.SessionId, next.SessionId);
        Assert.Equal(oldEnd, next.StartObservationOrdinal);
        Assert.Equal(5_000, next.ObservedAtMilliseconds);
        Assert.Equal(200, next.EntityId);
        Assert.Equal(2, Assert.Single(auras).InstanceSequenceId);
        var cursor = new JournalCursor(first.StartObservationOrdinal);
        var result = scene.Journal.ReadEntries(cursor, first.EndObservationOrdinalExclusive, 100, entries =>
        {
            for (var i = 0; i < entries.Count; i++)
            {
                Assert.Equal(first.SessionId, entries[i].SceneSessionId);
                Assert.True(entries[i].Domain != ObservedEventDomain.State || entries[i].State.StateCode != StateCodes.Cooldown4738);
            }
        });
        Assert.Equal(first.EndObservationOrdinalExclusive, result.Cursor.NextObservationOrdinal);
    }

    [Theory]
    [InlineData(SceneKind.Standard)]
    [InlineData(SceneKind.Boss)]
    public async Task CopyLocalPlayerAurasTo_KeepsAurasAndJournalOnOneRevisionDuringConcurrentCombatAndReset(SceneKind kind)
    {
        var scene = new SceneLiveReadModel(Started, new FixedTimeProvider(Started.AddSeconds(1)));
        scene.ChangeKind(kind, Started, archiveCurrent: false);
        var sink = SceneSinkFactory.CreateForLive(scene)();
        sink.AppendNickname(Source(), 100, "Local", isLocalPlayer: true);
        sink.AppendNpcCode(Source(), 300, 2_100_002);
        sink.AppendNpcKind(Source(), 300, NpcKind.Boss);
        var combat = new CombatWireObservation { SkillCode = 11_000_010, Damage = 10, HitCount = 1, AttemptCount = 1 };
        sink.AppendCombatWireObservation(Source(), 100, 300, in combat);
        using var rendezvous = new Barrier(2);
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellationSource.CancelAfter(TimeSpan.FromSeconds(30));
        var cancellationToken = cancellationSource.Token;
        var writer = Task.Run(() =>
        {
            for (var batch = 0; batch < 40; batch++)
            {
                rendezvous.SignalAndWait(cancellationToken);
                for (var i = 0; i < 128; i++)
                {
                    var sequenceId = batch * 128 + i + 1;
                    OpenAura(sink, 100, 200, sequenceId);
                    OpenAura(sink, 200 + i, 200, sequenceId);
                    sink.AppendCombatWireObservation(Source(), 100, 300, in combat);
                }
                scene.Reset(Started);
                sink.AppendCombatWireObservation(Source(), 100, 300, in combat);
            }
        }, cancellationToken);
        var reader = Task.Run(() =>
        {
            var auras = new List<AuraInstanceState>();
            for (var batch = 0; batch < 40; batch++)
            {
                rendezvous.SignalAndWait(cancellationToken);
                for (var i = 0; i < 128; i++)
                {
                    var frame = scene.CopyLocalPlayerAurasTo(auras);
                    Assert.Equal(100, frame.EntityId);
                    Assert.InRange(frame.ObservedAtMilliseconds, 0, 1_000);
                    foreach (var aura in auras)
                    {
                        Assert.Equal(frame.EntityId, aura.TargetEntityId);
                        Assert.InRange(aura.LastObservationOrdinal, frame.StartObservationOrdinal, frame.EndObservationOrdinalExclusive - 1);
                        scene.Journal.ReadEntry(aura.LastObservationOrdinal, entry => Assert.Equal(frame.SessionId, entry.SceneSessionId));
                    }
                }
            }
        }, cancellationToken);

        try
        {
            await await Task.WhenAny(writer, reader);
            await Task.WhenAll(writer, reader);
        }
        finally
        {
            await cancellationSource.CancelAsync();
        }
    }

    private static void OpenAura(IRuntimeObservationSink sink, int targetId, int originId, int sequenceId, ushort durationMilliseconds = ushort.MaxValue)
        => sink.RegisterObservation2A38(Source(), targetId, 1, 19, sequenceId, 0, durationMilliseconds, 0, 0, 0, originId, 1, ResourceEffectRef.FromRaw(12_780_001), 0, 0, 0);

    private static PacketObservationSource Source() => new(Started.AddSeconds(1).ToUnixTimeMilliseconds(), 0, 0, 0, 0, default);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
