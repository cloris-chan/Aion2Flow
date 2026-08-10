using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.SceneRuntime.Identity;

public enum Faction : byte
{
    Unknown = 0,
    Light = 1,
    Dark = 2
}

public readonly record struct PcMetadata(int EntityId, string Nickname, Faction Faction = Faction.Unknown, CharacterClass? CharacterClass = null, bool IsLocalPlayer = false, int? OriginServerId = null, string LegionName = "", PlayerGroupRelation GroupRelation = PlayerGroupRelation.Unknown)
{
    public bool HasNickname => !string.IsNullOrWhiteSpace(Nickname);
    public bool HasFaction => Faction != Faction.Unknown;
    public bool HasOriginServerId => OriginServerId is > 0;
    public bool HasLegionName => !string.IsNullOrWhiteSpace(LegionName);
    public bool HasGroupRelation => GroupRelation != PlayerGroupRelation.Unknown;
}

public readonly record struct PcMetadataEntry(int EntityId, PcMetadata Metadata);

public readonly record struct NpcCodeEntry(int InstanceId, int NpcCode);

public readonly record struct MapCodeEntry(uint InstanceId, uint MapCode);

internal readonly record struct PlayerIdentityProfileKey(int OriginServerId, string Nickname);

internal readonly record struct PlayerGroupProfileEntry(
    PlayerIdentityProfileKey Identity,
    PlayerGroupMembership Membership);

internal sealed class RuntimeMetadataContinuity(
    PcMetadata? localPlayer,
    PlayerIdentityProfileKey? localPlayerIdentity,
    PlayerGroupProfileEntry[] groupProfiles)
{
    private readonly PlayerGroupProfileEntry[] _groupProfiles = groupProfiles;

    public PcMetadata? LocalPlayer { get; } = localPlayer;
    public PlayerIdentityProfileKey? LocalPlayerIdentity { get; } = localPlayerIdentity;
    public ReadOnlySpan<PlayerGroupProfileEntry> GroupProfiles => _groupProfiles;
}

public sealed class RuntimeMetadataRegistry
{
    private readonly Dictionary<int, PcMetadata> _pcMetadataByEntityId = [];
    private readonly Dictionary<int, PlayerGroupMembership> _partyMembershipByEntityId = [];
    private readonly Dictionary<int, PlayerGroupMembership> _forceMembershipByEntityId = [];
    private readonly Dictionary<PlayerIdentityProfileKey, PlayerGroupMembership> _profileMembershipByKey = [];
    private readonly Dictionary<int, int> _npcCodesByInstanceId = [];
    private readonly Dictionary<uint, uint> _mapCodesByInstanceId = [];
    private int _localPlayerEntityId;
    private PlayerIdentityProfileKey? _localPlayerIdentity;
    private long _revision;

    public RuntimeMetadataRegistry()
    {
    }

    internal RuntimeMetadataRegistry(RuntimeMetadataContinuity continuity)
    {
        ArgumentNullException.ThrowIfNull(continuity);

        _localPlayerIdentity = continuity.LocalPlayerIdentity;
        foreach (var profile in continuity.GroupProfiles)
            _profileMembershipByKey[profile.Identity] = profile.Membership;

        if (continuity.LocalPlayer is not { } localPlayer)
            return;

        UpsertPcMetadata(
            localPlayer.EntityId,
            localPlayer.Nickname,
            localPlayer.Faction,
            localPlayer.CharacterClass,
            isLocalPlayer: true,
            originServerId: localPlayer.OriginServerId,
            legionName: localPlayer.LegionName);
    }

    public IReadOnlyDictionary<int, PcMetadata> PcMetadataByEntityId => _pcMetadataByEntityId;
    public IReadOnlyDictionary<int, int> NpcCodesByInstanceId => _npcCodesByInstanceId;
    public IReadOnlyDictionary<uint, uint> MapCodesByInstanceId => _mapCodesByInstanceId;
    public long Revision => _revision;

