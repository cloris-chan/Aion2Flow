using Cloris.Aion2Flow.SceneRuntime.Runtime;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public sealed class SceneBoundaryStore
{
    private readonly SceneBoundaryService _sceneBoundary = new();
    private long _revision;

    public uint CurrentMapId => _sceneBoundary.CurrentMapId;

    public uint CurrentMapInstanceId => _sceneBoundary.CurrentMapInstanceId;

    public long Revision => _revision;

    public void StageDestinationMap(uint mapId)
    {
        if (_sceneBoundary.StageDestinationMap(mapId))
            _revision++;
    }

    public void StageDestinationMapInstance(uint instanceId)
    {
        if (_sceneBoundary.StageDestinationMapInstance(instanceId))
            _revision++;
    }

    public SceneTransitionKind MarkSceneArrival()
    {
        var kind = _sceneBoundary.MarkSceneArrival();
        if (kind != SceneTransitionKind.None)
            _revision++;
        return kind;
    }

    public void Clear()
    {
        if (_sceneBoundary.IsEmpty)
            return;

        _sceneBoundary.Clear();
        _revision++;
    }
}
