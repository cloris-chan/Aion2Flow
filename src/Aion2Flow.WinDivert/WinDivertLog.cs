namespace Cloris.Aion2Flow.WinDivert;

public enum WinDivertLogLevel : byte
{
    Debug,
    Info,
    Warning,
    Error
}

public static class WinDivertLog
{
    public static Action<WinDivertLogLevel, string>? Sink { get; set; }

    public static void Write(WinDivertLogLevel level, string message) => Sink?.Invoke(level, message);
}
