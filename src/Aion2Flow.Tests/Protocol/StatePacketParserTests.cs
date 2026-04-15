using Cloris.Aion2Flow.PacketCapture.Protocol;

namespace Cloris.Aion2Flow.Tests.Protocol;

public sealed class StatePacketParserTests
{
    [Theory]
    [MemberData(nameof(FixtureCatalog.State4136Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_4136_State_Frame(FixtureCatalog.State4136Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet4136Parser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.SourceId, parsed.SourceId);
        Assert.Equal(sample.State0, parsed.State0);
        Assert.Equal(sample.State1, parsed.State1);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.State2136Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_2136_State_Frame(FixtureCatalog.State2136Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet2136Parser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.Sequence, parsed.Sequence);
        Assert.Equal(sample.Value0, parsed.Value0);
        Assert.Equal(sample.Value1, parsed.Value1);
        Assert.Equal(sample.Value7, parsed.Value7);
        Assert.Equal(sample.TailMarker, parsed.TailMarker);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.State0140Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_0140_State_Frame(FixtureCatalog.State0140Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet0140Parser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.Value0, parsed.Value0);
        Assert.Equal(sample.Value1, parsed.Value1);
        Assert.Equal(sample.TailLength, parsed.TailLength);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.State0240Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_0240_State_Frame(FixtureCatalog.State0240Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet0240Parser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.Value0, parsed.Value0);
        Assert.Equal(sample.Value1, parsed.Value1);
        Assert.Equal(sample.TailLength, parsed.TailLength);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.State4536Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_4536_State_Frame(FixtureCatalog.State4536Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet4536Parser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.SourceId, parsed.SourceId);
        Assert.Equal(sample.Value0, parsed.Value0);
        Assert.Equal(sample.TailLength, parsed.TailLength);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.State4636Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_4636_State_Frame(FixtureCatalog.State4636Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet4636Parser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.SourceId, parsed.SourceId);
        Assert.Equal(sample.State0, parsed.State0);
        Assert.Equal(sample.State1, parsed.State1);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.State1D37Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_1D37_State_Frame(FixtureCatalog.State1D37Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet1D37Parser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.SourceId, parsed.SourceId);
        Assert.Equal(sample.GroupCode, parsed.GroupCode);
        Assert.Equal(sample.StateCode, parsed.StateCode);
        Assert.Equal(sample.Family, parsed.Family);
        Assert.Equal(sample.TailSignature, parsed.TailSignature);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.Compact0238Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_0238_Compact_Control_Frame(FixtureCatalog.Compact0238Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet0238CompactControlParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.SourceId, parsed.SourceId);
        Assert.Equal(sample.Mode, parsed.Mode);
        Assert.Equal(sample.SkillCodeRaw, parsed.SkillCodeRaw);
        Assert.Equal(sample.Marker, parsed.Marker);
        Assert.Equal(sample.Flag, parsed.Flag);
        Assert.Equal(sample.EchoSourceId, parsed.EchoSourceId);
        Assert.Equal(sample.ZeroValue, parsed.ZeroValue);
        Assert.Equal(sample.TailValue, parsed.TailValue);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.Compact0638Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_0638_Compact_Control_Frame(FixtureCatalog.Compact0638Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet0638CompactControlParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.SourceId, parsed.SourceId);
        Assert.Equal(sample.SkillCodeRaw, parsed.SkillCodeRaw);
        Assert.Equal(sample.Marker, parsed.Marker);
        Assert.Equal(sample.Flag, parsed.Flag);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.Aux2B38Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_2B38_Aux_Frame(FixtureCatalog.Aux2B38Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet2B38Parser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.SourceId, parsed.SourceId);
        Assert.Equal(sample.Phase, parsed.Phase);
        Assert.Equal(sample.Marker, parsed.Marker);
        Assert.Equal(sample.ActionCode, parsed.ActionCode);
        Assert.Equal(sample.Family, parsed.Family);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.Aux2A38Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_2A38_Aux_Frame(FixtureCatalog.Aux2A38Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet2A38Parser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.SourceId, parsed.SourceId);
        Assert.Equal(sample.Mode, parsed.Mode);
        Assert.Equal(sample.GroupCode, parsed.GroupCode);
        Assert.Equal(sample.SequenceId, parsed.SequenceId);
        Assert.Equal(sample.BuffCodeRaw, parsed.BuffCodeRaw);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.Aux2C38Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_2C38_Aux_Frame(FixtureCatalog.Aux2C38Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet2C38Parser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.SourceId, parsed.SourceId);
        Assert.Equal(sample.Mode, parsed.Mode);
        Assert.Equal(sample.StateCode, parsed.StateCode);
        Assert.Equal(sample.SequenceId, parsed.SequenceId);
        Assert.Equal(sample.ResultCode, parsed.ResultCode);
        Assert.Equal(sample.Family, parsed.Family);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.State4936Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_4936_State_Frame(FixtureCatalog.State4936Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet4936Parser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.SourceId, parsed.SourceId);
        Assert.Equal(sample.Mode, parsed.Mode);
        Assert.Equal(sample.GroupCode, parsed.GroupCode);
        Assert.Equal(sample.Flag, parsed.Flag);
        Assert.Equal(sample.Value0, parsed.Value0);
        Assert.Equal(sample.Marker, parsed.Marker);
        Assert.Equal(sample.Value1, parsed.Value1);
        Assert.Equal(sample.Family, parsed.Family);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.State4036Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_4036_State_Frame(FixtureCatalog.State4036Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet4036Parser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.Family, parsed.Family);
        Assert.Equal(sample.Layout, parsed.Layout);
        Assert.Equal(sample.SourceId, parsed.SourceId);
        Assert.Equal(sample.Mode0, parsed.Mode0);
        Assert.Equal(sample.Mode1, parsed.Mode1);
        Assert.Equal(sample.Mode2, parsed.Mode2);
        Assert.Equal(sample.LinkedValue, parsed.LinkedValue);
        Assert.Equal(sample.Gauge0, parsed.Gauge0);
        Assert.Equal(sample.Gauge1, parsed.Gauge1);
        Assert.Equal(sample.TailMode, parsed.TailMode);
        Assert.Equal(sample.TailState, parsed.TailState);
        Assert.Equal(sample.TailFlag0, parsed.TailFlag0);
        Assert.Equal(sample.TailFlag1, parsed.TailFlag1);
        Assert.Equal(sample.TailValue, parsed.TailValue);
        Assert.Equal(sample.SharedTag, parsed.SharedTag);
        Assert.Equal(sample.SharedGauge0, parsed.SharedGauge0);
        Assert.Equal(sample.SharedGauge1, parsed.SharedGauge1);
        Assert.Equal(sample.SharedGauge2, parsed.SharedGauge2);
        Assert.Equal(sample.SharedGauge3, parsed.SharedGauge3);
        Assert.Equal(sample.SharedFlag, parsed.SharedFlag);
        Assert.Equal(sample.SharedMini0, parsed.SharedMini0);
        Assert.Equal(sample.SharedMini1, parsed.SharedMini1);
        Assert.Equal(sample.HeavyGauge0, parsed.HeavyGauge0);
        Assert.Equal(sample.HeavyGauge1, parsed.HeavyGauge1);
        Assert.Equal(sample.HeavyValue0, parsed.HeavyValue0);
        Assert.Equal(sample.HeavyValue1, parsed.HeavyValue1);
        Assert.Equal(sample.HeavyFlag, parsed.HeavyFlag);
        Assert.Equal(sample.HeavyMini0, parsed.HeavyMini0);
        Assert.Equal(sample.HeavySentinel0, parsed.HeavySentinel0);
        Assert.Equal(sample.HeavySentinel1, parsed.HeavySentinel1);
        Assert.Equal(sample.HeavyTrailer0, parsed.HeavyTrailer0);
        Assert.Equal(sample.HeavyTrailer1, parsed.HeavyTrailer1);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.NpcSpawn4036Samples), MemberType = typeof(FixtureCatalog))]
    public void TryParseNpcSpawn_Extracts_Entity_And_NpcCode(FixtureCatalog.NpcSpawn4036Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet4036CreateParser.TryParseNpcSpawn(packet, out var result);

        Assert.True(ok);
        Assert.Equal(sample.Family, result.Family);
        Assert.Equal(sample.EntityId, result.EntityId);
        Assert.Equal(sample.NpcCode, result.NpcCode);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.Wrapped8456Samples), MemberType = typeof(FixtureCatalog))]
    public void Parses_8456_Wrapped_Frame(FixtureCatalog.Wrapped8456Sample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet8456EnvelopeParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.Prefix0, parsed.Prefix0);
        Assert.Equal(sample.Prefix1, parsed.Prefix1);
        Assert.Equal(sample.Prefix2, parsed.Prefix2);
        Assert.Equal(sample.InnerFamily, parsed.InnerFamily);
        Assert.Equal(sample.InnerValue, parsed.InnerValue);
        Assert.Equal(sample.Trailer, parsed.Trailer);
    }
}
