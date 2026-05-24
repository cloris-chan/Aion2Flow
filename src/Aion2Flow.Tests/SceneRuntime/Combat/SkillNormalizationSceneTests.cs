using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class SkillNormalizationSceneTests
{
    [Fact]
    public void InferOriginalSkillCode_Resolves_Specialization_Variants_Without_Offset_Guessing()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(17750000, "Immortal Veil", SkillCategory.Chanter, SkillSourceType.PcSkill, "skill", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var resolved = CombatResourceRegistry.InferOriginalSkillCode(17750010);

        Assert.Equal(17750000, resolved);
    }

    [Fact]
    public void InferOriginalSkillCode_Does_Not_Map_Unmatched_Raw_Code_To_Nearby_Offset_Skill()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(1910261, "Black Hole", SkillCategory.Elementalist, SkillSourceType.PcSkill, "skill", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var resolved = CombatResourceRegistry.InferOriginalSkillCode(1910501);

        Assert.Null(resolved);
    }

    [Fact]
    public void InferOriginalSkillCode_Does_Not_Collapse_Unmatched_Npc_Code_To_Unrelated_Low_Id_Skill()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(10000, "Account Security", SkillCategory.Etc, SkillSourceType.Unknown, "system", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var resolved = CombatResourceRegistry.InferOriginalSkillCode(1232480);

        Assert.Null(resolved);
    }

    [Fact]
    public void InferOriginalSkillCode_Prefers_Nearby_Npc_Base_Over_Unrelated_Low_Id_Skill()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(10000, "Account Security", SkillCategory.Etc, SkillSourceType.Unknown, "system", null),
            new Skill(1232000, "Sting", SkillCategory.Npc, SkillSourceType.Unknown, "npc", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var resolved = CombatResourceRegistry.InferOriginalSkillCode(1232480);

        Assert.Equal(1232000, resolved);
    }

    [Fact]
    public void InferOriginalSkillCode_Resolves_Periodic_Shield_Variant_To_Base_Skill()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(18730000, "Protection Circle", SkillCategory.Templar, SkillSourceType.PcSkill, "skill", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var resolved = CombatResourceRegistry.InferOriginalSkillCode(1873000211);

        Assert.Equal(18730000, resolved);
    }

    [Fact]
    public void AppendCombatPacket_Normalizes_RegenerationHealing_RawNpcSkillCode()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(1230340, "Jumping Overhead Slam", SkillCategory.Npc, SkillSourceType.Unknown, "npc", null)
        ], new Dictionary<int, NpcCatalogEntry>());
        var regenPacket = new ParsedCombatPacket
        {
            SourceId = 4342,
            TargetId = 4342,
            OriginalSkillCode = 1239430,
            SkillCode = 1239430,
            Damage = 603,
            EventKind = CombatEventKind.Healing,
            ValueKind = CombatValueKind.Healing
        };
        regenPacket.SetEffectTag(PacketEffectTag.RegenerationHealing);

        CombatResourceRegistry.NormalizePacketForStorage(ref regenPacket);

        Assert.True(regenPacket.IsNormalized);
        Assert.Equal(1230340, regenPacket.SkillCode);
        Assert.Equal("Jumping Overhead Slam", CombatEventClassifier.DisplaySkillNameFor(regenPacket.SkillCode));
    }

    [Fact]
    public void Remaps_Triggered_Sibling_Skill_Packets_To_Sibling_Skill_Code()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        using var scene = new SceneTestHarness();
        const int sourceId = 3632;
        const int targetId = 19621;

        scene.AppendNickname(sourceId, "Cleric");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 17040257,
            OriginalSkillCode = 17040257,
            Damage = 38641
        });
        Thread.Sleep(5);
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 17040257,
            OriginalSkillCode = 17040257,
            Damage = 38641
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(sourceId, out var combatant));
        var skills = scene.CreateSkillBreakdown(snapshot, sourceId).Skills;
        Assert.DoesNotContain(17040250, skills.Keys);
        Assert.True(skills.TryGetValue(17050250, out var skill));
        Assert.Equal("天罰", CombatEventClassifier.DisplaySkillNameFor(skill.SkillCode));
        Assert.Equal(77282, skill.DamageAmount);
        Assert.Equal(2, skill.Times);
    }

    [Fact]
    public void Keeps_Exact_Known_Skill_Code_On_Primary_Skill_Packets()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        using var scene = new SceneTestHarness();
        const int sourceId = 3632;
        const int targetId = 19621;

        scene.AppendNickname(sourceId, "Cleric");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 17040250,
            OriginalSkillCode = 17040250,
            Damage = 9408
        });
        Thread.Sleep(5);
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 17040250,
            OriginalSkillCode = 17040250,
            Damage = 9408
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(sourceId, out var combatant));
        var skills = scene.CreateSkillBreakdown(snapshot, sourceId).Skills;
        Assert.True(skills.TryGetValue(17040250, out var skill));
        Assert.Equal("審判之電", CombatEventClassifier.DisplaySkillNameFor(skill.SkillCode));
        Assert.Equal(18816, skill.DamageAmount);
        Assert.Equal(2, skill.Times);
    }

    [Fact]
    public void Collapses_SameName_NonTriggered_PcSkill_Variants_To_Base_Skill()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(12240000, "審判", SkillCategory.Templar, SkillSourceType.PcSkill, "pc", null),
            new Skill(12240030, "審判", SkillCategory.Templar, SkillSourceType.PcSkill, "pc", null),
            new Skill(12240350, "審判", SkillCategory.Templar, SkillSourceType.PcSkill, "pc", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        const int sourceId = 3038;
        const int targetId = 29219;

        scene.AppendNickname(sourceId, "Templar");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 12240350,
            OriginalSkillCode = 12240350,
            Damage = 23108,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 12240039,
            OriginalSkillCode = 12240039,
            Damage = 15957,
            Timestamp = 1_100
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(sourceId, out var combatant));
        var skills = scene.CreateSkillBreakdown(snapshot, sourceId).Skills;
        Assert.True(skills.TryGetValue(12240000, out var judgment));
        Assert.Equal("審判", CombatEventClassifier.DisplaySkillNameFor(judgment.SkillCode));
        Assert.Equal(39065, judgment.DamageAmount);
        Assert.Equal(2, judgment.Times);
        Assert.DoesNotContain(12240350, skills.Keys);
        Assert.DoesNotContain(12240030, skills.Keys);
    }

    [Fact]
    public void Collapses_SameName_Variant_Without_Resource_Semantics()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(12240000, "審判", SkillCategory.Templar, SkillSourceType.PcSkill, "pc", null),
            new Skill(12240150, "審判", SkillCategory.Templar, SkillSourceType.PcSkill, "pc", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        const int sourceId = 3038;
        const int targetId = 29219;

        scene.AppendNickname(sourceId, "Templar");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 12240150,
            OriginalSkillCode = 12240150,
            Damage = 1200,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 12240150,
            OriginalSkillCode = 12240150,
            Damage = 800,
            Timestamp = 1_100
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(sourceId, out var combatant));
        var skills = scene.CreateSkillBreakdown(snapshot, sourceId).Skills;
        Assert.True(skills.TryGetValue(12240000, out var judgment));
        Assert.Equal(2000, judgment.DamageAmount);
        Assert.False(skills.ContainsKey(12240150));
    }

    [Fact]
    public void Counts_MurderousBurst_Triggered_Damage_Sibling_As_Damage()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        using var scene = new SceneTestHarness();
        const int sourceId = 2007;
        const int targetId = 55783;

        scene.AppendNickname(sourceId, "Perigee");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 11800008,
            OriginalSkillCode = 11800008,
            Damage = 77669
        });
        Thread.Sleep(5);
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 11800008,
            OriginalSkillCode = 11800008,
            Damage = 77669
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(sourceId, out var combatant));
        var skills = scene.CreateSkillBreakdown(snapshot, sourceId).Skills;
        Assert.True(skills.TryGetValue(11800008, out var skill));
        Assert.Equal("殺氣破裂", CombatEventClassifier.DisplaySkillNameFor(skill.SkillCode));
        Assert.Equal(155338, skill.DamageAmount);
        Assert.Equal(2, skill.Times);
        Assert.Equal(0, skill.SupportTimes);
    }

    [Fact]
    public void Attributes_Ambush_Drain_Heal_From_Tail_Extraction()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(13060250, "Ambush", SkillCategory.Assassin, SkillSourceType.PcSkill, "pc", null),
            new Skill(1010000, "Restore HP", SkillCategory.Npc, SkillSourceType.ItemSkill, "npc", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        const int playerId = 3406;
        const int npcId = 17629;

        scene.AppendNickname(playerId, "Perigee");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcId,
            SkillCode = 13060250,
            OriginalSkillCode = 13060250,
            Damage = 1200,
            DrainHealAmount = 240,
            Timestamp = 1_000
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 13060250,
            OriginalSkillCode = 13060250,
            Damage = 240,
            DrainHealAmount = 240,
            Timestamp = 1_000
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcId,
            SkillCode = 13060250,
            OriginalSkillCode = 13060250,
            Damage = 800,
            Timestamp = 1_040
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = npcId,
            TargetId = npcId,
            SkillCode = 1010000,
            OriginalSkillCode = 1010000,
            Damage = 120,
            Timestamp = 1_050
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(2000, combatant.DamageAmount);
        Assert.Equal(240, combatant.HealingAmount);
        Assert.Equal(240, combatant.DrainHealingAmount);

        var skills = scene.CreateSkillBreakdown(snapshot, playerId).Skills;
        Assert.True(skills.TryGetValue(13060250, out var skill));
        Assert.Equal(2000, skill.DamageAmount);
        Assert.Equal(240, skill.DrainHealingAmount);
        Assert.Equal(2, skill.Times);
        Assert.Equal(1, skill.DrainHealingTimes);
    }

    [Fact]
    public void Normalizes_Self_Periodic_Healing_Remaining_Total_At_Append_Time()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        const int playerId = 2508;
        using var scene = new SceneTestHarness();

        scene.AppendNickname(playerId, "Perigee");
        const int chainId = 4242;
        var seedPacket = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 17091250,
            OriginalSkillCode = 1709125011,
            Damage = 4676,
            Unknown = chainId,
            Timestamp = 1_000
        };
        seedPacket.SetPeriodicEffect(PeriodicEffectRelation.Self, 9);
        scene.AppendCombatPacket(seedPacket);

        var remainingTotals = new[] { 4209, 3742, 3275, 2808, 2341, 1874, 1407, 940, 473 };
        for (var index = 0; index < remainingTotals.Length; index++)
        {
            var tickPacket = new ParsedCombatPacket
            {
                SourceId = playerId,
                TargetId = playerId,
                SkillCode = 17091250,
                OriginalSkillCode = 1709125011,
                Damage = remainingTotals[index],
                Unknown = chainId,
                Timestamp = 3_000 + (index * 2_000L)
            };
            tickPacket.SetPeriodicEffect(PeriodicEffectRelation.Self, 11);
            scene.AppendCombatPacket(tickPacket);
        }

        scene.CreateSnapshot();
        var normalizedDamages = scene.Owner.Combat.Events.Select(static e => e.Observation.Damage).ToArray();

        Assert.Equal(9, normalizedDamages.Length);
        Assert.All(normalizedDamages, static damage => Assert.Equal(467, damage));
    }

    [Fact]
    public void Classifies_Self_ActionPoint_Restore_Followup_As_Support_By_Packet_Trait()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(13360010, "入侵", SkillCategory.Assassin, SkillSourceType.PcSkill, "skill", null),
            new Skill(13360120, "入侵", SkillCategory.Assassin, SkillSourceType.PcSkill, "skill", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        const int playerId = 7166;
        const int targetId = 262851;

        scene.AppendNickname(playerId, "Perigee");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 13360120,
            OriginalSkillCode = 13360120,
            Damage = 18167,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 13360120,
            OriginalSkillCode = 13360120,
            Damage = 32404,
            Timestamp = 1_050
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 13360010,
            OriginalSkillCode = 13360017,
            Damage = 30000,
            Timestamp = 1_100
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(50571, combatant.DamageAmount);

        var skills = scene.CreateSkillBreakdown(snapshot, playerId).Skills;
        Assert.True(skills.TryGetValue(13360120, out var damageSkill));
        Assert.Equal(50571, damageSkill.DamageAmount);
        Assert.Equal(2, damageSkill.Times);

        Assert.True(skills.TryGetValue(13360010, out var followupSkill));
        Assert.Equal(0, followupSkill.DamageAmount);
        Assert.Equal(0, followupSkill.Times);
        Assert.Equal(1, followupSkill.SupportTimes);
    }

    [Fact]
    public void Classifies_Packet_Tagged_Instance_Clear_Health_Restore_As_Healing_Without_Inflating_Damage_Totals()
    {
        CombatResourceRegistry.SetGameResources([], new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        const int playerId = 9024;
        const int targetId = 262851;

        scene.AppendNickname(playerId, "Perigee");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 13360120,
            OriginalSkillCode = 13360120,
            Damage = 18167,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 13360120,
            OriginalSkillCode = 13360120,
            Damage = 32404,
            Timestamp = 1_050
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 1900001,
            OriginalSkillCode = 1900911,
            Damage = 35373,
            ResourceKind = CombatResourceKind.Health,
            Timestamp = 1_100
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(50571, combatant.DamageAmount);

        var skills = scene.CreateSkillBreakdown(snapshot, playerId).Skills;
        Assert.True(skills.TryGetValue(13360120, out var damageSkill));
        Assert.Equal(50571, damageSkill.DamageAmount);
        Assert.Equal(2, damageSkill.Times);

        Assert.True(skills.TryGetValue(1900001, out var followupSkill));
        Assert.Equal(0, followupSkill.DamageAmount);
        Assert.Equal(0, followupSkill.Times);
        Assert.Equal(35373, followupSkill.HealingAmount);
        Assert.Equal(1, followupSkill.HealingTimes);
    }

    [Fact]
    public void Classifies_Charge7_Base_Skill_Resource_Followups_As_Support_Without_Inflating_Damage_Totals()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new Skill(11360017, "Rush Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "skill", null),
            new Skill(11360120, "Rush Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "skill", null)
        ], new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        const int playerId = 2672;
        const int targetId = 159265;

        scene.AppendNickname(playerId, "Perigee");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 11360120,
            OriginalSkillCode = 11360120,
            Damage = 3421,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 11360120,
            OriginalSkillCode = 11360120,
            Damage = 6615,
            Timestamp = 1_050
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 11360017,
            OriginalSkillCode = 11360017,
            Damage = 30000,
            Timestamp = 1_100
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 11360017,
            OriginalSkillCode = 11360017,
            Damage = 30000,
            Timestamp = 1_150
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(10036, combatant.DamageAmount);

        var skills = scene.CreateSkillBreakdown(snapshot, playerId).Skills;
        Assert.True(skills.TryGetValue(11360120, out var damageSkill));
        Assert.Equal(10036, damageSkill.DamageAmount);
        Assert.Equal(2, damageSkill.Times);

        Assert.True(skills.TryGetValue(11360017, out var followupSkill));
        Assert.Equal(0, followupSkill.DamageAmount);
        Assert.Equal(0, followupSkill.Times);
        Assert.Equal(2, followupSkill.SupportTimes);
    }
}
