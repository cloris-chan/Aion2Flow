using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class CharacterClassInferenceSceneTests
{
    [Fact]
    public void Does_Not_Infer_CharacterClass_From_Periodic_Self_Proc()
    {
        CombatResourceRegistry.LoadSkillMap("en-US");
        using var scene = new SceneTestHarness();
        const int playerId = 101;
        const int targetId = 9001;

        scene.AppendNickname(playerId, "Player");
        var periodicObservation = new CombatWireObservation
        {
            SkillCode = 18160030,
            Damage = 120,
            PeriodicRelation = PeriodicEffectRelation.Self
        };
        scene.AppendCombatWireObservation(playerId, targetId, in periodicObservation);

        Thread.Sleep(5);

        var unknownDamage = new CombatWireObservation
        {
            SkillCode = 99999999,
            Damage = 1350,
            HitCount = 1,
            AttemptCount = 1
        };
        scene.AppendCombatWireObservation(playerId, targetId, in unknownDamage);

        _ = scene.CreateSnapshot();

        Assert.True(scene.Owner.Entities.TryGet(playerId, out var player));
        Assert.Null(player.CharacterClass);
    }

    [Fact]
    public void Prefers_Offensive_Class_Evidence_Over_Sprint_Mantra_Proc()
    {
        CombatResourceRegistry.LoadSkillMap("en-US");
        using var scene = new SceneTestHarness();
        const int playerId = 102;
        const int targetId = 9002;

        scene.AppendNickname(playerId, "Ranger");
        var periodicObservation = new CombatWireObservation
        {
            SkillCode = 18160030,
            Damage = 90,
            PeriodicRelation = PeriodicEffectRelation.Self
        };
        scene.AppendCombatWireObservation(playerId, targetId, in periodicObservation);

        Thread.Sleep(5);

        var rangerDamage = new CombatWireObservation
        {
            SkillCode = 14342350,
            Damage = 2450,
            HitCount = 1,
            AttemptCount = 1
        };
        scene.AppendCombatWireObservation(playerId, targetId, in rangerDamage);

        _ = scene.CreateSnapshot();

        Assert.True(scene.Owner.Entities.TryGet(playerId, out var player));
        Assert.Equal(CharacterClass.Ranger, player.CharacterClass);
    }

    [Fact]
    public void DamageContribution_Includes_PreInference_Damage_When_Class_Is_Inferred_Later()
    {
        CombatResourceRegistry.LoadSkillMap("en-US");
        using var scene = new SceneTestHarness();
        const int playerId = 103;
        const int targetId = 9003;

        scene.AppendNickname(playerId, "Late Ranger");
        var unknownDamage = new CombatWireObservation
        {
            SkillCode = 99999999,
            Damage = 1000,
            HitCount = 1,
            AttemptCount = 1
        };
        scene.AppendCombatWireObservation(playerId, targetId, in unknownDamage);

        Thread.Sleep(5);

        var rangerDamage = new CombatWireObservation
        {
            SkillCode = 14342350,
            Damage = 500,
            HitCount = 1,
            AttemptCount = 1
        };
        scene.AppendCombatWireObservation(playerId, targetId, in rangerDamage);

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(CharacterClass.Ranger, combatant.CharacterClass);
        Assert.Equal(1500, combatant.DamageAmount);
        Assert.Equal(1d, combatant.DamageContribution, 10);
    }

    [Fact]
    public void CharacterClassEvidence_Rebuilds_When_SkillMap_Arrives_After_Combat()
    {
        CombatResourceRegistry.SetGameResources(ResourceCatalog.Load(ResourceLanguage.English));
        using var scene = new SceneTestHarness();
        const int playerId = 10408;
        const int targetId = 9004;
        const int lateSkillCode = 997_060_233;

        var firstDamage = new CombatWireObservation
        {
            SkillCode = lateSkillCode,
            Damage = 281_041,
            HitCount = 1,
            AttemptCount = 1
        };
        scene.AppendCombatWireObservation(playerId, targetId, in firstDamage, 1_000);
        var secondDamage = new CombatWireObservation
        {
            SkillCode = lateSkillCode,
            Damage = 532_761,
            HitCount = 1,
            AttemptCount = 1
        };
        scene.AppendCombatWireObservation(playerId, targetId, in secondDamage, 2_000);

        var snapshotBeforeResources = scene.CreateSnapshot();

        Assert.True(snapshotBeforeResources.Combatants.TryGetValue(playerId, out var beforeResources));
        Assert.Null(beforeResources.CharacterClass);

        CombatResourceTestFixture.SetResources(
        [
            new SkillDisplayEntry(lateSkillCode, "Thunderbolt MAX", SkillCategory.Cleric, SkillSourceType.PcSkill)
        ], new Dictionary<int, NpcDisplayEntry>());

        var snapshotAfterResources = scene.CreateSnapshot();

        Assert.True(snapshotAfterResources.Combatants.TryGetValue(playerId, out var afterResources));
        Assert.Equal(CharacterClass.Cleric, afterResources.CharacterClass);
        Assert.Equal(813_802, afterResources.DamageAmount);
    }

    [Fact]
    public void Ignores_Derived_Regeneration_ClassEvidence_And_Uses_Direct_Damage_Evidence()
    {
        CombatResourceTestFixture.SetResources(
        [
            new SkillDisplayEntry(13352450, "Heart Gore", SkillCategory.Assassin, SkillSourceType.PcSkill),
            new SkillDisplayEntry(16190040, "Enhance: Spirit's Benediction", SkillCategory.Elementalist, SkillSourceType.PcSkill)
        ], new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int assassinId = 9942;
        const int playerId = 10190;
        const int targetId = 12225;

        scene.AppendNickname(playerId, "Elementalist");
        var damageWithRegeneration = new CombatWireObservation
        {
            SkillCode = 13352450,
            Damage = 12000,
            HitCount = 1,
            AttemptCount = 1,
            RegenerationAmount = 781
        };
        scene.AppendCombatWireObservation(assassinId, playerId, in damageWithRegeneration, 1_000);

        var elementalistDamage = new CombatWireObservation
        {
            SkillCode = 16190040,
            Damage = 1,
            HitCount = 1,
            AttemptCount = 1
        };
        scene.AppendCombatWireObservation(playerId, targetId, in elementalistDamage, 1_040);

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(CharacterClass.Elementalist, combatant.CharacterClass);
    }
}
