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
    private readonly PendingPromotionRegistry _pendingPromotions = new();
    private readonly CaptureTimestampMapper _captureTimestampMapper = new();
    private long _nextConnectionOrdinal;

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
        _pendingPromotions.CancelAll();
        PacketCaptureChannel.Drain();
        _candidateConnections.DiscardAll();
        _downstreamConnections.Clear();
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
                            _pendingPromotions.CancelForSupersededAttempt(in startedDownstream);
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
                            var closeCaptureMilliseconds = _captureTimestampMapper.ToTimelineUnixMilliseconds(captureTicks);
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

                        var hasQueuedPromotion = _pendingPromotions.TryGetForClose(
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
                        var closeQueued = false;
                        if (ShouldDispatchConnectionClose(
                                in connection,
                                admission,
                                closeConnectionOrdinal,
                                hasQueuedPromotion))
                        {
                            closeQueued = PacketCaptureChannel.WriteConnectionClose(
                                in connection,
                                closeGeneration,
                                closeConnectionOrdinal,
                                _cts.Token);
                        }
                        var reverseConnection = connection.Reverse();
                        if (closingPromotion is not null)
                        {
                            if (closeQueued)
                            {
                                _pendingPromotions.DetachAfterQueuedClose(in connection, closingPromotion);
                            }
                            else
                            {
                                _pendingPromotions.CancelAfterFailedClose(in connection, closingPromotion);
                            }
                        }
                        _pendingPromotions.CancelUnselectedForClose(
                            in connection,
                            closeConnectionOrdinal,
                            closingPromotion);
                        if (closeConnectionOrdinal > 0)
                        {
                            _candidateConnections.Reset(in connection, closeConnectionOrdinal);
                            _candidateConnections.Reset(in reverseConnection, closeConnectionOrdinal);
                            _downstreamConnections.Remove(in connection, closeConnectionOrdinal);
                        }
                        else
                        {
                            _candidateConnections.Reset(in connection);
                            _candidateConnections.Reset(in reverseConnection);
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

                    var captureTimestampMilliseconds = _captureTimestampMapper.ToTimelineUnixMilliseconds(captureTicks);
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
        var hasPendingPromotion = _pendingPromotions.TryGetForPayload(in connection, connectionOrdinal, out var pendingPromotion) &&
            pendingPromotion is not null;
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

        _pendingPromotions.Register(in connection, promotion);
        if (!PacketCaptureChannel.WritePromotion(promotion, cancellationToken))
        {
            _pendingPromotions.DetachPromotion(promotion);
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
        _pendingPromotions.CancelAll();

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

    private void OnPromotionCompleted(CaptureConnectionPromotion promotion, bool wasPromoted)
    {
        var connection = promotion.Connection;
        if (wasPromoted)
        {
            _downstreamConnections.MarkPromoted(in connection, promotion.CandidateOrdinal);
        }

        _pendingPromotions.DetachPromotion(promotion);
    }

    private void OnProtocolRoundTripObserved(ProtocolRoundTripObservation observation)
    {
        if (observation.ArrivalTimestamp <= 0)
        {
            return;
        }

        var observedConnection = observation.Connection;
        var hasLockedConnection = CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection);
        if (hasLockedConnection && lockedConnection != observedConnection)
        {
            return;
        }

        if (_protocolRttEstimator.TryObserveEcho(
            in observedConnection,
            observation.ClientSentUnixMilliseconds,
            _captureTimestampMapper.ToCurrentUtcUnixMilliseconds(observation.ArrivalTimestamp),
            observation.ArrivalTimestamp,
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
