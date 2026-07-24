using System.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Capture;

internal enum CandidatePacketDisposition : byte
{
    Discarded,
    Buffered,
    Confirmed
}

internal enum CandidateConnectionPriority : byte
{
    UnknownInbound,
    ObservedHandshake,
    KnownProcess,
    ProtocolEvidence
}

internal sealed class CaptureConnectionPromotion(
    TcpConnection connection,
    uint? replayStartSequenceNumber,
    long candidateOrdinal,
    List<CapturedPacket> packets)
{
    private enum DispatchState : byte
    {
        Queued,
        Dispatching,
        Cancelled
    }

    private List<CapturedPacket>? _packets = packets;
    private int _dispatchState = (int)DispatchState.Queued;

    public TcpConnection Connection { get; } = connection;
    public uint? ReplayStartSequenceNumber { get; } = replayStartSequenceNumber;
    public long CandidateOrdinal { get; } = candidateOrdinal;
    public IReadOnlyList<CapturedPacket> Packets => _packets ?? throw new ObjectDisposedException(nameof(CaptureConnectionPromotion));

    internal bool TryAcquireForDispatch() =>
        Interlocked.CompareExchange(
            ref _dispatchState,
            (int)DispatchState.Dispatching,
            (int)DispatchState.Queued) == (int)DispatchState.Queued;

    internal bool TryCancelQueued() =>
        Interlocked.CompareExchange(
            ref _dispatchState,
            (int)DispatchState.Cancelled,
            (int)DispatchState.Queued) == (int)DispatchState.Queued;

    public void Return()
    {
        var ownedPackets = Interlocked.Exchange(ref _packets, null);
        if (ownedPackets is null)
        {
            return;
        }

        foreach (var packet in ownedPackets)
        {
            packet.Return();
        }
    }
}

internal sealed class TcpWorldConnectionCandidateTracker : IDisposable
{
    private readonly Dictionary<TcpConnection, CandidateState> _candidates = [];
    private int _bufferedBytes;

    public bool Contains(in TcpConnection connection) => _candidates.ContainsKey(connection);

    public bool TryGetOrdinal(in TcpConnection connection, out long connectionOrdinal)
    {
        if (_candidates.TryGetValue(connection, out var candidate))
        {
            connectionOrdinal = candidate.Ordinal;
            return true;
        }

        connectionOrdinal = 0;
        return false;
    }

    public CandidatePacketDisposition Add(
        CapturedPacket packet,
        bool allowNewCandidate,
        bool allowMidstreamRecovery,
        uint? initialSequenceNumber,
        long connectionOrdinal,
        long observedTimestamp,
        out CaptureConnectionPromotion? promotion)
    {
        return Add(
            packet,
            allowNewCandidate,
            allowMidstreamRecovery,
            initialSequenceNumber,
            connectionOrdinal,
            CandidateConnectionPriority.UnknownInbound,
            observedTimestamp,
            out promotion);
    }

