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
    private readonly long _captureClockOriginTicks = Stopwatch.GetTimestamp();
    private readonly long _captureClockOriginUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private readonly ProcessPortDiscoveryService _processPortDiscoveryService = processPortDiscoveryService;
    private readonly SceneLiveReadModel _scene = new(RawPacketDump.CurrentSessionStarted);
    private Func<IRuntimeObservationSink> RuntimeSinkFactory { get => field ??= SceneSinkFactory.CreateForLive(_scene); }
    public PacketCaptureDispatcher Dispatcher { get => field ??= new(RuntimeSinkFactory, OnProtocolRoundTripObserved, OnCaptureConnectionLocked); }
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

                    var hasCloseFlag = (tcp.Flags & (TcpControlBits.FIN | TcpControlBits.RST)) != 0;
                    var payloadOffset = ipHeaderLen + tcpHeaderLen;
                    var payloadLength = packetSpan.Length - payloadOffset;
                    var captureTicks = address.Timestamp;
                    var admission = CaptureConnectionGate.EvaluatePacket(in connection, hasCloseFlag);
                    if (!admission.IsAccepted)
                    {
                        continue;
                    }

                    if (admission.ReleasedLock)
                    {
                        _protocolRttEstimator.Clear();
                    }

                    if (admission.RequiresProcessPortMatch &&
                        (payloadLength == 0 ||
                         hasCloseFlag ||
                         !_processPortDiscoveryService.AllPorts.Contains(dstPort)))
                    {
                        continue;
                    }

                    if (payloadLength == 0)
                        continue;

                    RawPacketDump.Append("inbound", srcPort, dstPort, tcp.HostSequenceNumber, tcp.HostAcknowledgmentNumber, captureTicks, packetSpan.Slice(payloadOffset, payloadLength));

                    var captureTimestampMilliseconds = _captureClockOriginUnixMilliseconds +
                        (long)((captureTicks - _captureClockOriginTicks) * 1000d / Stopwatch.Frequency);
                    var capturedPacket = CapturedPacket.CreateCopy(connection, admission, packetSpan.Slice(payloadOffset, payloadLength), tcp.HostSequenceNumber, captureTimestampMilliseconds);
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

    public async Task StopAsync()
    {
        if (_divert is null)
            return;

        _cts?.Cancel();
        CaptureConnectionGate.Unlock();
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