    public bool UpsertPcMetadata(int entityId, string nickname, Faction faction = Faction.Unknown, CharacterClass? characterClass = null, bool isLocalPlayer = false, int? originServerId = null, string legionName = "")
    {
        if (entityId <= 0)
            return false;

        nickname ??= string.Empty;
        legionName ??= string.Empty;
        var exists = _pcMetadataByEntityId.TryGetValue(entityId, out var existing);
        var incomingClass = characterClass is CharacterClass.None ? null : characterClass;
        var resolvedNickname = !string.IsNullOrWhiteSpace(nickname)
            ? nickname
            : exists
                ? existing.Nickname
                : string.Empty;
        var resolvedFaction = faction != Faction.Unknown
            ? faction
            : exists
                ? existing.Faction
                : Faction.Unknown;
        var resolvedClass = incomingClass ?? (exists ? existing.CharacterClass : null);
        var resolvedOriginServerId = originServerId is > 0 ? originServerId : exists ? existing.OriginServerId : null;
        var resolvedLegionName = !string.IsNullOrWhiteSpace(legionName) ? legionName : exists ? existing.LegionName : string.Empty;
        var hasIdentityProfile = TryCreateIdentityProfile(
            resolvedOriginServerId,
            resolvedNickname,
            out var identityProfile);
        var isKnownLocalIdentity = hasIdentityProfile &&
                                   _localPlayerIdentity is { } localIdentity &&
                                   localIdentity == identityProfile;
        var supersedesStaleLocalBinding = exists &&
                                          existing.IsLocalPlayer &&
                                          !isLocalPlayer &&
                                          hasIdentityProfile &&
                                          (_localPlayerIdentity is not { } persistedIdentity ||
                                           persistedIdentity != identityProfile);
        var resolvedIsLocalPlayer = isLocalPlayer ||
                                    isKnownLocalIdentity ||
                                    exists && existing.IsLocalPlayer && !supersedesStaleLocalBinding;
        var changed = false;
        var recalculateGroupRelations = false;

        if (resolvedIsLocalPlayer && hasIdentityProfile)
            changed |= SetLocalPlayerIdentity(identityProfile);

        if (resolvedIsLocalPlayer)
        {
            List<int>? previousLocalEntityIds = null;
            foreach (var (candidateId, candidate) in _pcMetadataByEntityId)
            {
                if (candidateId == entityId || !candidate.IsLocalPlayer)
                    continue;

                previousLocalEntityIds ??= [];
                previousLocalEntityIds.Add(candidateId);
            }

            if (previousLocalEntityIds is not null)
            {
                for (var i = 0; i < previousLocalEntityIds.Count; i++)
                {
                    var previousLocalEntityId = previousLocalEntityIds[i];
                    _pcMetadataByEntityId[previousLocalEntityId] = _pcMetadataByEntityId[previousLocalEntityId] with { IsLocalPlayer = false };
                }

                changed = true;
            }

            if (_localPlayerEntityId != entityId)
            {
                _localPlayerEntityId = entityId;
                recalculateGroupRelations = true;
            }
        }
        else if (supersedesStaleLocalBinding && _localPlayerEntityId == entityId)
        {
            _localPlayerEntityId = 0;
            recalculateGroupRelations = true;
        }

        ref var current = ref CollectionsMarshal.GetValueRefOrAddDefault(_pcMetadataByEntityId, entityId, out _);
        var resolvedGroupRelation = resolvedIsLocalPlayer
            ? PlayerGroupRelation.Unknown
            : exists
                ? existing.GroupRelation
                : PlayerGroupRelation.Unknown;
        var next = new PcMetadata(entityId, resolvedNickname, resolvedFaction, resolvedClass, resolvedIsLocalPlayer, resolvedOriginServerId, resolvedLegionName, resolvedGroupRelation);
        if (!exists || !current.Equals(next))
        {
            current = next;
            changed = true;
        }

        if (hasIdentityProfile)
        {
            changed |= ApplyProfileGroupMembership(entityId, identityProfile);
            changed |= PersistEntityGroupProfile(entityId, identityProfile);
        }

        if (recalculateGroupRelations)
            changed |= RecalculateGroupRelations();

        if (changed)
            _revision++;
        return changed;
    }

