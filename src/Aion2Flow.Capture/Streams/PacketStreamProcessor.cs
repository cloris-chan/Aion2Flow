using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Capture.Streams;

public sealed class PacketStreamProcessor : IDisposable
{
    private readonly PacketTailBuffer _tail = new(CaptureBufferLimits.StreamTailBufferSize);
    private readonly IRuntimeObservationSink _sink;
    private readonly PacketFrameParser _parser;
    private static ReadOnlySpan<byte> Pattern => PacketTransportCodec.Pattern;

    public PacketStreamProcessor(IRuntimeObservationSink sink)
        : this(sink, null)
    {
    }

    internal PacketStreamProcessor(IRuntimeObservationSink sink, Action<ProtocolRoundTripObservation>? protocolRoundTripObserver)
    {
        _sink = sink;
        _parser = new PacketFrameParser(sink, protocolRoundTripObserver);
    }

    public void Dispose()
    {
        _parser.Dispose();
        _tail.Dispose();
    }

    public bool AppendAndProcess(ReadOnlySpan<byte> payload, in TcpConnection connection, long captureTimestampMilliseconds)
    {
        if (_sink is IRuntimeObservationSynchronization synchronization)
        {
            lock (synchronization.Gate)
                return AppendAndProcessCore(payload, in connection, captureTimestampMilliseconds);
        }

        return AppendAndProcessCore(payload, in connection, captureTimestampMilliseconds);
    }

    private bool AppendAndProcessCore(ReadOnlySpan<byte> payload, in TcpConnection connection, long captureTimestampMilliseconds)
    {
        var previousAppendFlushId = _parser.BeginAppendFlush();

        try
        {
            var hasParsed = false;

            if (_tail.Length != 0)
            {
                if (!payload.IsEmpty)
                {
                    AppendToTail(payload);
                }

                ProcessBufferedPackets(ref hasParsed, in connection, captureTimestampMilliseconds);
                return hasParsed;
            }

            if (payload.IsEmpty)
            {
                return false;
            }

            var remaining = payload;
            while (TryTakePacket(ref remaining, out var packet))
            {
                if (EmitPacket(packet, in connection, captureTimestampMilliseconds))
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
                _sink.CompleteFlush(_parser.CurrentAppendFlushId);
            }

            return hasParsed;
        }
        finally
        {
            _parser.EndAppendFlush(previousAppendFlushId);
        }
    }

    private void ProcessBufferedPackets(ref bool hasParsed, in TcpConnection connection, long captureTimestampMilliseconds)
    {
        while (true)
        {
            if (!TryDequeuePacketLength(out var packetLength))
            {
                return;
            }

            var packet = _tail.Data[..packetLength];
            try
            {
                if (EmitPacket(packet, in connection, captureTimestampMilliseconds))
                {
                    hasParsed = true;
                }
            }
            finally
            {
                _tail.Consume(packetLength);
            }
        }
    }

    private bool EmitPacket(ReadOnlySpan<byte> data, in TcpConnection connection, long captureTimestampMilliseconds)
        => _parser.ParsePacketEntry(data, in connection, captureTimestampMilliseconds);

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

        if (PacketTransportCodec.TryReadTransportLength(buffer, 0, out packetLength))
        {
            return packetLength <= buffer.Length;
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
        while (true)
        {
            if (buffer.IsEmpty)
            {
                return false;
            }

            break;
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