    public CandidatePacketDisposition Add(
        CapturedPacket packet,
        bool allowNewCandidate,
        bool allowMidstreamRecovery,
        uint? initialSequenceNumber,
        long connectionOrdinal,
        CandidateConnectionPriority priority,
        long observedTimestamp,
        out CaptureConnectionPromotion? promotion)
    {
        ArgumentNullException.ThrowIfNull(packet);
        promotion = null;
        Expire(observedTimestamp);

        var connection = packet.Connection;
        if (_candidates.TryGetValue(connection, out var existingCandidate) &&
            connectionOrdinal > 0 &&
            connectionOrdinal != existingCandidate.Ordinal)
        {
            if (connectionOrdinal < existingCandidate.Ordinal)
            {
                packet.Return();
                return CandidatePacketDisposition.Discarded;
            }

            RemoveAndDispose(connection);
        }

        if (!_candidates.TryGetValue(connection, out var candidate))
        {
            if (!allowNewCandidate)
            {
                packet.Return();
                return CandidatePacketDisposition.Discarded;
            }

            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(connectionOrdinal);
            if (!EnsureCandidateSlot(priority))
            {
                packet.Return();
                return CandidatePacketDisposition.Discarded;
            }

            candidate = new CandidateState(
                observedTimestamp,
                allowMidstreamRecovery,
                initialSequenceNumber,
                connectionOrdinal,
                priority);
            _candidates.Add(connection, candidate);
        }
        else
        {
            candidate.UpgradePriority(priority);
            if (allowMidstreamRecovery)
            {
                candidate.AllowMidstreamRecovery();
            }
        }

        var previousBufferedBytes = candidate.BufferedBytes;
        var classification = candidate.Add(packet, observedTimestamp);
        _bufferedBytes += candidate.BufferedBytes - previousBufferedBytes;
        if (classification == TcpWorldStreamClassification.Rejected)
        {
            RemoveAndDispose(connection);
            return CandidatePacketDisposition.Discarded;
        }

        EnforceTotalByteLimit(connection);
        if (!_candidates.TryGetValue(connection, out candidate))
        {
            return CandidatePacketDisposition.Discarded;
        }

        if (classification != TcpWorldStreamClassification.Confirmed)
        {
            return CandidatePacketDisposition.Buffered;
        }

        _candidates.Remove(connection);
        _bufferedBytes -= candidate.BufferedBytes;
        promotion = new CaptureConnectionPromotion(
            connection,
            candidate.ReplayStartSequenceNumber,
            candidate.Ordinal,
            candidate.DetachPackets());
        candidate.Dispose();
        return CandidatePacketDisposition.Confirmed;
    }

    public void Reset(in TcpConnection connection)
    {
        RemoveAndDispose(connection);
    }

    public void Reset(in TcpConnection connection, long expectedConnectionOrdinal)
    {
        if (_candidates.TryGetValue(connection, out var candidate) &&
            candidate.Ordinal == expectedConnectionOrdinal)
        {
            RemoveAndDispose(connection);
        }
    }

    public void DiscardAll()
    {
        foreach (var candidate in _candidates.Values)
        {
            candidate.Dispose();
        }

        _candidates.Clear();
        _bufferedBytes = 0;
    }

    public void Dispose()
    {
        DiscardAll();
    }

    private void Expire(long observedTimestamp)
    {
        List<TcpConnection>? expired = null;
        foreach (var (connection, candidate) in _candidates)
        {
            if (observedTimestamp >= candidate.FirstObservedTimestamp &&
                Stopwatch.GetElapsedTime(candidate.FirstObservedTimestamp, observedTimestamp) > CaptureBufferLimits.CandidateStreamLifetime)
            {
                expired ??= [];
                expired.Add(connection);
            }
        }

        if (expired is null)
        {
            return;
        }

        foreach (var connection in expired)
        {
            RemoveAndDispose(connection);
        }
    }

    private bool EnsureCandidateSlot(CandidateConnectionPriority incomingPriority)
    {
        if (_candidates.Count < CaptureBufferLimits.CandidateStreamCountLimit)
        {
            return true;
        }

        return TryEvictCandidate(incomingPriority, except: null);
    }

    private void EnforceTotalByteLimit(in TcpConnection currentConnection)
    {
        while (_bufferedBytes > CaptureBufferLimits.CandidateStreamsTotalByteLimit)
        {
            if (!_candidates.TryGetValue(currentConnection, out var currentCandidate))
            {
                return;
            }

            if (!TryEvictCandidate(currentCandidate.Priority, currentConnection))
            {
                RemoveAndDispose(currentConnection);
                return;
            }
        }
    }

    private bool TryEvictCandidate(
        CandidateConnectionPriority maximumPriority,
        TcpConnection? except)
    {
        var found = false;
        var selectedConnection = default(TcpConnection);
        var selectedPriority = CandidateConnectionPriority.ProtocolEvidence;
        var selectedTimestamp = long.MaxValue;
        foreach (var (connection, candidate) in _candidates)
        {
            if (except is { } retained && connection == retained)
            {
                continue;
            }

            if (candidate.Priority > maximumPriority)
            {
                continue;
            }

            if (!found ||
                candidate.Priority < selectedPriority ||
                (candidate.Priority == selectedPriority && candidate.FirstObservedTimestamp < selectedTimestamp))
            {
                found = true;
                selectedConnection = connection;
                selectedPriority = candidate.Priority;
                selectedTimestamp = candidate.FirstObservedTimestamp;
            }
        }

        if (found)
        {
            RemoveAndDispose(selectedConnection);
        }

        return found;
    }

