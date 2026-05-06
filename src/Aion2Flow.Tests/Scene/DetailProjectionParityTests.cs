using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.Scene;
using Cloris.Aion2Flow.Scene.Projection;
using Cloris.Aion2Flow.Scene.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Scene;

public class DetailProjectionParityTests
{
    [Fact]
    public void SceneProjection_TopDealer_MatchesLegacy()
    {
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        SceneDualWrite.Enabled = true;
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));
        SceneDualWrite.Enabled = false;

        var journal = replay.SceneJournal!;
        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var combat = new CombatStore();
        var applier = new DomainEventApplier(entities, metadata, combat);
        applier.ApplyJournal(journal);

        var projection = CombatPairProjection.FromCombatStore(combat);

        var legacyTopDealer = replay.Snapshot.Combatants
            .Where(static kv => kv.Value.DamageAmount > 0)
            .OrderByDescending(static kv => kv.Value.DamageAmount)
            .First();

        var sceneCombatant = projection.GetCombatant(legacyTopDealer.Key);
        Assert.NotNull(sceneCombatant);
        Assert.True(sceneCombatant!.OutgoingDamage > 0, $"Scene projection has 0 outgoing damage for legacy top dealer {legacyTopDealer.Key}");
    }

    [Fact]
    public void Subscription_DeltaReflectsCombatStoreState()
    {
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        SceneDualWrite.Enabled = true;
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));
        SceneDualWrite.Enabled = false;

        var journal = replay.SceneJournal!;
        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var combat = new CombatStore();
        var applier = new DomainEventApplier(entities, metadata, combat);
        applier.ApplyJournal(journal);

        var legacyTopDealer = replay.Snapshot.Combatants
            .Where(static kv => kv.Value.DamageAmount > 0)
            .OrderByDescending(static kv => kv.Value.DamageAmount)
            .First();

        var projection = CombatPairProjection.FromCombatStore(combat);
        var sub = new CombatDetailSubscription(combat, projection, legacyTopDealer.Key);

        var delta = sub.Poll();
        Assert.NotNull(delta);
        Assert.True(delta!.Combatant!.OutgoingDamage > 0);
        Assert.True(delta.OutgoingPairs.Count > 0, "Scene projection should have outgoing pairs for top dealer");
    }

    [Fact]
    public void SceneProjection_CombatantCount_MatchesLegacy()
    {
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        SceneDualWrite.Enabled = true;
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260415211500.log"));
        SceneDualWrite.Enabled = false;

        var journal = replay.SceneJournal!;
        var entities = new EntityStore();
        var metadata = new MetadataStore();
        var combat = new CombatStore();
        var applier = new DomainEventApplier(entities, metadata, combat);
        applier.ApplyJournal(journal);

        var projection = CombatPairProjection.FromCombatStore(combat);

        var legacyWithDamage = replay.Snapshot.Combatants
            .Count(static kv => kv.Value.DamageAmount > 0);

        var sceneWithDamage = projection.Combatants
            .Count(static kv => kv.Value.OutgoingDamage > 0);

        Assert.True(sceneWithDamage >= legacyWithDamage,
            $"Scene projection has {sceneWithDamage} combatants with damage, legacy has {legacyWithDamage}");
    }
}
