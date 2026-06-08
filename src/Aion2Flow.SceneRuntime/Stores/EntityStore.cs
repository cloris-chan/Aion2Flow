using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public sealed class EntityStore
{
    private readonly Dictionary<int, EntityRecord> _entities = [];
    private long _revision;

    public IReadOnlyDictionary<int, EntityRecord> Entities => _entities;
    public int Count => _entities.Count;
    public long Revision => _revision;

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
        if (entity.NpcCode == npcCode)
            return;

        entity.NpcCode = npcCode;
        entity.LastObservedOrdinal++;
        _revision++;
    }

    public void ApplyNpcKind(int instanceId, NpcKind kind)
    {
        var entity = GetOrAdd(instanceId);
        if (entity.Kind == kind)
            return;

        entity.Kind = kind;
        _revision++;
    }

    public void ApplyNickname(int entityId, string nickname)
    {
        var entity = GetOrAdd(entityId);
        if (entity.Nickname == nickname && entity.IsPlayer)
            return;

        entity.Nickname = nickname;
        entity.IsPlayer = true;
        entity.LastObservedOrdinal++;
        _revision++;
    }

    public bool ApplyCharacterClassEvidence(int entityId, in CombatObservation observation)
    {
        if (entityId <= 0 || !CombatantClassEvidence.TryCreate(in observation, out var characterClass, out var score))
            return false;

        var entity = GetOrAdd(entityId);
        if (entity.NpcCode.HasValue || entity.Kind is NpcKind.Monster or NpcKind.Boss or NpcKind.Friendly or NpcKind.Summon)
            return false;

        if (entity.CharacterClass is not null and not CharacterClass.None)
            return false;

        var evidence = entity.ClassEvidence;
        var previousClass = entity.CharacterClass;
        evidence.Add(characterClass, score);
        var nextClass = evidence.Resolve();
        entity.ClassEvidence = evidence;
        if (nextClass is null or CharacterClass.None)
            return false;

        if (previousClass == nextClass)
            return false;

        entity.CharacterClass = nextClass;
        _revision++;
        return true;
    }

    public bool ApplyMetadataCharacterClass(int entityId, CharacterClass characterClass)
    {
        if (entityId <= 0 || characterClass == CharacterClass.None)
            return false;

        var entity = GetOrAdd(entityId);
        if (entity.CharacterClass == characterClass && entity.IsPlayer)
            return false;

        entity.CharacterClass = characterClass;
        entity.IsPlayer = true;
        entity.LastObservedOrdinal++;
        _revision++;
        return true;
    }

    public void ApplySummon(int ownerId, int summonInstanceId)
    {
        var entity = GetOrAdd(summonInstanceId);
        if (entity.OwnerEntityId == ownerId && entity.Kind == NpcKind.Summon)
            return;

        entity.OwnerEntityId = ownerId;
        entity.Kind = NpcKind.Summon;
        _revision++;
    }

    public void ApplyNpcHp(int instanceId, int hp, int maxHp)
    {
        var entity = GetOrAdd(instanceId);
        var resolvedMaxHp = maxHp > 0 ? Math.Max(maxHp, hp) : Math.Max(entity.MaxHp ?? 0, hp);
        var combatActive = hp != 0 && entity.NpcCombatActive;
        if (entity.CurrentHp == hp && entity.MaxHp == resolvedMaxHp && entity.NpcCombatActive == combatActive)
            return;

        entity.CurrentHp = hp;
        entity.MaxHp = resolvedMaxHp;
        if (hp == 0)
            entity.NpcCombatActive = false;
        _revision++;
    }

    public void ApplyBattleToggle(int instanceId, bool isActive)
    {
        var entity = GetOrAdd(instanceId);
        if (entity.NpcCombatActive == isActive)
            return;

        entity.NpcCombatActive = isActive;
        _revision++;
    }

    public void ApplyNpc2136State(int instanceId, uint sequence, uint value0)
    {
        var entity = GetOrAdd(instanceId);
        if (entity.Sequence2136 == sequence && entity.Value2136 == value0)
            return;

        entity.Sequence2136 = sequence;
        entity.Value2136 = value0;
        _revision++;
    }

    public void ApplyNpc0140Value(int instanceId, uint value0)
    {
        var entity = GetOrAdd(instanceId);
        if (entity.Value0140 == value0)
            return;

        entity.Value0140 = value0;
        _revision++;
    }

    public void ApplyNpc0240Value(int instanceId, uint value0)
    {
        var entity = GetOrAdd(instanceId);
        if (entity.Value0240 == value0)
            return;

        entity.Value0240 = value0;
        _revision++;
    }

    public void ApplyNpc4636State(int instanceId, byte state0, byte state1)
    {
        var entity = GetOrAdd(instanceId);
        if (entity.State4636 == (state0, state1))
            return;

        entity.State4636 = (state0, state1);
        _revision++;
    }

    public void ApplyNpc2C38State(int instanceId, int sequenceId, int resultCode)
    {
        var entity = GetOrAdd(instanceId);
        if (entity.Latest2C38 == (sequenceId, resultCode))
            return;

        entity.Latest2C38 = (sequenceId, resultCode);
        _revision++;
    }

    public bool IsKnownEntity(int entityId) =>
        _entities.ContainsKey(entityId);

    internal EntityStoreSnapshot CreateSnapshot()
    {
        if (_entities.Count == 0)
            return new EntityStoreSnapshot([], _revision);

        var records = new EntityRecordSnapshot[_entities.Count];
        var index = 0;
        foreach (var record in _entities.Values)
            records[index++] = EntityRecordSnapshot.From(record);
        return new EntityStoreSnapshot(records, _revision);
    }

    internal static EntityStore FromSnapshot(EntityStoreSnapshot snapshot)
    {
        var store = new EntityStore();
        store._revision = snapshot.Revision;
        for (var i = 0; i < snapshot.Records.Length; i++)
        {
            var record = snapshot.Records[i].ToRecord();
            store._entities[record.EntityId] = record;
        }

        return store;
    }

    public void Clear()
    {
        if (_entities.Count == 0)
            return;

        _entities.Clear();
        _revision++;
    }

}

