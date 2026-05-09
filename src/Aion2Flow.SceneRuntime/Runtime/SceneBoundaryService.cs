namespace Cloris.Aion2Flow.SceneRuntime.Runtime;

public sealed class SceneBoundaryService
{
    private uint _pendingMapId;
    private uint _pendingInstanceId;
    private bool _hasPendingMap;
    private bool _hasPendingInstance;

    public uint CurrentMapId { get; private set; }
    public uint CurrentMapInstanceId { get; private set; }
    public bool IsEmpty => CurrentMapId == 0 && CurrentMapInstanceId == 0 && !_hasPendingMap && !_hasPendingInstance;

    public bool StageDestinationMap(uint mapId)
    {
        if (mapId == 0 || mapId == CurrentMapId)
            return false;

        if (!_hasPendingMap || _pendingMapId != mapId)
        {
            _pendingMapId = mapId;
            _hasPendingMap = true;
            _pendingInstanceId = 0;
            _hasPendingInstance = false;
            return true;
        }

        return false;
    }

    public bool StageDestinationMapInstance(uint instanceId)
    {
        if (instanceId == 0)
            return false;

        if (!_hasPendingMap)
        {
            if (CurrentMapInstanceId == instanceId && !_hasPendingInstance)
                return false;

            CurrentMapInstanceId = instanceId;
            _pendingInstanceId = 0;
            _hasPendingInstance = false;
            return true;
        }

        if (_hasPendingInstance && _pendingInstanceId == instanceId)
            return false;

        _pendingInstanceId = instanceId;
        _hasPendingInstance = true;
        return true;
    }

    public SceneTransitionKind MarkSceneArrival()
    {
        var kind = SceneTransitionKind.None;

        if (_hasPendingMap)
        {
            var mapChanged = CurrentMapId != _pendingMapId;
            CurrentMapId = _pendingMapId;
            if (mapChanged)
            {
                CurrentMapInstanceId = 0;
                kind = SceneTransitionKind.MapChanged;
            }
            _pendingMapId = 0;
            _hasPendingMap = false;
        }

        if (_hasPendingInstance)
        {
            CurrentMapInstanceId = _pendingInstanceId;
            _pendingInstanceId = 0;
            _hasPendingInstance = false;
            if (kind == SceneTransitionKind.None)
                kind = SceneTransitionKind.InstanceChanged;
        }

        return kind;
    }

    public void Clear()
    {
        _pendingMapId = 0;
        _pendingInstanceId = 0;
        _hasPendingMap = false;
        _hasPendingInstance = false;
        CurrentMapId = 0;
        CurrentMapInstanceId = 0;
    }
}

public enum SceneTransitionKind : byte { None, MapChanged, InstanceChanged }
