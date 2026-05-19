using System.Threading.Channels;

namespace Cloris.Aion2Flow.Capture;

internal static class PacketCaptureChannel
{
    private const int Capacity = 256;
    private static readonly Channel<CapturedPacket> _channel = Channel.CreateBounded<CapturedPacket>(new BoundedChannelOptions(Capacity)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait
    });

    public static bool TryWrite(CapturedPacket packet) => _channel.Writer.TryWrite(packet);

    public static IAsyncEnumerable<CapturedPacket> ReadAllAsync(CancellationToken cancellationToken = default) => _channel.Reader.ReadAllAsync(cancellationToken);
}
