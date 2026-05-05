using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Battle.Model;

namespace Cloris.Aion2Flow.Scene.Stores;

public sealed class EntityStore
{
    private readonly Dictionary<int, EntityRecord> _entities = [];

    public IReadOnlyDictionary<int, EntityRecord> Entities => _entities;
    public int Count => _entities.Count;

    public EntityRecord GetOrAdd(int entityId)
    {
        ref var record = ref CollectionsMarshal.GetValueRefOrAddDefault(_entities, entityId, out var exists);

        if (!exists)
        {
            record = new EntityRecord { EntityId = entityId };
        }

        return record!;
    }

    public bool TryGet(int entityId, [NotNullWhen(true)] out EntityRecord? record) => _entities.TryGetValue(entityId, out record);

    public void ApplyNpcCode(int instanceId, int npcCode)
    {
        var entity = GetOrAdd(instanceId);
        entity.NpcCode = npcCode;
        entity.LastObservedOrdinal++;
    }

    public void ApplyNpcKind(int instanceId, NpcKind kind)
    {
        var entity = GetOrAdd(instanceId);
        entity.Kind = kind;
    }

    public void ApplyNickname(int entityId, string nickname)
    {
        var entity = GetOrAdd(entityId);
        entity.Nickname = nickname;
        entity.IsPlayer = true;
        entity.LastObservedOrdinal++;
    }

    public void ApplySummon(int ownerId, int summonInstanceId)
    {
        var entity = GetOrAdd(summonInstanceId);
        entity.OwnerEntityId = ownerId;
        entity.Kind = NpcKind.Summon;
    }

    public void ApplyNpcHp(int instanceId, int hp, int maxHp)
    {
        var entity = GetOrAdd(instanceId);
        entity.CurrentHp = hp;
        entity.MaxHp = maxHp;
    }

    public void ApplyBattleToggle(int instanceId, bool isActive)
    {
        var entity = GetOrAdd(instanceId);
        entity.BattleActive = isActive;
    }

    public bool IsKnownEntity(int entityId) =>
        _entities.ContainsKey(entityId);

    public void Clear() => _entities.Clear();
}

public sealed class EntityRecord
{
    public int EntityId { get; init; }
    public int? NpcCode { get; set; }
    public NpcKind Kind { get; set; }
    public string? Nickname { get; set; }
    public bool IsPlayer { get; set; }
    public int? OwnerEntityId { get; set; }
    public int? CurrentHp { get; set; }
    public int? MaxHp { get; set; }
    public bool BattleActive { get; set; }
    public long LastObservedOrdinal { get; set; }
}
