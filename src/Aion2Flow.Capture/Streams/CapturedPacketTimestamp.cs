namespace Cloris.Aion2Flow.Capture.Streams;

internal readonly record struct CapturedPacketTimestamp(long UnixMilliseconds, long MonotonicTimestamp);
