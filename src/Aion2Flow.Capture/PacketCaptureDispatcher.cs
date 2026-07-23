using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Capture;

public sealed class PacketCaptureDispatcher
{
    private readonly Func<IRuntimeObservationSink> _sinkFactory;
    private readonly Action<ProtocolRoundTripObservation>? _protocolRoundTripObserver;
    private readonly Action<TcpConnection>? _connectionLockedObserver;
    private readonly Action? _connectionChangedObserver;
    private readonly Action<CaptureConnectionPromotion, bool>? _promotionCompletedObserver;
    private readonly Dictionary<TcpConnection, TcpCaptureStreamState> _tcpStreams = [];
    private TcpConnection _lastActiveConnection;
    private long _lastActiveGeneration;
    private long _lastPromotedCandidateOrdinal;
    private long _lastTimelineTimestampMilliseconds;
    private bool _hasLastActiveConnection;
    private bool _hasTimelineTimestamp;
    private Task? _worker;
    private CancellationTokenSource? _cts;

    public PacketCaptureDispatcher(Func<IRuntimeObservationSink> sinkFactory)
        : this(sinkFactory, null, null)
    {
    }

    internal PacketCaptureDispatcher(
        Func<IRuntimeObservationSink> sinkFactory,
        Action<ProtocolRoundTripObservation>? protocolRoundTripObserver,
        Action<TcpConnection>? connectionLockedObserver,
        Action? connectionChangedObserver = null,
        Action<CaptureConnectionPromotion, bool>? promotionCompletedObserver = null)
    {
        ArgumentNullException.ThrowIfNull(sinkFactory);
        _sinkFactory = sinkFactory;
        _protocolRoundTripObserver = protocolRoundTripObserver;
        _connectionLockedObserver = connectionLockedObserver;
        _connectionChangedObserver = connectionChangedObserver;
        _promotionCompletedObserver = promotionCompletedObserver;
    }

    public async Task StartAsync(CancellationToken token)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        _worker = Task.Factory.StartNew(async () =>
        {
            try
            {
                await foreach (var item in PacketCaptureChannel.ReadAllAsync(_cts.Token))
                {
                    try
                    {
                        try
                        {
                            DispatchItem(item);
                        }
                        catch (Exception ex)
                        {
                            CaptureLog.Write(CaptureLogLevel.Error, $"Packet dispatch failed: {ex.Message}");
                        }
                    }
                    finally
                    {
                        try { item.Return(); } catch (Exception) { }
                    }
                }
            }
            catch (OperationCanceledException) when (_cts.Token.IsCancellationRequested) { }
            catch (Exception)
            {
                throw;
            }
        }, TaskCreationOptions.LongRunning).Unwrap();
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();

        if (_worker is not null)
        {
            try
            {
                await _worker;
            }
            catch (TaskCanceledException)
            {

            }

            _worker = null;
        }

