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
}
