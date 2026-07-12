using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class CharacterClassInferenceSceneTests
{
    [Fact]
    public void Does_Not_Infer_CharacterClass_From_Periodic_Self_Support_Proc()
    {
        CombatResourceRegistry.LoadSkillMap("en-US");
        using var scene = new SceneTestHarness();
        const int playerId = 101;
        const int targetId = 9001;

        scene.AppendNickname(playerId, "Player");
        var periodicPacket = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 18160030,
            Damage = 120
        };
        periodicPacket.SetPeriodicEffect(PeriodicEffectRelation.Self, 0);
        scene.AppendCombatPacket(periodicPacket);

        Thread.Sleep(5);

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 99999999,
            Damage = 1350,
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Null(combatant.CharacterClass);
    }

    [Fact]
    public void Prefers_Offensive_Class_Evidence_Over_Sprint_Mantra_Proc()
    {
        CombatResourceRegistry.LoadSkillMap("en-US");
        using var scene = new SceneTestHarness();
        const int playerId = 102;
        const int targetId = 9002;

        scene.AppendNickname(playerId, "Ranger");
        var periodicPacket = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 18160030,
            Damage = 90
        };
        periodicPacket.SetPeriodicEffect(PeriodicEffectRelation.Self, 0);
        scene.AppendCombatPacket(periodicPacket);

        Thread.Sleep(5);

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 14342350,
            Damage = 2450,
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(CharacterClass.Ranger, combatant.CharacterClass);
    }

    [Fact]
    public void DamageContribution_Includes_PreInference_Damage_When_Class_Is_Inferred_Later()
    {
        CombatResourceRegistry.LoadSkillMap("en-US");
        using var scene = new SceneTestHarness();
        const int playerId = 103;
        const int targetId = 9003;

        scene.AppendNickname(playerId, "Late Ranger");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 99999999,
            Damage = 1000,
        });

        Thread.Sleep(5);

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 14342350,
            Damage = 500,
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(CharacterClass.Ranger, combatant.CharacterClass);
        Assert.Equal(1500, combatant.DamageAmount);
        Assert.Equal(1d, combatant.DamageContribution, 10);
    }

    [Fact]
    public void CharacterClassEvidence_Rebuilds_When_SkillMap_Arrives_After_Combat()
    {
        CombatResourceRegistry.SkillMap = [];
        using var scene = new SceneTestHarness();
        const int playerId = 10408;
        const int targetId = 9004;

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 17060233,
            Damage = 281_041,
            Timestamp = 1_000,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 17060233,
            Damage = 532_761,
            Timestamp = 2_000,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });

        var snapshotBeforeResources = scene.CreateSnapshot();

        Assert.True(snapshotBeforeResources.Combatants.TryGetValue(playerId, out var beforeResources));
        Assert.Null(beforeResources.CharacterClass);

        CombatResourceRegistry.SetGameResources(
        [
            new SkillDisplayEntry(17060233, "Thunderbolt MAX", SkillCategory.Cleric, SkillSourceType.PcSkill)
        ], new Dictionary<int, NpcDisplayEntry>());

        var snapshotAfterResources = scene.CreateSnapshot();

        Assert.True(snapshotAfterResources.Combatants.TryGetValue(playerId, out var afterResources));
        Assert.Equal(CharacterClass.Cleric, afterResources.CharacterClass);
        Assert.Equal(813_802, afterResources.DamageAmount);
    }

    [Fact]
    public void Ignores_Derived_RegenerationHealing_ClassEvidence_And_Uses_SourceSupportEvidence()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new SkillDisplayEntry(13352450, "Heart Gore", SkillCategory.Assassin, SkillSourceType.PcSkill),
            new SkillDisplayEntry(16790001, "Revitalization Contract", SkillCategory.Elementalist, SkillSourceType.PcSkill),
            new SkillDisplayEntry(16200130, "Defiance", SkillCategory.Elementalist, SkillSourceType.PcSkill),
            new SkillDisplayEntry(16190040, "Enhance: Spirit's Benediction", SkillCategory.Elementalist, SkillSourceType.PcSkill)
        ], new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int assassinId = 9942;
        const int playerId = 10190;
        const int targetId = 12225;

        scene.AppendNickname(playerId, "Elementalist");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = assassinId,
            TargetId = playerId,
            SkillCode = 13352450,
            Damage = 12000,
            Timestamp = 1_000
        });

        var regeneration = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 13352450,
            Damage = 781,
            EventKind = CombatEventKind.Healing,
            ValueKind = CombatValueKind.Healing,
            Timestamp = 1_010
        };
        regeneration.SetEffectTag(PacketEffectTag.RegenerationHealing);
        scene.AppendCombatPacket(regeneration);

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 16790001,
            Damage = 1,
            EventKind = CombatEventKind.Support,
            ValueKind = CombatValueKind.Support,
            Timestamp = 1_020
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 16200130,
            Damage = 1,
            EventKind = CombatEventKind.Support,
            ValueKind = CombatValueKind.Support,
            Timestamp = 1_030
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 16190040,
            Damage = 1,
            EventKind = CombatEventKind.Support,
            ValueKind = CombatValueKind.Support,
            Timestamp = 1_040
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(CharacterClass.Elementalist, combatant.CharacterClass);
    }
}
