using System.Buffers;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.PacketCapture.Streams;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Logging;
using Cloris.Aion2Flow.WinDivert;
using Cloris.Aion2Flow.WinDivert.Network;

namespace Cloris.Aion2Flow.PacketCapture.Capture;

public sealed class WinDivertCaptureService(
    CombatMetricsStore store,
    ProcessPortDiscoveryService processPortDiscoveryService) : IAsyncDisposable
{
    private WinDivertSession? _divert;
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private readonly TcpRoundTripEstimator _tcpRttEstimator = new();
    private readonly ProtocolRoundTripEstimator _protocolRttEstimator = new();

    private readonly ProcessPortDiscoveryService _processPortDiscoveryService = processPortDiscoveryService;
    public PacketCaptureDispatcher Dispatcher { get => field ??= new(store); }
    public bool IsDriverActive => _divert is not null;
    public bool HasDriverError { get; private set; }
    public double? CurrentRoundTripTimeMilliseconds
    {
        get
        {
            if (!CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection))
            {
                return null;
            }

            return lockedConnection.SourceIsLocal
                ? _protocolRttEstimator.CurrentMilliseconds
                : _tcpRttEstimator.CurrentMilliseconds;
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
        _tcpRttEstimator.Clear();
        _protocolRttEstimator.Clear();
        try
        {
            _cts = new CancellationTokenSource();
            _divert = new WinDivertSession("tcp", WinDivertLayer.Network, WinDivertFlags.Sniff | WinDivertFlags.ReceiveOnly);
            RawPacketDump.FrameEventObserved += OnFrameEventObserved;
            _worker = Task.Factory.StartNew(DivertCaptureWorker, TaskCreationOptions.LongRunning);

            _ = Dispatcher.StartAsync(_cts.Token).ConfigureAwait(false);
            PublishStatus("WinDivert capture started");
        }
        catch (Win32Exception ex)
        {
            await StopAsync();
            var message = $"WinDivert error: {ex.Message}";
            AppLog.Write(AppLogLevel.Error, message);
            PublishStatus(message, isError: true);
            throw;
        }
        catch (Exception ex)
        {
            await StopAsync();
            var message = $"Failed to start capture: {ex.Message}";
            AppLog.Write(AppLogLevel.Error, message);
            PublishStatus(message, isError: true);
            throw;
        }

        unsafe void DivertCaptureWorker()
        {
            var address = new WinDivertAddress();
            IMemoryOwner<byte>? bufferOwner = null;

            const int MaxPacketSize = 70 * 1024;

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    bufferOwner ??= MemoryPool<byte>.Shared.Rent(MaxPacketSize);

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

                    if (!CaptureConnectionGate.ShouldProcessPacket(in connection, tcp.Flags, out var isReversed))
                        continue;

                    var payloadOffset = ipHeaderLen + tcpHeaderLen;
                    var payloadLength = packetSpan.Length - payloadOffset;
                    var captureTicks = address.Timestamp;

                    var isLocal = isReversed ? connection.DestinationIsLocal : connection.SourceIsLocal;

                    if (CaptureConnectionGate.IsLocked)
                    {
                        if (isLocal)
                        {
                            _tcpRttEstimator.Clear();

                            if (isReversed)
                            {
                                TrackLocalProtocolOutboundPayload(_protocolRttEstimator, payloadLength, captureTicks);
                            }
                        }
                        else if (isReversed)
                        {
                            _tcpRttEstimator.TrackOutbound(tcp.HostSequenceNumber, payloadLength, captureTicks);
                        }
                        else if (_tcpRttEstimator.TryResolveInbound(tcp.HostAcknowledgmentNumber, captureTicks, out var smoothedRtt))
                        {
                            RttResolved?.Invoke(smoothedRtt);
                        }
                    }
                    else
                    {
                        _tcpRttEstimator.Clear();
                        _protocolRttEstimator.Clear();

                        if (!_processPortDiscoveryService.AllPorts.Contains(dstPort))
                            continue;
                    }

                    if (payloadLength == 0)
                        continue;

                    var direction = isReversed ? "outbound" : "inbound";
                    RawPacketDump.Append(direction, srcPort, dstPort, tcp.HostSequenceNumber, tcp.HostAcknowledgmentNumber, captureTicks, packetSpan.Slice(payloadOffset, payloadLength));

                    if (isReversed)
                    {
                        continue;
                    }

                    var capturedPacket = CapturedPacket.Create(connection, bufferOwner, payloadOffset, payloadLength, tcp.HostSequenceNumber);
                    bufferOwner = null;
                    if (!PacketCaptureChannel.TryWrite(capturedPacket))
                    {
                        capturedPacket.Return();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Win32Exception ex)
                {
                    PublishStatus($"WinDivert recv error: {ex.Message}", isError: true);
                    break;
                }
                catch (Exception ex)
                {
                    PublishStatus($"Capture loop error: {ex.Message}", isError: true);
                    break;
                }
            }

            bufferOwner?.Dispose();
        }
    }

    public async Task StopAsync()
    {
        if (_divert is null)
            return;

        _cts?.Cancel();
        CaptureConnectionGate.Unlock();
        _tcpRttEstimator.Clear();
        _protocolRttEstimator.Clear();
        RawPacketDump.FrameEventObserved -= OnFrameEventObserved;

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

        await Dispatcher.StopAsync().ConfigureAwait(false);

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

    private void OnFrameEventObserved(RawPacketDump.FrameEventObservation observation)
    {
        if (!CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection))
        {
            _protocolRttEstimator.Clear();
            return;
        }

        if (!lockedConnection.SourceIsLocal)
        {
            _protocolRttEstimator.Clear();
            return;
        }

        var observedConnection = observation.Connection;
        if (!lockedConnection.IsSameConnection(in observedConnection, out var isReversed))
        {
            return;
        }

        if (isReversed)
        {
            return;
        }

        if (_protocolRttEstimator.TryResolveInboundEvent(observation.EventName, observation.TimestampTicks, out var smoothedRtt))
        {
            RttResolved?.Invoke(smoothedRtt);
        }
    }

    internal static void TrackLocalProtocolOutboundPayload(ProtocolRoundTripEstimator estimator, int payloadLength, long timestamp)
    {
        if (payloadLength <= 0)
        {
            return;
        }

        estimator.TrackOutboundFrame(payloadLength, timestamp);
    }
}
