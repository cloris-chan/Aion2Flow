using System.Text;
using System.Globalization;
using System.Diagnostics;
using Cloris.Aion2Flow.PacketCapture.Streams;

namespace Cloris.Aion2Flow.PacketCapture.Diagnostics;

internal static class RawPacketDump
{
#if DEBUG
    private static bool IsEnabled => true;
#else
    private static bool IsEnabled => false;
#endif
    private static readonly Lock SyncRoot = new();
    private static readonly string _logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
    private static string _rawLogPath = string.Empty;
    private static string _streamLogPath = string.Empty;
    private static string _frameLogPath = string.Empty;
    private static StreamWriter? _rawWriter;
    private static StreamWriter? _streamWriter;
    private static StreamWriter? _frameWriter;

    public static event Action<FrameEventObservation>? FrameEventObserved;

    static RawPacketDump()
    {
        if (IsEnabled)
        {
            RotateLogs();
        }
    }

    public static string RawLogPath => _rawLogPath;
    public static string StreamLogPath => _streamLogPath;
    public static string FrameLogPath => _frameLogPath;

    public static void RotateLogs()
    {
        if (!IsEnabled)
        {
            return;
        }

        lock (SyncRoot)
        {
            DisposeWriter(ref _rawWriter);
            DisposeWriter(ref _streamWriter);
            DisposeWriter(ref _frameWriter);

            Directory.CreateDirectory(_logDirectory);
            var stamp = DateTimeOffset.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            _rawLogPath = Path.Combine(_logDirectory, $"aion2flow.raw.{stamp}.log");
            _streamLogPath = Path.Combine(_logDirectory, $"aion2flow.stream.{stamp}.log");
            _frameLogPath = Path.Combine(_logDirectory, $"aion2flow.frame.{stamp}.log");

            _rawWriter = CreateWriter(_rawLogPath);
            _streamWriter = CreateWriter(_streamLogPath);
            _frameWriter = CreateWriter(_frameLogPath);
        }
    }

    public static void Append(string direction, ushort srcPort, ushort dstPort, uint sequenceNumber, uint acknowledgmentNumber, ReadOnlySpan<byte> payload)
    {
        if (!IsEnabled || _rawWriter is null)
        {
            return;
        }

        try
        {
            var line = $"{DateTimeOffset.Now:O}|dir={direction}|{srcPort}->{dstPort}|seq={sequenceNumber}|ack={acknowledgmentNumber}|len={payload.Length}|data={Convert.ToHexString(payload)}";
            lock (SyncRoot)
            {
                _rawWriter.WriteLine(line);
            }
        }
        catch
        {
        }
    }

    public static void AppendReassembled(string direction, in TcpConnection connection, uint sequenceNumber, ReadOnlySpan<byte> payload)
    {
        if (!IsEnabled || _streamWriter is null)
        {
            return;
        }

        try
        {
            var line = $"{DateTimeOffset.Now:O}|dir={direction}|{connection.SourceAddress}:{connection.SourcePort}->{connection.DestinationAddress}:{connection.DestinationPort}|seq={sequenceNumber}|len={payload.Length}|data={Convert.ToHexString(payload)}";
            lock (SyncRoot)
            {
                _streamWriter.WriteLine(line);
            }
        }
        catch
        {
        }
    }

    public static void AppendFrameEvent(string eventName, in TcpConnection connection, string detail, ReadOnlySpan<byte> payload)
    {
        var hasObserver = FrameEventObserved is not null;
        if ((!IsEnabled || _frameWriter is null) && !hasObserver)
        {
            return;
        }

        try
        {
            var timestamp = DateTimeOffset.Now;
            var timestampTicks = Stopwatch.GetTimestamp();
            if (IsEnabled && _frameWriter is not null)
            {
                var line = $"{timestamp:O}|{eventName}|{connection.SourceAddress}:{connection.SourcePort}->{connection.DestinationAddress}:{connection.DestinationPort}|{detail}|data={Convert.ToHexString(payload)}";
                lock (SyncRoot)
                {
                    _frameWriter.WriteLine(line);
                }
            }

            if (hasObserver)
            {
                try
                {
                    FrameEventObserved?.Invoke(new FrameEventObservation(timestampTicks, eventName, connection, detail));
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static StreamWriter CreateWriter(string path)
    {
        Directory.CreateDirectory(_logDirectory);
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        return new StreamWriter(stream, new UTF8Encoding(false))
        {
            AutoFlush = true
        };
    }

    private static void DisposeWriter(ref StreamWriter? writer)
    {
        writer?.Dispose();
        writer = null;
    }

    public readonly record struct FrameEventObservation(long TimestampTicks, string EventName, TcpConnection Connection, string Detail);
}
