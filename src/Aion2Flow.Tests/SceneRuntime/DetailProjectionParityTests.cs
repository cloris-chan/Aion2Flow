using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public class DetailProjectionParityTests
{
    [Fact]
    public void SceneProjection_TopDealer_MatchesBaseline()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));

        var journal = replay.SceneJournal;
        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var combat = new CombatStore();
        var applier = new DomainEventApplier(entities, metadata, combat);
        applier.ApplyJournal(journal);

        var baselineTopDealer = replay.Snapshot.Combatants
            .Where(static kv => kv.Value.DamageAmount > 0)
            .OrderByDescending(static kv => kv.Value.DamageAmount)
            .First();

        var sceneCombatant = CombatPairProjection.GetCombatant(combat, baselineTopDealer.Key);
        Assert.NotNull(sceneCombatant);
        Assert.True(sceneCombatant.Value.OutgoingDamage > 0, $"Scene projection has 0 outgoing damage for baseline top dealer {baselineTopDealer.Key}");
    }

    [Fact]
    public void Subscription_DeltaReflectsCombatStoreState()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));

        var journal = replay.SceneJournal;
        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var combat = new CombatStore();
        var applier = new DomainEventApplier(entities, metadata, combat);
        applier.ApplyJournal(journal);

        var baselineTopDealer = replay.Snapshot.Combatants
            .Where(static kv => kv.Value.DamageAmount > 0)
            .OrderByDescending(static kv => kv.Value.DamageAmount)
            .First();

        var sub = new CombatDetailSubscription(combat, baselineTopDealer.Key);

        var delta = sub.Poll();
        Assert.NotNull(delta);
        Assert.True(delta!.Combatant!.Value.OutgoingDamage > 0);
        Assert.True(delta.OutgoingPairs.Count > 0, "Scene projection should have outgoing pairs for top dealer");
    }

    [Fact]
    public void SceneProjection_CombatantCount_MatchesBaseline()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260415211500.log"));

        var journal = replay.SceneJournal;
        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var combat = new CombatStore();
        var applier = new DomainEventApplier(entities, metadata, combat);
        applier.ApplyJournal(journal);

        var baselineWithDamage = replay.Snapshot.Combatants
            .Count(static kv => kv.Value.DamageAmount > 0);

        var sceneWithDamage = CombatPairProjection.BuildCombatantSummaryMap(combat)
            .Count(static kv => kv.Value.OutgoingDamage > 0);

        Assert.True(sceneWithDamage >= baselineWithDamage,
            $"Scene projection has {sceneWithDamage} combatants with damage, baseline has {baselineWithDamage}");
    }
}
