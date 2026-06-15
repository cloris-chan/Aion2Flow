using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.SceneRuntime.Identity;

public enum Faction : byte
{
    Unknown = 0,
    Light = 1,
    Dark = 2
}

public readonly record struct PcMetadata(int EntityId, string Nickname, int? OriginServerId, Faction Faction = Faction.Unknown, CharacterClass? CharacterClass = null, bool IsLocalPlayer = false)
{
    public bool HasNickname => !string.IsNullOrWhiteSpace(Nickname);
    public bool HasFaction => Faction != Faction.Unknown;
}

public readonly record struct PcMetadataEntry(int EntityId, PcMetadata Metadata);

public readonly record struct NpcCodeEntry(int InstanceId, int NpcCode);

public readonly record struct MapCodeEntry(uint InstanceId, uint MapCode);

public sealed class RuntimeMetadataRegistry
{
    private readonly Dictionary<int, PcMetadata> _pcMetadataByEntityId = [];
    private readonly Dictionary<int, int> _npcCodesByInstanceId = [];
    private readonly Dictionary<uint, uint> _mapCodesByInstanceId = [];
    private long _revision;

    public IReadOnlyDictionary<int, PcMetadata> PcMetadataByEntityId => _pcMetadataByEntityId;
    public IReadOnlyDictionary<int, int> NpcCodesByInstanceId => _npcCodesByInstanceId;
    public IReadOnlyDictionary<uint, uint> MapCodesByInstanceId => _mapCodesByInstanceId;
    public long Revision => _revision;

    public bool UpsertPcMetadata(int entityId, string nickname, int? originServerId = null, Faction faction = Faction.Unknown, CharacterClass? characterClass = null, bool isLocalPlayer = false)
    {
        if (entityId <= 0)
            return false;

        nickname ??= string.Empty;
        var changed = false;
        if (isLocalPlayer)
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
        }

        ref var current = ref CollectionsMarshal.GetValueRefOrAddDefault(_pcMetadataByEntityId, entityId, out var exists);
        var incomingClass = characterClass is CharacterClass.None ? null : characterClass;
        var resolvedOriginServerId = originServerId ?? (exists ? current.OriginServerId : null);
        var resolvedFaction = faction != Faction.Unknown
            ? faction
            : exists
                ? current.Faction
                : Faction.Unknown;
        var resolvedClass = incomingClass ?? (exists ? current.CharacterClass : null);
        var resolvedIsLocalPlayer = isLocalPlayer || exists && current.IsLocalPlayer;
        var next = new PcMetadata(entityId, nickname, resolvedOriginServerId, resolvedFaction, resolvedClass, resolvedIsLocalPlayer);
        if (exists && current.Equals(next))
        {
            if (changed)
                _revision++;
            return changed;
        }

        current = next;
        _revision++;
        return true;
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
        var registry = new RuntimeMetadataRegistry();
        registry._revision = snapshot.Revision;
        for (var i = 0; i < snapshot.PcMetadata.Length; i++)
        {
            var entry = snapshot.PcMetadata[i];
            registry._pcMetadataByEntityId[entry.EntityId] = entry.Metadata;
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
        if (_pcMetadataByEntityId.Count == 0 && _npcCodesByInstanceId.Count == 0 && _mapCodesByInstanceId.Count == 0)
            return;

        _pcMetadataByEntityId.Clear();
        _npcCodesByInstanceId.Clear();
        _mapCodesByInstanceId.Clear();
        _revision++;
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

    public SceneIdentityScope DeepClone()
    {
        var pcs = PcMetadataSpan.ToArray();
        var npcs = NpcCodeSpan.ToArray();
        var maps = MapCodeSpan.ToArray();
        return new SceneIdentityScope(pcs, npcs, maps);
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
        if (metadata.EntityId <= 0 || !metadata.HasNickname && metadata.CharacterClass is null && !metadata.IsLocalPlayer)
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
