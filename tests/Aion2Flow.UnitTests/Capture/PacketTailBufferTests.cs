using System.Reflection;
using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketTailBufferTests
{
    [Fact]
    public void Constructor_DoesNotRentFullBackingBuffer()
    {
        using var buffer = new PacketTailBuffer(1024 * 1024);

        Assert.Equal(0, ReadBufferCapacity(buffer));
        Assert.True(buffer.Data.IsEmpty);
    }

    [Fact]
    public void SmallAppend_UsesSmallBackingBuffer()
    {
        using var buffer = new PacketTailBuffer(1024 * 1024);

        buffer.Append([1, 2, 3, 4]);

        Assert.True(ReadBufferCapacity(buffer) <= 8 * 1024);
        Assert.Equal([1, 2, 3, 4], buffer.Data.ToArray());
    }

    private static int ReadBufferCapacity(PacketTailBuffer buffer)
    {
        var field = typeof(PacketTailBuffer).GetField("_bufferCapacity", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (int)field.GetValue(buffer)!;
    }
}