    public bool UpsertPlayerGroupMembership(int entityId, PlayerGroupMembership membership)
    {
        if (entityId <= 0 || !membership.IsKnown)
            return false;

        var changed = UpsertPlayerGroupMembershipCore(entityId, membership);
        if (_pcMetadataByEntityId.TryGetValue(entityId, out var metadata) &&
            TryCreateIdentityProfile(metadata.OriginServerId, metadata.Nickname, out var identityProfile))
        {
            changed |= UpsertProfileMembership(identityProfile, membership);
        }

        if (changed)
            _revision++;
        return changed;
    }

    public bool UpsertPlayerGroupProfile(int originServerId, string nickname, PlayerGroupMembership membership)
    {
        if (originServerId <= 0 || string.IsNullOrWhiteSpace(nickname) || !membership.IsKnown)
            return false;

        var key = new PlayerIdentityProfileKey(originServerId, nickname);
        var changed = false;
        changed |= UpsertProfileMembership(key, membership);
        var effectiveMembership = _profileMembershipByKey[key];

        List<int>? matchingEntityIds = null;
        foreach (var (entityId, metadata) in _pcMetadataByEntityId)
        {
            if (metadata.OriginServerId == originServerId && string.Equals(metadata.Nickname, nickname, StringComparison.Ordinal))
            {
                matchingEntityIds ??= [];
                matchingEntityIds.Add(entityId);
            }
        }

        if (matchingEntityIds is not null)
        {
            for (var i = 0; i < matchingEntityIds.Count; i++)
                changed |= UpsertPlayerGroupMembershipCore(matchingEntityIds[i], effectiveMembership);
        }

        if (changed)
            _revision++;
        return changed;
    }

    public bool UpsertNpcCode(int instanceId, int npcCode)
    {
        if (instanceId <= 0 || npcCode <= 0)
            return false;

        ref var current = ref CollectionsMarshal.GetValueRefOrAddDefault(_npcCodesByInstanceId, instanceId, out var exists);
        if (exists && current == npcCode)
            return false;

        current = npcCode;
        _revision++;
        return true;
    }

    public bool UpsertMapCode(uint mapInstanceId, uint mapCode)
    {
        if (mapInstanceId == 0 || mapCode == 0)
            return false;

        ref var current = ref CollectionsMarshal.GetValueRefOrAddDefault(_mapCodesByInstanceId, mapInstanceId, out var exists);
        if (exists && current == mapCode)
            return false;

        current = mapCode;
        _revision++;
        return true;
    }

    public bool TryGetPcMetadata(int entityId, out PcMetadata metadata) =>
        _pcMetadataByEntityId.TryGetValue(entityId, out metadata);

    public bool TryGetNpcCode(int instanceId, out int npcCode) =>
        _npcCodesByInstanceId.TryGetValue(instanceId, out npcCode);

    public bool TryGetMapCode(uint mapInstanceId, out uint mapCode) =>
        _mapCodesByInstanceId.TryGetValue(mapInstanceId, out mapCode);

    internal RuntimeMetadataContinuity CreateContinuity()
    {
        PcMetadata? localPlayer = null;
        if (_localPlayerEntityId > 0 &&
            _pcMetadataByEntityId.TryGetValue(_localPlayerEntityId, out var currentLocalPlayer) &&
            currentLocalPlayer.IsLocalPlayer)
        {
            localPlayer = currentLocalPlayer;
        }
        else
        {
            foreach (var metadata in _pcMetadataByEntityId.Values)
            {
                if (metadata.IsLocalPlayer)
                {
                    localPlayer = metadata;
                    break;
                }
            }
        }

        var groupProfiles = new PlayerGroupProfileEntry[_profileMembershipByKey.Count];
        var index = 0;
        foreach (var (identity, membership) in _profileMembershipByKey)
            groupProfiles[index++] = new PlayerGroupProfileEntry(identity, membership);

        return new RuntimeMetadataContinuity(localPlayer, _localPlayerIdentity, groupProfiles);
    }

