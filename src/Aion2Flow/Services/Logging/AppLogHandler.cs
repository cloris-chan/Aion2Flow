using System.Runtime.CompilerServices;

namespace Cloris.Aion2Flow.Services.Logging;

[InterpolatedStringHandler]
public ref struct AppLogHandler
{
    private DefaultInterpolatedStringHandler _inner;
    internal readonly bool IsEnabled;

    public AppLogHandler(int literalLength, int formattedCount, AppLogLevel level, out bool isEnabled)
    {
        IsEnabled = isEnabled = AppLog.IsEnabled(level);
        if (isEnabled)
            _inner = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
    }

    public void AppendLiteral(string s)
    {
        if (IsEnabled) _inner.AppendLiteral(s);
    }

    public void AppendFormatted<T>(T value)
    {
        if (IsEnabled) _inner.AppendFormatted(value);
    }

    public void AppendFormatted<T>(T value, string? format)
    {
        if (IsEnabled) _inner.AppendFormatted(value, format);
    }

    public void AppendFormatted<T>(T value, int alignment)
    {
        if (IsEnabled) _inner.AppendFormatted(value, alignment);
    }

    public void AppendFormatted<T>(T value, int alignment, string? format)
    {
        if (IsEnabled) _inner.AppendFormatted(value, alignment, format);
    }

    public void AppendFormatted(ReadOnlySpan<char> value)
    {
        if (IsEnabled) _inner.AppendFormatted(value);
    }

    public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null)
    {
        if (IsEnabled) _inner.AppendFormatted(value, alignment, format);
    }

    public void AppendFormatted(string? value)
    {
        if (IsEnabled) _inner.AppendFormatted(value);
    }

    internal string ToStringAndClear() => IsEnabled ? _inner.ToStringAndClear() : string.Empty;
}
