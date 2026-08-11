using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Capture;

public sealed class PacketCaptureDispatcher
{
    private enum PromotionAdmissionKind : byte
    {
        PrimaryReplacement,
        Supplemental
    }

    private readonly Func<IRuntimeObservationSink> _sinkFactory;
    private readonly Action<ProtocolRoundTripObservation>? _protocolRoundTripObserver;
    private readonly Action<TcpConnection>? _connectionLockedObserver;
    private readonly Action? _connectionChangedObserver;
    private readonly Action<CaptureConnectionPromotion, bool>? _promotionCompletedObserver;
    private readonly LatestTcpAcknowledgmentTracker _acknowledgments;
    private readonly Func<long>? _transportOrdinalAllocator;
    private readonly CanonicalPacketMirrorDeduplicator _mirrorDeduplicator = new();
    private readonly Dictionary<TcpConnection, TcpCaptureStreamState> _tcpStreams = [];
    private readonly Dictionary<TcpConnection, long> _lastPromotedCandidateOrdinals = [];
    private readonly Dictionary<TcpConnection, LinkedListNode<RetiredCandidateOrdinal>> _retiredCandidateOrdinals = [];
    private readonly LinkedList<RetiredCandidateOrdinal> _retiredCandidateOrder = [];
    private TcpConnection _lastActiveConnection;
    private long _lastActiveGeneration;
    private long _lastTimelineTimestampMilliseconds;
    private long _nextFallbackTransportOrdinal;
    private bool _hasLastActiveConnection;
    private bool _hasTimelineTimestamp;
    private Task? _worker;
    private CancellationTokenSource? _cts;

    public PacketCaptureDispatcher(Func<IRuntimeObservationSink> sinkFactory)
        : this(sinkFactory, null, null, null, null, null, null)
    {
    }

    internal PacketCaptureDispatcher(
        Func<IRuntimeObservationSink> sinkFactory,
        Action<ProtocolRoundTripObservation>? protocolRoundTripObserver,
        Action<TcpConnection>? connectionLockedObserver,
        Action? connectionChangedObserver = null,
        Action<CaptureConnectionPromotion, bool>? promotionCompletedObserver = null,
        LatestTcpAcknowledgmentTracker? acknowledgments = null,
        Func<long>? transportOrdinalAllocator = null)
    {
        ArgumentNullException.ThrowIfNull(sinkFactory);
        _sinkFactory = sinkFactory;
        _protocolRoundTripObserver = protocolRoundTripObserver;
        _connectionLockedObserver = connectionLockedObserver;
        _connectionChangedObserver = connectionChangedObserver;
        _promotionCompletedObserver = promotionCompletedObserver;
        _acknowledgments = acknowledgments ?? new LatestTcpAcknowledgmentTracker();
        _transportOrdinalAllocator = transportOrdinalAllocator;
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
        _acknowledgments.Clear();
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
            CaptureDispatchItemKind.AcknowledgmentAvailable => DispatchAcknowledgmentAvailable(),
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
        return DispatchCapturedPacket(packet, connectionOrdinal: admission.ConnectionOrdinal);
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
        return DispatchCapturedPacket(packet, connectionOrdinal: admission.ConnectionOrdinal);
    }

