using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class Packet4136ParserTests
{
    [Fact]
    public void TryParse_Parses_852100_ExtendedStateHpPair()
    {
        var packet = Convert.FromHexString("7B4136F4CB0185210031672C000802347CABC78E983B48548147477AEF3F437588F9CF0ABEC3D9E9BC125250BC01AF24AF2464000000640000000000000000000000640000006400000001000000000000000000000000000000000000000E000801001400000007C9C12D2B1FEC1B22C91100561680AC00");

        Assert.True(Packet4136Parser.TryParse(packet, out var parsed));
        Assert.Equal(26100, parsed.EntityId);
        Assert.Equal(2910001, parsed.NpcCode);
        Assert.Equal(4655, parsed.CurrentHp);
        Assert.Equal(4655, parsed.MaxHp);
    }

    [Fact]
    public void TryParse_Parses_052000_ExtendedStateHpPair()
    {
        var packet = Convert.FromHexString("714136DFB0020520000B0E20000802DD7E0E483BF47D4644600E479D0D8943ECC294FB2441A6A00FC3D4F9653F018BF5078BF5076400000064000000000000000000000064000000640000000100000000000000000000000000000000000000060009010023000000F7149C4D00");

        Assert.True(Packet4136Parser.TryParse(packet, out var parsed));
        Assert.Equal(39007, parsed.EntityId);
        Assert.Equal(2100747, parsed.NpcCode);
        Assert.Equal(129675, parsed.CurrentHp);
        Assert.Equal(129675, parsed.MaxHp);
    }
}