        _cts = null;
        DisposeAllStreams();
    }

    internal bool DispatchItem(CaptureDispatchItem item)
    {
        return item.Kind switch
        {
            CaptureDispatchItemKind.Packet => item.Packet is { } packet && DispatchActivePacket(packet),
            CaptureDispatchItemKind.CandidateContinuation => item.Packet is { } packet &&
                DispatchCandidateContinuation(packet, item.ConnectionOrdinal),
            CaptureDispatchItemKind.Promotion => item.Promotion is { } promotion && DispatchPromotion(promotion),
            CaptureDispatchItemKind.ConnectionClose => DispatchConnectionClose(
                item.Connection,
                item.ConnectionGeneration,
                item.ConnectionOrdinal),
            _ => false
        };
    }

    private bool DispatchActivePacket(CapturedPacket packet)
    {
        var connection = packet.Connection;
        var admission = packet.Admission;
        if (!CaptureConnectionGate.IsAdmissionCurrent(in connection, in admission))
        {
            return false;
        }

        AppendRawPacket(packet);
        return DispatchCapturedPacket(packet);
    }

    private bool DispatchCandidateContinuation(CapturedPacket packet, long connectionOrdinal)
    {
        if (connectionOrdinal <= 0)
        {
            return false;
        }

        var connection = packet.Connection;
        if (!CaptureConnectionGate.TryGetActiveAdmission(
                in connection,
                connectionOrdinal,
                out var admission))
        {
            return false;
        }

        packet.UpdateAdmission(admission);
        AppendRawPacket(packet);
        return DispatchCapturedPacket(packet);
    }

    private bool DispatchPromotion(CaptureConnectionPromotion promotion)
    {
        var wasPromoted = false;
        try
        {
            if (promotion.IsCancelled)
            {
                return false;
            }

            if (promotion.CandidateOrdinal > 0 &&
                promotion.CandidateOrdinal <= _lastPromotedCandidateOrdinal)
            {
                return false;
            }

            var connection = promotion.Connection;
            if (!CaptureConnectionGate.TryPromote(
                    in connection,
                    out var activeAdmission,
                    out _,
                    forceNewGeneration: true,
                    promotion.CandidateOrdinal))
            {
                return false;
            }

            wasPromoted = true;
            if (promotion.CandidateOrdinal > 0)
            {
                _lastPromotedCandidateOrdinal = promotion.CandidateOrdinal;
            }

            _connectionChangedObserver?.Invoke();
            foreach (var packet in promotion.Packets)
            {
                AppendRawPacket(packet);
            }

            var parsed = false;
            var isFirstPacket = true;
            foreach (var packet in promotion.Packets)
            {
                packet.UpdateAdmission(activeAdmission);
                parsed |= DispatchCapturedPacket(
                    packet,
                    isFirstPacket ? promotion.ReplayStartSequenceNumber : null,
                    isFirstPacket ? promotion.CandidateOrdinal : 0);
                isFirstPacket = false;
            }

            return parsed;
        }
        finally
        {
            _promotionCompletedObserver?.Invoke(promotion, wasPromoted);
        }
    }

    private bool DispatchConnectionClose(
        TcpConnection connection,
        long connectionGeneration,
        long connectionOrdinal)
    {
        if (!CaptureConnectionGate.TryClose(
                in connection,
                connectionGeneration,
                connectionOrdinal,
                out var closedConnection))
        {
            return false;
        }

        _connectionChangedObserver?.Invoke();
        DisposeStream(closedConnection);
        return true;
    }

    private static void AppendRawPacket(CapturedPacket packet)
    {
        if (packet.CaptureTimestamp == 0)
        {
            return;
        }

        var connection = packet.Connection;
        RawPacketDump.Append(
            "inbound",
            connection.SourcePort,
            connection.DestinationPort,
            packet.SequenceNumber,
            packet.AcknowledgmentNumber,
            packet.CaptureTimestamp,
            packet.Payload);
    }

    internal bool DispatchCapturedPacket(
        CapturedPacket packet,
        uint? initialSequenceNumber = null,
        long connectionOrdinal = 0)
    {
        var connection = packet.Connection;
        var admission = packet.Admission;
        if (!CaptureConnectionGate.IsAdmissionCurrent(in connection, in admission))
        {
            return false;
        }

        var createdStream = false;
        if (!_tcpStreams.TryGetValue(connection, out var tcpStream) ||
            tcpStream.Generation != admission.Generation)
        {
            if (tcpStream is not null)
            {
                _tcpStreams.Remove(connection);
                tcpStream.Dispose();
            }

            createdStream = true;
            tcpStream = TcpCaptureStreamState.Create(
                _sinkFactory,
                _protocolRoundTripObserver,
                admission.Generation,
                initialSequenceNumber,
                connectionOrdinal);
            _tcpStreams[connection] = tcpStream;

            if (_hasLastActiveConnection &&
                (_lastActiveConnection != connection || _lastActiveGeneration != admission.Generation))
            {
                var timelineTimestampMilliseconds = NormalizeTimelineTimestamp(packet.CaptureTimestampMilliseconds);
                var source = new PacketObservationSource(
                    timelineTimestampMilliseconds,
                    0,
                    0,
                    0,
                    packet.SequenceNumber,
                    default);
                tcpStream.Sink.MarkSceneTransportBoundary(in source);
            }

            _lastActiveConnection = connection;
            _lastActiveGeneration = admission.Generation;
            _hasLastActiveConnection = true;
            DisposeOtherStreams(connection);
        }

        var context = new DispatchContext(this, tcpStream, connection);
        tcpStream.Reassembler.Feed(packet.SequenceNumber, packet.Payload, packet.CaptureTimestampMilliseconds, ref context, HandleReassembledChunk);

        if (createdStream)
        {
            _connectionLockedObserver?.Invoke(connection);
        }

        return context.HasParsed;
    }

    private static void HandleReassembledChunk(uint sequenceNumber, ReadOnlySpan<byte> chunk, long captureTimestampMilliseconds, ref DispatchContext context)
    {
        var timelineTimestampMilliseconds = context.Dispatcher.NormalizeTimelineTimestamp(captureTimestampMilliseconds);
        RawPacketDump.AppendReassembled(
            "inbound",
            context.Connection,
            sequenceNumber,
            timelineTimestampMilliseconds,
            context.Stream.ConnectionOrdinal,
            chunk);
        if (context.Stream.Processor.AppendAndProcess(chunk, context.Connection, timelineTimestampMilliseconds))
        {
            context.HasParsed = true;
        }
    }

    private long NormalizeTimelineTimestamp(long captureTimestampMilliseconds)
    {
        if (!_hasTimelineTimestamp)
        {
            _hasTimelineTimestamp = true;
            _lastTimelineTimestampMilliseconds = captureTimestampMilliseconds;
            return captureTimestampMilliseconds;
        }

        if (captureTimestampMilliseconds > _lastTimelineTimestampMilliseconds)
        {
            _lastTimelineTimestampMilliseconds = captureTimestampMilliseconds;
        }

        return _lastTimelineTimestampMilliseconds;
    }

    private void DisposeOtherStreams(TcpConnection keepConnection)
    {
        List<TcpConnection>? connectionsToRemove = null;
        foreach (var connection in _tcpStreams.Keys)
        {
            if (connection == keepConnection)
            {
                continue;
            }

            connectionsToRemove ??= new(Math.Max(_tcpStreams.Count - 1, 1));
            connectionsToRemove.Add(connection);
        }

        if (connectionsToRemove is null)
        {
            return;
        }

        foreach (var connection in connectionsToRemove)
        {
            DisposeStream(connection);
        }
    }

    private void DisposeAllStreams()
    {
        foreach (var stream in _tcpStreams.Values)
        {
            stream.Dispose();
        }

        _tcpStreams.Clear();
        _hasLastActiveConnection = false;
        _lastActiveConnection = default;
        _lastActiveGeneration = 0;
    }

    private void DisposeStream(TcpConnection connection)
    {
        if (_tcpStreams.Remove(connection, out var stream))
        {
            stream.Dispose();
        }
    }

    private struct DispatchContext(PacketCaptureDispatcher dispatcher, TcpCaptureStreamState stream, TcpConnection connection)
    {
        public readonly PacketCaptureDispatcher Dispatcher = dispatcher;
        public readonly TcpCaptureStreamState Stream = stream;
        public readonly TcpConnection Connection = connection;
        public bool HasParsed;
    }

    private sealed class TcpCaptureStreamState(
        TcpStreamReassembler reassembler,
        PacketStreamProcessor processor,
        IRuntimeObservationSink sink,
        long generation,
        long connectionOrdinal) : IDisposable
    {
        public long Generation { get; } = generation;

        public long ConnectionOrdinal { get; } = connectionOrdinal;

        public TcpStreamReassembler Reassembler { get; } = reassembler;

        public PacketStreamProcessor Processor { get; } = processor;

        public IRuntimeObservationSink Sink { get; } = sink;

        public void Dispose()
        {
            Processor.Dispose();
            Reassembler.Dispose();
        }

        public static TcpCaptureStreamState Create(
            Func<IRuntimeObservationSink> sinkFactory,
            Action<ProtocolRoundTripObservation>? protocolRoundTripObserver,
            long generation,
            uint? initialSequenceNumber,
            long connectionOrdinal)
        {
            var sink = sinkFactory();
            var reassembler = new TcpStreamReassembler();
            if (initialSequenceNumber is { } sequenceNumber)
            {
                reassembler.StartAt(sequenceNumber);
            }

            return new TcpCaptureStreamState(
                reassembler,
                new PacketStreamProcessor(sink, protocolRoundTripObserver),
                sink,
                generation,
                connectionOrdinal);
        }
    }
}
