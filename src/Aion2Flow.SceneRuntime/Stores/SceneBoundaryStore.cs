using Cloris.Aion2Flow.SceneRuntime.Runtime;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public sealed class SceneBoundaryStore
{
    private readonly SceneBoundaryService _sceneBoundary;
    private long _revision;

    public SceneBoundaryStore() : this(new SceneBoundaryService(), 0)
    {
    }

    private SceneBoundaryStore(SceneBoundaryService sceneBoundary, long revision)
    {
        _sceneBoundary = sceneBoundary;
        _revision = revision;
    }

    public uint CurrentMapId => _sceneBoundary.CurrentMapId;

    public uint CurrentMapInstanceId => _sceneBoundary.CurrentMapInstanceId;

    public long SceneTransitionRevision => _sceneBoundary.SceneTransitionRevision;

    public long Revision => _revision;

    public void StageDestinationMap(uint mapId)
    {
        if (_sceneBoundary.StageDestinationMap(mapId))
            _revision++;
    }

    public void StageDestinationMap(uint mapId, bool allowSameMapReload)
    {
        if (_sceneBoundary.StageDestinationMap(mapId, allowSameMapReload))
            _revision++;
    }

    public void StagePendingDestinationMap(uint mapId, bool allowSameMapReload)
    {
        if (_sceneBoundary.StagePendingDestinationMap(mapId, allowSameMapReload))
            _revision++;
    }

    public void ConfirmDestinationMap(uint mapId, bool allowSameMapReload)
    {
        if (_sceneBoundary.ConfirmDestinationMap(mapId, allowSameMapReload))
            _revision++;
    }

    public void ConfirmPendingDestinationMapArrival()
    {
        if (_sceneBoundary.ConfirmPendingDestinationMapArrival())
            _revision++;
    }

    public void StageDestinationMapInstance(uint instanceId)
    {
        if (_sceneBoundary.StageDestinationMapInstance(instanceId))
            _revision++;
    }

    public void ConfirmDestinationMapInstance(uint instanceId)
    {
        if (_sceneBoundary.ConfirmDestinationMapInstance(instanceId))
            _revision++;
    }

    public SceneTransitionKind MarkSceneTransportBoundary()
    {
        var kind = _sceneBoundary.MarkSceneTransportBoundary();
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

    public SceneBoundaryStore DeepClone()
        => new(_sceneBoundary.DeepClone(), _revision);
}
