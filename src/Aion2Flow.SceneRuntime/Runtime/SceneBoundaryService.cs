using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Runtime;

public sealed class SceneBoundaryService
{
    private uint _pendingMapId;
    private bool _pendingAllowSameMapReload;

    public uint CurrentMapId { get; private set; }
    public uint CurrentMapInstanceId { get; private set; }
    public long SceneTransitionRevision { get; private set; }
    public bool IsEmpty => CurrentMapId == 0 && CurrentMapInstanceId == 0 && SceneTransitionRevision == 0 && _pendingMapId == 0;

    public bool StageDestinationMap(uint mapId) => StageDestinationMap(mapId, allowSameMapReload: false);

    public bool StageDestinationMap(uint mapId, bool allowSameMapReload) => CommitConfirmedMap(mapId, allowSameMapReload);

    public bool StagePendingDestinationMap(uint mapId, bool allowSameMapReload)
    {
        if (mapId == 0)
            return false;

        if (_pendingMapId == mapId && _pendingAllowSameMapReload == allowSameMapReload)
            return false;

        _pendingMapId = mapId;
        _pendingAllowSameMapReload = allowSameMapReload;
        return true;
    }

    public bool ConfirmDestinationMap(uint mapId, bool allowSameMapReload)
    {
        if (mapId == 0)
            return false;

        var confirmsPendingMap = _pendingMapId == mapId;
        ClearPendingDestination();
        return CommitConfirmedMap(mapId, allowSameMapReload && confirmsPendingMap);
    }

    public bool ConfirmPendingDestinationMapArrival()
    {
        if (_pendingMapId == 0)
            return false;

        var mapId = _pendingMapId;
        ClearPendingDestination();
        return CommitConfirmedMap(mapId, allowSameMapReload: false);
    }

    public bool StageDestinationMapInstance(uint instanceId)
    {
        if (instanceId == 0)
            return false;

        if (CurrentMapInstanceId == instanceId)
            return false;

        CurrentMapInstanceId = instanceId;
        return true;
    }

    public bool ConfirmDestinationMapInstance(uint instanceId)
    {
        if (instanceId == 0)
            return false;

        var changed = false;
        if (_pendingMapId != 0)
        {
            changed |= CommitConfirmedMap(_pendingMapId, _pendingAllowSameMapReload && _pendingMapId == CurrentMapId);
            ClearPendingDestination();
        }

        changed |= StageDestinationMapInstance(instanceId);
        return changed;
    }

    internal SceneTransitionKind ApplySceneObservation(in SceneObservation scene)
    {
        var previousMapId = CurrentMapId;
        var previousMapInstanceId = CurrentMapInstanceId;

        switch (scene.DiagnosticKey)
        {
            case "stage-destination-map":
                StageDestinationMap(scene.MapId, scene.Value0 != 0);
                break;
            case "pending-destination-map":
                StagePendingDestinationMap(scene.MapId, scene.Value0 != 0);
                break;
            case "confirm-destination-map":
                ConfirmDestinationMap(scene.MapId, scene.Value0 != 0);
                break;
            case "confirm-pending-destination-map-arrival":
                ConfirmPendingDestinationMapArrival();
                break;
            case "stage-destination-instance":
                StageDestinationMapInstance(scene.MapInstanceId);
                break;
            case "confirm-destination-instance":
                ConfirmDestinationMapInstance(scene.MapInstanceId);
                break;
        }

        if (CurrentMapId != previousMapId)
            return SceneTransitionKind.MapChanged;

        return CurrentMapInstanceId != previousMapInstanceId
            ? SceneTransitionKind.InstanceChanged
            : SceneTransitionKind.None;
    }

    internal SceneBoundaryServiceSnapshot CreateSnapshot() => new(CurrentMapId, CurrentMapInstanceId, SceneTransitionRevision, _pendingMapId, _pendingAllowSameMapReload);

    internal static SceneBoundaryService FromSnapshot(SceneBoundaryServiceSnapshot snapshot) => new()
    {
        CurrentMapId = snapshot.CurrentMapId,
        CurrentMapInstanceId = snapshot.CurrentMapInstanceId,
        SceneTransitionRevision = snapshot.SceneTransitionRevision,
        _pendingMapId = snapshot.PendingMapId,
        _pendingAllowSameMapReload = snapshot.PendingAllowSameMapReload
    };

    private bool CommitConfirmedMap(uint mapId, bool allowSameMapReload)
    {
        if (mapId == 0)
            return false;

        if (mapId == CurrentMapId)
        {
            if (!allowSameMapReload)
                return false;

            ClearPendingDestination();
            SceneTransitionRevision++;
            return true;
        }

        CurrentMapId = mapId;
        CurrentMapInstanceId = 0;
        SceneTransitionRevision++;
        ClearPendingDestination();
        return true;
    }

    private void ClearPendingDestination()
    {
        _pendingMapId = 0;
        _pendingAllowSameMapReload = false;
    }

    public void Clear()
    {
        ClearPendingDestination();
        CurrentMapId = 0;
        CurrentMapInstanceId = 0;
        SceneTransitionRevision = 0;
    }
}

public enum SceneTransitionKind : byte { None, MapChanged, InstanceChanged }

internal readonly record struct SceneBoundaryServiceSnapshot(uint CurrentMapId, uint CurrentMapInstanceId, long SceneTransitionRevision, uint PendingMapId, bool PendingAllowSameMapReload);
