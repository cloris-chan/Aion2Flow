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
    public uint SequenceNumber { get; private set; }

    public ReadOnlySpan<byte> Payload => _bufferOwner!.Memory.Span.Slice(_payloadOffset, _payloadLength);

    private CapturedPacket() { }

    public void Return()
    {
        _bufferOwner?.Dispose();
        _bufferOwner = null;
        _pool.Return(this);
    }

    public static CapturedPacket Create(TcpConnection connection, IMemoryOwner<byte> bufferOwner, int payloadOffset, int payloadLength, uint sequenceNumber)
    {
        var instance = _pool.Get();
        instance.Connection = connection;
        instance._bufferOwner = bufferOwner;
        instance._payloadOffset = payloadOffset;
        instance._payloadLength = payloadLength;
        instance.SequenceNumber = sequenceNumber;
        return instance;
    }

    public static CapturedPacket CreateCopy(TcpConnection connection, ReadOnlySpan<byte> payload, uint sequenceNumber)
    {
        var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(owner.Memory.Span);
        return Create(connection, owner, 0, payload.Length, sequenceNumber);
    }

    sealed class PooledCapturedPacketPolicy : PooledObjectPolicy<CapturedPacket>
    {
        public override CapturedPacket Create() => new();
        public override bool Return(CapturedPacket obj) => true;
    }
}
