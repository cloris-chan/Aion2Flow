namespace Cloris.Aion2Flow.Capture.Streams;

internal readonly record struct PacketProcessingTimestamp(long TimelineUnixMilliseconds, long ArrivalTimestamp);
