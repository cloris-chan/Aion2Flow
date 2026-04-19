using System.Threading.Channels;

namespace Cloris.Aion2Flow.Services.Logging;

public sealed class AppLogWriter : IDisposable
{
    private readonly record struct LogEntry(
        AppLogLevel Level,
        string Message,
        string? SourceFile,
        int SourceLine,
        DateTimeOffset Timestamp);

    private const int MaxFileSize = 5 * 1024 * 1024;
    private const int MaxRotatedFiles = 3;

    private readonly Channel<LogEntry> _channel;
    private readonly string _logDirectory;
    private readonly Task _drainTask;

    private StreamWriter? _writer;
    private long _currentFileSize;

    public AppLogLevel MinLevel { get; }

    public AppLogWriter(AppLogLevel minLevel, string? logDirectory = null)
    {
        MinLevel = minLevel;
        _logDirectory = logDirectory ?? GetDefaultLogDirectory();
        Directory.CreateDirectory(_logDirectory);

        _channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false,
        });

        _drainTask = Task.Factory.StartNew(DrainAsync, TaskCreationOptions.LongRunning).Unwrap();
    }

    internal void Enqueue(AppLogLevel level, string message, string? sourceFile, int sourceLine)
    {
        _channel.Writer.TryWrite(new LogEntry(level, message, sourceFile, sourceLine, DateTimeOffset.Now));
    }

    private async Task DrainAsync()
    {
        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync())
            {
                try { WriteEntry(in entry); }
                catch { }
            }
        }
        finally
        {
            while (_channel.Reader.TryRead(out var remaining))
            {
                try { WriteEntry(in remaining); } catch { }
            }

            _writer?.Dispose();
            _writer = null;
        }
    }

    private void WriteEntry(in LogEntry entry)
    {
        EnsureWriter();
        if (_writer is null) return;

        const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";

        Span<char> tsBuf = stackalloc char[TimestampFormat.Length];
        entry.Timestamp.TryFormat(tsBuf, out _, TimestampFormat);

        var tag = GetLevelTag(entry.Level);
        var srcName = entry.SourceFile is not null ? Path.GetFileNameWithoutExtension(entry.SourceFile) : null;

        _writer.Write('[');
        _writer.Write(tsBuf);
        _writer.Write("] [");
        _writer.Write(tag);
        _writer.Write(']');

        int written = 1 + TimestampFormat.Length + 3 + tag.Length + 1;

        if (srcName is not null)
        {
            _writer.Write(" [");
            _writer.Write(srcName);
            _writer.Write(':');

            Span<char> lineBuf = stackalloc char[10];
            entry.SourceLine.TryFormat(lineBuf, out int lineChars);
            _writer.Write(lineBuf[..lineChars]);

            _writer.Write(']');
            written += 2 + srcName.Length + 1 + lineChars + 1;
        }

        _writer.Write(' ');
        _writer.Write(entry.Message);
        _writer.WriteLine();
        _writer.Flush();

        written += 1 + entry.Message.Length + Environment.NewLine.Length;
        _currentFileSize += written;
        if (_currentFileSize >= MaxFileSize)
            RotateFile();
    }

    private void EnsureWriter()
    {
        if (_writer is not null) return;

        Directory.CreateDirectory(_logDirectory);
        var filePath = Path.Combine(_logDirectory, "app.log");

        _currentFileSize = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;
        if (_currentFileSize >= MaxFileSize)
            RotateFile();

        _writer ??= new StreamWriter(filePath, append: true) { AutoFlush = false };
    }

    private void RotateFile()
    {
        _writer?.Dispose();
        _writer = null;
        _currentFileSize = 0;

        var basePath = Path.Combine(_logDirectory, "app");

        try
        {
            var oldest = $"{basePath}.{MaxRotatedFiles}.log";
            if (File.Exists(oldest)) File.Delete(oldest);

            for (int i = MaxRotatedFiles - 1; i >= 1; i--)
            {
                var src = $"{basePath}.{i}.log";
                var dst = $"{basePath}.{i + 1}.log";
                if (File.Exists(src)) File.Move(src, dst);
            }

            var current = $"{basePath}.log";
            if (File.Exists(current)) File.Move(current, $"{basePath}.1.log");
        }
        catch
        {
        }
    }

    private static string GetLevelTag(AppLogLevel level) => level switch
    {
        AppLogLevel.Debug => "DBG",
        AppLogLevel.Info => "INF",
        AppLogLevel.Warning => "WRN",
        AppLogLevel.Error => "ERR",
        _ => "???",
    };

    private static string GetDefaultLogDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Aion2Flow", "logs");
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        try { _drainTask.Wait(TimeSpan.FromSeconds(3)); }
        catch { }
    }
}
