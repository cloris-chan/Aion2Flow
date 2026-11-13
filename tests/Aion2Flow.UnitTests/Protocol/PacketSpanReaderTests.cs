using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Tests.Protocol;

public sealed class PacketSpanReaderTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(127L)]
    [InlineData(128L)]
    [InlineData(int.MaxValue)]
    [InlineData(uint.MaxValue)]
    public void VarIntReadsTheFullUInt32Range(long value)
    {
        var encoded = Encode(value);

        Assert.True(PacketSpanReader.TryReadVarInt(encoded, 0, out var actual, out var consumed));
        Assert.Equal(unchecked((int)value), actual);
        Assert.Equal(encoded.Length, consumed);
    }

    [Theory]
    [InlineData(4_294_967_296L)]
    [InlineData(8_589_934_591L)]
    [InlineData(long.MaxValue)]
    public void VarIntRejectsValuesThatOverflowUInt32(long value)
    {
        var encoded = Encode(value);

        Assert.False(PacketSpanReader.TryReadVarInt(encoded, 0, out var actual, out var consumed));
        Assert.Equal(0, actual);
        Assert.Equal(0, consumed);
    }

    [Fact]
    public void VarIntReadsDoNotAllocate()
    {
        var encoded = Encode(uint.MaxValue);
        for (var i = 0; i < 64; i++)
            PacketSpanReader.TryReadVarInt(encoded, 0, out _);

        var before = GC.GetAllocatedBytesForCurrentThread();
        long checksum = 0;
        for (var i = 0; i < 10_000; i++)
        {
            AssertRead(encoded, out var value);
            checksum += value;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(-10_000, checksum);
        Assert.Equal(0, allocated);
    }

    private static void AssertRead(byte[] encoded, out int value)
    {
        if (!PacketSpanReader.TryReadVarInt(encoded, 0, out value))
            throw new InvalidOperationException("A valid varint must be readable.");
    }

    [Fact]
    public void VarIntRejectsTruncatedValues()
    {
        var encoded = Encode(uint.MaxValue);
        var reader = new PacketSpanReader(encoded.AsSpan(0, encoded.Length - 1));

        Assert.False(reader.TryReadVarInt(out _));
        Assert.Equal(encoded.Length - 1, reader.Offset);
        Assert.Equal(0, reader.Remaining);
    }

    private static byte[] Encode(long value)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write7BitEncodedInt64(value);
        return stream.ToArray();
    }
}
