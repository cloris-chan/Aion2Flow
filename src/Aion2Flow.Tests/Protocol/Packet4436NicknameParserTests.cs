using Cloris.Aion2Flow.Protocol.Packets;

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
        Assert.Equal(18, parsed.ClassCode);
        Assert.Equal(1, parsed.FactionCode);
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
        Assert.Equal(32, parsed.ClassCode);
        Assert.Equal(2, parsed.FactionCode);
    }

    [Fact]
    public void Parses_Light_Faction_From_4436_Nickname_Tail()
    {
        var packet = Convert.FromHexString("C90C4436E2080320A401070CE7BAA2E8B186E586B0E7B3951E000000010280D2");

        var ok = Packet4436NicknameParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal("红豆冰糕", parsed.Nickname);
        Assert.Equal(30, parsed.ClassCode);
        Assert.Equal(1, parsed.FactionCode);
    }

    [Fact]
    public void Parses_Dark_Faction_From_4436_Nickname_Tail()
    {
        var packet = Convert.FromHexString("C80D44368B5A0320A001070CE98791E889B2E8AA93E7BAA610000000020280D2");

        var ok = Packet4436NicknameParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal("金色誓约", parsed.Nickname);
        Assert.Equal(16, parsed.ClassCode);
        Assert.Equal(2, parsed.FactionCode);
    }
}
