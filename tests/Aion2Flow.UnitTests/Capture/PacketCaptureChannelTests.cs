using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketCaptureChannelTests
{
    [Fact]
    public async Task ReadAllAsyncDrainsQueuedPacketsBeforeAcknowledgmentNotifications()
    {
        var first = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var second = first with { DestinationPort = 49_629 };
        var cancellationToken = TestContext.Current.CancellationToken;

        PacketCaptureChannel.Drain();
        try
        {
            Assert.True(PacketCaptureChannel.WriteConnectionClose(first, 1, 1, cancellationToken));
            Assert.True(PacketCaptureChannel.TryWriteAcknowledgmentAvailable());
            Assert.True(PacketCaptureChannel.WriteConnectionClose(second, 1, 2, cancellationToken));

            await using var reader = PacketCaptureChannel.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            Assert.True(await reader.MoveNextAsync());
            Assert.Equal(CaptureDispatchItemKind.ConnectionClose, reader.Current.Kind);
            Assert.Equal(first, reader.Current.Connection);

            Assert.True(await reader.MoveNextAsync());
            Assert.Equal(CaptureDispatchItemKind.ConnectionClose, reader.Current.Kind);
            Assert.Equal(second, reader.Current.Connection);

            Assert.True(await reader.MoveNextAsync());
            Assert.Equal(CaptureDispatchItemKind.AcknowledgmentAvailable, reader.Current.Kind);
        }
        finally
        {
            PacketCaptureChannel.Drain();
        }
    }
}
