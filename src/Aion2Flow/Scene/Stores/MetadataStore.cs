namespace Cloris.Aion2Flow.Scene.Stores;

public sealed class MetadataStore
{
    private readonly Dictionary<int, string> _npcNamesByCode = [];
    private readonly Dictionary<int, string> _displayNamesByEntityId = [];

    public IReadOnlyDictionary<int, string> NpcNamesByCode => _npcNamesByCode;
    public IReadOnlyDictionary<int, string> DisplayNamesByEntityId => _displayNamesByEntityId;

    public void ApplyNpcName(int npcCode, string name) => _npcNamesByCode[npcCode] = name;

    public void ApplyDisplayName(int entityId, string displayName) => _displayNamesByEntityId[entityId] = displayName;

    public bool TryGetNpcName(int npcCode, out string? name) => _npcNamesByCode.TryGetValue(npcCode, out name);

    public bool TryGetDisplayName(int entityId, out string? name) => _displayNamesByEntityId.TryGetValue(entityId, out name);

    public void Clear()
    {
        _npcNamesByCode.Clear();
        _displayNamesByEntityId.Clear();
    }
}