    private void RemoveAndDispose(in TcpConnection connection)
    {
        if (!_candidates.Remove(connection, out var candidate))
        {
            return;
        }

        _bufferedBytes -= candidate.BufferedBytes;
        candidate.Dispose();
    }

    private sealed class CandidateState : IDisposable
    {
        private TcpStreamReassembler _reassembler = new();
        private TcpWorldStreamClassifier _classifier;
        private List<CapturedPacket>? _packets = [];
        private bool _allowMidstreamRecovery;
        private bool _isUsingInitialSequenceAnchor;
        private bool _hasReassembledBaseSequence;
        private uint _reassembledBaseSequence;
        private TcpWorldStreamClassification _classification;

        public long FirstObservedTimestamp { get; }
        public uint? ReplayStartSequenceNumber { get; private set; }
        public long Ordinal { get; }
        public CandidateConnectionPriority Priority { get; private set; }
        public int BufferedBytes { get; private set; }

        public CandidateState(
            long firstObservedTimestamp,
            bool allowMidstreamRecovery,
            uint? initialSequenceNumber,
            long ordinal,
            CandidateConnectionPriority priority)
        {
            FirstObservedTimestamp = firstObservedTimestamp;
            Ordinal = ordinal;
            Priority = priority;
            _isUsingInitialSequenceAnchor = initialSequenceNumber.HasValue;
            _allowMidstreamRecovery = allowMidstreamRecovery || !_isUsingInitialSequenceAnchor;
            _classifier = new TcpWorldStreamClassifier(_allowMidstreamRecovery);
            if (initialSequenceNumber is { } sequenceNumber)
            {
                _reassembler.StartAt(sequenceNumber);
            }
        }

        public void UpgradePriority(CandidateConnectionPriority priority)
        {
            if (priority > Priority)
            {
                Priority = priority;
            }
        }

        public void AllowMidstreamRecovery()
        {
            _allowMidstreamRecovery = true;
            _classifier.AllowMidstreamRecovery();
        }

        public TcpWorldStreamClassification Add(CapturedPacket packet, long observedTimestamp)
        {
            if (IsExactRetransmission(packet))
            {
                packet.Return();
                TryRecoverFromMissingAnchor(observedTimestamp);
                return _classification;
            }

            if (_packets!.Count >= CaptureBufferLimits.CandidateStreamSegmentLimit ||
                BufferedBytes + packet.Payload.Length > CaptureBufferLimits.CandidateStreamByteLimit)
            {
                packet.Return();
                return TcpWorldStreamClassification.Rejected;
            }

            _packets!.Add(packet);
            BufferedBytes += packet.Payload.Length;

            _classification = ShouldRebuildFrom(packet.SequenceNumber)
                ? RebuildFrom(packet.SequenceNumber)
                : Feed(packet);
            TryRecoverFromMissingAnchor(observedTimestamp);
            UpgradePriorityFromProtocolEvidence();

            return _classification;
        }

        private bool IsExactRetransmission(CapturedPacket packet)
        {
            foreach (var bufferedPacket in _packets!)
            {
                if (bufferedPacket.SequenceNumber == packet.SequenceNumber &&
                    bufferedPacket.Payload.SequenceEqual(packet.Payload))
                {
                    return true;
                }
            }

            return false;
        }

        private void TryRecoverFromMissingAnchor(long observedTimestamp)
        {
            if (_classification != TcpWorldStreamClassification.Pending ||
                !ShouldRecoverFromMissingAnchor(observedTimestamp))
            {
                return;
            }

            _isUsingInitialSequenceAnchor = false;
            _allowMidstreamRecovery = true;
            _classification = RebuildFrom(FindEarliestBufferedSequenceNumber());
            UpgradePriorityFromProtocolEvidence();
        }

        private void UpgradePriorityFromProtocolEvidence()
        {
            if (_classifier.HasProtocolEvidence)
            {
                UpgradePriority(CandidateConnectionPriority.ProtocolEvidence);
            }
        }

