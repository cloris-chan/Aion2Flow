using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public sealed class EntityStore
{
    private readonly Dictionary<int, EntityRecord> _entities = [];
    private long _identityRevision;
    private long _volatileStateRevision;

    public IReadOnlyDictionary<int, EntityRecord> Entities => _entities;
    public int Count => _entities.Count;
    public long IdentityRevision => _identityRevision;
    public long VolatileStateRevision => _volatileStateRevision;

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
        _identityRevision++;
    }

    public void ApplyNpcKind(int instanceId, NpcKind kind)
    {
        var entity = GetOrAdd(instanceId);
        if (entity.Kind == kind)
            return;

        entity.Kind = kind;
        _identityRevision++;
    }

    public void ApplyNickname(int entityId, string nickname)
    {
        var entity = GetOrAdd(entityId);
        if (entity.Nickname == nickname && entity.IsPlayer)
            return;

        entity.Nickname = nickname;
        entity.IsPlayer = true;
        entity.LastObservedOrdinal++;
        _identityRevision++;
    }

    public void ApplyPlayerIdentity(int entityId)
    {
        var entity = GetOrAdd(entityId);
        if (entity.IsPlayer)
            return;

        entity.IsPlayer = true;
        entity.LastObservedOrdinal++;
        _identityRevision++;
    }

    public bool ApplyCharacterClassEvidence(
        int entityId,
        in CombatWireObservation observation,
        in CombatContribution contribution)
    {
        if (entityId <= 0 ||
            !CombatantClassEvidence.TryCreate(in observation, in contribution, out var characterClass, out var score))
        {
            return false;
        }

        var entity = GetOrAdd(entityId);
        if (entity.OwnerEntityId.HasValue || entity.NpcCode.HasValue || entity.Kind is NpcKind.Monster or NpcKind.Boss or NpcKind.Friendly or NpcKind.Summon or NpcKind.TrainingDummy)
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
        _identityRevision++;
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
        _identityRevision++;
        return true;
    }

    public void ApplySummon(int ownerId, int summonInstanceId)
    {
        var entity = GetOrAdd(summonInstanceId);
        if (entity.OwnerEntityId == ownerId && entity.OwnerKind == EntityOwnerKind.Summon && entity.Kind == NpcKind.Summon)
            return;

        entity.OwnerEntityId = ownerId;
        entity.OwnerKind = EntityOwnerKind.Summon;
        entity.Kind = NpcKind.Summon;
        _identityRevision++;
    }

    public void ApplyBattleToggle(int instanceId, bool isActive)
    {
        var entity = GetOrAdd(instanceId);
        if (entity.NpcCombatActive == isActive)
            return;

        entity.NpcCombatActive = isActive;
        _volatileStateRevision++;
    }

    public void ApplyNpc2136State(int instanceId, long sequence, long value0)
    {
        var entity = GetOrAdd(instanceId);
        if (entity.Sequence2136 == sequence && entity.Value2136 == value0)
            return;

        entity.Sequence2136 = sequence;
        entity.Value2136 = value0;
        _volatileStateRevision++;
    }

    public void ApplyNpc0140Value(int instanceId, long value0)
    {
        var entity = GetOrAdd(instanceId);
        if (entity.Value0140 == value0)
            return;

        entity.Value0140 = value0;
        _volatileStateRevision++;
    }

    public void ApplyNpc0240Value(int instanceId, long value0)
    {
        var entity = GetOrAdd(instanceId);
        if (entity.Value0240 == value0)
            return;

        entity.Value0240 = value0;
        _volatileStateRevision++;
    }

    public void ApplyNpc4636State(int instanceId, byte state0, byte state1)
    {
        var entity = GetOrAdd(instanceId);
        if (entity.State4636 == (state0, state1))
            return;

        entity.State4636 = (state0, state1);
        _volatileStateRevision++;
    }

    public void ApplyNpc2C38State(int instanceId, int sequenceId, int resultCode)
    {
        var entity = GetOrAdd(instanceId);
        if (entity.Latest2C38 == (sequenceId, resultCode))
            return;

        entity.Latest2C38 = (sequenceId, resultCode);
        _volatileStateRevision++;
    }

    public bool IsKnownEntity(int entityId) =>
        _entities.ContainsKey(entityId);

    public void Clear()
    {
        if (_entities.Count == 0)
            return;

        _entities.Clear();
        _identityRevision++;
        _volatileStateRevision++;
    }

}

public enum EntityOwnerKind
{
    None,
    Summon
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
    public EntityOwnerKind OwnerKind { get; set; }
    public bool NpcCombatActive { get; set; }
    public long? Value2136 { get; set; }
    public long? Sequence2136 { get; set; }
    public long? Value0140 { get; set; }
    public long? Value0240 { get; set; }
    public (byte State0, byte State1)? State4636 { get; set; }
    public (int SequenceId, int ResultCode)? Latest2C38 { get; set; }
    public long LastObservedOrdinal { get; set; }

}
