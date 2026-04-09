using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Tests.Combat;

public sealed class CombatEventClassifierTests
{
    private static Skill CreateSkill(
        int id,
        string name,
        SkillCategory category,
        SkillSourceType sourceType,
        SkillKind kind,
        SkillSemantics semantics,
        string sourceKey = "skill",
        string? triggeredSkillIdsCsv = null)
        => new(id, name, category, sourceType, sourceKey, kind, semantics, triggeredSkillIdsCsv);

    [Fact]
    public void Classifies_Direct_Healing_By_Resource_Skill_Kind()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(17091250, "Light of Regeneration", SkillCategory.Cleric, SkillSourceType.PcSkill,
                SkillKind.PeriodicHealing, SkillSemantics.PeriodicHealing)
        ];

        var packet = new ParsedCombatPacket
        {
            SourceId = 12115,
            TargetId = 12115,
            SkillCode = 17091250,
            OriginalSkillCode = 1709125011,
            Damage = 4747
        };

        var kind = CombatEventClassifier.Classify(packet);

        Assert.Equal(CombatEventKind.Healing, kind);
        Assert.Equal(SkillKind.PeriodicHealing, CombatEventClassifier.ResolveSkillKind(17091250));
    }

    [Fact]
    public void Classifies_Self_Periodic_Effect_As_Healing()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(17091250, "Light of Regeneration", SkillCategory.Cleric, SkillSourceType.PcSkill,
                SkillKind.PeriodicHealing, SkillSemantics.PeriodicHealing)
        ];

        var packet = new ParsedCombatPacket
        {
            TargetId = 12115,
            SourceId = 12115,
            SkillCode = 17091250,
            OriginalSkillCode = 1709125011,
            EffectFamily = "periodic-self-tick",
            Damage = 4273
        };

        var kind = CombatEventClassifier.Classify(packet);

        Assert.Equal(CombatEventKind.Healing, kind);
    }

    [Fact]
    public void Classifies_Target_Periodic_Effect_As_Damage()
    {
        var packet = new ParsedCombatPacket
        {
            TargetId = 17640,
            SourceId = 12115,
            SkillCode = 17080240,
            OriginalSkillCode = 1708024011,
            EffectFamily = "periodic-target-tick",
            Damage = 1117
        };

        var kind = CombatEventClassifier.Classify(packet);

        Assert.Equal(CombatEventKind.Damage, kind);
        Assert.Equal(CombatValueKind.PeriodicDamage, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Direct_Hit_On_PeriodicDamage_Skill_As_Direct_Damage()
    {
        CombatMetricsEngine.SkillMap =
        [
            new Skill(17070240, "苦痛連鎖", SkillCategory.Cleric, SkillSourceType.PcSkill, "skill",
                SkillKind.PeriodicDamage, SkillSemantics.PeriodicDamage | SkillSemantics.Damage, null)
        ];

        var packet = new ParsedCombatPacket
        {
            TargetId = 17640,
            SourceId = 12115,
            SkillCode = 17070240,
            OriginalSkillCode = 1707024011,
            EffectFamily = "direct-hit",
            Damage = 15392
        };

        Assert.Equal(CombatEventKind.Damage, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Damage, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Target_Periodic_Initial_As_Direct_Damage_Not_Dot()
    {
        var packet = new ParsedCombatPacket
        {
            TargetId = 17640,
            SourceId = 12115,
            SkillCode = 17070240,
            OriginalSkillCode = 1707024011,
            EffectFamily = "periodic-target-initial",
            Damage = 15392
        };

        Assert.Equal(CombatEventKind.Damage, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Damage, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Target_Periodic_Mode8_As_Support_Not_Dot()
    {
        var packet = new ParsedCombatPacket
        {
            TargetId = 17640,
            SourceId = 12115,
            SkillCode = 17070240,
            OriginalSkillCode = 1707024011,
            EffectFamily = "periodic-target-mode-8",
            Damage = 15392
        };

        Assert.Equal(CombatEventKind.Support, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Support, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Target_Periodic_Healing_As_Healing_Not_Damage()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(17091250, "Light of Rebirth", SkillCategory.Cleric, SkillSourceType.PcSkill,
                SkillKind.PeriodicHealing, SkillSemantics.PeriodicHealing)
        ];

        var packet = new ParsedCombatPacket
        {
            TargetId = 17640,
            SourceId = 12115,
            SkillCode = 17091250,
            OriginalSkillCode = 1709125011,
            EffectFamily = "periodic-target-mode-11",
            Damage = 4273
        };

        Assert.Equal(CombatEventKind.Healing, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.PeriodicHealing, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Target_Periodic_Healing_Sentinel_Overflow_As_Support_Not_Damage()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(
                12250010,
                "Comrade in Arms",
                SkillCategory.Templar,
                SkillSourceType.PcSkill,
                SkillKind.PeriodicHealing,
                SkillSemantics.Damage | SkillSemantics.Healing | SkillSemantics.PeriodicHealing | SkillSemantics.Support)
        ];

        var packet = new ParsedCombatPacket
        {
            TargetId = 4056,
            SourceId = 4031,
            SkillCode = 12250010,
            OriginalSkillCode = 1225001011,
            EffectFamily = "periodic-target-mode-9",
            Damage = int.MaxValue
        };

        Assert.Equal(CombatEventKind.Support, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Support, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Target_Periodic_Healing_Initial_As_Direct_Healing_Not_Damage()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(17091250, "Light of Rebirth", SkillCategory.Cleric, SkillSourceType.PcSkill,
                SkillKind.PeriodicHealing, SkillSemantics.PeriodicHealing)
        ];

        var packet = new ParsedCombatPacket
        {
            TargetId = 17640,
            SourceId = 12115,
            SkillCode = 17091250,
            OriginalSkillCode = 1709125011,
            EffectFamily = "periodic-target-initial",
            Damage = 4747
        };

        Assert.Equal(CombatEventKind.Healing, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Healing, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Target_Periodic_Effect_As_Damage_Even_When_Skill_Removes_Shields()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(
                17300030,
                "破滅之語",
                SkillCategory.Cleric,
                SkillSourceType.PcSkill,
                SkillKind.PeriodicDamage,
                SkillSemantics.Damage | SkillSemantics.PeriodicDamage)
        ];

        var packet = new ParsedCombatPacket
        {
            TargetId = 19621,
            SourceId = 4121,
            SkillCode = 17300030,
            OriginalSkillCode = 1730003012,
            EffectFamily = "periodic-target-mode-10",
            Damage = 2457
        };

        Assert.Equal(CombatEventKind.Damage, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.PeriodicDamage, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Target_Periodic_Effect_As_Damage_When_Metadata_Mixes_Damage_And_Healing()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(
                13730007,
                "Poison",
                SkillCategory.Assassin,
                SkillSourceType.PcSkill,
                SkillKind.PeriodicDamage,
                SkillSemantics.Damage | SkillSemantics.PeriodicDamage | SkillSemantics.Healing | SkillSemantics.Support)
        ];

        var packet = new ParsedCombatPacket
        {
            TargetId = 19621,
            SourceId = 4121,
            SkillCode = 13730007,
            OriginalSkillCode = 1373000712,
            EffectFamily = "periodic-target-mode-10",
            Damage = 2457
        };

        Assert.Equal(CombatEventKind.Damage, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.PeriodicDamage, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Exposes_Resource_Derived_Skill_Kind_On_Packet()
    {
        var packet = new ParsedCombatPacket
        {
            SkillCode = 22120011
        };

        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(22120011, "Absorption Scroll", SkillCategory.Abnormal, SkillSourceType.Abnormal,
                SkillKind.ShieldOrBarrier, SkillSemantics.ShieldOrBarrier)
        ];

        packet.SkillKind = CombatEventClassifier.ResolveSkillKind(packet.SkillCode);
        packet.SkillSemantics = CombatEventClassifier.ResolveSkillSemantics(packet.SkillCode);
        packet.ValueKind = CombatEventClassifier.ClassifyValueKind(packet);
        packet.EventKind = CombatEventClassifier.Classify(packet);

        Assert.Equal(SkillKind.ShieldOrBarrier, packet.SkillKind);
        Assert.True((packet.SkillSemantics & SkillSemantics.ShieldOrBarrier) != 0);
        Assert.Equal(CombatValueKind.Shield, packet.ValueKind);
        Assert.Equal(CombatEventKind.Support, packet.EventKind);
    }

    [Fact]
    public void Classifies_Shield_Skill_As_Support()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(22120011, "Absorption Scroll", SkillCategory.Abnormal, SkillSourceType.Abnormal,
                SkillKind.ShieldOrBarrier, SkillSemantics.ShieldOrBarrier)
        ];

        var packet = new ParsedCombatPacket
        {
            SourceId = 12115,
            TargetId = 12115,
            SkillCode = 22120011,
            OriginalSkillCode = 22120011,
            Damage = 0
        };

        var kind = CombatEventClassifier.Classify(packet);

        Assert.Equal(CombatEventKind.Support, kind);
    }

    [Fact]
    public void Classifies_Self_Periodic_Shield_As_Shield_Not_Hot()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(22120011, "Absorption Scroll", SkillCategory.Abnormal, SkillSourceType.Abnormal,
                SkillKind.ShieldOrBarrier, SkillSemantics.ShieldOrBarrier)
        ];

        var packet = new ParsedCombatPacket
        {
            EffectFamily = "periodic-self-tick",
            SourceId = 12115,
            TargetId = 12115,
            SkillCode = 22120011,
            OriginalSkillCode = 221200111,
            Damage = 1025
        };

        var eventKind = CombatEventClassifier.Classify(packet);
        var valueKind = CombatEventClassifier.ClassifyValueKind(packet);

        Assert.Equal(CombatEventKind.Support, eventKind);
        Assert.Equal(CombatValueKind.Shield, valueKind);
    }

    [Fact]
    public void Classifies_Drain_Skill_On_Target_As_Damage()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(16046601, "Cry of Life", SkillCategory.Abnormal, SkillSourceType.Abnormal,
                SkillKind.DrainOrAbsorb, SkillSemantics.DrainOrAbsorb)
        ];

        var packet = new ParsedCombatPacket
        {
            SourceId = 12115,
            TargetId = 17640,
            SkillCode = 16046601,
            OriginalSkillCode = 1604660111,
            Damage = 1234
        };

        var kind = CombatEventClassifier.Classify(packet);

        Assert.Equal(CombatEventKind.Damage, kind);
        Assert.Equal(SkillKind.DrainOrAbsorb, CombatEventClassifier.ResolveSkillKind(16046601));
        Assert.Equal(CombatValueKind.Damage, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Drain_Skill_On_Self_As_Healing_Flow()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(16046601, "Cry of Life", SkillCategory.Abnormal, SkillSourceType.Abnormal,
                SkillKind.DrainOrAbsorb, SkillSemantics.DrainOrAbsorb)
        ];

        var packet = new ParsedCombatPacket
        {
            SourceId = 12115,
            TargetId = 12115,
            SkillCode = 16046601,
            OriginalSkillCode = 1604660111,
            Damage = 567
        };

        var eventKind = CombatEventClassifier.Classify(packet);
        var valueKind = CombatEventClassifier.ClassifyValueKind(packet);

        Assert.Equal(CombatEventKind.Healing, eventKind);
        Assert.Equal(CombatValueKind.DrainHealing, valueKind);
    }

    [Fact]
    public void Classifies_Bare_Offensive_Skill_Name_As_Damage()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(14342350, "Tempest Shot", SkillCategory.Ranger, SkillSourceType.PcSkill,
                SkillKind.Damage, SkillSemantics.Damage)
        ];

        var packet = new ParsedCombatPacket
        {
            SourceId = 12115,
            TargetId = 17640,
            SkillCode = 14342350,
            OriginalSkillCode = 1434235011,
            Damage = 1234
        };

        var kind = CombatEventClassifier.Classify(packet);

        Assert.Equal(SkillKind.Damage, CombatEventClassifier.ResolveSkillKind(14342350));
        Assert.True((CombatEventClassifier.ResolveSkillSemantics(14342350) & SkillSemantics.Damage) != 0);
        Assert.Equal(CombatEventKind.Damage, kind);
    }

    [Fact]
    public void Classifies_ShieldSmite_As_Damage_Not_Shield()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(
                12100000,
                "Shield Smite",
                SkillCategory.Templar,
                SkillSourceType.PcSkill,
                SkillKind.Damage,
                SkillSemantics.Damage)
        ];

        var packet = new ParsedCombatPacket
        {
            SourceId = 12115,
            TargetId = 17640,
            SkillCode = 12100000,
            OriginalSkillCode = 1210000011,
            Damage = 9408
        };

        Assert.Equal(SkillKind.Damage, CombatEventClassifier.ResolveSkillKind(12100000));
        Assert.True((CombatEventClassifier.ResolveSkillSemantics(12100000) & SkillSemantics.Damage) != 0);
        Assert.False((CombatEventClassifier.ResolveSkillSemantics(12100000) & SkillSemantics.ShieldOrBarrier) != 0);
        Assert.Equal(CombatEventKind.Damage, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Damage, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Mixed_Damage_And_Drain_Skill_Target_As_Damage_While_Keeping_Drain_Semantics()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(12240010, "Judgment", SkillCategory.Templar, SkillSourceType.PcSkill,
                SkillKind.Damage, SkillSemantics.Damage | SkillSemantics.DrainOrAbsorb)
        ];

        var packet = new ParsedCombatPacket
        {
            SourceId = 12115,
            TargetId = 17640,
            SkillCode = 12240010,
            OriginalSkillCode = 1224001011,
            Damage = 1800
        };

        Assert.Equal(SkillKind.Damage, CombatEventClassifier.ResolveSkillKind(12240010));
        Assert.True((CombatEventClassifier.ResolveSkillSemantics(12240010) & SkillSemantics.DrainOrAbsorb) != 0);
        Assert.Equal(CombatEventKind.Damage, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Damage, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Mixed_Damage_And_Drain_Skill_Self_As_DrainHealing()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(12240010, "Judgment", SkillCategory.Templar, SkillSourceType.PcSkill,
                SkillKind.Damage, SkillSemantics.Damage | SkillSemantics.DrainOrAbsorb)
        ];

        var packet = new ParsedCombatPacket
        {
            SourceId = 12115,
            TargetId = 12115,
            SkillCode = 12240010,
            OriginalSkillCode = 1224001011,
            Damage = 540
        };

        Assert.Equal(CombatEventKind.Healing, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.DrainHealing, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Generic_RestoreHp_Self_Tick_As_PeriodicHealing()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(1010000, "Restore HP", SkillCategory.Npc, SkillSourceType.ItemSkill,
                SkillKind.PeriodicHealing, SkillSemantics.PeriodicHealing)
        ];

        var packet = new ParsedCombatPacket
        {
            SourceId = 17640,
            TargetId = 17640,
            SkillCode = 1010000,
            OriginalSkillCode = 1010000,
            Damage = 425
        };

        Assert.Equal(CombatEventKind.Healing, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.PeriodicHealing, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Self_Healing_On_Attack_Proc_As_PeriodicHealing()
    {
        CombatMetricsEngine.SkillMap =
        [
            new Skill(
                18160030,
                "Sprint Mantra",
                SkillCategory.Chanter,
                SkillSourceType.PcSkill,
                "skill",
                SkillKind.PeriodicHealing,
                SkillSemantics.PeriodicHealing | SkillSemantics.Support,
                null)
        ];

        var packet = new ParsedCombatPacket
        {
            SourceId = 12115,
            TargetId = 12115,
            SkillCode = 18160030,
            OriginalSkillCode = 18160032,
            Damage = 704
        };

        Assert.Equal(CombatEventKind.Healing, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.PeriodicHealing, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Self_ActionPoint_Restore_Metadata_As_Support()
    {
        CombatMetricsEngine.SkillMap =
        [
            new Skill(
                13360010,
                "入侵",
                SkillCategory.Assassin,
                SkillSourceType.PcSkill,
                "skill",
                SkillKind.Damage,
                SkillSemantics.Damage | SkillSemantics.Support | SkillSemantics.NonHealthResourceRestore,
                null)
        ];

        var packet = new ParsedCombatPacket
        {
            SourceId = 7166,
            TargetId = 7166,
            SkillCode = 13360010,
            OriginalSkillCode = 13360017,
            Damage = 30000,
            EffectFamily = "direct-hit"
        };

        Assert.Equal(CombatEventKind.Support, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Support, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Charge7_Self_Followup_As_Support_When_Base_Family_Has_Resource_Restore_Sibling()
    {
        CombatMetricsEngine.SkillMap =
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
        ];

        var packet = new ParsedCombatPacket
        {
            SourceId = 2672,
            TargetId = 2672,
            SkillCode = 11360017,
            OriginalSkillCode = 11360017,
            BaseSkillCode = 11360000,
            ChargeStage = 7,
            Damage = 30000,
            EffectFamily = "direct-hit"
        };

        Assert.Equal(CombatEventKind.Support, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Support, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Missing_Unknown_Self_Direct_Followup_As_Support()
    {
        CombatMetricsEngine.SkillMap = [];

        var packet = new ParsedCombatPacket
        {
            SourceId = 9024,
            TargetId = 9024,
            SkillCode = 1900001,
            OriginalSkillCode = 1900911,
            Damage = 29586,
            EffectFamily = "direct-hit"
        };

        Assert.Equal(CombatEventKind.Support, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Support, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Unknown_Kind_Self_Direct_Followup_As_Support()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(
                2010302,
                "Superior Wind Serum",
                SkillCategory.Item,
                SkillSourceType.ItemSkill,
                SkillKind.Unknown,
                SkillSemantics.None)
        ];

        var packet = new ParsedCombatPacket
        {
            SourceId = 9024,
            TargetId = 9024,
            SkillCode = 2010302,
            OriginalSkillCode = 2010302,
            Damage = 400000,
            EffectFamily = "direct-hit"
        };

        Assert.Equal(CombatEventKind.Support, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Support, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Self_Periodic_Mode11_As_Healing_Even_When_Resource_Semantics_Are_Only_Support()
    {
        CombatMetricsEngine.SkillMap =
        [
            new Skill(
                16190020,
                "Enhanced: Elemental Ward",
                SkillCategory.Elementalist,
                SkillSourceType.PcSkill,
                "skill",
                SkillKind.Support,
                SkillSemantics.Support,
                null)
        ];

        var packet = new ParsedCombatPacket
        {
            EffectFamily = "periodic-self-mode-11",
            SourceId = 12115,
            TargetId = 12115,
            SkillCode = 16190020,
            OriginalSkillCode = 1619002011,
            Damage = 704
        };

        Assert.Equal(CombatEventKind.Healing, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.PeriodicHealing, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Support_Skill_With_Healing_Metadata_As_Healing()
    {
        CombatMetricsEngine.SkillMap =
        [
            new Skill(
                17410040,
                "Light of Protection",
                SkillCategory.Cleric,
                SkillSourceType.PcSkill,
                "skill",
                SkillKind.Support,
                SkillSemantics.Support | SkillSemantics.Healing,
                null)
        ];

        var packet = new ParsedCombatPacket
        {
            SourceId = 12115,
            TargetId = 12115,
            SkillCode = 17410040,
            OriginalSkillCode = 17410040,
            Damage = 4104
        };

        Assert.Equal(SkillKind.Support, CombatEventClassifier.ResolveSkillKind(17410040));
        Assert.Equal(SkillSemantics.Support | SkillSemantics.Healing, CombatEventClassifier.ResolveSkillSemantics(17410040));
        Assert.Equal(CombatEventKind.Healing, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Healing, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Self_Periodic_NonHealing_Buff_Tick_As_Support()
    {
        CombatMetricsEngine.SkillMap =
        [
            CreateSkill(
                17150000,
                "Divine Aura",
                SkillCategory.Cleric,
                SkillSourceType.PcSkill,
                SkillKind.Damage,
                SkillSemantics.Damage)
        ];

        var packet = new ParsedCombatPacket
        {
            EffectFamily = "periodic-self-mode-2",
            SourceId = 40969,
            TargetId = 40969,
            SkillCode = 17150000,
            OriginalSkillCode = 1715000611,
            Damage = 2022
        };

        Assert.Equal(CombatEventKind.Support, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Support, CombatEventClassifier.ClassifyValueKind(packet));
    }
}
