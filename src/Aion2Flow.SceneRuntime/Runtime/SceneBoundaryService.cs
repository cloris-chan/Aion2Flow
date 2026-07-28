using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Runtime;

public sealed class SceneBoundaryService
{
    public uint CurrentMapId { get; private set; }

    public uint CurrentMapInstanceId { get; private set; }

    public long SceneTransitionRevision { get; private set; }

    public bool IsEmpty =>
        CurrentMapId == 0 &&
        CurrentMapInstanceId == 0 &&
        SceneTransitionRevision == 0;

    public bool SetCurrentMap(uint mapId)
    {
        if (mapId == 0 || CurrentMapId == mapId)
            return false;

        CurrentMapId = mapId;
        return true;
    }

    public bool StartMapContext(uint mapId)
    {
        CurrentMapId = mapId;
        CurrentMapInstanceId = 0;
        SceneTransitionRevision++;
        return true;
    }

    public bool SetMapInstance(uint instanceId)
    {
        if (instanceId == 0 || CurrentMapInstanceId == instanceId)
            return false;

        CurrentMapInstanceId = instanceId;
        return true;
    }

    internal void ApplySceneObservation(in SceneObservation scene)
    {
        switch (scene.Kind)
        {
            case SceneObservationKind.CurrentMap:
            case SceneObservationKind.DestinationMapArrival:
                SetCurrentMap(scene.MapId);
                break;
            case SceneObservationKind.MapEventRegistered:
                SetMapInstance(scene.MapInstanceId);
                break;
            case SceneObservationKind.MapContextStarted:
                StartMapContext(scene.MapId);
                break;
        }
    }

    internal SceneBoundaryServiceSnapshot CreateSnapshot() =>
        new(CurrentMapId, CurrentMapInstanceId, SceneTransitionRevision);

    internal static SceneBoundaryService FromSnapshot(SceneBoundaryServiceSnapshot snapshot) => new()
    {
        CurrentMapId = snapshot.CurrentMapId,
        CurrentMapInstanceId = snapshot.CurrentMapInstanceId,
        SceneTransitionRevision = snapshot.SceneTransitionRevision
    };

    public void Clear()
    {
        CurrentMapId = 0;
        CurrentMapInstanceId = 0;
        SceneTransitionRevision = 0;
    }
}

internal readonly record struct SceneBoundaryServiceSnapshot(
    uint CurrentMapId,
    uint CurrentMapInstanceId,
    long SceneTransitionRevision);