    private bool DispatchPromotion(CaptureConnectionPromotion promotion)
    {
        var wasPromoted = false;
        try
        {
            if (!promotion.TryAcquireForDispatch())
            {
                return false;
            }

            var connection = promotion.Connection;
            var promotionKind = ResolvePromotionAdmissionKind(in promotion);
            if (promotion.CandidateOrdinal > 0 &&
                IsStalePromotion(in connection, promotion.CandidateOrdinal))
            {
                return false;
            }

            CapturePacketAdmission activeAdmission;
            var eviction = default(CaptureConnectionEviction);
            var promoted = promotionKind switch
            {
                PromotionAdmissionKind.Supplemental => CaptureConnectionGate.TryPromoteSupplemental(
                    in connection,
                    promotion.CandidateOrdinal,
                    out activeAdmission,
                    out eviction),
                _ => CaptureConnectionGate.TryPromote(
                    in connection,
                    out activeAdmission,
                    out _,
                    forceNewGeneration: true,
                    promotion.CandidateOrdinal)
            };
            if (!promoted)
            {
                return false;
            }

            if (eviction.HasValue)
            {
                var evictedConnection = eviction.Connection;
                RetireCandidateOrdinal(in evictedConnection, eviction.ConnectionOrdinal);
                DisposeStream(evictedConnection);
            }

            if (promotionKind == PromotionAdmissionKind.PrimaryReplacement)
                _mirrorDeduplicator.Clear();

            wasPromoted = true;
            if (promotion.CandidateOrdinal > 0)
                RememberPromotedCandidate(in connection, promotion.CandidateOrdinal);

            if (promotionKind == PromotionAdmissionKind.PrimaryReplacement)
            {
                _connectionChangedObserver?.Invoke();
            }
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

    private static PromotionAdmissionKind ResolvePromotionAdmissionKind(in CaptureConnectionPromotion promotion)
    {
        if (!CaptureConnectionGate.TryGetLockedConnection(out var primaryConnection) ||
            primaryConnection == promotion.Connection)
        {
            return PromotionAdmissionKind.PrimaryReplacement;
        }

        return promotion.CandidateOrdinal > 0
            ? PromotionAdmissionKind.Supplemental
            : PromotionAdmissionKind.PrimaryReplacement;
    }

    private bool DispatchConnectionClose(
        TcpConnection connection,
        long connectionGeneration,
        long connectionOrdinal)
    {
        var wasPrimary = CaptureConnectionGate.IsPrimaryConnection(in connection);
        if (!CaptureConnectionGate.TryClose(
                in connection,
                connectionGeneration,
                connectionOrdinal,
                out var closedConnection))
        {
            return false;
        }

        var closeTimestampMilliseconds = _hasTimelineTimestamp
            ? _lastTimelineTimestampMilliseconds
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        RawPacketDump.AppendTransportClose(
            in closedConnection,
            closeTimestampMilliseconds,
            connectionOrdinal);

        if (wasPrimary)
        {
            _connectionChangedObserver?.Invoke();
            if (CaptureConnectionGate.TryGetLockedConnection(out var survivingConnection))
            {
                DisposeStream(closedConnection);
                _lastActiveConnection = survivingConnection;
                _lastActiveGeneration = CaptureConnectionGate.TryGetActiveAdmission(
                    in survivingConnection,
                    out var survivingAdmission)
                        ? survivingAdmission.Generation
                        : _lastActiveGeneration;
                _hasLastActiveConnection = true;
            }
            else
            {
                DisposeAllStreams();
            }
        }
        else
        {
            DisposeStream(closedConnection);
        }
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

        var streamConnectionOrdinal = admission.ConnectionOrdinal > 0
            ? admission.ConnectionOrdinal
            : connectionOrdinal;
        var createdStream = false;
        if (!_tcpStreams.TryGetValue(connection, out var tcpStream) ||
            tcpStream.Generation != admission.Generation ||
            admission.ConnectionOrdinal > 0 &&
            tcpStream.ConnectionOrdinal != admission.ConnectionOrdinal)
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
                streamConnectionOrdinal,
                _mirrorDeduplicator);
            _tcpStreams[connection] = tcpStream;
            _nextFallbackTransportOrdinal = Math.Max(
                _nextFallbackTransportOrdinal,
                streamConnectionOrdinal);

            if (admission.Role == CaptureConnectionRole.Primary &&
                _hasLastActiveConnection &&
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
                tcpStream.Sink.MarkTransportStreamActivated(in source);
            }

            if (admission.Role == CaptureConnectionRole.Primary)
            {
                _lastActiveConnection = connection;
                _lastActiveGeneration = admission.Generation;
                _hasLastActiveConnection = true;
                DisposeOtherStreams(connection);
            }
        }

        var context = new DispatchContext(this, tcpStream, connection);
        tcpStream.Reassembler.Feed(
            packet.SequenceNumber,
            packet.Payload,
            new CapturedPacketTimestamp(packet.CaptureTimestampMilliseconds, packet.CaptureTimestamp),
            ref context,
            HandleReassembledChunk);
        tcpStream.ObserveInboundCapture(packet.CaptureOrdinal);
        if (_acknowledgments.TryGetLatest(out var acknowledgment) &&
            acknowledgment.Connection == connection &&
            acknowledgment.Generation == tcpStream.Generation &&
            acknowledgment.ConnectionOrdinal == tcpStream.ConnectionOrdinal &&
            tcpStream.HasObservedInboundAfter(acknowledgment.CaptureOrdinal) &&
            tcpStream.Reassembler.TryGetAcknowledgedGap(acknowledgment.AcknowledgmentNumber, out _))
        {
            PacketCaptureChannel.TryWriteAcknowledgmentAvailable();
        }

        if (createdStream)
        {
            if (admission.Role == CaptureConnectionRole.Primary)
            {
                _connectionLockedObserver?.Invoke(connection);
            }
        }

        return context.HasParsed;
    }

