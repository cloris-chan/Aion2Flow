using System.Buffers;
using Cloris.Aion2Flow.Capture.Streams;
using Microsoft.Extensions.ObjectPool;

namespace Cloris.Aion2Flow.Capture;

public sealed class CapturedPacket
{
    private static readonly ObjectPool<CapturedPacket> _pool = new DefaultObjectPool<CapturedPacket>(new PooledCapturedPacketPolicy());

    private IMemoryOwner<byte>? _bufferOwner;

    private int _payloadOffset;
    private int _payloadLength;
    public TcpConnection Connection { get; private set; }
    public CapturePacketAdmission Admission { get; private set; }
    public uint SequenceNumber { get; private set; }
    public uint AcknowledgmentNumber { get; private set; }
    public long CaptureTimestamp { get; private set; }
    public long CaptureTimestampMilliseconds { get; private set; }

    public ReadOnlySpan<byte> Payload => _bufferOwner!.Memory.Span.Slice(_payloadOffset, _payloadLength);

    private CapturedPacket() { }

    public void Return()
    {
        var bufferOwner = Interlocked.Exchange(ref _bufferOwner, null);
        if (bufferOwner is null)
        {
            return;
        }

        bufferOwner.Dispose();
        _pool.Return(this);
    }

    public static CapturedPacket Create(
        TcpConnection connection,
        CapturePacketAdmission admission,
        IMemoryOwner<byte> bufferOwner,
        int payloadOffset,
        int payloadLength,
        uint sequenceNumber,
        long captureTimestampMilliseconds,
        uint acknowledgmentNumber = 0,
        long captureTimestamp = 0)
    {
        var instance = _pool.Get();
        instance.Connection = connection;
        instance.Admission = admission;
        instance._bufferOwner = bufferOwner;
        instance._payloadOffset = payloadOffset;
        instance._payloadLength = payloadLength;
        instance.SequenceNumber = sequenceNumber;
        instance.AcknowledgmentNumber = acknowledgmentNumber;
        instance.CaptureTimestamp = captureTimestamp;
        instance.CaptureTimestampMilliseconds = captureTimestampMilliseconds;
        return instance;
    }

    public static CapturedPacket CreateCopy(
        TcpConnection connection,
        CapturePacketAdmission admission,
        ReadOnlySpan<byte> payload,
        uint sequenceNumber,
        long captureTimestampMilliseconds,
        uint acknowledgmentNumber = 0,
        long captureTimestamp = 0)
    {
        var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(owner.Memory.Span);
        return Create(
            connection,
            admission,
            owner,
            0,
            payload.Length,
            sequenceNumber,
            captureTimestampMilliseconds,
            acknowledgmentNumber,
            captureTimestamp);
    }

    internal void UpdateAdmission(CapturePacketAdmission admission)
    {
        Admission = admission;
    }

    sealed class PooledCapturedPacketPolicy : PooledObjectPolicy<CapturedPacket>
    {
        public override CapturedPacket Create() => new();
        public override bool Return(CapturedPacket obj) => true;
    }
}
