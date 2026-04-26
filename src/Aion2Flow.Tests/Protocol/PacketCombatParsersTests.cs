using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.PacketCapture.Protocol;
using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.Tests.Protocol;

public sealed class PacketCombatParsersTests
{
    [Theory]
    [MemberData(nameof(FixtureCatalog.DamageSamples), MemberType = typeof(FixtureCatalog))]
    public void Parses_0438_Damage_Frame(FixtureCatalog.DamageSample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet0438DamageParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.TargetId, parsed.TargetId);
        Assert.Equal(sample.SourceId, parsed.SourceId);
        Assert.Equal(sample.SkillCodeRaw, parsed.SkillCodeRaw);
        Assert.Equal(sample.Type, parsed.Type);
        Assert.Equal(sample.Modifiers, parsed.Modifiers);
        Assert.Equal(sample.Unknown, parsed.Unknown);
        Assert.Equal(sample.Damage, parsed.Damage);
        Assert.Equal(sample.Loop, parsed.Loop);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.DamageSamples), MemberType = typeof(FixtureCatalog))]
    public void Parses_0438_Damage_Payload_With_Shared_Layout_Rules(FixtureCatalog.DamageSample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);
        var reader = new PacketSpanReader(packet);
        Assert.True(reader.TryReadVarInt(out _));

        var ok = Packet0438DamageParser.TryParsePayload(packet.AsSpan()[reader.Offset..], out var parsed, out var consumed);

        Assert.True(ok);
        Assert.True(Packet0438Layout.TryGetDetailLength(parsed.LayoutTag, out var detailLength));
        Assert.True(detailLength > 0);
        Assert.Equal(sample.TargetId, parsed.TargetId);
        Assert.Equal(sample.SourceId, parsed.SourceId);
        Assert.Equal(sample.Damage, parsed.Damage);
        Assert.Equal(packet.Length - reader.Offset - parsed.TailLength, consumed);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.ObservedDamageSamples), MemberType = typeof(FixtureCatalog))]
    public void Parses_0438_Observed_Damage_Frame(FixtureCatalog.ObservedDamageSample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet0438DamageParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.TargetId, parsed.TargetId);
        Assert.Equal(sample.SourceId, parsed.SourceId);
        Assert.Equal(sample.SkillCodeRaw, parsed.SkillCodeRaw);
        Assert.Equal(sample.Type, parsed.Type);
        Assert.Equal(sample.Damage, parsed.Damage);
    }

    [Fact]
    public void Parses_0438_Damage_Frame_With_Separate_Marker_Byte()
    {
        var packet = HexHelper.Parse("2B043892D5013604EB449A48C700040311005C02D84D01000000FC8901E8AA090101C1180100AC3E");

        var ok = Packet0438DamageParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(4, parsed.Marker);
        Assert.Equal(3, parsed.Type);
        Assert.Equal(DamageModifiers.Critical | DamageModifiers.Back | DamageModifiers.Smite, parsed.Modifiers);
    }

    [Fact]
    public void Parses_0438_Drain_Heal_Tail_Inside_Declared_Frame()
    {
        var packet = HexHelper.Parse("280438C49F011604B5409A48C700020311005C02D84D01000000C88301CC9309010100AC3E14008DC49F010201000100000000000000");

        var ok = Packet0438DamageParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(13060250, parsed.SkillCodeRaw);
        Assert.Equal(7980, parsed.DrainHealAmount);
        Assert.Equal(0, parsed.TailLength);
    }

    [Fact]
    public void Does_Not_Read_Next_Frame_Length_As_0438_Drain_Heal_Tail()
    {
        var packet = HexHelper.Parse("250438FA84021600DE4432D4C6001703010093E3AA4D01000000B29901F32901010014008DFA84020201007E7E6E0000000000");

        var ok = Packet0438DamageParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(13030450, parsed.SkillCodeRaw);
        Assert.Equal(0, parsed.DrainHealAmount);
        Assert.Equal(2, parsed.TailLength);
    }

    [Fact]
    public void Payload_Drain_Heal_Tail_Must_End_At_Frame_Boundary()
    {
        var realDrainPayload = HexHelper.Parse("0438C49F011604B5409A48C700020311005C02D84D01000000C88301CC9309010100AC3E14008DC49F010201000100000000000000");
        var falseTailPayload = HexHelper.Parse("0438FA84021600DE4432D4C6001703010093E3AA4D01000000B29901F32901010014008DFA84020201007E7E6E0000000000");

        Assert.True(Packet0438DamageParser.TryParsePayload(realDrainPayload, out var realDrain, out _));
        Assert.True(Packet0438DamageParser.TryParsePayload(falseTailPayload, out var falseTail, out _));

        Assert.Equal(7980, realDrain.DrainHealAmount);
        Assert.Equal(0, falseTail.DrainHealAmount);
    }

    [Theory]
    [InlineData("260438A3E4010604F6345006B8006D0201004B77E24701000000BF8401C65E0100EB02", 363)]
    [InlineData("2A0438A3E4012604F6345006B800960301004B77E24701000000BF84018EBE0101F3030100DA05", 730)]
    [InlineData("270438A3E4010604F6345006B800A10311004B77E24701000000BF8401FBED020100D603", 470)]
    public void Parses_0438_PunishingStrike_Absorb_Tail_As_Drain_Healing(string hex, int expectedDrainHeal)
    {
        var packet = HexHelper.Parse(hex);

        var ok = Packet0438DamageParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(12060240, parsed.SkillCodeRaw);
        Assert.Equal(expectedDrainHeal, parsed.DrainHealAmount);
        Assert.Equal(0, parsed.TailLength);
    }

    [Fact]
    public void Does_Not_Synthesize_PunishingStrike_Absorb_When_No_Absorb_Tail_Is_Present()
    {
        var packet = HexHelper.Parse("240438A3E4010600F6345006B800AF0201004B77E24701000000BF8401ED620100");

        var ok = Packet0438DamageParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(12060240, parsed.SkillCodeRaw);
        Assert.Equal(0, parsed.DrainHealAmount);
        Assert.True(parsed.TailLength > 0);
    }

    [Theory]
    [InlineData("2804388B7F4610A5A00DF4C81000250204005B7F8E0601000000904E0101", DamageModifiers.Parry)]
    [InlineData("2804388B7F4610A5A00DF4C81000280202005B7F8E0601000000904E0101", DamageModifiers.Block)]
    [InlineData("2804388B7F4610A5A00DF4C81000290282005B7F8E0601000000904E0101", DamageModifiers.Block | DamageModifiers.Perfect)]
    [InlineData("2804388B7F4610A5A00DF4C810000E0284005B7F8E0601000000904E0101", DamageModifiers.Parry | DamageModifiers.Perfect)]
    public void Parses_0438_Defensive_Modifiers_Semantically(string hex, DamageModifiers expectedModifiers)
    {
        var packet = HexHelper.Parse(hex);
        var reader = new PacketSpanReader(packet);

        Assert.True(reader.TryReadVarInt(out _));
        var ok = Packet0438DamageParser.TryParsePayload(packet.AsSpan()[reader.Offset..], out var parsed, out _);

        Assert.True(ok);
        Assert.Equal(expectedModifiers, parsed.Modifiers & expectedModifiers);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.Compact0438Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_Compact_0438_Value_Frame(FixtureCatalog.Compact0438Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet0438CompactValueParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(0, parsed.LayoutTag);
        Assert.Equal(sample.TargetId, parsed.TargetId);
        Assert.Equal(sample.SourceId, parsed.SourceId);
        Assert.Equal(sample.SkillCodeRaw, parsed.SkillCodeRaw);
        Assert.Equal(sample.Marker, parsed.Marker);
        Assert.Equal(sample.Type, parsed.Type);
        Assert.Equal(sample.Unknown, parsed.Unknown);
        Assert.Equal(sample.Value, parsed.Value);
        Assert.Equal(sample.Loop, parsed.Loop);
        Assert.Equal(sample.TailLength, parsed.TailLength);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.PeriodicValueSamples), MemberType = typeof(FixtureCatalog))]
    public void Parses_0538_Periodic_Value_Frame(FixtureCatalog.PeriodicValueSample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet0538PeriodicValueParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.TargetId, parsed.TargetId);
        Assert.Equal(sample.Mode, parsed.Mode);
        Assert.Equal(sample.SourceId, parsed.SourceId);
        Assert.Equal(sample.SkillCodeRaw, parsed.SkillCodeRaw);
        Assert.Equal(sample.LegacySkillCode, parsed.LegacySkillCode);
        Assert.Equal(sample.Damage, parsed.Damage);
        Assert.Equal(sample.LinkId, parsed.LinkId);
        Assert.Equal(sample.TailRaw, parsed.TailRaw);
        Assert.Equal(sample.IsLinkRecord, parsed.IsLinkRecord);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.RemainHpSamples), MemberType = typeof(FixtureCatalog))]
    public void Parses_008D_Remain_Hp_Frame(FixtureCatalog.RemainHpSample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet008DRemainHpParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.NpcId, parsed.NpcId);
        Assert.Equal(sample.Value0, parsed.Value0);
        Assert.Equal(sample.Value1, parsed.Value1);
        Assert.Equal(sample.Value2, parsed.Value2);
        Assert.Equal(sample.Hp, parsed.Hp);
        Assert.Equal(sample.TailLength, parsed.TailLength);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.BattleToggleSamples), MemberType = typeof(FixtureCatalog))]
    public void Parses_218D_Battle_Toggle_Frame(FixtureCatalog.BattleToggleSample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet218DBattleToggleParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.NpcId, parsed.NpcId);
        Assert.Equal(sample.TailLength, parsed.TailLength);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.Create4036Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_4036_Create_Frame(FixtureCatalog.Create4036Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet4036CreateParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.Kind, parsed.Kind);
        Assert.Equal(sample.OwnerId, parsed.OwnerId);
        Assert.Equal(sample.SummonId, parsed.SummonId);
        Assert.Equal(sample.NpcCode, parsed.NpcCode);
        Assert.True(parsed.TailOffset > 0);
    }
}
