using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Capture.Streams;

public sealed class PacketStreamProcessor : IDisposable
{
    private const int MaxBufferSize = 1024 * 1024;
    private readonly PacketTailBuffer _tail = new(2 * MaxBufferSize);
    private readonly IRuntimeObservationSink _sink;
    private readonly PacketFrameParser _parser;
    private long? _timestampOverrideMilliseconds;

    private static ReadOnlySpan<byte> Pattern => PacketTransportCodec.Pattern;

    public PacketStreamProcessor(IRuntimeObservationSink sink)
    {
        _sink = sink;
        _parser = new PacketFrameParser(sink);
    }

    public void Dispose()
    {
        _tail.Dispose();
    }

    public bool AppendAndProcess(ReadOnlySpan<byte> payload, in TcpConnection connection)
    {
        if (_sink is IRuntimeObservationSynchronization synchronization)
        {
            lock (synchronization.Gate)
                return AppendAndProcessCore(payload, in connection);
        }

        return AppendAndProcessCore(payload, in connection);
    }

    private bool AppendAndProcessCore(ReadOnlySpan<byte> payload, in TcpConnection connection)
    {
        var previousAppendBatchOrdinal = _parser.BeginAppendBatch();

        try
        {
            var hasParsed = false;

            if (_tail.Length != 0)
            {
                if (!payload.IsEmpty)
                {
                    AppendToTail(payload);
                }

                ProcessBufferedPackets(ref hasParsed, in connection);
                return hasParsed;
            }

            if (payload.IsEmpty)
            {
                return false;
            }

            var remaining = payload;
            while (TryTakePacket(ref remaining, out var packet))
            {
                if (EmitPacket(packet, in connection))
                {
                    hasParsed = true;
                }
            }

            if (!remaining.IsEmpty)
            {
                AppendToTail(remaining);
            }
            else
            {
                _sink.CompleteBatch(_parser.CurrentAppendBatchOrdinal);
            }

            return hasParsed;
        }
        finally
        {
            _parser.EndAppendBatch(previousAppendBatchOrdinal);
        }
    }

    public bool AppendAndProcess(ReadOnlySpan<byte> payload, in TcpConnection connection, long timestampMilliseconds)
    {
        var previousTimestampOverride = _timestampOverrideMilliseconds;
        _timestampOverrideMilliseconds = timestampMilliseconds > 0
            ? timestampMilliseconds
            : null;

        try
        {
            return AppendAndProcess(payload, connection);
        }
        finally
        {
            _timestampOverrideMilliseconds = previousTimestampOverride;
        }
    }

    private long CurrentTimestampMilliseconds
        => _timestampOverrideMilliseconds ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void ProcessBufferedPackets(ref bool hasParsed, in TcpConnection connection)
    {
        while (TryDequeuePacketLength(out var packetLength))
        {
            var packet = _tail.Data[..packetLength];
            if (EmitPacket(packet, in connection))
            {
                hasParsed = true;
            }

            _tail.Consume(packetLength);
        }
    }

    private bool EmitPacket(ReadOnlySpan<byte> data, in TcpConnection connection)
        => _parser.ParsePacketEntry(data, in connection, CurrentTimestampMilliseconds);

    private void AppendToTail(ReadOnlySpan<byte> data)
    {
        _tail.Append(data);
    }

    private bool TryDequeuePacketLength(out int packetLength)
    {
        packetLength = 0;

        var buffer = _tail.Data;
        if (buffer.IsEmpty)
        {
            return false;
        }

        if (PacketTransportCodec.TryReadTransportLength(buffer, 0, out packetLength) && packetLength <= buffer.Length)
        {
            return true;
        }

        var patternIndex = buffer.IndexOf(Pattern);
        if (patternIndex >= 0)
        {
            packetLength = patternIndex + Pattern.Length;
            return true;
        }

        var keepBytes = Pattern.Length - 1;
        if (buffer.Length > keepBytes)
        {
            _tail.Consume(buffer.Length - keepBytes);
        }

        return false;
    }

    private static bool TryTakePacket(ref ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> packet)
    {
        packet = default;
        if (buffer.IsEmpty)
        {
            return false;
        }

        if (PacketTransportCodec.TryReadTransportLength(buffer, 0, out var packetLength))
        {
            if (packetLength <= buffer.Length)
            {
                packet = buffer[..packetLength];
                buffer = buffer[packetLength..];
                return true;
            }

            return false;
        }

        var patternIndex = buffer.IndexOf(Pattern);
        if (patternIndex >= 0)
        {
            var consumed = patternIndex + Pattern.Length;
            packet = buffer[..consumed];
            buffer = buffer[consumed..];
            return true;
        }

        var keepBytes = Pattern.Length - 1;
        if (buffer.Length > keepBytes)
        {
            buffer = buffer[^keepBytes..];
        }

        return false;
    }
}
