using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.SceneRuntime.Observation;

public readonly record struct ObservedEventHeader(Guid SceneSessionId, TimelineStamp Stamp, int SourceEntityId, int TargetEntityId, RawPacketReference Raw);