        private TcpWorldStreamClassification Feed(CapturedPacket packet)
        {
            var context = new ClassifierContext(this);
            _reassembler.Feed(
                packet.SequenceNumber,
                packet.Payload,
                new CapturedPacketTimestamp(packet.CaptureTimestampMilliseconds, packet.CaptureTimestamp),
                ref context,
                ClassifyReassembledChunk);
            return context.Classification;
        }

        private bool ShouldRebuildFrom(uint sequenceNumber) =>
            !_isUsingInitialSequenceAnchor &&
            _hasReassembledBaseSequence &&
            unchecked((int)(sequenceNumber - _reassembledBaseSequence)) < 0;

        private bool ShouldRecoverFromMissingAnchor(long observedTimestamp) =>
            _isUsingInitialSequenceAnchor &&
            !_hasReassembledBaseSequence &&
            observedTimestamp >= FirstObservedTimestamp &&
            Stopwatch.GetElapsedTime(FirstObservedTimestamp, observedTimestamp) >= CaptureBufferLimits.CandidateAnchorRecoveryDelay;

        private uint FindEarliestBufferedSequenceNumber()
        {
            var earliest = _packets![0].SequenceNumber;
            for (var index = 1; index < _packets.Count; index++)
            {
                var sequenceNumber = _packets[index].SequenceNumber;
                if (unchecked((int)(sequenceNumber - earliest)) < 0)
                {
                    earliest = sequenceNumber;
                }
            }

            return earliest;
        }

        private TcpWorldStreamClassification RebuildFrom(uint sequenceNumber)
        {
            _classifier.Dispose();
            _reassembler.Dispose();
            _classifier = new TcpWorldStreamClassifier(_allowMidstreamRecovery);
            _reassembler = new TcpStreamReassembler();
            _reassembler.StartAt(sequenceNumber);
            _hasReassembledBaseSequence = false;
            ReplayStartSequenceNumber = null;

            var context = new ClassifierContext(this);
            foreach (var bufferedPacket in _packets!)
            {
                _reassembler.Feed(
                    bufferedPacket.SequenceNumber,
                    bufferedPacket.Payload,
                    new CapturedPacketTimestamp(
                        bufferedPacket.CaptureTimestampMilliseconds,
                        bufferedPacket.CaptureTimestamp),
                    ref context,
                    ClassifyReassembledChunk);
            }

            return context.Classification;
        }

        public List<CapturedPacket> DetachPackets()
        {
            var packets = _packets!;
            _packets = null;
            BufferedBytes = 0;
            return packets;
        }

        public void Dispose()
        {
            _classifier.Dispose();
            _reassembler.Dispose();
            if (_packets is null)
            {
                return;
            }

            foreach (var packet in _packets)
            {
                packet.Return();
            }

            _packets = null;
            BufferedBytes = 0;
        }

        private static void ClassifyReassembledChunk(
            uint sequenceNumber,
            ReadOnlySpan<byte> chunk,
            CapturedPacketTimestamp timestamp,
            ref ClassifierContext context)
        {
            _ = sequenceNumber;
            context.Classification = context.Candidate.ClassifyChunk(
                sequenceNumber,
                chunk,
                timestamp.UnixMilliseconds);
        }

        private TcpWorldStreamClassification ClassifyChunk(
            uint sequenceNumber,
            ReadOnlySpan<byte> chunk,
            long captureTimestampMilliseconds)
        {
            if (!_hasReassembledBaseSequence)
            {
                _hasReassembledBaseSequence = true;
                _reassembledBaseSequence = sequenceNumber;
            }

            var classification = _classifier.Append(chunk, captureTimestampMilliseconds);
            if (classification == TcpWorldStreamClassification.Confirmed)
            {
                ReplayStartSequenceNumber = unchecked(
                    _reassembledBaseSequence + (uint)_classifier.ReplayStartByteOffset);
            }

            return classification;
        }

        private struct ClassifierContext(CandidateState candidate)
        {
            public readonly CandidateState Candidate = candidate;
            public TcpWorldStreamClassification Classification = TcpWorldStreamClassification.Pending;
        }
    }
}
