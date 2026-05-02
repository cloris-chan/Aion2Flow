using Cloris.Aion2Flow.PacketCapture.Protocol;

namespace Cloris.Aion2Flow.Tests.Protocol;

public sealed class Packet3336NicknameParserTests
{
    [Theory]
    [MemberData(nameof(FixtureCatalog.OwnNicknameSamples), MemberType = typeof(FixtureCatalog))]
    public void Parses_Own_Nickname_Frame(FixtureCatalog.NicknameSample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet3336NicknameParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.PlayerId, parsed.PlayerId);
        Assert.Equal(sample.Nickname, parsed.Nickname);
    }

    [Fact]
    public void Parses_Own_Nickname_Frame_With_Trailing_Payload()
    {
        var packet = HexHelper.FromFixture("nickname/3336-own-thanks-with-tail.hex");

        var ok = Packet3336NicknameParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(190, parsed.PlayerId);
        Assert.Equal("謝謝惠顧", parsed.Nickname);
    }

    [Fact]
    public void Parses_Observed_Own_Nickname_Variant_With_Embedded_Tail()
    {
        var packet = Convert.FromHexString("D1143336D70F5FB17904070750657269676565EF0306000000012D000000");

        var ok = Packet3336NicknameParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(2007, parsed.PlayerId);
        Assert.Equal("Perigee", parsed.Nickname);
        Assert.Equal(495, parsed.OriginServerId);
    }

    [Fact]
    public void Parses_Cross_Server_Own_Nickname_With_0f_Marker()
    {
        var packet = Convert.FromHexString("3336D84C5FB171000F06E99B85E69882EF0312000000012D000000");

        var ok = Packet3336NicknameParser.TryParsePayload(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(9816, parsed.PlayerId);
        Assert.Equal("雅昂", parsed.Nickname);
        Assert.Equal(495, parsed.OriginServerId);
    }
}