internal sealed record EntityStoreSnapshot(EntityRecordSnapshot[] Records, long Revision);

internal readonly record struct EntityRecordSnapshot(
    int EntityId,
    int? NpcCode,
    NpcKind Kind,
    string? Nickname,
    bool IsPlayer,
    CharacterClass? CharacterClass,
    CombatantClassEvidence ClassEvidence,
    int? OwnerEntityId,
    int? CurrentHp,
    int? MaxHp,
    bool NpcCombatActive,
    uint? Value2136,
    uint? Sequence2136,
    uint? Value0140,
    uint? Value0240,
    (byte State0, byte State1)? State4636,
    (int SequenceId, int ResultCode)? Latest2C38,
    long LastObservedOrdinal)
{
    public static EntityRecordSnapshot From(EntityRecord record) => new(
        record.EntityId,
        record.NpcCode,
        record.Kind,
        record.Nickname,
        record.IsPlayer,
        record.CharacterClass,
        record.ClassEvidence,
        record.OwnerEntityId,
        record.CurrentHp,
        record.MaxHp,
        record.NpcCombatActive,
        record.Value2136,
        record.Sequence2136,
        record.Value0140,
        record.Value0240,
        record.State4636,
        record.Latest2C38,
        record.LastObservedOrdinal);

    public EntityRecord ToRecord() => new()
    {
        EntityId = EntityId,
        NpcCode = NpcCode,
        Kind = Kind,
        Nickname = Nickname,
        IsPlayer = IsPlayer,
        CharacterClass = CharacterClass,
        ClassEvidence = ClassEvidence,
        OwnerEntityId = OwnerEntityId,
        CurrentHp = CurrentHp,
        MaxHp = MaxHp,
        NpcCombatActive = NpcCombatActive,
        Value2136 = Value2136,
        Sequence2136 = Sequence2136,
        Value0140 = Value0140,
        Value0240 = Value0240,
        State4636 = State4636,
        Latest2C38 = Latest2C38,
        LastObservedOrdinal = LastObservedOrdinal
    };
}

public sealed class EntityRecord
{
    public int EntityId { get; init; }
    public int? NpcCode { get; set; }
    public NpcKind Kind { get; set; }
    public string? Nickname { get; set; }
    public bool IsPlayer { get; set; }
    public CharacterClass? CharacterClass { get; set; }
    public CombatantClassEvidence ClassEvidence { get; set; }
    public int? OwnerEntityId { get; set; }
    public int? CurrentHp { get; set; }
    public int? MaxHp { get; set; }
    public bool NpcCombatActive { get; set; }
    public uint? Value2136 { get; set; }
    public uint? Sequence2136 { get; set; }
    public uint? Value0140 { get; set; }
    public uint? Value0240 { get; set; }
    public (byte State0, byte State1)? State4636 { get; set; }
    public (int SequenceId, int ResultCode)? Latest2C38 { get; set; }
    public long LastObservedOrdinal { get; set; }

}
