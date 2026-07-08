using System.Globalization;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.Services;

public sealed class SceneDisplayContext(SceneIdentityScope identityScope, RuntimeMetadataRegistry? metadataRegistry, SceneCombatSnapshot? snapshot, GameResourceService resources, string unknownSceneName)
{
    private AnonymousOrdinalIndex? _anonymousOrdinalIndex;
    private long _anonymousOrdinalIndexMetadataRevision = long.MinValue;

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

        return GetAnonymousOrdinalIndex().Resolve(entityId, this);
    }

    private AnonymousOrdinalIndex GetAnonymousOrdinalIndex()
    {
        var metadataRevision = MetadataRegistry?.Revision ?? -1;
        var index = _anonymousOrdinalIndex;
        if (index is not null && _anonymousOrdinalIndexMetadataRevision == metadataRevision)
        {
            return index;
        }

        index = BuildAnonymousOrdinalIndex();
        _anonymousOrdinalIndex = index;
        _anonymousOrdinalIndexMetadataRevision = metadataRevision;
        return index;
    }

    private AnonymousOrdinalIndex BuildAnonymousOrdinalIndex()
    {
        var candidateIds = new List<int>();
        var scoped = IdentityScope.PcMetadataSpan;
        for (var i = 0; i < scoped.Length; i++)
        {
            candidateIds.Add(scoped[i].EntityId);
        }

        if (MetadataRegistry is not null)
        {
            foreach (var (candidateId, _) in MetadataRegistry.PcMetadataByEntityId)
            {
                if (!IdentityScope.TryGetPcMetadata(candidateId, out _))
                {
                    candidateIds.Add(candidateId);
                }
            }
        }

        var combatants = Snapshot.Combatants.AsSpan();
        for (var i = 0; i < combatants.Length; i++)
        {
            ref readonly var entry = ref combatants[i];
            if (!IdentityScope.TryGetPcMetadata(entry.Id, out _) &&
                MetadataRegistry?.TryGetPcMetadata(entry.Id, out _) != true &&
                entry.Metrics.IsVisiblePlayerCombatant)
            {
                candidateIds.Add(entry.Id);
            }
        }

        if (candidateIds.Count == 0)
        {
            return AnonymousOrdinalIndex.Empty;
        }

        candidateIds.Sort();
        var uniqueCount = 0;
        var previousId = 0;
        for (var i = 0; i < candidateIds.Count; i++)
        {
            var candidateId = candidateIds[i];
            if (i > 0 && candidateId == previousId)
            {
                continue;
            }

            candidateIds[uniqueCount++] = candidateId;
            previousId = candidateId;
        }

        var entries = new AnonymousOrdinalEntry[uniqueCount];
        var ordinalsByEntityId = new Dictionary<int, int>(uniqueCount);
        var ordinalsByClass = new Dictionary<int, int>();
        for (var i = 0; i < uniqueCount; i++)
        {
            var candidateId = candidateIds[i];
            var classKey = GetAnonymousClassKey(ResolvePcClass(candidateId));
            ordinalsByClass.TryGetValue(classKey, out var ordinal);
            ordinal++;
            ordinalsByClass[classKey] = ordinal;
            entries[i] = new AnonymousOrdinalEntry(candidateId, classKey, ordinal);
            ordinalsByEntityId[candidateId] = ordinal;
        }

        return new AnonymousOrdinalIndex(entries, ordinalsByEntityId);
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
        var groupRelation = scoped.HasGroupRelation ? scoped.GroupRelation : registered.GroupRelation;
        return new PcMetadata(entityId, nickname, faction, characterClass, isLocalPlayer, originServerId, legionName, groupRelation);
    }

    private bool TryGetNpcCode(int instanceId, out int npcCode)
    {
        if (IdentityScope.TryGetNpcCode(instanceId, out npcCode))
        {
            return true;
        }

        return MetadataRegistry is not null && MetadataRegistry.TryGetNpcCode(instanceId, out npcCode);
    }

    private static int GetAnonymousClassKey(CharacterClass? characterClass)
        => characterClass.HasValue ? (int)characterClass.Value : -1;

    private readonly record struct AnonymousOrdinalEntry(int EntityId, int ClassKey, int Ordinal);

    private sealed class AnonymousOrdinalIndex(AnonymousOrdinalEntry[] entries, Dictionary<int, int> ordinalsByEntityId)
    {
        public static AnonymousOrdinalIndex Empty { get; } = new([], []);

        public int Resolve(int entityId, SceneDisplayContext context)
        {
            if (ordinalsByEntityId.TryGetValue(entityId, out var ordinal))
            {
                return ordinal;
            }

            var classKey = GetAnonymousClassKey(context.ResolvePcClass(entityId));
            ordinal = 1;
            for (var i = 0; i < entries.Length; i++)
            {
                ref readonly var entry = ref entries[i];
                if (entry.EntityId >= entityId)
                {
                    break;
                }

                if (entry.ClassKey == classKey)
                {
                    ordinal++;
                }
            }

            return ordinal;
        }
    }
}
