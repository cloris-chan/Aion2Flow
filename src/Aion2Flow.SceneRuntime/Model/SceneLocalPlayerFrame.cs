namespace Cloris.Aion2Flow.SceneRuntime.Model;

public readonly record struct SceneLocalPlayerFrame(
    Guid SessionId,
    long StartObservationOrdinal,
    long EndObservationOrdinalExclusive,
    int EntityId,
    long ObservedAtMilliseconds);
