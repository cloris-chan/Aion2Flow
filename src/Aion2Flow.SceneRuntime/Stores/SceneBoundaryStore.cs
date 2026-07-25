using Cloris.Aion2Flow.SceneRuntime.Observation;
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

    public void SetCurrentMap(uint mapId)
    {
        if (_sceneBoundary.SetCurrentMap(mapId))
            _revision++;
    }

    public void AnnounceDestinationMapTransition(uint mapId)
    {
        if (_sceneBoundary.AnnounceDestinationMapTransition(mapId))
            _revision++;
    }

    public void CommitDestinationMapTransition(uint mapId)
    {
        if (_sceneBoundary.CommitDestinationMapTransition(mapId))
            _revision++;
    }

    public void StageSceneMapCandidate(uint mapId)
    {
        if (_sceneBoundary.StageSceneMapCandidate(mapId))
            _revision++;
    }

    public void ConfirmSceneMap(uint mapId)
    {
        if (_sceneBoundary.ConfirmSceneMap(mapId))
            _revision++;
    }

    public void ConfirmDestinationMapArrival()
    {
        if (_sceneBoundary.ConfirmDestinationMapArrival())
            _revision++;
    }

    public void StageMapInstance(uint instanceId)
    {
        if (_sceneBoundary.StageMapInstance(instanceId))
            _revision++;
    }

    public void ConfirmMapInstance(uint instanceId)
    {
        if (_sceneBoundary.ConfirmMapInstance(instanceId))
            _revision++;
    }

    internal SceneTransitionKind ApplySceneObservation(in SceneObservation scene)
    {
        var before = _sceneBoundary.CreateSnapshot();
        var transitionKind = _sceneBoundary.ApplySceneObservation(in scene);
        if (before != _sceneBoundary.CreateSnapshot())
            _revision++;

        return transitionKind;
    }

    internal SceneBoundaryStoreSnapshot CreateSnapshot() => new(_sceneBoundary.CreateSnapshot(), _revision);

    internal static SceneBoundaryStore FromSnapshot(SceneBoundaryStoreSnapshot snapshot) => new(SceneBoundaryService.FromSnapshot(snapshot.Boundary), snapshot.Revision);

    public void Clear()
    {
        if (_sceneBoundary.IsEmpty)
            return;

        _sceneBoundary.Clear();
        _revision++;
    }

}

internal readonly record struct SceneBoundaryStoreSnapshot(SceneBoundaryServiceSnapshot Boundary, long Revision);
