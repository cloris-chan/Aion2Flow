using Cloris.Aion2Flow.PacketCapture.Protocol;

namespace Cloris.Aion2Flow.Tests.Protocol;

public sealed class Packet4436NicknameParserTests
{
    [Theory]
    [MemberData(nameof(FixtureCatalog.OtherNicknameSamples), MemberType = typeof(FixtureCatalog))]
    public void Parses_Other_Full_Name(FixtureCatalog.NicknameSample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet4436NicknameParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.PlayerId, parsed.PlayerId);
        Assert.Equal(sample.Nickname, parsed.Nickname);
    }

    [Fact]
    public void Parses_Cross_Server_Name_With_17_Marker()
    {
        var packet = Convert.FromHexString("B4114436B0180320A4031706E6B585E5B09D12000000010201");

        var ok = Packet4436NicknameParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(3120, parsed.PlayerId);
        Assert.Equal("浅尝", parsed.Nickname);
        Assert.Equal(420, parsed.OriginServerId);
    }

    [Fact]
    public void Parses_Cross_Server_Name_With_Multibyte_Nickname()
    {
        var packet = Convert.FromHexString("B8124436DE0C0730A001170CE4BBA5E69C88E4B98BE5908D20000000020202");

        var ok = Packet4436NicknameParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(1630, parsed.PlayerId);
        Assert.Equal("以月之名", parsed.Nickname);
        Assert.Equal(160, parsed.OriginServerId);
    }
}
