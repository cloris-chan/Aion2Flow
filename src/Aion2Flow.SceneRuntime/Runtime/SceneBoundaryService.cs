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

    public bool StageDestinationMap(uint mapId, bool allowSameMapReload)
        => CommitConfirmedMap(mapId, allowSameMapReload);

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

    public SceneTransitionKind MarkSceneTransportBoundary()
        => SceneTransitionKind.None;

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

public enum SceneTransitionKind : byte { None, MapChanged, InstanceChanged, SceneReload, TransportBoundary }
