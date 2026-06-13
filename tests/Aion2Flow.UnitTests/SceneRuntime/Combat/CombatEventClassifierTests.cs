namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class CombatEventClassifierTests
{
    [Fact]
    public void Classifies_Other_Target_Direct_Value_As_Damage()
    {
        var packet = DirectPacket(45872, 1734, 1800030, 185);

        AssertClassifies(packet, CombatEventKind.Damage, CombatValueKind.Damage);
    }

    [Fact]
    public void Classifies_Self_Target_Direct_Value_As_Support()
    {
        var packet = DirectPacket(9024, 9024, 2010302, 400000);

        AssertClassifies(packet, CombatEventKind.Support, CombatValueKind.Support);
    }

    [Fact]
    public void Classifies_Outcome_Only_Avoidance_As_Damage_Attempt()
    {
        var packet = DirectPacket(271532, 3737, 14000010, 0);
        packet.Modifiers = DamageModifiers.Invincible;
        packet.AttemptContribution = 1;
        packet.SetEffectTag(PacketEffectTag.PeriodicLinkInvincible);

        AssertClassifies(packet, CombatEventKind.Damage, CombatValueKind.Damage);
    }

    [Theory]
    [InlineData(CombatResourceKind.Health, CombatEventKind.Healing, CombatValueKind.Healing)]
    [InlineData(CombatResourceKind.Mana, CombatEventKind.Support, CombatValueKind.Support)]
    public void Classifies_Direct_Resource_Restore_From_Packet_ResourceKind(
        CombatResourceKind resourceKind,
        CombatEventKind expectedEventKind,
        CombatValueKind expectedValueKind)
    {
        var packet = DirectPacket(12115, 12115, 17410040, 1234);
        packet.ResourceKind = resourceKind;

        AssertClassifies(packet, expectedEventKind, expectedValueKind);
    }

    [Fact]
    public void Classifies_Other_Target_Loop2_Direct_Value_As_Damage()
    {
        var packet = DirectPacket(9782, 139201, 16190020, 6354);
        packet.LayoutTag = 4;
        packet.Flag = 0;
        packet.Type = 2;
        packet.Loop = 2;

        AssertClassifies(packet, CombatEventKind.Damage, CombatValueKind.Damage);
    }

    [Fact]
    public void Classifies_Drain_Heal_Synthesis_As_DrainHealing()
    {
        var packet = DirectPacket(12115, 12115, 12240010, 540);
        packet.DrainHealAmount = 540;

        AssertClassifies(packet, CombatEventKind.Healing, CombatValueKind.DrainHealing);
    }

    [Fact]
    public void Classifies_Target_Periodic_Initial_As_Direct_Damage()
    {
        var packet = DirectPacket(12115, 17640, 17070240, 15392);
        packet.SetPeriodicEffect(PeriodicEffectRelation.Target, 1);

        AssertClassifies(packet, CombatEventKind.Damage, CombatValueKind.Damage);
    }

    [Fact]
    public void Classifies_Target_Periodic_Tick_As_PeriodicDamage()
    {
        var packet = DirectPacket(12115, 17640, 17080240, 1117);
        packet.SetPeriodicEffect(PeriodicEffectRelation.Target, 2);

        AssertClassifies(packet, CombatEventKind.Damage, CombatValueKind.PeriodicDamage);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(11)]
    public void Classifies_Target_Periodic_Support_Modes_As_Support_Seed(int periodicMode)
    {
        var packet = DirectPacket(4121, 19621, 17730000, 2457);
        packet.SetPeriodicEffect(PeriodicEffectRelation.Target, periodicMode);

        AssertClassifies(packet, CombatEventKind.Support, CombatValueKind.Support);
    }

    [Fact]
    public void Classifies_Target_Periodic_Health_Initial_As_Healing()
    {
        var packet = DirectPacket(12115, 17640, 17091250, 4747);
        packet.ResourceKind = CombatResourceKind.Health;
        packet.SetPeriodicEffect(PeriodicEffectRelation.Target, 1);

        AssertClassifies(packet, CombatEventKind.Healing, CombatValueKind.Healing);
    }

    [Fact]
    public void Classifies_Target_Periodic_Health_Tick_As_PeriodicHealing()
    {
        var packet = DirectPacket(12115, 17640, 17091250, 4747);
        packet.ResourceKind = CombatResourceKind.Health;
        packet.SetPeriodicEffect(PeriodicEffectRelation.Target, 2);

        AssertClassifies(packet, CombatEventKind.Healing, CombatValueKind.PeriodicHealing);
    }

    [Fact]
    public void Classifies_Self_Periodic_Mode11_As_PeriodicHealing()
    {
        var packet = DirectPacket(12115, 12115, 17091250, 4747);
        packet.SetPeriodicEffect(PeriodicEffectRelation.Self, 11);

        AssertClassifies(packet, CombatEventKind.Healing, CombatValueKind.PeriodicHealing);
    }

    [Fact]
    public void Classifies_Self_Periodic_Mode10_As_Support()
    {
        var packet = DirectPacket(12115, 12115, 17091250, 4747);
        packet.SetPeriodicEffect(PeriodicEffectRelation.Self, 10);

        AssertClassifies(packet, CombatEventKind.Support, CombatValueKind.Support);
    }

    [Fact]
    public void SkillCode_Does_Not_Turn_Direct_Self_Value_Into_Healing_Or_Shield()
    {
        var packet = DirectPacket(12115, 12115, 1010000, 425);

        AssertClassifies(packet, CombatEventKind.Support, CombatValueKind.Support);
    }

    [Fact]
    public void ResourceEffectRefs_Do_Not_Drive_Direct_Event_Semantics()
    {
        var packet = DirectPacket(4156, 34135, 16770001, 198);
        packet.BodyResourceEffectRef = ResourceEffectRef.FromRaw(1677000111);
        packet.DetailResourceEffectRef = ResourceEffectRef.FromRaw(1677000112);
        packet.LayoutTag = 4;
        packet.Type = 2;
        packet.Loop = 2;

        AssertClassifies(packet, CombatEventKind.Damage, CombatValueKind.Damage);
    }

    private static void AssertClassifies(ParsedCombatPacket packet, CombatEventKind eventKind, CombatValueKind valueKind)
    {
        Assert.Equal(eventKind, CombatEventClassifier.Classify(packet));
        Assert.Equal(valueKind, CombatEventClassifier.ClassifyValueKind(packet));
    }

    private static ParsedCombatPacket DirectPacket(int sourceId, int targetId, int skillCode, int damage) =>
        new()
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = skillCode,
            Damage = damage
        };
}
