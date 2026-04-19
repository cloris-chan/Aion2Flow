using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Tests.Combat;

public sealed class CombatMetricsEngineSkillNormalizationTests
{
    [Fact]
    public void InferOriginalSkillCode_Resolves_Specialization_Variants_Without_Offset_Guessing()
    {
        CombatMetricsEngine.SetGameResources(
        [
            new Skill(17750000, "Immortal Veil", SkillCategory.Chanter, SkillSourceType.PcSkill, "skill", SkillKind.Support, SkillSemantics.Support, null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var resolved = CombatMetricsEngine.InferOriginalSkillCode(17750010);

        Assert.Equal(17750000, resolved);
    }

    [Fact]
    public void InferOriginalSkillCode_Does_Not_Map_Unmatched_Raw_Code_To_Nearby_Offset_Skill()
    {
        CombatMetricsEngine.SetGameResources(
        [
            new Skill(1910261, "Black Hole", SkillCategory.Elementalist, SkillSourceType.PcSkill, "skill", SkillKind.Support, SkillSemantics.Support, null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var resolved = CombatMetricsEngine.InferOriginalSkillCode(1910501);

        Assert.Null(resolved);
    }

    [Fact]
    public void Remaps_Triggered_Sibling_Skill_Packets_To_Sibling_Skill_Code()
    {
        CombatMetricsEngine.LoadSkillMap("zh-TW");
        var engine = new CombatMetricsEngine();
        const int sourceId = 3632;
        const int targetId = 19621;

        engine.Store.AppendNickname(sourceId, "Cleric");
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 17040257,
            OriginalSkillCode = 17040257,
            Damage = 38641,
            EffectFamily = "direct-hit"
        });
        Thread.Sleep(5);
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 17040257,
            OriginalSkillCode = 17040257,
            Damage = 38641,
            EffectFamily = "direct-hit"
        });

        var snapshot = engine.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(sourceId, out var combatant));
        Assert.DoesNotContain(17040250, combatant.Skills.Keys);
        Assert.True(combatant.Skills.TryGetValue(17050250, out var skill));
        Assert.Equal("天罰", skill.SkillName);
        Assert.Equal(77282, skill.DamageAmount);
        Assert.Equal(2, skill.Times);
    }

    [Fact]
    public void Keeps_Exact_Known_Skill_Code_On_Primary_Skill_Packets()
    {
        CombatMetricsEngine.LoadSkillMap("zh-TW");
        var engine = new CombatMetricsEngine();
        const int sourceId = 3632;
        const int targetId = 19621;

        engine.Store.AppendNickname(sourceId, "Cleric");
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 17040250,
            OriginalSkillCode = 17040250,
            Damage = 9408,
            EffectFamily = "direct-hit"
        });
        Thread.Sleep(5);
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 17040250,
            OriginalSkillCode = 17040250,
            Damage = 9408,
            EffectFamily = "direct-hit"
        });

        var snapshot = engine.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(sourceId, out var combatant));
        Assert.True(combatant.Skills.TryGetValue(17040250, out var skill));
        Assert.Equal("審判之電", skill.SkillName);
        Assert.Equal(18816, skill.DamageAmount);
        Assert.Equal(2, skill.Times);
    }

    [Fact]
    public void Counts_MurderousBurst_Triggered_Damage_Sibling_As_Damage()
    {
        CombatMetricsEngine.LoadSkillMap("zh-TW");
        var engine = new CombatMetricsEngine();
        const int sourceId = 2007;
        const int targetId = 55783;

        engine.Store.AppendNickname(sourceId, "Perigee");
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 11800008,
            OriginalSkillCode = 11800008,
            Damage = 77669,
            EffectFamily = "direct-hit"
        });
        Thread.Sleep(5);
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 11800008,
            OriginalSkillCode = 11800008,
            Damage = 77669,
            EffectFamily = "direct-hit"
        });

        var snapshot = engine.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(sourceId, out var combatant));
        Assert.True(combatant.Skills.TryGetValue(11800008, out var skill));
        Assert.Equal("殺氣破裂", skill.SkillName);
        Assert.Equal(155338, skill.DamageAmount);
        Assert.Equal(2, skill.Times);
        Assert.Equal(0, skill.SupportTimes);
    }

    [Fact]
    public void Attributes_Ambush_Drain_Heal_From_Tail_Extraction()
    {
        CombatMetricsEngine.SetGameResources(
        [
            new Skill(13060250, "Ambush", SkillCategory.Assassin, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage | SkillSemantics.DrainOrAbsorb, null),
            new Skill(1010000, "Restore HP", SkillCategory.Npc, SkillSourceType.ItemSkill, "npc", SkillKind.PeriodicHealing, SkillSemantics.PeriodicHealing, null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var engine = new CombatMetricsEngine();
        const int playerId = 3406;
        const int npcId = 17629;

        engine.Store.AppendNickname(playerId, "Perigee");
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcId,
            SkillCode = 13060250,
            OriginalSkillCode = 13060250,
            Damage = 1200,
            DrainHealAmount = 240,
            EffectFamily = "direct-hit",
            Timestamp = 1_000
        });

        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 13060250,
            OriginalSkillCode = 13060250,
            Damage = 240,
            EffectFamily = "direct-hit",
            Timestamp = 1_000
        });

        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcId,
            SkillCode = 13060250,
            OriginalSkillCode = 13060250,
            Damage = 800,
            EffectFamily = "direct-hit",
            Timestamp = 1_040
        });

        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = npcId,
            TargetId = npcId,
            SkillCode = 1010000,
            OriginalSkillCode = 1010000,
            Damage = 120,
            EffectFamily = "direct-hit",
            Timestamp = 1_050
        });

        var snapshot = engine.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(2000, combatant.DamageAmount);
        Assert.Equal(240, combatant.HealingAmount);
        Assert.Equal(240, combatant.DrainHealingAmount);

        Assert.True(combatant.Skills.TryGetValue(13060250, out var skill));
        Assert.Equal(2000, skill.DamageAmount);
        Assert.Equal(240, skill.DrainHealingAmount);
        Assert.Equal(2, skill.Times);
        Assert.Equal(1, skill.DrainHealingTimes);
    }

    [Fact]
    public void Normalizes_Self_Periodic_Healing_Remaining_Total_At_Append_Time()
    {
        CombatMetricsEngine.LoadSkillMap("zh-TW");
        const int playerId = 2508;
        var store = new CombatMetricsStore();

        store.AppendNickname(playerId, "Perigee");
        store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 17091250,
            OriginalSkillCode = 1709125011,
            Damage = 4676,
            EffectFamily = "periodic-self-mode-9",
            Timestamp = 1_000
        });

        var remainingTotals = new[] { 4209, 3742, 3275, 2808, 2341, 1874, 1407, 940, 473 };
        for (var index = 0; index < remainingTotals.Length; index++)
        {
            store.AppendCombatPacket(new ParsedCombatPacket
            {
                SourceId = playerId,
                TargetId = playerId,
                SkillCode = 17091250,
                OriginalSkillCode = 1709125011,
                Damage = remainingTotals[index],
                EffectFamily = "periodic-self-mode-11",
                Timestamp = 3_000 + (index * 2_000L)
            });
        }

        Assert.True(store.CombatPacketsBySource.TryGetValue(playerId, out var packets));
        var normalizedDamages = packets.Select(static packet => packet.Damage).ToArray();

        Assert.Equal(10, normalizedDamages.Length);
        Assert.Equal(0, normalizedDamages[0]);
        Assert.All(normalizedDamages.Skip(1), static damage => Assert.Equal(467, damage));
    }

    [Fact]
    public void Classifies_Self_ActionPoint_Restore_Followup_As_Support_When_Metadata_Declares_Resource_Restore()
    {
        CombatMetricsEngine.SetGameResources(
        [
            new Skill(
                13360010,
                "入侵",
                SkillCategory.Assassin,
                SkillSourceType.PcSkill,
                "skill",
                SkillKind.Damage,
                SkillSemantics.Damage | SkillSemantics.Support | SkillSemantics.NonHealthResourceRestore,
                null),
            new Skill(
                13360120,
                "入侵",
                SkillCategory.Assassin,
                SkillSourceType.PcSkill,
                "skill",
                SkillKind.Damage,
                SkillSemantics.Damage,
                null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var engine = new CombatMetricsEngine();
        const int playerId = 7166;
        const int targetId = 262851;

        engine.Store.AppendNickname(playerId, "Perigee");
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 13360120,
            OriginalSkillCode = 13360120,
            Damage = 18167,
            EffectFamily = "direct-hit",
            Timestamp = 1_000
        });
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 13360120,
            OriginalSkillCode = 13360120,
            Damage = 32404,
            EffectFamily = "direct-hit",
            Timestamp = 1_050
        });
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 13360010,
            OriginalSkillCode = 13360017,
            Damage = 30000,
            EffectFamily = "direct-hit",
            Timestamp = 1_100
        });

        var snapshot = engine.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(50571, combatant.DamageAmount);

        Assert.True(combatant.Skills.TryGetValue(13360120, out var damageSkill));
        Assert.Equal(50571, damageSkill.DamageAmount);
        Assert.Equal(2, damageSkill.Times);

        Assert.True(combatant.Skills.TryGetValue(13360010, out var followupSkill));
        Assert.Equal(0, followupSkill.DamageAmount);
        Assert.Equal(0, followupSkill.Times);
        Assert.Equal(1, followupSkill.SupportTimes);
    }

    [Fact]
    public void Classifies_Missing_Unknown_Self_Followup_As_Support_Without_Inflating_Damage_Totals()
    {
        CombatMetricsEngine.SetGameResources([], new Dictionary<int, NpcCatalogEntry>());

        var engine = new CombatMetricsEngine();
        const int playerId = 9024;
        const int targetId = 262851;

        engine.Store.AppendNickname(playerId, "Perigee");
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 13360120,
            OriginalSkillCode = 13360120,
            Damage = 18167,
            EffectFamily = "direct-hit",
            Timestamp = 1_000
        });
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 13360120,
            OriginalSkillCode = 13360120,
            Damage = 32404,
            EffectFamily = "direct-hit",
            Timestamp = 1_050
        });
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 1900001,
            OriginalSkillCode = 1900911,
            Damage = 35373,
            EffectFamily = "direct-hit",
            Timestamp = 1_100
        });

        var snapshot = engine.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(50571, combatant.DamageAmount);

        Assert.True(combatant.Skills.TryGetValue(13360120, out var damageSkill));
        Assert.Equal(50571, damageSkill.DamageAmount);
        Assert.Equal(2, damageSkill.Times);

        Assert.True(combatant.Skills.TryGetValue(1900001, out var followupSkill));
        Assert.Equal(0, followupSkill.DamageAmount);
        Assert.Equal(0, followupSkill.Times);
        Assert.Equal(35373, followupSkill.HealingAmount);
        Assert.Equal(1, followupSkill.HealingTimes);
    }

    [Fact]
    public void Classifies_Charge7_Family_Resource_Followups_As_Support_Without_Inflating_Damage_Totals()
    {
        CombatMetricsEngine.SetGameResources(
        [
            new Skill(
                11360017,
                "Rush Strike",
                SkillCategory.Gladiator,
                SkillSourceType.PcSkill,
                "skill",
                SkillKind.Damage,
                SkillSemantics.Damage,
                null),
            new Skill(
                11360120,
                "Rush Strike",
                SkillCategory.Gladiator,
                SkillSourceType.PcSkill,
                "skill",
                SkillKind.Damage,
                SkillSemantics.Damage | SkillSemantics.Support | SkillSemantics.NonHealthResourceRestore,
                null)
        ], new Dictionary<int, NpcCatalogEntry>());

        var engine = new CombatMetricsEngine();
        const int playerId = 2672;
        const int targetId = 159265;

        engine.Store.AppendNickname(playerId, "Perigee");
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 11360120,
            OriginalSkillCode = 11360120,
            Damage = 3421,
            EffectFamily = "direct-hit",
            Timestamp = 1_000
        });
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 11360120,
            OriginalSkillCode = 11360120,
            Damage = 6615,
            EffectFamily = "direct-hit",
            Timestamp = 1_050
        });
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 11360017,
            OriginalSkillCode = 11360017,
            Damage = 30000,
            EffectFamily = "direct-hit",
            Timestamp = 1_100
        });
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 11360017,
            OriginalSkillCode = 11360017,
            Damage = 30000,
            EffectFamily = "direct-hit",
            Timestamp = 1_150
        });

        var snapshot = engine.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(10036, combatant.DamageAmount);

        Assert.True(combatant.Skills.TryGetValue(11360120, out var damageSkill));
        Assert.Equal(10036, damageSkill.DamageAmount);
        Assert.Equal(2, damageSkill.Times);

        Assert.True(combatant.Skills.TryGetValue(11360017, out var followupSkill));
        Assert.Equal(0, followupSkill.DamageAmount);
        Assert.Equal(0, followupSkill.Times);
        Assert.Equal(2, followupSkill.SupportTimes);
    }
}
