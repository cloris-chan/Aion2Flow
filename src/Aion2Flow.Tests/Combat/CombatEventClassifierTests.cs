using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;

namespace Cloris.Aion2Flow.Tests.Combat;

public sealed class CombatEventClassifierTests
{
    [Fact]
    public void Classifies_Other_Target_Direct_Value_As_Damage()
    {
        var packet = DirectPacket(45872, 1734, 1800030, 185);

        Assert.Equal(CombatEventKind.Damage, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Damage, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Self_Direct_Unknown_As_Support()
    {
        var packet = DirectPacket(9024, 9024, 2010302, 400000);

        Assert.Equal(CombatEventKind.Support, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Support, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Outcome_Only_Invincible_As_Damage_Attempt()
    {
        var packet = DirectPacket(271532, 3737, 14000010, 0);
        packet.Modifiers = DamageModifiers.Invincible;
        packet.AttemptContribution = 1;
        packet.SetEffectTag(PacketEffectTag.PeriodicLinkInvincible);

        Assert.Equal(CombatEventKind.Damage, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Damage, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Target_Periodic_Initial_As_Direct_Damage_Not_Dot()
    {
        var packet = DirectPacket(12115, 17640, 17070240, 15392);
        packet.SetPeriodicEffect(PeriodicEffectRelation.Target, 1);

        Assert.Equal(CombatEventKind.Damage, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Damage, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Target_Periodic_Tick_As_PeriodicDamage()
    {
        var packet = DirectPacket(12115, 17640, 17080240, 1117);
        packet.SetPeriodicEffect(PeriodicEffectRelation.Target, 2);

        Assert.Equal(CombatEventKind.Damage, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.PeriodicDamage, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    public void Classifies_Known_Target_Periodic_Support_As_Support(int periodicMode)
    {
        var packet = DirectPacket(4121, 19621, 17730000, 2457);
        packet.SetPeriodicEffect(PeriodicEffectRelation.Target, periodicMode);

        Assert.Equal(CombatEventKind.Support, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Support, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Target_Periodic_Sentinel_Overflow_As_Support()
    {
        var packet = DirectPacket(4031, 4056, 12250010, int.MaxValue);
        packet.SetPeriodicEffect(PeriodicEffectRelation.Target, 9);

        Assert.Equal(CombatEventKind.Support, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Support, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Theory]
    [InlineData(13710007, 13710000)]
    [InlineData(13790007, 13790000)]
    [InlineData(17102450, 17100000)]
    public void Classifies_Known_Direct_Healing_Packet_Families(int skillCode, int baseSkillCode)
    {
        var packet = DirectPacket(12115, 12115, skillCode, 1234);
        packet.BaseSkillCode = baseSkillCode;

        Assert.Equal(CombatEventKind.Healing, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Healing, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_LightOfProtection_State_Value_As_Support()
    {
        var packet = DirectPacket(12115, 12115, 17410041, 1_696_413);
        packet.BaseSkillCode = 17410000;
        packet.Type = 2;
        packet.Unknown = 79;
        packet.Loop = 1;

        Assert.Equal(CombatEventKind.Support, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Support, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_LightOfProtection_Direct_Healing_Packet_Shape_As_Healing()
    {
        var packet = DirectPacket(12115, 12115, 17410040, 1234);
        packet.BaseSkillCode = 17410000;
        packet.LayoutTag = 4;
        packet.Type = 2;
        packet.Loop = 2;
        packet.DetailRaw = 0x0000000267C58D55L;

        Assert.Equal(CombatEventKind.Healing, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Healing, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_LightOfProtection_With_Packet_Hp_Restore_As_Healing()
    {
        var packet = DirectPacket(12115, 12115, 17410040, 1234);
        packet.BaseSkillCode = 17410000;
        packet.ResourceKind = CombatResourceKind.Health;

        Assert.Equal(CombatEventKind.Healing, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Healing, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_SpiritDescent_Self_Restore_As_Support()
    {
        var packet = DirectPacket(38013, 38013, 16990004, 10_000);

        Assert.Equal(CombatEventKind.Support, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Support, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_SpiritDescent_Other_Target_Value_As_Support()
    {
        var packet = DirectPacket(38013, 4086, 16990004, 10_000);

        Assert.Equal(CombatEventKind.Support, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Support, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Leaves_WindSpirit_Owner_Restore_To_Store_Context()
    {
        var packet = DirectPacket(38013, 4086, 16990003, 114);

        Assert.Equal(CombatEventKind.Damage, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Damage, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Theory]
    [InlineData(16190020, 16190000)]
    [InlineData(16190030, 16190000)]
    public void Classifies_EnhanceSpiritBenediction_Direct_Status_Value_As_Support(int skillCode, int baseSkillCode)
    {
        var packet = DirectPacket(9782, 139201, skillCode, 6354);
        packet.BaseSkillCode = baseSkillCode;
        packet.LayoutTag = 4;
        packet.Type = 2;
        packet.Loop = 2;

        Assert.Equal(CombatEventKind.Support, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Support, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_EnhanceSpiritBenediction_Self_Periodic_Seed_As_PeriodicHealing()
    {
        var packet = DirectPacket(9782, 9782, 1619000023, 4899);
        packet.SkillCode = 16190000;
        packet.SetPeriodicEffect(PeriodicEffectRelation.Self, 9);

        Assert.Equal(CombatEventKind.Healing, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.PeriodicHealing, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_EnhanceSpiritBenediction_Target_Periodic_Pool_As_PeriodicHealing()
    {
        var packet = DirectPacket(9782, 139201, 1619000023, 4510);
        packet.SkillCode = 16190000;
        packet.SetPeriodicEffect(PeriodicEffectRelation.Target, 11);

        Assert.Equal(CombatEventKind.Healing, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.PeriodicHealing, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Theory]
    [InlineData(17091250, 17090000)]
    [InlineData(17121450, 17120000)]
    [InlineData(18121450, 18120000)]
    public void Classifies_Known_Periodic_Healing_Packet_Families(int skillCode, int baseSkillCode)
    {
        var packet = DirectPacket(12115, 12115, skillCode, 4747);
        packet.BaseSkillCode = baseSkillCode;
        packet.SetPeriodicEffect(PeriodicEffectRelation.Self, 11);

        Assert.Equal(CombatEventKind.Healing, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.PeriodicHealing, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Target_Periodic_Healing_Initial_As_Direct_Healing()
    {
        var packet = DirectPacket(12115, 17640, 17091250, 4747);
        packet.BaseSkillCode = 17090000;
        packet.SetPeriodicEffect(PeriodicEffectRelation.Target, 1);

        Assert.Equal(CombatEventKind.Healing, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Healing, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Theory]
    [InlineData(22120011)]
    [InlineData(15160000)]
    [InlineData(18730000)]
    public void Classifies_Known_Shield_As_Support_Shield(int skillCode)
    {
        var packet = DirectPacket(12115, 12115, skillCode, 1025);

        Assert.Equal(CombatEventKind.Support, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Shield, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Generic_RestoreHp_As_PeriodicHealing()
    {
        var packet = DirectPacket(17640, 17640, 1010000, 425);

        Assert.Equal(CombatEventKind.Healing, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.PeriodicHealing, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Drain_Heal_Synthesis_As_DrainHealing()
    {
        var packet = DirectPacket(12115, 12115, 12240010, 540);
        packet.DrainHealAmount = 540;

        Assert.Equal(CombatEventKind.Healing, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.DrainHealing, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Theory]
    [InlineData(CombatResourceKind.Health, CombatEventKind.Healing, CombatValueKind.Healing)]
    [InlineData(CombatResourceKind.Mana, CombatEventKind.Support, CombatValueKind.Support)]
    public void Classifies_Instance_Clear_Resource_Restore(
        CombatResourceKind resourceKind,
        CombatEventKind expectedEventKind,
        CombatValueKind expectedValueKind)
    {
        var packet = DirectPacket(9024, 9024, 1900911, 29586);
        packet.BaseSkillCode = 1900000;
        packet.ResourceKind = resourceKind;

        Assert.Equal(expectedEventKind, CombatEventClassifier.Classify(packet));
        Assert.Equal(expectedValueKind, CombatEventClassifier.ClassifyValueKind(packet));
    }

    [Fact]
    public void Classifies_Self_ActionPoint_Restore_Followup_As_Support()
    {
        var packet = DirectPacket(7166, 7166, 13360017, 30000);
        packet.BaseSkillCode = 13360000;
        packet.ChargeStage = 7;

        Assert.Equal(CombatEventKind.Support, CombatEventClassifier.Classify(packet));
        Assert.Equal(CombatValueKind.Support, CombatEventClassifier.ClassifyValueKind(packet));
    }

    private static ParsedCombatPacket DirectPacket(int sourceId, int targetId, int skillCode, int damage) =>
        new()
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = skillCode,
            OriginalSkillCode = skillCode,
            Damage = damage
        };
}
