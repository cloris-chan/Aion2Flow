using System.Globalization;
using System.Text;
using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Capture.Diagnostics;

internal static class RawPacketDump
{
    private static readonly bool IsEnabled =
#if DEBUG
        true;
#else
        false;
#endif
    private static readonly Lock SyncRoot = new();
    private static string _logRootDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
    private static string _rawLogPath = string.Empty;
    private static string _streamLogPath = string.Empty;
    private static StreamWriter? _rawWriter;
    private static StreamWriter? _streamWriter;
    private static DateTimeOffset _currentSessionStarted = DateTimeOffset.Now;

    public static string RawLogPath => _rawLogPath;
    public static string StreamLogPath => _streamLogPath;
    public static DateTimeOffset CurrentSessionStarted => _currentSessionStarted;

    public static void ConfigureLogDirectory(string logDirectory)
    {
        _logRootDirectory = logDirectory;
        RotateLogs();
    }

    public static DateTimeOffset RotateLogs()
    {
        var sessionStarted = DateTimeOffset.Now;

        lock (SyncRoot)
        {
            _currentSessionStarted = sessionStarted;
            if (!IsEnabled)
            {
                return sessionStarted;
            }

            DisposeWriter(ref _rawWriter);
            DisposeWriter(ref _streamWriter);

            var sessionDirectory = ResolveUniqueDumpLogDirectory(_logRootDirectory, sessionStarted);
            Directory.CreateDirectory(sessionDirectory);
            _rawLogPath = Path.Combine(sessionDirectory, "raw.log");
            _streamLogPath = Path.Combine(sessionDirectory, "stream.log");

            _rawWriter = CreateWriter(_rawLogPath);
            _streamWriter = CreateWriter(_streamLogPath);
        }

        return sessionStarted;
    }

    public static void Append(string direction, ushort srcPort, ushort dstPort, uint sequenceNumber, uint acknowledgmentNumber, long captureTicks, ReadOnlySpan<byte> payload)
    {
        if (!IsEnabled || _rawWriter is null)
        {
            return;
        }

        try
        {
            var line = $"{DateTimeOffset.Now:O}|dir={direction}|{srcPort}->{dstPort}|seq={sequenceNumber}|ack={acknowledgmentNumber}|len={payload.Length}|qpc={captureTicks}|data={Convert.ToHexString(payload)}";
            lock (SyncRoot)
            {
                _rawWriter.WriteLine(line);
            }
        }
        catch
        {
        }
    }

    public static void AppendReassembled(string direction, in TcpConnection connection, uint sequenceNumber, long captureTimestampMilliseconds, ReadOnlySpan<byte> payload)
    {
        if (!IsEnabled || _streamWriter is null)
        {
            return;
        }

        try
        {
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(captureTimestampMilliseconds);
            var line = $"{timestamp:O}|dir={direction}|{connection.SourceAddress}:{connection.SourcePort}->{connection.DestinationAddress}:{connection.DestinationPort}|seq={sequenceNumber}|len={payload.Length}|data={Convert.ToHexString(payload)}";
            lock (SyncRoot)
            {
                _streamWriter.WriteLine(line);
            }
        }
        catch
        {
        }
    }

    private static StreamWriter CreateWriter(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

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

    private static string ResolveUniqueDumpLogDirectory(string logDirectory, DateTimeOffset timestamp)
    {
        var baseDirectory = ResolveDumpLogDirectory(logDirectory, timestamp);
        if (!Directory.Exists(baseDirectory))
        {
            return baseDirectory;
        }

        for (var suffix = 1; suffix <= 999; suffix++)
        {
            var candidate = string.Create(
                CultureInfo.InvariantCulture,
                $"{baseDirectory}-{suffix:00}");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return $"{baseDirectory}-{Guid.NewGuid():N}";
    }

    private static string ResolveDumpLogDirectory(string logDirectory, DateTimeOffset timestamp)
        => Path.Combine(logDirectory, "dumps", timestamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
}
