using System.Runtime.CompilerServices;

namespace Cloris.Aion2Flow.Services.Logging;

public static class AppLog
{
    private static AppLogWriter? _writer;

    public static void Initialize(AppLogWriter writer) => _writer = writer;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsEnabled(AppLogLevel level) => _writer is { } w && level >= w.MinLevel;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write(
        AppLogLevel level,
        string message,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        if (!IsEnabled(level)) return;
        _writer!.Enqueue(level, message, file, line);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write(
        AppLogLevel level,
        [InterpolatedStringHandlerArgument("level")] ref AppLogHandler handler,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        if (!handler.IsEnabled) return;
        _writer!.Enqueue(level, handler.ToStringAndClear(), file, line);
    }
}
