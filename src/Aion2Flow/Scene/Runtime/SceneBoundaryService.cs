namespace Cloris.Aion2Flow.Scene.Runtime;

public sealed class SceneBoundaryService
{
    private uint _pendingMapId;
    private uint _pendingInstanceId;
    private bool _hasPendingMap;
    private bool _hasPendingInstance;

    public uint CurrentMapId { get; private set; }
    public uint CurrentMapInstanceId { get; private set; }

    public void StageDestinationMap(uint mapId)
    {
        if (mapId == 0 || mapId == CurrentMapId)
            return;

        if (!_hasPendingMap || _pendingMapId != mapId)
        {
            _pendingMapId = mapId;
            _hasPendingMap = true;
            _pendingInstanceId = 0;
            _hasPendingInstance = false;
        }
    }

    public void StageDestinationMapInstance(uint instanceId)
    {
        if (instanceId == 0)
            return;

        if (!_hasPendingMap)
        {
            CurrentMapInstanceId = instanceId;
            _pendingInstanceId = 0;
            _hasPendingInstance = false;
            return;
        }

        _pendingInstanceId = instanceId;
        _hasPendingInstance = true;
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
}

public enum SceneTransitionKind : byte { None, MapChanged, InstanceChanged }
