using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Capture;

public sealed class PacketCaptureDispatcher
{
    private readonly Func<IRuntimeObservationSink> _sinkFactory;
    private readonly Action<ProtocolRoundTripObservation>? _protocolRoundTripObserver;
    private readonly Action<TcpConnection>? _connectionLockedObserver;
    private readonly Dictionary<TcpConnection, TcpCaptureStreamState> _tcpStreams = [];
    private TcpConnection _lastParsedConnection;
    private bool _hasLastParsedConnection;
    private Task? _worker;
    private CancellationTokenSource? _cts;

    public PacketCaptureDispatcher(Func<IRuntimeObservationSink> sinkFactory)
        : this(sinkFactory, null, null)
    {
    }

    internal PacketCaptureDispatcher(
        Func<IRuntimeObservationSink> sinkFactory,
        Action<ProtocolRoundTripObservation>? protocolRoundTripObserver,
        Action<TcpConnection>? connectionLockedObserver)
    {
        ArgumentNullException.ThrowIfNull(sinkFactory);
        _sinkFactory = sinkFactory;
        _protocolRoundTripObserver = protocolRoundTripObserver;
        _connectionLockedObserver = connectionLockedObserver;
    }

    public async Task StartAsync(CancellationToken token)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        _worker = Task.Factory.StartNew(async () =>
        {
            try
            {
                await foreach (var packet in PacketCaptureChannel.ReadAllAsync(_cts.Token))
                {
                    try
                    {
                        try
                        {
                            DispatchCapturedPacket(packet);
                        }
                        catch (Exception ex)
                        {
                            CaptureLog.Write(CaptureLogLevel.Error, $"Packet dispatch failed: {ex.Message}");
                        }
                    }
                    finally
                    {
                        try { packet.Return(); } catch (Exception) { }
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

    internal bool DispatchCapturedPacket(CapturedPacket packet)
    {
        var connection = packet.Connection;
        var admission = packet.Admission;
        if (!CaptureConnectionGate.IsAdmissionCurrent(in connection, in admission))
        {
            return false;
        }

        if (!_tcpStreams.TryGetValue(connection, out var tcpStream))
        {
            tcpStream = TcpCaptureStreamState.Create(_sinkFactory, _protocolRoundTripObserver);
            _tcpStreams[connection] = tcpStream;
        }

        var context = new DispatchContext(tcpStream, connection);
        tcpStream.Reassembler.Feed(packet.SequenceNumber, packet.Payload, packet.CaptureTimestampMilliseconds, ref context, HandleReassembledChunk);

        if (context.HasParsed &&
            CaptureConnectionGate.TryLock(in connection, admission.Generation, out var acquired) &&
            acquired)
        {
            if (_hasLastParsedConnection && _lastParsedConnection != connection)
            {
                var source = new PacketObservationSource(context.LastParsedTimestampMilliseconds, 0, 0, 0, packet.SequenceNumber, default);
                tcpStream.Sink.MarkSceneTransportBoundary(in source);
            }

            _lastParsedConnection = connection;
            _hasLastParsedConnection = true;
            DisposeOtherStreams(connection);
            _connectionLockedObserver?.Invoke(connection);
        }

        return context.HasParsed;
    }

    private static void HandleReassembledChunk(uint sequenceNumber, ReadOnlySpan<byte> chunk, long captureTimestampMilliseconds, ref DispatchContext context)
    {
        var result = context.Stream.ClassifyReassembledChunk(chunk);
        if (result.Kind != TcpConnectionStartKind.Game)
        {
            return;
        }

        try
        {
            var acceptedChunk = result.ResolveAcceptedPayload(chunk);
            RawPacketDump.AppendReassembled("inbound", context.Connection, sequenceNumber, captureTimestampMilliseconds, acceptedChunk);

            if (context.Stream.Processor.AppendAndProcess(acceptedChunk, context.Connection, captureTimestampMilliseconds))
            {
                context.Stream.MarkGameStream();
                context.HasParsed = true;
                context.LastParsedTimestampMilliseconds = captureTimestampMilliseconds;
            }
        }
        finally
        {
            result.Return();
        }
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
        _hasLastParsedConnection = false;
        _lastParsedConnection = default;
    }

    private void DisposeStream(TcpConnection connection)
    {
        if (_tcpStreams.Remove(connection, out var stream))
        {
            stream.Dispose();
        }
    }

    private struct DispatchContext(TcpCaptureStreamState stream, TcpConnection connection)
    {
        public readonly TcpCaptureStreamState Stream = stream;
        public readonly TcpConnection Connection = connection;
        public bool HasParsed;
        public long LastParsedTimestampMilliseconds;
    }

    private sealed class TcpCaptureStreamState(TcpStreamReassembler reassembler, PacketStreamProcessor processor, IRuntimeObservationSink sink) : IDisposable
    {
        private readonly TcpConnectionStartClassifier _classifier = new();

        public TcpStreamReassembler Reassembler { get; } = reassembler;

        public PacketStreamProcessor Processor { get; } = processor;

        public IRuntimeObservationSink Sink { get; } = sink;

        public TcpConnectionStartResult ClassifyReassembledChunk(ReadOnlySpan<byte> chunk) => _classifier.Classify(chunk);

        public void MarkGameStream()
        {
            _classifier.MarkGameStream();
        }

        public void Dispose()
        {
            Processor.Dispose();
            Reassembler.Dispose();
        }

        public static TcpCaptureStreamState Create(
            Func<IRuntimeObservationSink> sinkFactory,
            Action<ProtocolRoundTripObservation>? protocolRoundTripObserver)
        {
            var sink = sinkFactory();
            return new TcpCaptureStreamState(
                new TcpStreamReassembler(),
                new PacketStreamProcessor(sink, protocolRoundTripObserver),
                sink);
        }
    }
}
