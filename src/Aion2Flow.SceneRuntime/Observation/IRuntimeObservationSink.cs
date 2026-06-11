using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.SceneRuntime.Observation;

public interface IRuntimeObservationSink
{
    int CurrentTarget { get; set; }

    int ResolveLifecycleId(int rawInstanceId);

    int RebindInstanceLifecycle(int rawInstanceId);

    bool IsKnownEntity(int id);

    bool HasSummonOwner(int instanceId);

    bool TryGetNpcRuntimeState(int instanceId, out RuntimeNpcStateSnapshot state);

    int ResolveNpcObservationSource();

    void RememberNpcObservationSource(int instanceId);

    void StageDestinationMap(in PacketObservationSource packet, uint mapId);

    void StageDestinationMap(in PacketObservationSource packet, uint mapId, bool allowSameMapReload);

    void StagePendingDestinationMap(in PacketObservationSource packet, uint mapId, bool allowSameMapReload);

    void ConfirmDestinationMap(in PacketObservationSource packet, uint mapId, bool allowSameMapReload);

    void ConfirmPendingDestinationMapArrival(in PacketObservationSource packet);

    void StageDestinationMapInstance(in PacketObservationSource packet, uint instanceId);

    void ConfirmDestinationMapInstance(in PacketObservationSource packet, uint instanceId);

    void MarkSceneTransportBoundary(in PacketObservationSource packet);

    void AppendCombatObservation(in PacketObservationSource packet, int sourceId, int targetId, in CombatObservation observation);

    void CompleteBatch(long batchOrdinal);

    void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int layoutTag, int type);

    void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int layoutTag, int type, int value);

    void RegisterCompactControl0238(in PacketObservationSource packet, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker);

    void RegisterCompactControl0638(in PacketObservationSource packet, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int flag);

    void RegisterObservation2A38(in PacketObservationSource packet, int sourceId, int mode, int groupCode, int sequenceId, ushort headValue, ResourceEffectRef buffResourceEffectRef);

    void RegisterObservation2B38(in PacketObservationSource packet, int sourceId, int sourceIdCopy, int phase, int marker, ResourceEffectRef actionResourceEffectRef, int sequenceId, int stateValue, int detailValue, int tailLength);

    void RegisterObservation2C38(in PacketObservationSource packet, int instanceId, int mode, int sequenceId, int resultCode, int tailFirstValue, int tailUInt32Raw);

    void AppendNickname(in PacketObservationSource packet, int uid, string nickname, int? originServerId = null, Faction faction = Faction.Unknown, CharacterClass? characterClass = null);

    void AppendNpcCode(in PacketObservationSource packet, int instanceId, int npcCode);

    void AppendNpcName(in PacketObservationSource packet, int npcCode, string name);

    void AppendNpcKind(in PacketObservationSource packet, int instanceId, NpcKind kind);

    void AppendNpcHp(in PacketObservationSource packet, int instanceId, int hp);

    void AppendNpcHp(in PacketObservationSource packet, int instanceId, int hp, int maxHp);

    void SetNpcBattle(in PacketObservationSource packet, int instanceId, bool isActive);

    void ToggleNpcBattle(in PacketObservationSource packet, int instanceId);

    void AppendNpc2136State(in PacketObservationSource packet, int instanceId, uint sequence, uint value0);

    void AppendNpc0140Value(in PacketObservationSource packet, int instanceId, uint value0);

    void AppendNpc0240Value(in PacketObservationSource packet, int instanceId, uint value0);

    void AppendNpc4636State(in PacketObservationSource packet, int instanceId, byte state0, byte state1);

    void AppendSummon(in PacketObservationSource packet, int ownerId, int summonInstanceId);
}

public readonly record struct RuntimeNpcStateSnapshot(int? NpcCode, int? Hp, int? MaxHp, long? HpObservedAtMilliseconds, bool? BattleToggledOn, NpcKind? Kind, uint? Value2136, uint? Sequence2136, uint? Value0140, uint? Value0240, (byte State0, byte State1)? State4636, (int SequenceId, int ResultCode)? Latest2C38);
