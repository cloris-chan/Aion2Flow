using Cloris.Aion2Flow.Scene.Model;

namespace Cloris.Aion2Flow.Scene.Observation;

public readonly record struct ObservedEventEnvelope(
    Guid SceneSessionId,
    TimelineStamp Stamp,
    ObservedEventDomain Domain,
    int SourceEntityId,
    int TargetEntityId,
    RawPacketReference Raw,
    CombatObservation? Combat = null,
    StateObservation? State = null,
    SceneObservation? Scene = null,
    ResourceObservation? Resource = null,
    AuraObservation? Aura = null);
