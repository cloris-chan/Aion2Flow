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
