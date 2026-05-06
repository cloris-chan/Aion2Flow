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

    public void ApplyNpc2136State(int instanceId, uint sequence, uint value0)
    {
        var entity = GetOrAdd(instanceId);
        entity.Sequence2136 = sequence;
        entity.Value2136 = value0;
    }

    public void ApplyNpc0140Value(int instanceId, uint value0)
    {
        var entity = GetOrAdd(instanceId);
        entity.Value0140 = value0;
    }

    public void ApplyNpc0240Value(int instanceId, uint value0)
    {
        var entity = GetOrAdd(instanceId);
        entity.Value0240 = value0;
    }

    public void ApplyNpc4636State(int instanceId, byte state0, byte state1)
    {
        var entity = GetOrAdd(instanceId);
        entity.State4636 = (state0, state1);
    }

    public void ApplyNpc2C38State(int instanceId, int sequenceId, int resultCode)
    {
        var entity = GetOrAdd(instanceId);
        entity.Latest2C38 = (sequenceId, resultCode);
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
    public uint? Value2136 { get; set; }
    public uint? Sequence2136 { get; set; }
    public uint? Value0140 { get; set; }
    public uint? Value0240 { get; set; }
    public (byte State0, byte State1)? State4636 { get; set; }
    public (int SequenceId, int ResultCode)? Latest2C38 { get; set; }
    public long LastObservedOrdinal { get; set; }
}
