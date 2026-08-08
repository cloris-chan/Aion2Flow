using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Capture.Streams;

public sealed class PacketStreamProcessor : IDisposable
{
    private readonly PacketTransportStreamDeframer _transport;
    private readonly IRuntimeObservationSink _sink;
    private readonly PacketFrameParser _parser;
    private static ReadOnlySpan<byte> Pattern => PacketTransportCodec.Pattern;

    public PacketStreamProcessor(IRuntimeObservationSink sink)
        : this(sink, null)
    {
    }

    internal PacketStreamProcessor(IRuntimeObservationSink sink, Action<ProtocolRoundTripObservation>? protocolRoundTripObserver)
        : this(sink, protocolRoundTripObserver, PacketTransportFraming.Auto, 0)
    {
    }

    internal PacketStreamProcessor(
        IRuntimeObservationSink sink,
        Action<ProtocolRoundTripObservation>? protocolRoundTripObserver,
        PacketTransportFraming framing,
        int canonicalPrefixLength)
    {
        _sink = sink;
        _parser = new PacketFrameParser(sink, protocolRoundTripObserver);
        _transport = new PacketTransportStreamDeframer(framing, canonicalPrefixLength);
    }

    public void Dispose()
    {
        _parser.Dispose();
        _transport.Dispose();
    }

    public bool AppendAndProcess(ReadOnlySpan<byte> payload, in TcpConnection connection, long captureTimestampMilliseconds)
    {
        var timestamp = new PacketProcessingTimestamp(captureTimestampMilliseconds, 0);
        return AppendAndProcess(payload, in connection, in timestamp);
    }

    internal bool AppendAndProcess(ReadOnlySpan<byte> payload, in TcpConnection connection, in PacketProcessingTimestamp timestamp)
    {
        if (_sink is IRuntimeObservationSynchronization synchronization)
        {
            lock (synchronization.Gate)
                return AppendAndProcessCore(payload, in connection, in timestamp);
        }

        return AppendAndProcessCore(payload, in connection, in timestamp);
    }

    private bool AppendAndProcessCore(ReadOnlySpan<byte> payload, in TcpConnection connection, in PacketProcessingTimestamp timestamp)
    {
        if (_transport.IsFaulted)
        {
            return false;
        }

        var previousAppendFlushId = _parser.BeginAppendFlush();

        try
        {
            var hasParsed = false;
            _transport.Append(payload);
            ProcessBufferedPackets(ref hasParsed, in connection, in timestamp);
            _sink.CompleteFlush(_parser.CurrentAppendFlushId);

            return hasParsed;
        }
        finally
        {
            _parser.EndAppendFlush(previousAppendFlushId);
        }
    }

    private void ProcessBufferedPackets(ref bool hasParsed, in TcpConnection connection, in PacketProcessingTimestamp timestamp)
    {
        while (true)
        {
            var availability = _transport.PrepareCanonicalData();
            if (availability == PacketTransportDataAvailability.NeedMore)
            {
                return;
            }

            if (availability == PacketTransportDataAvailability.Invalid)
            {
                CaptureLog.Write(CaptureLogLevel.Error, "Packet stream rejected invalid length-prefixed framing.");
                return;
            }

            var data = _transport.CanonicalData;
            if (_transport.IsDirectRecoveryEnabled &&
                TryFindConfirmedTick(
                    data,
                    timestamp.TimelineUnixMilliseconds,
                    out var tickOffset) &&
                tickOffset != 0)
            {
                _transport.ConsumeCanonical(tickOffset);
                continue;
            }

            var probe = PacketTransportCodec.ProbeCanonicalFrame(
                data,
                CaptureBufferLimits.StreamTailBufferSize);
            if (probe.Kind == PacketCanonicalFrameProbeKind.NeedMore)
            {
                if (_transport.TryExpandCanonicalData())
                {
                    continue;
                }

                if (_transport.IsFaulted)
                {
                    CaptureLog.Write(CaptureLogLevel.Error, "Packet stream rejected invalid length-prefixed framing.");
                }

                return;
            }

            var packetLength = probe.FrameLength;
            if (probe.Kind == PacketCanonicalFrameProbeKind.Invalid)
            {
                var patternIndex = data.IndexOf(Pattern);
                if (patternIndex < 0)
                {
                    if (_transport.TryExpandCanonicalData())
                    {
                        continue;
                    }

                    if (_transport.IsFaulted)
                    {
                        CaptureLog.Write(CaptureLogLevel.Error, "Packet stream rejected invalid length-prefixed framing.");
                        return;
                    }

                    const int tickFrameLength = 11;
                    var keepBytes = _transport.IsDirectRecoveryEnabled
                        ? tickFrameLength - 1
                        : Pattern.Length - 1;
                    if (data.Length > keepBytes)
                    {
                        _transport.ConsumeCanonical(data.Length - keepBytes);
                    }

                    return;
                }

                packetLength = patternIndex + Pattern.Length;
            }

            var packet = data[..packetLength];
            try
            {
                var parsed = EmitPacket(packet, in connection, in timestamp);
                if (parsed ||
                    (!_transport.IsLengthPrefixed &&
                     TcpWorldStreamClassifier.IsConfirmed0036(
                         packet,
                         timestamp.TimelineUnixMilliseconds)))
                {
                    _transport.MarkDirectCanonicalAlignment();
                }

                if (parsed)
                {
                    hasParsed = true;
                }
            }
            finally
            {
                _transport.ConsumeCanonical(packetLength);
            }
        }
    }

    private bool EmitPacket(ReadOnlySpan<byte> data, in TcpConnection connection, in PacketProcessingTimestamp timestamp)
        => _parser.ParsePacketEntry(data, in connection, in timestamp);

    private static bool TryFindConfirmedTick(
        ReadOnlySpan<byte> data,
        long captureTimestampMilliseconds,
        out int offset)
    {
        const int tickFrameLength = 11;
        for (var candidateOffset = 0;
             candidateOffset <= data.Length - tickFrameLength;
             candidateOffset++)
        {
            if (TcpWorldStreamClassifier.IsConfirmed0036(
                    data.Slice(candidateOffset, tickFrameLength),
                    captureTimestampMilliseconds))
            {
                offset = candidateOffset;
                return true;
            }
        }

        offset = 0;
        return false;
    }
}
