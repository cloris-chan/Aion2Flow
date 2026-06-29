using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.UnitTests.SceneRuntime;

public sealed class Packet0238CompactControlParserTests
{
    [Theory]
    [InlineData("1A0238E67C00C1620E010A00E67C00000000DF88010100", 9, 17503, 1, 0)]
    [InlineData("1B0238E67C00474005010F00E67C00000000DF8801018032", 10, 17503, 1, 6400)]
    public void TryParse_AcceptsZeroPrefixedVarIntTail(string hex, int tailLength, int firstValue, int secondValue, int thirdValue)
    {
        var packet = Convert.FromHexString(hex);

        Assert.True(Packet0238CompactControlParser.TryParse(packet, out var parsed));
        Assert.Equal(15974, parsed.SourceId);
        Assert.Equal(0, parsed.Mode);
        Assert.Equal(15974, parsed.EchoSourceId);
        Assert.Equal(tailLength, parsed.TailLength);
        Assert.Equal(firstValue, parsed.TailFirstValue);
        Assert.Equal(secondValue, parsed.TailSecondValue);
        Assert.Equal(thirdValue, parsed.TailThirdValue);
    }

    [Fact]
    public void TryParse_PreservesOpaqueTailWithoutSemanticValues()
    {
        var packet = Convert.FromHexString("1B0238E67C00474005010F00E67C01020304DF8801018032");

        Assert.True(Packet0238CompactControlParser.TryParse(packet, out var parsed));
        Assert.Equal(10, parsed.TailLength);
        Assert.Equal(0, parsed.TailFirstValue);
        Assert.Equal(0, parsed.TailSecondValue);
        Assert.Equal(0, parsed.TailThirdValue);
    }
}
