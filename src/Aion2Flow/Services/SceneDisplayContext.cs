using System.Globalization;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources.Catalog;
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

    public int ResolvePcAnonymousOrdinal(int entityId)
    {
        if (entityId <= 0)
        {
            return 1;
        }

        var characterClass = ResolvePcClass(entityId);
        var ordinal = 1;
        var scoped = IdentityScope.PcMetadataSpan;
        for (var i = 0; i < scoped.Length; i++)
        {
            var entry = scoped[i];
            if (entry.EntityId >= entityId)
            {
                break;
            }

            if (ResolvePcClass(entry.EntityId) == characterClass)
            {
                ordinal++;
            }
        }

        if (MetadataRegistry is not null)
        {
            foreach (var (candidateId, metadata) in MetadataRegistry.PcMetadataByEntityId)
            {
                if (candidateId < entityId &&
                    !IdentityScope.TryGetPcMetadata(candidateId, out _) &&
                    ResolvePcClass(candidateId) == characterClass)
                {
                    ordinal++;
                }
            }
        }

        var combatants = Snapshot.Combatants.AsSpan();
        for (var i = 0; i < combatants.Length; i++)
        {
            ref readonly var entry = ref combatants[i];
            if (entry.Id >= entityId)
            {
                break;
            }

            if (!IdentityScope.TryGetPcMetadata(entry.Id, out _) &&
                MetadataRegistry?.TryGetPcMetadata(entry.Id, out _) != true &&
                entry.Metrics.IsVisiblePlayerCombatant &&
                ResolvePcClass(entry.Id) == characterClass)
            {
                ordinal++;
            }
        }

        return ordinal;
    }

    public bool HasPcMetadata(int entityId) => entityId > 0 && TryGetPcMetadata(entityId, out _);

    public bool TryResolvePcMetadata(int entityId, out PcMetadata metadata)
    {
        if (entityId <= 0)
        {
            metadata = default;
            return false;
        }

        return TryGetPcMetadata(entityId, out metadata);
    }

    public CharacterClass? ResolvePcClass(int entityId)
        => Snapshot.Combatants.TryGetValue(entityId, out var combatant)
            ? combatant.CharacterClass
            : TryGetPcMetadata(entityId, out var pc) ? pc.CharacterClass : null;

    public Faction ResolveFaction(int entityId) => TryGetPcMetadata(entityId, out var pc) ? pc.Faction : Faction.Unknown;

    public bool IsLocalPlayer(int entityId) => TryGetPcMetadata(entityId, out var pc) && pc.IsLocalPlayer;

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

    public bool HasNpcCode(int instanceId) => instanceId > 0 && TryResolveNpcCode(instanceId, out _);

    public bool TryResolveNpcCode(int instanceId, out int npcCode)
    {
        if (instanceId <= 0)
        {
            npcCode = 0;
            return false;
        }

        return TryGetNpcCode(instanceId, out npcCode);
    }

    public string ResolveNpcCodeName(int npcCode) => npcCode > 0 ? Resources.ResolveNpcName(npcCode) : string.Empty;

    public NpcDisplayEntry? ResolveNpcCodeCatalogEntry(int npcCode) => npcCode > 0 && Resources.TryResolveNpcCatalogEntry(npcCode, out var entry) ? entry : null;

    public NpcDisplayEntry? ResolveNpcCatalogEntry(int instanceId)
    {
        if (instanceId <= 0 || !TryGetNpcCode(instanceId, out var npcCode))
        {
            return null;
        }

        return ResolveNpcCodeCatalogEntry(npcCode);
    }

    public string ResolveSkillName(int skillCode) => skillCode > 0 ? Resources.ResolveSkillName(skillCode) : string.Empty;

    public string ResolveSkillName(ResourceEffectRef effectRef)
        => CombatResourceRegistry.TryResolveSkillIdByEffectRef(effectRef, out var skillId)
            ? Resources.ResolveSkillName(skillId)
            : string.Empty;

    public bool ContainsSkill(int skillCode) => Resources.ContainsSkill(skillCode);

    public string? ResolveSkillIconAssetName(int skillCode) => skillCode > 0 ? Resources.ResolveSkillIconAssetName(skillCode) : null;

    public string ResolveShortServerName(int code) => code > 0 ? Resources.ResolveShortServerName(code) : string.Empty;

    public string ResolveMapName(uint mapId)
    {
        var mapName = mapId == 0 ? string.Empty : Resources.ResolveMapName(mapId);
        return string.IsNullOrWhiteSpace(mapName) ? UnknownSceneName : mapName;
    }

    public string ResolveSceneName(SceneKind kind, uint mapId, IReadOnlyList<int> bossNpcCodes)
    {
        if (kind != SceneKind.Boss)
            return ResolveMapName(mapId);

        if (bossNpcCodes.Count == 0)
            return UnknownSceneName;

        var names = new string[bossNpcCodes.Count];
        for (var i = 0; i < names.Length; i++)
            names[i] = ResolveNpcCodeName(bossNpcCodes[i]);
        return string.Join(" / ", names);
    }

    public string GetEntitySortKey(int entityId) => ResolveEntityName(entityId);

    public string GetSkillSortKey(int skillCode) => ResolveSkillName(skillCode);

    private bool TryGetPcMetadata(int entityId, out PcMetadata metadata)
    {
        var hasScoped = IdentityScope.TryGetPcMetadata(entityId, out var scoped);
        var registered = default(PcMetadata);
        var hasRegistry = MetadataRegistry is not null && MetadataRegistry.TryGetPcMetadata(entityId, out registered);
        if (hasScoped && hasRegistry)
        {
            metadata = MergePcMetadata(scoped, registered);
            return true;
        }

        if (hasScoped)
        {
            metadata = scoped;
            return true;
        }

        if (hasRegistry)
        {
            metadata = registered;
            return true;
        }

        metadata = default;
        return false;
    }

    private static PcMetadata MergePcMetadata(PcMetadata scoped, PcMetadata registered)
    {
        var nickname = scoped.HasNickname ? scoped.Nickname : registered.Nickname;
        var faction = scoped.HasFaction ? scoped.Faction : registered.Faction;
        var characterClass = scoped.CharacterClass ?? registered.CharacterClass;
        var isLocalPlayer = scoped.IsLocalPlayer || registered.IsLocalPlayer;
        var originServerId = scoped.OriginServerId is > 0 ? scoped.OriginServerId : registered.OriginServerId;
        var legionName = scoped.HasLegionName ? scoped.LegionName : registered.LegionName;
        var entityId = scoped.EntityId > 0 ? scoped.EntityId : registered.EntityId;
        return new PcMetadata(entityId, nickname, faction, characterClass, isLocalPlayer, originServerId, legionName);
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
