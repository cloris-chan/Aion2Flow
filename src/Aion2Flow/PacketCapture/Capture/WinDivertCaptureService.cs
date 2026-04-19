using System.Buffers;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Divert.Interop;
using Cloris.Aion2Flow.Divert.Network;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.PacketCapture.Streams;
using Cloris.Aion2Flow.Services;

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

            return lockedConnection.IsLocalNetwork
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
            PublishStatus($"WinDivert error: {ex.Message}", isError: true);
            throw;
        }
        catch (Exception ex)
        {
            await StopAsync();
            PublishStatus($"Failed to start capture: {ex.Message}", isError: true);
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
                    var trackedPorts = _processPortDiscoveryService.AllPorts.AsSpan();

                    var connection = new TcpConnection(ip.SourceAddress, ip.DestinationAddress, srcPort, dstPort);

                    if (!CaptureConnectionGate.ShouldProcessPacket(in connection, tcp.Flags, out var isReversed))
                        continue;

                    var payloadOffset = ipHeaderLen + tcpHeaderLen;
                    var payloadLength = packetSpan.Length - payloadOffset;
                    var captureTicks = address.Timestamp;
                    var isLocked = CaptureConnectionGate.IsLocked;
                    bool isOutbound;

                    if (isLocked)
                    {
                        isOutbound = isReversed;
                    }
                    else if (ContainsTrackedPort(trackedPorts, srcPort))
                    {
                        isOutbound = true;
                    }
                    else if (ContainsTrackedPort(trackedPorts, dstPort))
                    {
                        isOutbound = false;
                    }
                    else
                    {
                        continue;
                    }

                    if (isLocked)
                    {
                        var isLocalLocked = CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection) &&
                            lockedConnection.IsLocalNetwork;

                        if (isLocalLocked)
                        {
                            _tcpRttEstimator.Clear();
                        }
                        else if (isOutbound)
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
                    }

                    if (payloadLength == 0)
                        continue;

                    var direction = isOutbound ? "outbound" : "inbound";
                    RawPacketDump.Append(direction, srcPort, dstPort, tcp.HostSequenceNumber, tcp.HostAcknowledgmentNumber, captureTicks, packetSpan.Slice(payloadOffset, payloadLength));

                    var capturedPacket = CapturedPacket.Create(connection, bufferOwner, payloadOffset, payloadLength, tcp.HostSequenceNumber, tcp.HostAcknowledgmentNumber, isOutbound);
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

    private static bool ContainsTrackedPort(ReadOnlySpan<ushort> sortedPorts, ushort port)
    {
        var low = 0;
        var high = sortedPorts.Length - 1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var candidate = sortedPorts[mid];
            if (candidate == port)
            {
                return true;
            }

            if (candidate < port)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return false;
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

        if (!lockedConnection.IsLocalNetwork)
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
            if (observation.EventName == "frame-batch" &&
                TryExtractFrameLength(observation.Detail, out var frameLength))
            {
                _protocolRttEstimator.TrackOutboundFrame(frameLength, observation.TimestampTicks);
            }

            return;
        }

        if (_protocolRttEstimator.TryResolveInboundEvent(observation.EventName, observation.TimestampTicks, out var smoothedRtt))
        {
            RttResolved?.Invoke(smoothedRtt);
        }
    }

    private static bool TryExtractFrameLength(string detail, out int frameLength)
    {
        frameLength = 0;
        const string pattern = "frameLength=";
        var start = detail.IndexOf(pattern, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        start += pattern.Length;
        var end = detail.IndexOf('|', start);
        var value = end >= 0 ? detail[start..end] : detail[start..];
        return int.TryParse(value, out frameLength);
    }
}
