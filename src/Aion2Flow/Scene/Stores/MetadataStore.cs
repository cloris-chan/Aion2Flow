using Cloris.Aion2Flow.Scene.Runtime;

namespace Cloris.Aion2Flow.Scene.Stores;

public sealed class MetadataStore
{
    private readonly Dictionary<int, string> _npcNamesByCode = [];
    private readonly Dictionary<int, string> _displayNamesByEntityId = [];
    private readonly SceneBoundaryService _sceneBoundary = new();

    public IReadOnlyDictionary<int, string> NpcNamesByCode => _npcNamesByCode;
    public IReadOnlyDictionary<int, string> DisplayNamesByEntityId => _displayNamesByEntityId;
    public uint CurrentMapId => _sceneBoundary.CurrentMapId;
    public uint CurrentMapInstanceId => _sceneBoundary.CurrentMapInstanceId;

    public void ApplyNpcName(int npcCode, string name) => _npcNamesByCode[npcCode] = name;

    public void ApplyDisplayName(int entityId, string displayName) => _displayNamesByEntityId[entityId] = displayName;

    public bool TryGetNpcName(int npcCode, out string? name) => _npcNamesByCode.TryGetValue(npcCode, out name);

    public bool TryGetDisplayName(int entityId, out string? name) => _displayNamesByEntityId.TryGetValue(entityId, out name);

    public void StageDestinationMap(uint mapId) => _sceneBoundary.StageDestinationMap(mapId);

    public void StageDestinationMapInstance(uint instanceId) => _sceneBoundary.StageDestinationMapInstance(instanceId);

    public SceneTransitionKind MarkSceneArrival() => _sceneBoundary.MarkSceneArrival();

    public void Clear()
    {
        _npcNamesByCode.Clear();
        _displayNamesByEntityId.Clear();
        _sceneBoundary.Clear();
    }
}
