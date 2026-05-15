namespace Cloris.Aion2Flow.SceneRuntime.Runtime;

public sealed class SceneBoundaryService
{
    private bool _hasPendingSameMapReload;

    public uint CurrentMapId { get; private set; }
    public uint CurrentMapInstanceId { get; private set; }
    public long SceneTransitionRevision { get; private set; }
    public bool IsEmpty => CurrentMapId == 0 && CurrentMapInstanceId == 0 && SceneTransitionRevision == 0 && !_hasPendingSameMapReload;

    public bool StageDestinationMap(uint mapId) => StageDestinationMap(mapId, allowSameMapReload: false);

    public bool StageDestinationMap(uint mapId, bool allowSameMapReload)
    {
        if (mapId == 0)
            return false;

        if (mapId == CurrentMapId)
        {
            if (!allowSameMapReload || _hasPendingSameMapReload)
                return false;

            _hasPendingSameMapReload = true;
            return true;
        }

        CurrentMapId = mapId;
        CurrentMapInstanceId = 0;
        SceneTransitionRevision++;
        _hasPendingSameMapReload = false;
        return true;
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

    public SceneTransitionKind MarkSceneArrival()
    {
        var kind = SceneTransitionKind.None;

        if (_hasPendingSameMapReload)
        {
            _hasPendingSameMapReload = false;
            if (kind == SceneTransitionKind.None)
            {
                kind = SceneTransitionKind.SceneReload;
            }
        }

        if (kind != SceneTransitionKind.None)
        {
            SceneTransitionRevision++;
        }

        return kind;
    }

    public SceneTransitionKind MarkSceneTransportBoundary()
    {
        if (_hasPendingSameMapReload)
            return MarkSceneArrival();

        return SceneTransitionKind.None;
    }

    public void Clear()
    {
        _hasPendingSameMapReload = false;
        CurrentMapId = 0;
        CurrentMapInstanceId = 0;
        SceneTransitionRevision = 0;
    }
}

public enum SceneTransitionKind : byte { None, MapChanged, InstanceChanged, SceneReload, TransportBoundary }