    internal RuntimeMetadataRegistrySnapshot CreateSnapshot()
    {
        var pcMetadata = new PcMetadataEntry[_pcMetadataByEntityId.Count];
        var index = 0;
        foreach (var (entityId, metadata) in _pcMetadataByEntityId)
            pcMetadata[index++] = new PcMetadataEntry(entityId, metadata);

        var npcCodes = new NpcCodeEntry[_npcCodesByInstanceId.Count];
        index = 0;
        foreach (var (instanceId, npcCode) in _npcCodesByInstanceId)
            npcCodes[index++] = new NpcCodeEntry(instanceId, npcCode);

        var mapCodes = new MapCodeEntry[_mapCodesByInstanceId.Count];
        index = 0;
        foreach (var (instanceId, mapCode) in _mapCodesByInstanceId)
            mapCodes[index++] = new MapCodeEntry(instanceId, mapCode);

        return new RuntimeMetadataRegistrySnapshot(pcMetadata, npcCodes, mapCodes, _revision);
    }

    internal static RuntimeMetadataRegistry FromSnapshot(RuntimeMetadataRegistrySnapshot snapshot)
    {
        var registry = new RuntimeMetadataRegistry
        {
            _revision = snapshot.Revision
        };
        for (var i = 0; i < snapshot.PcMetadata.Length; i++)
        {
            var entry = snapshot.PcMetadata[i];
            registry._pcMetadataByEntityId[entry.EntityId] = entry.Metadata;
            if (entry.Metadata.IsLocalPlayer)
            {
                registry._localPlayerEntityId = entry.EntityId;
                if (TryCreateIdentityProfile(entry.Metadata.OriginServerId, entry.Metadata.Nickname, out var localIdentity))
                    registry._localPlayerIdentity = localIdentity;
            }
        }

        for (var i = 0; i < snapshot.NpcCodes.Length; i++)
        {
            var entry = snapshot.NpcCodes[i];
            registry._npcCodesByInstanceId[entry.InstanceId] = entry.NpcCode;
        }

        for (var i = 0; i < snapshot.MapCodes.Length; i++)
        {
            var entry = snapshot.MapCodes[i];
            registry._mapCodesByInstanceId[entry.InstanceId] = entry.MapCode;
        }

        return registry;
    }

    public void Clear()
    {
        if (_pcMetadataByEntityId.Count == 0 &&
            _partyMembershipByEntityId.Count == 0 &&
            _forceMembershipByEntityId.Count == 0 &&
            _profileMembershipByKey.Count == 0 &&
            _npcCodesByInstanceId.Count == 0 &&
            _mapCodesByInstanceId.Count == 0 &&
            _localPlayerIdentity is null)
            return;

        _pcMetadataByEntityId.Clear();
        _partyMembershipByEntityId.Clear();
        _forceMembershipByEntityId.Clear();
        _profileMembershipByKey.Clear();
        _npcCodesByInstanceId.Clear();
        _mapCodesByInstanceId.Clear();
        _localPlayerEntityId = 0;
        _localPlayerIdentity = null;
        _revision++;
    }

    private bool SetLocalPlayerIdentity(in PlayerIdentityProfileKey identity)
    {
        if (_localPlayerIdentity is { } currentIdentity && currentIdentity == identity)
            return false;

        var hadPreviousIdentity = _localPlayerIdentity is not null;
        _localPlayerIdentity = identity;
        if (hadPreviousIdentity)
            _profileMembershipByKey.Clear();

        return true;
    }

    private bool UpsertProfileMembership(
        in PlayerIdentityProfileKey identity,
        in PlayerGroupMembership membership)
    {
        ref var current = ref CollectionsMarshal.GetValueRefOrAddDefault(
            _profileMembershipByKey,
            identity,
            out var exists);
        var effectiveMembership = exists
            ? ResolveProfileMembership(current, membership)
            : membership;
        if (exists && current.Equals(effectiveMembership))
            return false;

        current = effectiveMembership;
        return true;
    }

