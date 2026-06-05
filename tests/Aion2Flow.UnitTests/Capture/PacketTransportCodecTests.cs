using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketTransportCodecTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(16_384)]
    [InlineData(1_048_575)]
    public void VarInt_RoundTrips_Transport_Length_Prefix(int value)
    {
        Span<byte> bytes = stackalloc byte[5];
        Assert.True(PacketTransportCodec.TryWriteVarInt(value, bytes, out var written));
        Assert.True(PacketTransportCodec.TryReadVarInt(bytes[..written], 0, out var result));

        Assert.Equal(value, result.Value);
        Assert.Equal(written, result.ByteCount);
    }

    [Fact]
    public void Transport_Length_Uses_Decoded_Value_Plus_Prefix_Minus_Header()
    {
        Span<byte> bytes = stackalloc byte[5];
        Assert.True(PacketTransportCodec.TryWriteVarInt(35, bytes, out var written));

        Assert.True(PacketTransportCodec.TryReadTransportLength(bytes[..written], 0, out var packetLength));

        Assert.Equal(35 + written - 4, packetLength);
    }
}