    private static void HandleReassembledChunk(uint sequenceNumber, ReadOnlySpan<byte> chunk, CapturedPacketTimestamp timestamp, ref DispatchContext context)
    {
        if (context.Stream.ProcessChunk(
                context.Dispatcher,
                context.Connection,
                sequenceNumber,
                chunk,
                timestamp))
        {
            context.HasParsed = true;
        }
    }

    private void RecoverAcknowledgedGaps(
        TcpCaptureStreamState stream,
        in TcpConnection connection,
        uint acknowledgmentNumber,
        ref DispatchContext context)
    {
        while (stream.Reassembler.TryGetAcknowledgedGap(
                   acknowledgmentNumber,
                   out var gap))
        {
            var transportOrdinal = AllocateTransportOrdinal();
            CaptureLog.Write(
                CaptureLogLevel.Warning,
                $"TCP capture gap confirmed by ACK: expected={gap.ExpectedSequence}, resume={gap.ResumeSequence}, ack={gap.AcknowledgmentNumber}, missing={gap.ByteCount}, pendingSegments={gap.PendingSegmentCount}, pendingBytes={gap.PendingByteCount}, transport={transportOrdinal}.");
            stream.BeginRecovery(transportOrdinal);
            stream.Reassembler.SkipGapAndDrain(
                in gap,
                ref context,
                HandleReassembledChunk);
        }
    }

    private bool DispatchAcknowledgmentAvailable()
    {
        var parsed = false;
        while (_acknowledgments.TryGetLatest(out var acknowledgment))
        {
            var connection = acknowledgment.Connection;
            if (_tcpStreams.TryGetValue(connection, out var stream) &&
                stream.Generation == acknowledgment.Generation &&
                stream.ConnectionOrdinal == acknowledgment.ConnectionOrdinal &&
                CaptureConnectionGate.TryGetActiveAdmission(
                    in connection,
                    acknowledgment.ConnectionOrdinal,
                    out var activeAdmission) &&
                activeAdmission.Generation == acknowledgment.Generation &&
                stream.HasObservedInboundAfter(acknowledgment.CaptureOrdinal))
            {
                var context = new DispatchContext(this, stream, connection);
                RecoverAcknowledgedGaps(
                    stream,
                    connection,
                    acknowledgment.AcknowledgmentNumber,
                    ref context);
                parsed |= context.HasParsed;
            }

            if (_acknowledgments.CompleteNotification(acknowledgment.Version))
            {
                return parsed;
            }
        }

        return parsed;
    }

    private long AllocateTransportOrdinal()
    {
        var ordinal = _transportOrdinalAllocator is null
            ? ++_nextFallbackTransportOrdinal
            : _transportOrdinalAllocator();
        if (ordinal <= 0)
        {
            throw new InvalidOperationException("TCP transport ordinal space was exhausted.");
        }

        _nextFallbackTransportOrdinal = Math.Max(_nextFallbackTransportOrdinal, ordinal);
        return ordinal;
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
        _lastPromotedCandidateOrdinals.Clear();
        _retiredCandidateOrdinals.Clear();
        _retiredCandidateOrder.Clear();
        _mirrorDeduplicator.Clear();
    }

    private void DisposeStream(TcpConnection connection)
    {
        _lastPromotedCandidateOrdinals.Remove(connection);
        _lastPromotedCandidateOrdinals.Remove(connection.Reverse());
        if (_tcpStreams.Remove(connection, out var stream))
        {
            stream.Dispose();
        }
    }

    private bool IsStalePromotion(in TcpConnection connection, long candidateOrdinal)
    {
        if (_lastPromotedCandidateOrdinals.TryGetValue(connection, out var directOrdinal))
            return directOrdinal >= candidateOrdinal;

        var reverse = connection.Reverse();
        if (_lastPromotedCandidateOrdinals.TryGetValue(reverse, out var reverseOrdinal))
            return reverseOrdinal >= candidateOrdinal;

        return TryGetRetiredCandidate(in connection, out var retired) &&
               retired.Value.ConnectionOrdinal >= candidateOrdinal;
    }