    private bool PersistEntityGroupProfile(int entityId, in PlayerIdentityProfileKey identity)
    {
        if (_partyMembershipByEntityId.TryGetValue(entityId, out var partyMembership))
            return UpsertProfileMembership(identity, partyMembership);

        return _forceMembershipByEntityId.TryGetValue(entityId, out var forceMembership) &&
               UpsertProfileMembership(identity, forceMembership);
    }

    private static bool TryCreateIdentityProfile(
        int? originServerId,
        string nickname,
        out PlayerIdentityProfileKey identity)
    {
        if (originServerId is > 0 && !string.IsNullOrWhiteSpace(nickname))
        {
            identity = new PlayerIdentityProfileKey(originServerId.Value, nickname);
            return true;
        }

        identity = default;
        return false;
    }

    private bool UpsertPlayerGroupMembershipCore(int entityId, in PlayerGroupMembership membership)
    {
        if (entityId <= 0 || !membership.IsKnown)
            return false;

        var changed = membership.Kind switch
        {
            PlayerGroupKind.Party => UpsertMembership(_partyMembershipByEntityId, entityId, membership),
            PlayerGroupKind.Force => UpsertMembership(_forceMembershipByEntityId, entityId, membership),
            _ => false
        };

        changed |= ApplyCurrentGroupRelation(entityId);

        if (entityId == _localPlayerEntityId && membership.Kind == PlayerGroupKind.Force)
            changed |= RecalculateGroupRelations();

        return changed;
    }

    private static bool UpsertMembership(Dictionary<int, PlayerGroupMembership> memberships, int entityId, in PlayerGroupMembership membership)
    {
        ref var current = ref CollectionsMarshal.GetValueRefOrAddDefault(memberships, entityId, out var exists);
        if (exists && current.Equals(membership))
            return false;

        current = membership;
        return true;
    }

    private static PlayerGroupMembership ResolveProfileMembership(in PlayerGroupMembership current, in PlayerGroupMembership incoming)
    {
        // Party profiles carry narrower membership than force roster profiles for the same player identity.
        if (current.Kind == PlayerGroupKind.Party && incoming.Kind == PlayerGroupKind.Force)
            return current;

        return incoming;
    }

    private bool ApplyProfileGroupMembership(int entityId, in PlayerIdentityProfileKey identity)
    {
        return _profileMembershipByKey.TryGetValue(identity, out var membership) &&
               UpsertPlayerGroupMembershipCore(entityId, membership);
    }

    private bool ApplyCurrentGroupRelation(int entityId)
    {
        if (entityId == _localPlayerEntityId)
            return SetGroupRelation(entityId, PlayerGroupRelation.Unknown);

        if (_partyMembershipByEntityId.ContainsKey(entityId))
            return SetGroupRelation(entityId, PlayerGroupRelation.PartyMember);

        return _forceMembershipByEntityId.TryGetValue(entityId, out var membership) &&
               ApplyForceGroupRelation(entityId, membership);
    }

    private bool ApplyForceGroupRelation(int entityId, in PlayerGroupMembership membership)
    {
        if (entityId == _localPlayerEntityId)
            return SetGroupRelation(entityId, PlayerGroupRelation.Unknown);

        if (_partyMembershipByEntityId.ContainsKey(entityId))
            return SetGroupRelation(entityId, PlayerGroupRelation.PartyMember);

        if (membership.GroupId == 0)
            return SetGroupRelation(entityId, PlayerGroupRelation.ForceMember);

        if (!_forceMembershipByEntityId.TryGetValue(_localPlayerEntityId, out var localMembership) ||
            localMembership.GroupId == 0 ||
            membership.GroupId != localMembership.GroupId)
            return false;

        return SetGroupRelation(
            entityId,
            membership.SubPartyIndex != 0 && membership.SubPartyIndex == localMembership.SubPartyIndex
                ? PlayerGroupRelation.PartyMember
                : PlayerGroupRelation.ForceMember);
    }

