using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.PacketCapture.Streams;
using Cloris.Aion2Flow.Services.Logging;

namespace Cloris.Aion2Flow.PacketCapture.Capture;

public sealed class PacketCaptureDispatcher(CombatMetricsStore store)
{
    private readonly Dictionary<TcpConnection, TcpCaptureStreamState> _tcpStreams = [];

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
                            AppLog.Write(AppLogLevel.Error, $"Packet dispatch failed: {ex.Message}");
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
        if (!_tcpStreams.TryGetValue(packet.Connection, out var tcpStream))
        {
            tcpStream = TcpCaptureStreamState.Create(store);
            _tcpStreams[packet.Connection] = tcpStream;
        }

        var context = new DispatchContext(tcpStream, packet.Connection);
        tcpStream.Reassembler.Feed(packet.SequenceNumber, packet.Payload, ref context, HandleReassembledChunk);

        if (context.HasParsed && !CaptureConnectionGate.IsLocked)
        {
            CaptureConnectionGate.LockOn(packet.Connection);
            DisposeOtherStreams(packet.Connection);
        }

        return context.HasParsed;
    }

    private static void HandleReassembledChunk(uint sequenceNumber, ReadOnlySpan<byte> chunk, ref DispatchContext context)
    {
        RawPacketDump.AppendReassembled("inbound", context.Connection, sequenceNumber, chunk);

        context.HasParsed |= context.Stream.Processor.AppendAndProcess(chunk, context.Connection);
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
    }

    private sealed class TcpCaptureStreamState(TcpStreamReassembler reassembler, PacketStreamProcessor processor) : IDisposable
    {
        public TcpStreamReassembler Reassembler { get; } = reassembler;

        public PacketStreamProcessor Processor { get; } = processor;

        public void Dispose()
        {
            Processor.Dispose();
            Reassembler.Dispose();
        }

        public static TcpCaptureStreamState Create(CombatMetricsStore store)
        {
            return new TcpCaptureStreamState(
                new TcpStreamReassembler(),
                new PacketStreamProcessor(store));
        }
    }
}
