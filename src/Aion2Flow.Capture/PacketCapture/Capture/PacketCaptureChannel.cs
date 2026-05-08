using System.Threading.Channels;

namespace Cloris.Aion2Flow.PacketCapture.Capture;

internal static class PacketCaptureChannel
{
    private static readonly Channel<CapturedPacket> _channel = Channel.CreateUnbounded<CapturedPacket>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    public static bool TryWrite(CapturedPacket packet) => _channel.Writer.TryWrite(packet);

    public static IAsyncEnumerable<CapturedPacket> ReadAllAsync(CancellationToken cancellationToken = default) => _channel.Reader.ReadAllAsync(cancellationToken);
}
