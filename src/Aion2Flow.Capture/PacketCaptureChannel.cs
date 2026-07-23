using System.Threading.Channels;
using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Capture;

internal enum CaptureDispatchItemKind : byte
{
    Packet,
    CandidateContinuation,
    Promotion,
    ConnectionClose
}

internal readonly struct CaptureDispatchItem
{
    private CaptureDispatchItem(
        CaptureDispatchItemKind kind,
        CapturedPacket? packet,
        CaptureConnectionPromotion? promotion,
        TcpConnection connection,
        long connectionGeneration,
        long connectionOrdinal)
    {
        Kind = kind;
        Packet = packet;
        Promotion = promotion;
        Connection = connection;
        ConnectionGeneration = connectionGeneration;
        ConnectionOrdinal = connectionOrdinal;
    }

    public CaptureDispatchItemKind Kind { get; }
    public CapturedPacket? Packet { get; }
    public CaptureConnectionPromotion? Promotion { get; }
    public TcpConnection Connection { get; }
    public long ConnectionGeneration { get; }
    public long ConnectionOrdinal { get; }

    public static CaptureDispatchItem ForPacket(CapturedPacket packet) =>
        new(CaptureDispatchItemKind.Packet, packet, null, default, 0, 0);

    public static CaptureDispatchItem ForCandidateContinuation(
        CapturedPacket packet,
        long connectionOrdinal) =>
        new(CaptureDispatchItemKind.CandidateContinuation, packet, null, default, 0, connectionOrdinal);

    public static CaptureDispatchItem ForPromotion(CaptureConnectionPromotion promotion) =>
        new(CaptureDispatchItemKind.Promotion, null, promotion, default, 0, 0);

    public static CaptureDispatchItem ForConnectionClose(
        in TcpConnection connection,
        long connectionGeneration,
        long connectionOrdinal) =>
        new(
            CaptureDispatchItemKind.ConnectionClose,
            null,
            null,
            connection,
            connectionGeneration,
            connectionOrdinal);

    public void Return()
    {
        Packet?.Return();
        Promotion?.Return();
    }
}

internal static class PacketCaptureChannel
{
    private const int Capacity = 256;
    private static readonly Channel<CaptureDispatchItem> _channel = Channel.CreateBounded<CaptureDispatchItem>(new BoundedChannelOptions(Capacity)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait
    });

    public static bool WritePacket(CapturedPacket packet, CancellationToken cancellationToken) =>
        Write(CaptureDispatchItem.ForPacket(packet), cancellationToken);

    public static bool WriteCandidateContinuation(
        CapturedPacket packet,
        long connectionOrdinal,
        CancellationToken cancellationToken) =>
        Write(CaptureDispatchItem.ForCandidateContinuation(packet, connectionOrdinal), cancellationToken);

    public static bool WritePromotion(CaptureConnectionPromotion promotion, CancellationToken cancellationToken) =>
        Write(CaptureDispatchItem.ForPromotion(promotion), cancellationToken);

    public static bool WriteConnectionClose(
        in TcpConnection connection,
        long connectionGeneration,
        long connectionOrdinal,
        CancellationToken cancellationToken) =>
        Write(
            CaptureDispatchItem.ForConnectionClose(in connection, connectionGeneration, connectionOrdinal),
            cancellationToken);

    public static IAsyncEnumerable<CaptureDispatchItem> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public static void Drain()
    {
        while (_channel.Reader.TryRead(out var item))
        {
            item.Return();
        }
    }

    private static bool Write(CaptureDispatchItem item, CancellationToken cancellationToken)
    {
        try
        {
            while (!_channel.Writer.TryWrite(item))
            {
                if (!_channel.Writer.WaitToWriteAsync(cancellationToken).AsTask().GetAwaiter().GetResult())
                {
                    return false;
                }
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