    private void RememberPromotedCandidate(in TcpConnection connection, long candidateOrdinal)
    {
        RemoveRetiredCandidate(in connection);
        if (_lastPromotedCandidateOrdinals.TryGetValue(connection, out var directOrdinal))
        {
            if (candidateOrdinal > directOrdinal)
                _lastPromotedCandidateOrdinals[connection] = candidateOrdinal;
            return;
        }

        var reverse = connection.Reverse();
        if (_lastPromotedCandidateOrdinals.TryGetValue(reverse, out var reverseOrdinal))
        {
            if (candidateOrdinal > reverseOrdinal)
                _lastPromotedCandidateOrdinals[reverse] = candidateOrdinal;
            return;
        }

        _lastPromotedCandidateOrdinals[connection] = candidateOrdinal;
    }

    private void RetireCandidateOrdinal(in TcpConnection connection, long candidateOrdinal)
    {
        if (TryGetRetiredCandidate(in connection, out var existing))
        {
            var retainedConnection = existing.Value.Connection;
            var retainedOrdinal = Math.Max(existing.Value.ConnectionOrdinal, candidateOrdinal);
            _retiredCandidateOrder.Remove(existing);
            var updated = _retiredCandidateOrder.AddLast(
                new RetiredCandidateOrdinal(retainedConnection, retainedOrdinal));
            _retiredCandidateOrdinals[retainedConnection] = updated;
            return;
        }

        var node = _retiredCandidateOrder.AddLast(new RetiredCandidateOrdinal(connection, candidateOrdinal));
        _retiredCandidateOrdinals.Add(connection, node);
        while (_retiredCandidateOrdinals.Count > CaptureBufferLimits.CandidateStreamCountLimit)
        {
            var oldest = _retiredCandidateOrder.First!;
            _retiredCandidateOrder.RemoveFirst();
            _retiredCandidateOrdinals.Remove(oldest.Value.Connection);
        }
    }

    private bool TryGetRetiredCandidate(
        in TcpConnection connection,
        out LinkedListNode<RetiredCandidateOrdinal> retired)
    {
        if (_retiredCandidateOrdinals.TryGetValue(connection, out retired!))
            return true;

        var reverse = connection.Reverse();
        return _retiredCandidateOrdinals.TryGetValue(reverse, out retired!);
    }

    private void RemoveRetiredCandidate(in TcpConnection connection)
    {
        if (!TryGetRetiredCandidate(in connection, out var retired))
            return;

        _retiredCandidateOrder.Remove(retired);
        _retiredCandidateOrdinals.Remove(retired.Value.Connection);
    }

    private struct DispatchContext(PacketCaptureDispatcher dispatcher, TcpCaptureStreamState stream, TcpConnection connection)
    {
        public readonly PacketCaptureDispatcher Dispatcher = dispatcher;
        public readonly TcpCaptureStreamState Stream = stream;
        public readonly TcpConnection Connection = connection;
        public bool HasParsed;
    }

    private readonly record struct RetiredCandidateOrdinal(
        TcpConnection Connection,
        long ConnectionOrdinal);

