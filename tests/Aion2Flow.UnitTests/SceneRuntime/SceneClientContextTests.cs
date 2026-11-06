using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Playback;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class SceneClientContextTests
{
    [Fact]
    public void ArchivedPlaybackKeepsItsResourceContextAfterAnotherSessionIsCreated()
    {
        const int skillCode = 997_060_233;
        const int playerId = 100;
        const int targetId = 200;
        var firstContext = CombatResourceTestFixture.CreateContext(
            [new SkillDisplayEntry(skillCode, "First skill", SkillCategory.Cleric, SkillSourceType.PcSkill)],
            new Dictionary<int, NpcDisplayEntry>());
        var secondContext = CombatResourceTestFixture.CreateContext(
            [new SkillDisplayEntry(skillCode, "Second skill", SkillCategory.Ranger, SkillSourceType.PcSkill)],
            new Dictionary<int, NpcDisplayEntry>());
        using var firstScene = new SceneTestHarness(firstContext);
        using var secondScene = new SceneTestHarness(secondContext);
        var damage = new CombatWireObservation
        {
            SkillCode = skillCode,
            Damage = 500,
            HitCount = 1,
            AttemptCount = 1
        };
        foreach (var scene in new[] { firstScene, secondScene })
        {
            scene.AppendCombatWireObservation(playerId, targetId, in damage, 1_000);
            scene.AppendCombatWireObservation(playerId, targetId, in damage, 2_000);
        }

        _ = firstScene.CreateSnapshot();
        var payload = firstScene.Owner.CreateArchivePayload();
        var secondSnapshot = secondScene.CreateSnapshot();
        using var source = new ArchivedScenePlaybackSource(payload);
        var playback = new ScenePlaybackSession(source);
        var frame = playback.Seek(long.MaxValue, TestContext.Current.CancellationToken);

        Assert.Same(firstContext, source.ClientContext);
        Assert.Same(firstContext, frame.Snapshot.ClientContext);
        Assert.Same(firstContext, payload.TimelineSegment.CreateBoundedSnapshot().ClientContext);
        Assert.Equal(CharacterClass.Cleric, frame.Snapshot.Combatants[playerId].CharacterClass);
        Assert.Equal(CharacterClass.Ranger, secondSnapshot.Combatants[playerId].CharacterClass);
        Assert.Equal(1000, frame.Snapshot.Combatants[playerId].DamageAmount);
        Assert.Equal("First skill", frame.Snapshot.ClientContext.CombatResources.DisplaySkillNameFor(skillCode));
        Assert.Equal("Second skill", secondSnapshot.ClientContext.CombatResources.DisplaySkillNameFor(skillCode));
    }

    [Fact]
    public void ResetRetainsContextInNewOwnerAndSink()
    {
        var context = CombatResourceTestFixture.CreateContext([], new Dictionary<int, NpcDisplayEntry>());
        var scene = new SceneLiveReadModel(context);
        var firstSessionId = scene.SessionId;
        scene.Reset();

        Assert.NotEqual(firstSessionId, scene.SessionId);
        Assert.Same(context, scene.ClientContext);
        Assert.Same(context, scene.Owner.ClientContext);
        Assert.Same(context, SceneSinkFactory.CreateForLive(scene)().ClientContext);
        Assert.Same(context, scene.CreateFrame().Snapshot.ClientContext);
    }
}
