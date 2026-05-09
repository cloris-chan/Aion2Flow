using Cloris.Aion2Flow.SceneRuntime.Runtime;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public sealed class MetadataStore
{
    private readonly Dictionary<int, string> _npcNamesByCode = [];
    private readonly Dictionary<int, string> _displayNamesByEntityId = [];
    private readonly SceneBoundaryService _sceneBoundary = new();
    private long _revision;

    public IReadOnlyDictionary<int, string> NpcNamesByCode => _npcNamesByCode;
    public IReadOnlyDictionary<int, string> DisplayNamesByEntityId => _displayNamesByEntityId;
    public uint CurrentMapId => _sceneBoundary.CurrentMapId;
    public uint CurrentMapInstanceId => _sceneBoundary.CurrentMapInstanceId;
    public long Revision => _revision;

    public void ApplyNpcName(int npcCode, string name)
    {
        if (_npcNamesByCode.TryGetValue(npcCode, out var current) && current == name)
            return;

        _npcNamesByCode[npcCode] = name;
        _revision++;
    }

    public void ApplyDisplayName(int entityId, string displayName)
    {
        if (_displayNamesByEntityId.TryGetValue(entityId, out var current) && current == displayName)
            return;

        _displayNamesByEntityId[entityId] = displayName;
        _revision++;
    }

    public bool TryGetNpcName(int npcCode, out string? name) => _npcNamesByCode.TryGetValue(npcCode, out name);

    public bool TryGetDisplayName(int entityId, out string? name) => _displayNamesByEntityId.TryGetValue(entityId, out name);

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
        if (_npcNamesByCode.Count == 0 && _displayNamesByEntityId.Count == 0 && _sceneBoundary.IsEmpty)
            return;

        _npcNamesByCode.Clear();
        _displayNamesByEntityId.Clear();
        _sceneBoundary.Clear();
        _revision++;
    }
}
