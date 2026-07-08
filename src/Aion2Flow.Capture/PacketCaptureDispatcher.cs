using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Capture;

public sealed class PacketCaptureDispatcher(Func<IRuntimeObservationSink> sinkFactory)
{
    private readonly Dictionary<TcpConnection, TcpCaptureStreamState> _tcpStreams = [];
    private TcpConnection _lastParsedConnection;
    private bool _hasLastParsedConnection;
    private Task? _worker;
    private CancellationTokenSource? _cts;

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
        if (!_tcpStreams.TryGetValue(connection, out var tcpStream))
        {
            tcpStream = TcpCaptureStreamState.Create(sinkFactory);
            _tcpStreams[connection] = tcpStream;
        }

        var context = new DispatchContext(tcpStream, connection);
        tcpStream.Reassembler.Feed(packet.SequenceNumber, packet.Payload, packet.CaptureTimestampMilliseconds, ref context, HandleReassembledChunk);

        if (context.HasParsed && ShouldLockParsedConnection(in connection))
        {
            if (_hasLastParsedConnection && !_lastParsedConnection.IsSameConnection(in connection, out _))
            {
                var source = new PacketObservationSource(context.LastParsedTimestampMilliseconds, 0, 0, 0, packet.SequenceNumber, default);
                tcpStream.Sink.MarkSceneTransportBoundary(in source);
            }

            _lastParsedConnection = connection;
            _hasLastParsedConnection = true;
            CaptureConnectionGate.LockOn(connection);
            DisposeOtherStreams(connection);
        }

        return context.HasParsed;
    }

    private static bool ShouldLockParsedConnection(in TcpConnection connection)
    {
        if (!CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection))
            return true;

        return !lockedConnection.IsSameConnection(in connection, out _);
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

        public static TcpCaptureStreamState Create(Func<IRuntimeObservationSink> sinkFactory)
        {
            var sink = sinkFactory();
            return new TcpCaptureStreamState(
                new TcpStreamReassembler(),
                new PacketStreamProcessor(sink),
                sink);
        }
    }
}
