namespace Cloris.Aion2Flow.Scene.Model;

public readonly record struct TimelineStamp(long OffsetTicks, long ObservationOrdinal, long FrameOrdinal, long BatchOrdinal);
