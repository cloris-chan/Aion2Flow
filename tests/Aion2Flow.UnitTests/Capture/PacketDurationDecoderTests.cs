using Cloris.Aion2Flow.Protocol.Combat;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketDurationDecoderTests
{
    [Fact]
    public void CombinesLowAndUpperPacketFields()
    {
        Assert.True(PacketDurationDecoder.TryDecodeMilliseconds(37_856, 4, out var durationMilliseconds));
        Assert.Equal(300_000, durationMilliseconds);
    }

    [Fact]
    public void KeepsCommonLowOnlyDurationsUnchanged()
    {
        Assert.True(PacketDurationDecoder.TryDecodeMilliseconds(5_000, 0, out var durationMilliseconds));
        Assert.Equal(5_000, durationMilliseconds);
    }

    [Fact]
    public void RecognizesThePacketIndefiniteMarker()
    {
        Assert.False(PacketDurationDecoder.TryDecodeMilliseconds(ushort.MaxValue, 0x0000_FFFF_FFFF_FFFF, out _));
    }
}