    private bool RecalculateGroupRelations()
    {
        var changed = false;
        foreach (var entityId in _partyMembershipByEntityId.Keys)
            changed |= ApplyCurrentGroupRelation(entityId);

        foreach (var entityId in _forceMembershipByEntityId.Keys)
            changed |= ApplyCurrentGroupRelation(entityId);

        return changed;
    }

    private bool SetGroupRelation(int entityId, PlayerGroupRelation relation)
    {
        if (entityId <= 0)
            return false;

        ref var metadata = ref CollectionsMarshal.GetValueRefOrAddDefault(_pcMetadataByEntityId, entityId, out var exists);
        var next = exists
            ? metadata with { GroupRelation = relation }
            : new PcMetadata(entityId, string.Empty, GroupRelation: relation);
        if (exists && metadata.Equals(next))
            return false;

        metadata = next;
        return true;
    }
}

internal sealed record RuntimeMetadataRegistrySnapshot(PcMetadataEntry[] PcMetadata, NpcCodeEntry[] NpcCodes, MapCodeEntry[] MapCodes, long Revision);

public readonly struct SceneIdentityScope
{
    private static readonly PcMetadataEntry[] EmptyPcMetadata = [];
    private static readonly NpcCodeEntry[] EmptyNpcCodes = [];
    private static readonly MapCodeEntry[] EmptyMapCodes = [];

    private readonly PcMetadataEntry[]? _pcMetadata;
    private readonly NpcCodeEntry[]? _npcCodes;
    private readonly MapCodeEntry[]? _mapCodes;

    internal SceneIdentityScope(PcMetadataEntry[] pcMetadata, NpcCodeEntry[] npcCodes, MapCodeEntry[] mapCodes)
    {
        _pcMetadata = pcMetadata.Length == 0 ? EmptyPcMetadata : pcMetadata;
        _npcCodes = npcCodes.Length == 0 ? EmptyNpcCodes : npcCodes;
        _mapCodes = mapCodes.Length == 0 ? EmptyMapCodes : mapCodes;
    }

    public static SceneIdentityScope Empty => default;
    public bool IsEmpty => PcMetadataSpan.Length == 0 && NpcCodeSpan.Length == 0 && MapCodeSpan.Length == 0;
    public ReadOnlySpan<PcMetadataEntry> PcMetadataSpan => _pcMetadata ?? EmptyPcMetadata;
    public ReadOnlySpan<NpcCodeEntry> NpcCodeSpan => _npcCodes ?? EmptyNpcCodes;
    public ReadOnlySpan<MapCodeEntry> MapCodeSpan => _mapCodes ?? EmptyMapCodes;
    public ReadOnlySpan<PcMetadataEntry> PcMetadataAsSpan() => PcMetadataSpan;
    public ReadOnlySpan<NpcCodeEntry> NpcCodesAsSpan() => NpcCodeSpan;
    public ReadOnlySpan<MapCodeEntry> MapCodesAsSpan() => MapCodeSpan;

    public bool TryGetPcMetadata(int entityId, out PcMetadata metadata)
    {
        var span = PcMetadataSpan;
        var low = 0;
        var high = span.Length - 1;
        while (low <= high)
        {
            var mid = (int)(((uint)low + (uint)high) >> 1);
            var current = span[mid].EntityId;
            if (current == entityId)
            {
                metadata = span[mid].Metadata;
                return true;
            }

            if (current < entityId)
                low = mid + 1;
            else
                high = mid - 1;
        }

        metadata = default;
        return false;
    }

    public bool TryGetNpcCode(int instanceId, out int npcCode)
    {
        var span = NpcCodeSpan;
        var low = 0;
        var high = span.Length - 1;
        while (low <= high)
        {
            var mid = (int)(((uint)low + (uint)high) >> 1);
            var current = span[mid].InstanceId;
            if (current == instanceId)
            {
                npcCode = span[mid].NpcCode;
                return true;
            }

            if (current < instanceId)
                low = mid + 1;
            else
                high = mid - 1;
        }

        npcCode = 0;
        return false;
    }

    public bool TryGetMapCode(uint mapInstanceId, out uint mapCode)
    {
        var span = MapCodeSpan;
        var low = 0;
        var high = span.Length - 1;
        while (low <= high)
        {
            var mid = (int)(((uint)low + (uint)high) >> 1);
            var current = span[mid].InstanceId;
            if (current == mapInstanceId)
            {
                mapCode = span[mid].MapCode;
                return true;
            }

            if (current < mapInstanceId)
                low = mid + 1;
            else
                high = mid - 1;
        }

        mapCode = 0;
        return false;
    }
}