    private sealed class TcpCaptureStreamState(
        TcpStreamReassembler reassembler,
        PacketStreamProcessor processor,
        IRuntimeObservationSink sink,
        Action<ProtocolRoundTripObservation>? protocolRoundTripObserver,
        PacketFlushState flushState,
        PacketPlayerGroupState playerGroupState,
        long generation,
        long connectionOrdinal,
        CanonicalPacketMirrorDeduplicator mirrorDeduplicator) : IDisposable
    {
        private PacketStreamProcessor _processor = processor;
        private TcpWorldStreamRecoveryBuffer? _recovery;
        private long _transportOrdinal = connectionOrdinal;
        private long _lastInboundCaptureOrdinal;

        public long Generation { get; } = generation;

        public long ConnectionOrdinal { get; } = connectionOrdinal;

        public TcpStreamReassembler Reassembler { get; } = reassembler;

        public IRuntimeObservationSink Sink { get; } = sink;

        public void ObserveInboundCapture(long captureOrdinal)
        {
            _lastInboundCaptureOrdinal = Math.Max(_lastInboundCaptureOrdinal, captureOrdinal);
        }

        public bool HasObservedInboundAfter(long captureOrdinal) =>
            captureOrdinal <= 0 || _lastInboundCaptureOrdinal > captureOrdinal;

        public void BeginRecovery(long transportOrdinal)
        {
            if (_recovery is null)
            {
                _processor.Dispose();
            }

            _recovery?.Dispose();
            _recovery = new TcpWorldStreamRecoveryBuffer();
            _transportOrdinal = transportOrdinal;
        }

        public bool ProcessChunk(
            PacketCaptureDispatcher dispatcher,
            in TcpConnection connection,
            uint sequenceNumber,
            ReadOnlySpan<byte> chunk,
            CapturedPacketTimestamp timestamp)
        {
            if (_recovery is null)
            {
                return ProcessAcceptedChunk(
                    dispatcher,
                    in connection,
                    sequenceNumber,
                    chunk,
                    timestamp,
                    markTransportStreamActivated: false);
            }

            var recovery = _recovery;
            var result = recovery.Append(sequenceNumber, chunk, timestamp);
            if (result == TcpWorldStreamRecoveryResult.Pending)
            {
                return false;
            }

            if (result == TcpWorldStreamRecoveryResult.Rejected)
            {
                CaptureLog.Write(
                    CaptureLogLevel.Warning,
                    $"TCP capture gap recovery candidate rejected: transport={_transportOrdinal}.");
                recovery.Dispose();
                _recovery = new TcpWorldStreamRecoveryBuffer();
                return false;
            }

            _processor = new PacketStreamProcessor(
                Sink,
                protocolRoundTripObserver,
                PacketTransportFraming.Auto,
                0,
                flushState,
                playerGroupState,
                mirrorDeduplicator,
                ConnectionOrdinal);
            var replayConnection = connection;
            var activate = true;
            var parsed = recovery.Replay((replaySequence, replayPayload, replayTimestamp) =>
            {
                var segmentParsed = ProcessAcceptedChunk(
                    dispatcher,
                    in replayConnection,
                    replaySequence,
                    replayPayload,
                    replayTimestamp,
                    activate);
                activate = false;
                return segmentParsed;
            });
            recovery.Dispose();
            _recovery = null;
            CaptureLog.Write(
                CaptureLogLevel.Info,
                $"TCP capture gap recovery completed: transport={_transportOrdinal}.");
            return parsed;
        }

        public void Dispose()
        {
            _processor.Dispose();
            _recovery?.Dispose();
            Reassembler.Dispose();
        }

        private bool ProcessAcceptedChunk(
            PacketCaptureDispatcher dispatcher,
            in TcpConnection connection,
            uint sequenceNumber,
            ReadOnlySpan<byte> chunk,
            CapturedPacketTimestamp timestamp,
            bool markTransportStreamActivated)
        {
            var timelineTimestampMilliseconds = dispatcher.NormalizeTimelineTimestamp(timestamp.UnixMilliseconds);
            if (markTransportStreamActivated)
            {
                var source = new PacketObservationSource(
                    timelineTimestampMilliseconds,
                    0,
                    0,
                    chunk.Length,
                    sequenceNumber,
                    default);
                Sink.MarkTransportStreamActivated(in source);
            }

            RawPacketDump.AppendReassembled(
                "inbound",
                in connection,
                sequenceNumber,
                timelineTimestampMilliseconds,
                _transportOrdinal,
                chunk);
            var processingTimestamp = new PacketProcessingTimestamp(
                timelineTimestampMilliseconds,
                timestamp.MonotonicTimestamp);
            return _processor.AppendAndProcess(chunk, in connection, in processingTimestamp);
        }

        public static TcpCaptureStreamState Create(
            Func<IRuntimeObservationSink> sinkFactory,
            Action<ProtocolRoundTripObservation>? protocolRoundTripObserver,
            long generation,
            uint? initialSequenceNumber,
            long connectionOrdinal,
            CanonicalPacketMirrorDeduplicator mirrorDeduplicator)
        {
            var sink = sinkFactory();
            var flushState = new PacketFlushState();
            var playerGroupState = new PacketPlayerGroupState();
            var reassembler = new TcpStreamReassembler();
            if (initialSequenceNumber is { } sequenceNumber)
            {
                reassembler.StartAt(sequenceNumber);
            }

            return new TcpCaptureStreamState(
                reassembler,
                new PacketStreamProcessor(
                    sink,
                    protocolRoundTripObserver,
                    PacketTransportFraming.Auto,
                    0,
                    flushState,
                    playerGroupState,
                    mirrorDeduplicator,
                    connectionOrdinal),
                sink,
                protocolRoundTripObserver,
                flushState,
                playerGroupState,
                generation,
                connectionOrdinal,
                mirrorDeduplicator);
        }
    }
}
