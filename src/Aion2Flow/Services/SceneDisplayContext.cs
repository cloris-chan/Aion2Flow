using System.Globalization;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.Services;

public sealed class SceneDisplayContext(SceneIdentityScope identityScope, RuntimeMetadataRegistry? metadataRegistry, SceneCombatSnapshot? snapshot, GameResourceService resources, string unknownSceneName)
{
    public SceneIdentityScope IdentityScope { get; } = identityScope;
    public RuntimeMetadataRegistry? MetadataRegistry { get; } = metadataRegistry;
    public SceneCombatSnapshot Snapshot { get; } = snapshot ?? SceneCombatSnapshot.Empty;
    public GameResourceService Resources { get; } = resources;
    public string UnknownSceneName { get; } = string.IsNullOrWhiteSpace(unknownSceneName) ? "Scene_Unknown" : unknownSceneName;

    public string ResolveEntityName(int entityId)
    {
        if (entityId <= 0)
        {
            return string.Empty;
        }

        if (TryGetPcMetadata(entityId, out var pc) && pc.HasNickname)
        {
            return pc.Nickname;
        }

        if (TryGetNpcCode(entityId, out var npcCode))
        {
            return Resources.ResolveNpcName(npcCode);
        }

        return entityId.ToString(CultureInfo.InvariantCulture);
    }

    public string ResolvePcName(int entityId)
    {
        if (entityId <= 0)
        {
            return string.Empty;
        }

        return TryGetPcMetadata(entityId, out var pc) && pc.HasNickname
            ? pc.Nickname
            : entityId.ToString(CultureInfo.InvariantCulture);
    }

    public CharacterClass? ResolvePcClass(int entityId)
        => Snapshot.Combatants.TryGetValue(entityId, out var combatant)
            ? combatant.CharacterClass
            : null;

    public string ResolveNpcName(int instanceId)
    {
        if (instanceId <= 0)
        {
            return string.Empty;
        }

        return TryGetNpcCode(instanceId, out var npcCode)
            ? Resources.ResolveNpcName(npcCode)
            : instanceId.ToString(CultureInfo.InvariantCulture);
    }

    public string ResolveNpcCodeName(int npcCode)
        => npcCode > 0 ? Resources.ResolveNpcName(npcCode) : string.Empty;

    public string ResolveSkillName(int skillCode)
        => skillCode > 0 ? Resources.ResolveSkillName(skillCode) : string.Empty;

    public string ResolveMapName(uint mapId)
    {
        var mapName = mapId == 0 ? string.Empty : Resources.ResolveMapName(mapId);
        return string.IsNullOrWhiteSpace(mapName) ? UnknownSceneName : mapName;
    }

    public string GetEntitySortKey(int entityId)
        => ResolveEntityName(entityId);

    public string GetSkillSortKey(int skillCode)
        => ResolveSkillName(skillCode);

    private bool TryGetPcMetadata(int entityId, out PcMetadata metadata)
    {
        if (IdentityScope.TryGetPcMetadata(entityId, out metadata))
        {
            return true;
        }

        return MetadataRegistry is not null && MetadataRegistry.TryGetPcMetadata(entityId, out metadata);
    }

    private bool TryGetNpcCode(int instanceId, out int npcCode)
    {
        if (IdentityScope.TryGetNpcCode(instanceId, out npcCode))
        {
            return true;
        }

        return MetadataRegistry is not null && MetadataRegistry.TryGetNpcCode(instanceId, out npcCode);
    }
}
