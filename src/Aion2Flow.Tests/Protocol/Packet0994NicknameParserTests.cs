using Cloris.Aion2Flow.PacketCapture.Protocol;

namespace Cloris.Aion2Flow.Tests.Protocol;

public sealed class Packet0994NicknameParserTests
{
    [Theory]
    [MemberData(nameof(FixtureCatalog.RosterNicknameSamples), MemberType = typeof(FixtureCatalog))]
    public void Parses_Length_Prefixed_Roster_Name(FixtureCatalog.NicknameSample sample)
    {
        var packet = HexHelper.FromFixture(sample.Path);

        var ok = Packet0994NicknameParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(sample.PlayerId, parsed.PlayerId);
        Assert.Equal(sample.Nickname, parsed.Nickname);
    }
}