public sealed class SceneIdentityScopeBuilder
{
    private readonly Dictionary<int, PcMetadata> _pcMetadata = [];
    private readonly Dictionary<int, int> _npcCodes = [];
    private readonly Dictionary<uint, uint> _mapCodes = [];

    public void Reset(int entityCapacity = 0)
    {
        _pcMetadata.Clear();
        _npcCodes.Clear();
        _mapCodes.Clear();
        if (entityCapacity > 0)
        {
            _pcMetadata.EnsureCapacity(entityCapacity);
            _npcCodes.EnsureCapacity(entityCapacity);
        }
    }

    public void AddPcMetadata(PcMetadata metadata)
    {
        if (metadata.EntityId <= 0 || !metadata.HasNickname && metadata.CharacterClass is null && !metadata.IsLocalPlayer && metadata.OriginServerId is null && !metadata.HasLegionName && !metadata.HasGroupRelation)
            return;

        _pcMetadata[metadata.EntityId] = metadata;
    }

    public void AddNpcCode(int instanceId, int npcCode)
    {
        if (instanceId <= 0 || npcCode <= 0)
            return;

        _npcCodes[instanceId] = npcCode;
    }

    public void AddMapCode(uint mapInstanceId, uint mapCode)
    {
        if (mapInstanceId == 0 || mapCode == 0)
            return;

        _mapCodes[mapInstanceId] = mapCode;
    }

    public SceneIdentityScope ToScope()
    {
        var pcs = _pcMetadata.Count == 0 ? [] : new PcMetadataEntry[_pcMetadata.Count];
        var index = 0;
        foreach (var (entityId, metadata) in _pcMetadata)
            pcs[index++] = new PcMetadataEntry(entityId, metadata);
        Array.Sort(pcs, static (left, right) => left.EntityId.CompareTo(right.EntityId));

        var npcs = _npcCodes.Count == 0 ? [] : new NpcCodeEntry[_npcCodes.Count];
        index = 0;
        foreach (var (instanceId, npcCode) in _npcCodes)
            npcs[index++] = new NpcCodeEntry(instanceId, npcCode);
        Array.Sort(npcs, static (left, right) => left.InstanceId.CompareTo(right.InstanceId));

        var maps = _mapCodes.Count == 0 ? [] : new MapCodeEntry[_mapCodes.Count];
        index = 0;
        foreach (var (instanceId, mapCode) in _mapCodes)
            maps[index++] = new MapCodeEntry(instanceId, mapCode);
        Array.Sort(maps, static (left, right) => left.InstanceId.CompareTo(right.InstanceId));

        return new SceneIdentityScope(pcs, npcs, maps);
    }
}

public readonly struct SceneIdentityResolver(SceneIdentityScope scope, RuntimeMetadataRegistry? registry)
{
    public bool TryGetPcMetadata(int entityId, out PcMetadata metadata)
    {
        if (scope.TryGetPcMetadata(entityId, out metadata))
            return true;

        return registry is not null && registry.TryGetPcMetadata(entityId, out metadata);
    }

    public bool TryGetNpcCode(int instanceId, out int npcCode)
    {
        if (scope.TryGetNpcCode(instanceId, out npcCode))
            return true;

        return registry is not null && registry.TryGetNpcCode(instanceId, out npcCode);
    }

    public bool TryGetMapCode(uint mapInstanceId, out uint mapCode)
    {
        if (scope.TryGetMapCode(mapInstanceId, out mapCode))
            return true;

        return registry is not null && registry.TryGetMapCode(mapInstanceId, out mapCode);
    }

}
