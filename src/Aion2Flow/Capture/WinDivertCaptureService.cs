using System.Buffers;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Logging;
using Cloris.Aion2Flow.WinDivert;
using Cloris.Aion2Flow.WinDivert.Network;

namespace Cloris.Aion2Flow.Capture;

public sealed class WinDivertCaptureService(ProcessPortDiscoveryService processPortDiscoveryService) : IAsyncDisposable
{
    private WinDivertSession? _divert;
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private readonly ProtocolRoundTripEstimator _protocolRttEstimator = new();
    private readonly TcpWorldConnectionCandidateTracker _candidateConnections = new();
    private readonly TcpDownstreamConnectionTracker _downstreamConnections = new();
    private readonly Lock _pendingPromotionGate = new();
    private readonly Dictionary<TcpConnection, CaptureConnectionPromotion> _pendingPromotions = [];
    private long _nextConnectionOrdinal;
    private readonly long _captureClockOriginTicks = Stopwatch.GetTimestamp();
    private readonly long _captureClockOriginUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private readonly ProcessPortDiscoveryService _processPortDiscoveryService = processPortDiscoveryService;
    private readonly SceneLiveReadModel _scene = new(RawPacketDump.CurrentSessionStarted);
    private Func<IRuntimeObservationSink> RuntimeSinkFactory { get => field ??= SceneSinkFactory.CreateForLive(_scene); }
    public PacketCaptureDispatcher Dispatcher
    {
        get => field ??= new(
            RuntimeSinkFactory,
            OnProtocolRoundTripObserved,
            OnCaptureConnectionLocked,
            _protocolRttEstimator.Clear,
            OnPromotionCompleted);
    }
    public SceneLiveReadModel Scene => _scene;
    public bool IsDriverActive => _divert is not null;
    public bool HasDriverError { get; private set; }
    public double? CurrentRoundTripTimeMilliseconds
    {
        get
        {
            if (!CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection))
            {
                _protocolRttEstimator.Clear();
                return null;
            }

            return _protocolRttEstimator.GetCurrentMilliseconds(in lockedConnection);
        }
    }
    public string LastStatusMessage { get; private set; } = string.Empty;

    public event Action<string>? StatusChanged;

    public event Action<double>? RttResolved;

    public async Task StartAsync()
    {
        if (_divert is not null)
            return;

        CaptureConnectionGate.Unlock();
        PacketCaptureChannel.Drain();
        _candidateConnections.DiscardAll();
        _downstreamConnections.Clear();
        CancelPendingPromotions();
        _protocolRttEstimator.Clear();
        try
        {
            _cts = new CancellationTokenSource();
            _divert = new WinDivertSession("tcp", WinDivertLayer.Network, WinDivertFlags.Sniff | WinDivertFlags.ReceiveOnly);
            _worker = Task.Factory.StartNew(DivertCaptureWorker, TaskCreationOptions.LongRunning);

            _ = Dispatcher.StartAsync(_cts.Token).ConfigureAwait(false);
            PublishStatus("WinDivert capture started");
        }
        catch (Win32Exception ex)
        {
            await StopAsync();
            var message = $"WinDivert error: {ex.Message}";
            AppLog.Write(AppLogLevel.Error, $"{message}{Environment.NewLine}{ex}");
            PublishStatus(message, isError: true);
            throw;
        }
        catch (Exception ex)
        {
            await StopAsync();
            var message = $"Failed to start capture: {ex.Message}";
            AppLog.Write(AppLogLevel.Error, $"{message}{Environment.NewLine}{ex}");
            PublishStatus(message, isError: true);
            throw;
        }

        unsafe void DivertCaptureWorker()
        {
            var address = new WinDivertAddress();
            IMemoryOwner<byte>? bufferOwner = null;

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    bufferOwner ??= MemoryPool<byte>.Shared.Rent(CaptureBufferLimits.WinDivertPacketBufferSize);

                    var length = _divert.Receive(bufferOwner.Memory.Span, ref address);

                    if (length <= 0)
                        continue;

                    if (address.IPv6)
                        continue;

                    var packetSpan = bufferOwner.Memory.Span[..length];

                    if (packetSpan.Length < sizeof(IPv4Header))
                        continue;

                    ref byte packetRef = ref MemoryMarshal.GetReference(packetSpan);

                    ref readonly IPv4Header ip = ref Unsafe.As<byte, IPv4Header>(ref packetRef);

                    if (ip.Version != 4 || ip.Protocol != IPv4Protocol.Tcp)
                        continue;

                    var ipHeaderLen = ip.HeaderLength;
                    if (ipHeaderLen < sizeof(IPv4Header) || packetSpan.Length < ipHeaderLen + sizeof(TcpHeader))
                        continue;

                    if (ip.IsFragmented)
                        continue;

                    ref readonly TcpHeader tcp = ref Unsafe.As<byte, TcpHeader>(ref Unsafe.Add(ref packetRef, ipHeaderLen));

                    var tcpHeaderLen = tcp.HeaderLength;
                    if (tcpHeaderLen < sizeof(TcpHeader) || packetSpan.Length < ipHeaderLen + tcpHeaderLen)
                        continue;

                    ushort dstPort = BinaryPrimitives.ReverseEndianness(tcp.DestinationPort);
                    ushort srcPort = BinaryPrimitives.ReverseEndianness(tcp.SourcePort);

                    var connection = new TcpConnection(ip.SourceAddress, ip.DestinationAddress, srcPort, dstPort);

                    var hasSynFlag = (tcp.Flags & TcpControlBits.SYN) != 0;
                    var hasAcknowledgmentFlag = (tcp.Flags & TcpControlBits.ACK) != 0;
                    var hasCloseFlag = (tcp.Flags & (TcpControlBits.FIN | TcpControlBits.RST)) != 0;
                    var payloadOffset = ipHeaderLen + tcpHeaderLen;
                    var payloadLength = packetSpan.Length - payloadOffset;
                    var captureTicks = address.Timestamp;
                    var observedTimestamp = Stopwatch.GetTimestamp();
                    var isKnownProcessPort = _processPortDiscoveryService.AllPorts.Contains(dstPort);

                    var startsNewConnection = false;
                    if (hasSynFlag)
                    {
                        startsNewConnection = _downstreamConnections.ObserveSyn(
                            in connection,
                            hasAcknowledgmentFlag,
                            acceptUnpairedAcknowledgment: !address.Outbound,
                            tcp.HostSequenceNumber,
                            tcp.HostAcknowledgmentNumber,
                            AllocateConnectionOrdinal(),
                            observedTimestamp);
                        var startedDownstream = hasAcknowledgmentFlag ? connection : connection.Reverse();
                        if (!hasAcknowledgmentFlag && startsNewConnection)
                        {
                            CaptureConnectionGate.ObserveConnectionStart(in startedDownstream);
                        }
                        if (startsNewConnection)
                        {
                            _candidateConnections.Reset(in startedDownstream);
                            RemovePendingPromotion(in startedDownstream);
                        }
                    }

                    var payloadSequenceNumber = ResolvePayloadSequenceNumber(tcp.HostSequenceNumber, hasSynFlag);
                    var transportResolution = _downstreamConnections.ResolvePacket(
                        in connection,
                        payloadSequenceNumber,
                        hasAcknowledgmentFlag,
                        tcp.HostAcknowledgmentNumber,
                        observedTimestamp);
                    var isExpectedDownstream = transportResolution.IsExpectedDownstream;
                    var initialSequenceNumber = transportResolution.InitialSequenceNumber;
                    var connectionOrdinal = transportResolution.ExpectedConnectionOrdinal;
                    var packetConnectionOrdinal = transportResolution.HasResolvedConnectionOrdinal
                        ? transportResolution.ResolvedConnectionOrdinal
                        : connectionOrdinal;
                    var packetIsExpectedDownstream = isExpectedDownstream &&
                        (!transportResolution.HasResolvedConnectionOrdinal ||
                         transportResolution.ResolvedConnectionOrdinal == connectionOrdinal);
                    var hasDownstreamStart = hasSynFlag &&
                        hasAcknowledgmentFlag &&
                        (isExpectedDownstream || (isKnownProcessPort && startsNewConnection));

                    var admission = CaptureConnectionGate.EvaluatePacket(
                        in connection,
                        hasDownstreamStart,
                        hasCloseFlag: false);

                    if (hasCloseFlag)
                    {
                        if (payloadLength != 0 && admission.IsAccepted)
                        {
                            var closePayload = packetSpan.Slice(payloadOffset, payloadLength);
                            var closeCaptureMilliseconds = _captureClockOriginUnixMilliseconds +
                                (long)((captureTicks - _captureClockOriginTicks) * 1000d / Stopwatch.Frequency);
                            RoutePayload(
                                in connection,
                                admission,
                                closePayload,
                                payloadSequenceNumber,
                                tcp.HostAcknowledgmentNumber,
                                closeCaptureMilliseconds,
                                 captureTicks,
                                 isInbound: !address.Outbound,
                                 packetIsExpectedDownstream,
                                 isKnownProcessPort,
                                 initialSequenceNumber,
                                 packetConnectionOrdinal,
                                 observedTimestamp,
                                 _cts.Token);
                        }

                        var hasQueuedPromotion = TryGetPendingPromotionForClose(
                            in connection,
                            packetConnectionOrdinal,
                            out var closingPromotion);
                        var closeConnectionOrdinal = ResolveCloseConnectionOrdinal(
                            in connection,
                            packetConnectionOrdinal,
                            hasQueuedPromotion ? closingPromotion!.CandidateOrdinal : 0);
                        var closeGeneration = admission.Kind == CapturePacketAdmissionKind.ActiveConnection
                            ? admission.Generation
                            : 0;
                        if (ShouldDispatchConnectionClose(
                                in connection,
                                admission,
                                closeConnectionOrdinal,
                                hasQueuedPromotion))
                        {
                            PacketCaptureChannel.WriteConnectionClose(
                                in connection,
                                closeGeneration,
                                closeConnectionOrdinal,
                                _cts.Token);
                        }
                        var reverseConnection = connection.Reverse();
                        if (closeConnectionOrdinal > 0)
                        {
                            _candidateConnections.Reset(in connection, closeConnectionOrdinal);
                            _candidateConnections.Reset(in reverseConnection, closeConnectionOrdinal);
                            RemovePendingPromotion(in connection, closeConnectionOrdinal);
                            RemovePendingPromotion(in reverseConnection, closeConnectionOrdinal);
                            _downstreamConnections.Remove(in connection, closeConnectionOrdinal);
                        }
                        else
                        {
                            _candidateConnections.Reset(in connection);
                            _candidateConnections.Reset(in reverseConnection);
                            RemovePendingPromotion(in connection);
                            RemovePendingPromotion(in reverseConnection);
                            _downstreamConnections.Remove(in connection);
                        }

                        continue;
                    }

                    if (!admission.IsAccepted)
                    {
                        continue;
                    }

                    if (payloadLength == 0)
                    {
                        continue;
                    }

                    var captureTimestampMilliseconds = _captureClockOriginUnixMilliseconds +
                        (long)((captureTicks - _captureClockOriginTicks) * 1000d / Stopwatch.Frequency);
                    RoutePayload(
                        in connection,
                        admission,
                        packetSpan.Slice(payloadOffset, payloadLength),
                        payloadSequenceNumber,
                        tcp.HostAcknowledgmentNumber,
                        captureTimestampMilliseconds,
                        captureTicks,
                        isInbound: !address.Outbound,
                        packetIsExpectedDownstream,
                        isKnownProcessPort,
                        initialSequenceNumber,
                        packetConnectionOrdinal,
                        observedTimestamp,
                        _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Win32Exception ex)
                {
                    var message = $"WinDivert recv error: {ex.Message}";
                    AppLog.Write(AppLogLevel.Error, $"{message}{Environment.NewLine}{ex}");
                    PublishStatus(message, isError: true);
                    break;
                }
                catch (Exception ex)
                {
                    var message = $"Capture loop error after {ex.GetType().Name}: {ex.Message}";
                    AppLog.Write(AppLogLevel.Error, $"{message}{Environment.NewLine}{ex}");
                    PublishStatus(message, isError: true);
                    break;
                }
            }

            bufferOwner?.Dispose();
        }
    }

    private void RoutePayload(
        in TcpConnection connection,
        CapturePacketAdmission admission,
        ReadOnlySpan<byte> payload,
        uint sequenceNumber,
        uint acknowledgmentNumber,
        long captureTimestampMilliseconds,
        long captureTimestamp,
        bool isInbound,
        bool isExpectedDownstream,
        bool isKnownProcessPort,
        uint? initialSequenceNumber,
        long connectionOrdinal,
        long observedTimestamp,
        CancellationToken cancellationToken)
    {
        var hasPendingPromotion = TryGetPendingPromotion(in connection, out var pendingPromotion) &&
            (connectionOrdinal <= 0 || pendingPromotion!.CandidateOrdinal == connectionOrdinal);
        var activeConnectionOrdinal = 0L;
        var hasActiveConnectionOrdinal = admission.Kind == CapturePacketAdmissionKind.ActiveConnection &&
            CaptureConnectionGate.TryGetActiveConnectionOrdinal(
                in connection,
                out activeConnectionOrdinal);
        if (admission.Kind == CapturePacketAdmissionKind.ActiveConnection &&
            connectionOrdinal > 0 &&
            hasActiveConnectionOrdinal &&
            activeConnectionOrdinal > 0 &&
            activeConnectionOrdinal != connectionOrdinal &&
            !isExpectedDownstream &&
            !hasPendingPromotion)
        {
            return;
        }

        if (admission.Kind == CapturePacketAdmissionKind.ActiveConnection)
        {
            admission = ResolveActivePayloadAdmission(
                in connection,
                admission,
                isExpectedDownstream,
                hasPendingPromotion,
                connectionOrdinal);
        }

        var hasBufferedCandidate = false;
        var bufferedCandidateOrdinal = 0L;
        if (admission.RequiresClassification && !hasPendingPromotion)
        {
            hasBufferedCandidate = _candidateConnections.TryGetOrdinal(
                in connection,
                out bufferedCandidateOrdinal);
            if (!ShouldTrackCandidate(
                    isInbound,
                    isExpectedDownstream,
                    isKnownProcessPort,
                    hasBufferedCandidate))
            {
                return;
            }
        }

        var capturedPacket = CapturedPacket.CreateCopy(
            connection,
            admission,
            payload,
            sequenceNumber,
            captureTimestampMilliseconds,
            acknowledgmentNumber,
            captureTimestamp);
        if (!admission.RequiresClassification)
        {
            if (!PacketCaptureChannel.WritePacket(capturedPacket, cancellationToken))
            {
                capturedPacket.Return();
            }

            return;
        }

        if (hasPendingPromotion)
        {
            if (!PacketCaptureChannel.WriteCandidateContinuation(
                    capturedPacket,
                    pendingPromotion!.CandidateOrdinal,
                    cancellationToken))
            {
                capturedPacket.Return();
            }

            return;
        }

        var disposition = _candidateConnections.Add(
            capturedPacket,
            allowNewCandidate: true,
            allowMidstreamRecovery: !isExpectedDownstream,
            initialSequenceNumber: isExpectedDownstream ? initialSequenceNumber : null,
            connectionOrdinal: connectionOrdinal > 0
                ? connectionOrdinal
                : hasBufferedCandidate
                    ? bufferedCandidateOrdinal
                    : AllocateConnectionOrdinal(),
            priority: ResolveCandidatePriority(isExpectedDownstream, isKnownProcessPort),
            observedTimestamp: observedTimestamp,
            out var promotion);
        if (disposition != CandidatePacketDisposition.Confirmed || promotion is null)
        {
            return;
        }

        SetPendingPromotion(in connection, promotion);
        if (!PacketCaptureChannel.WritePromotion(promotion, cancellationToken))
        {
            RemovePendingPromotion(in connection, promotion);
            promotion.Return();
        }
    }

    internal static uint ResolvePayloadSequenceNumber(uint sequenceNumber, bool hasSynFlag) =>
        hasSynFlag ? unchecked(sequenceNumber + 1) : sequenceNumber;

    internal static CandidateConnectionPriority ResolveCandidatePriority(
        bool isExpectedDownstream,
        bool isKnownProcessPort) =>
        isKnownProcessPort
            ? CandidateConnectionPriority.KnownProcess
            : isExpectedDownstream
                ? CandidateConnectionPriority.ObservedHandshake
                : CandidateConnectionPriority.UnknownInbound;

    internal static bool ShouldClassifyActivePayload(
        bool isExpectedDownstream,
        bool hasPendingPromotion,
        bool hasCurrentActiveAdmission) =>
        hasPendingPromotion || (isExpectedDownstream && !hasCurrentActiveAdmission);

    internal static CapturePacketAdmission ResolveActivePayloadAdmission(
        in TcpConnection connection,
        CapturePacketAdmission admission,
        bool isExpectedDownstream,
        bool hasPendingPromotion,
        long connectionOrdinal)
    {
        if (admission.Kind != CapturePacketAdmissionKind.ActiveConnection)
        {
            return admission;
        }

        var currentActiveAdmission = default(CapturePacketAdmission);
        var hasCurrentActiveAdmission = !hasPendingPromotion &&
            connectionOrdinal > 0 &&
            CaptureConnectionGate.TryGetActiveAdmission(
                in connection,
                connectionOrdinal,
                out currentActiveAdmission);
        if (hasCurrentActiveAdmission)
        {
            // Promotion may have completed after the transport snapshot was read.
            // Refresh the generation so this packet is not rejected as stale.
            return currentActiveAdmission;
        }

        return ShouldClassifyActivePayload(
            isExpectedDownstream,
            hasPendingPromotion,
            hasCurrentActiveAdmission)
                ? new CapturePacketAdmission(
                    CapturePacketAdmissionKind.Candidate,
                    admission.Generation,
                    ReleasedLock: false)
                : admission;
    }

    internal static bool ShouldTrackCandidate(
        bool isInbound,
        bool isExpectedDownstream,
        bool isKnownProcessPort,
        bool hasBufferedCandidate) =>
        isInbound || isExpectedDownstream || isKnownProcessPort || hasBufferedCandidate;

    internal static bool ShouldDispatchConnectionClose(
        in TcpConnection connection,
        in CapturePacketAdmission admission,
        long connectionOrdinal,
        bool hasQueuedPromotion = false)
    {
        if (admission.Kind == CapturePacketAdmissionKind.ActiveConnection || hasQueuedPromotion)
        {
            return true;
        }

        return connectionOrdinal > 0 &&
               CaptureConnectionGate.TryGetActiveConnectionOrdinal(
                   in connection,
                   out var activeConnectionOrdinal) &&
               activeConnectionOrdinal == connectionOrdinal;
    }

    internal static long ResolveCloseConnectionOrdinal(
        in TcpConnection connection,
        long packetConnectionOrdinal,
        long queuedPromotionOrdinal)
    {
        if (packetConnectionOrdinal > 0)
        {
            return packetConnectionOrdinal;
        }

        if (queuedPromotionOrdinal > 0)
        {
            return queuedPromotionOrdinal;
        }

        return CaptureConnectionGate.TryGetActiveConnectionOrdinal(
            in connection,
            out var activeConnectionOrdinal)
                ? activeConnectionOrdinal
                : 0;
    }

    internal static CaptureConnectionPromotion? SelectPendingPromotionForClose(
        long packetConnectionOrdinal,
        CaptureConnectionPromotion? directPromotion,
        CaptureConnectionPromotion? reversePromotion)
    {
        if (directPromotion is not null &&
            (packetConnectionOrdinal <= 0 ||
             directPromotion.CandidateOrdinal == packetConnectionOrdinal))
        {
            return directPromotion;
        }

        if (reversePromotion is not null &&
            (packetConnectionOrdinal <= 0 ||
             reversePromotion.CandidateOrdinal == packetConnectionOrdinal))
        {
            return reversePromotion;
        }

        return null;
    }

    private long AllocateConnectionOrdinal()
    {
        var ordinal = Interlocked.Increment(ref _nextConnectionOrdinal);
        if (ordinal <= 0)
        {
            throw new InvalidOperationException("TCP connection ordinal space was exhausted.");
        }

        return ordinal;
    }

    public async Task StopAsync()
    {
        if (_divert is null)
            return;

        _cts?.Cancel();
        _protocolRttEstimator.Clear();
        _divert.ShutdownReceive();

        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
            }
        }

        _candidateConnections.DiscardAll();
        _downstreamConnections.Clear();
        CancelPendingPromotions();

        await Dispatcher.StopAsync().ConfigureAwait(false);
        PacketCaptureChannel.Drain();
        CaptureConnectionGate.Unlock();

        try
        {
            _divert.Dispose();
        }
        catch (Win32Exception)
        {
        }


        _worker = null;
        _cts = null;
        _divert = null;
        PublishStatus("WinDivert capture stopped.");
    }


    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private void PublishStatus(string message, bool isError = false)
    {
        LastStatusMessage = message;
        HasDriverError = isError;
        StatusChanged?.Invoke(message);
    }

    private bool TryGetPendingPromotion(
        in TcpConnection connection,
        out CaptureConnectionPromotion? promotion)
    {
        lock (_pendingPromotionGate)
        {
            return _pendingPromotions.TryGetValue(connection, out promotion);
        }
    }

    private bool TryGetPendingPromotionForClose(
        in TcpConnection connection,
        long packetConnectionOrdinal,
        out CaptureConnectionPromotion? promotion)
    {
        lock (_pendingPromotionGate)
        {
            _pendingPromotions.TryGetValue(connection, out var directPromotion);
            _pendingPromotions.TryGetValue(connection.Reverse(), out var reversePromotion);
            promotion = SelectPendingPromotionForClose(
                packetConnectionOrdinal,
                directPromotion,
                reversePromotion);
            return promotion is not null;
        }
    }

    private void SetPendingPromotion(in TcpConnection connection, CaptureConnectionPromotion promotion)
    {
        lock (_pendingPromotionGate)
        {
            _pendingPromotions[connection] = promotion;
        }
    }

    private void RemovePendingPromotion(in TcpConnection connection)
    {
        lock (_pendingPromotionGate)
        {
            _pendingPromotions.Remove(connection);
        }
    }

    private void RemovePendingPromotion(in TcpConnection connection, long expectedConnectionOrdinal)
    {
        lock (_pendingPromotionGate)
        {
            if (_pendingPromotions.TryGetValue(connection, out var promotion) &&
                promotion.CandidateOrdinal == expectedConnectionOrdinal)
            {
                _pendingPromotions.Remove(connection);
            }
        }
    }

    private void RemovePendingPromotion(
        in TcpConnection connection,
        CaptureConnectionPromotion expectedPromotion)
    {
        lock (_pendingPromotionGate)
        {
            if (_pendingPromotions.TryGetValue(connection, out var currentPromotion) &&
                ReferenceEquals(currentPromotion, expectedPromotion))
            {
                _pendingPromotions.Remove(connection);
            }
        }
    }

    private void OnPromotionCompleted(CaptureConnectionPromotion promotion, bool wasPromoted)
    {
        var connection = promotion.Connection;
        if (wasPromoted)
        {
            _downstreamConnections.MarkPromoted(in connection, promotion.CandidateOrdinal);
        }

        RemovePendingPromotion(in connection, promotion);
    }

    private void CancelPendingPromotions()
    {
        lock (_pendingPromotionGate)
        {
            foreach (var promotion in _pendingPromotions.Values)
            {
                promotion.Cancel();
            }

            _pendingPromotions.Clear();
        }
    }

    private void OnProtocolRoundTripObserved(ProtocolRoundTripObservation observation)
    {
        var observedConnection = observation.Connection;
        var hasLockedConnection = CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection);
        if (hasLockedConnection && lockedConnection != observedConnection)
        {
            return;
        }

        if (_protocolRttEstimator.TryObserveEcho(
            in observedConnection,
            observation.ClientSentUnixMilliseconds,
            observation.ArrivalUnixMilliseconds,
            out var roundTripMilliseconds))
        {
            if (hasLockedConnection)
            {
                RttResolved?.Invoke(roundTripMilliseconds);
            }
        }
    }

    private void OnCaptureConnectionLocked(TcpConnection connection)
    {
        if (CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection) &&
            lockedConnection == connection &&
            _protocolRttEstimator.GetCurrentMilliseconds(in connection) is { } roundTripMilliseconds)
        {
            RttResolved?.Invoke(roundTripMilliseconds);
        }
    }
}
