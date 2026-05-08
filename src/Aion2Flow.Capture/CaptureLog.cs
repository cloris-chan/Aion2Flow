namespace Cloris.Aion2Flow.Capture;

public enum CaptureLogLevel : byte
{
    Debug,
    Info,
    Warning,
    Error
}

public static class CaptureLog
{
    public static Action<CaptureLogLevel, string>? Sink { get; set; }

    public static void Write(CaptureLogLevel level, string message)
    {
        Sink?.Invoke(level, message);
    }
}
