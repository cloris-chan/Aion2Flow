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

    void SeedNpcRuntimeState(in PacketObservationSource packet, int instanceId, in RuntimeNpcStateSnapshot state);

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

    void AppendCombatWireObservation(in PacketObservationSource packet, int sourceId, int targetId, in CombatWireObservation observation);

    void CompleteFlush(long flushId);

    void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, int bodySkillVariantRaw, int marker, int layoutTag, int type);

    void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, int bodySkillVariantRaw, int marker, int layoutTag, int type, int value);

    void RegisterCompactControl0238(in PacketObservationSource packet, int sourceId, int mode, uint bodyCodeRaw, int marker, int flag, int echoSourceId);

    void RegisterCompactControl0638(in PacketObservationSource packet, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int flag);

    void RegisterObservation2A38(in PacketObservationSource packet, int entityId, int mode, int groupCode, int instanceSequenceId, uint headCode, ushort headValue, ulong headMiddleRaw, uint timelineValue, uint stableValue, int echoSourceId, int stackValue, ResourceEffectRef buffResourceEffectRef, int tailLength, ulong tailLow64, ulong tailHigh64);

    void RegisterObservation2B38(in PacketObservationSource packet, int sourceId, int sourceIdCopy, int phase, int instanceSequenceId, ResourceEffectRef actionResourceEffectRef, int sequenceValue, int stateValue, int detailValue, int tailLength);

    void RegisterObservation2C38(in PacketObservationSource packet, int entityId, scoped ReadOnlySpan<AuraResultRecord> results);

    void AppendNickname(in PacketObservationSource packet, int uid, string nickname, Faction faction = Faction.Unknown, CharacterClass? characterClass = null, bool isLocalPlayer = false, int? originServerId = null, string legionName = "");

    void AppendPlayerGroupMember(in PacketObservationSource packet, int uid, in PlayerGroupMembership membership);

    void AppendPlayerGroupProfile(in PacketObservationSource packet, int originServerId, string nickname, in PlayerGroupMembership membership);

    void AppendNpcCode(in PacketObservationSource packet, int instanceId, int npcCode);

    void AppendNpcName(in PacketObservationSource packet, int npcCode, string name);

    void AppendNpcKind(in PacketObservationSource packet, int instanceId, NpcKind kind);

    void AppendNpcHp(in PacketObservationSource packet, int instanceId, long hp);

    void AppendNpcHp(in PacketObservationSource packet, int instanceId, long hp, long maxHp);

    void SetNpcBattle(in PacketObservationSource packet, int instanceId, bool isActive);

    void ToggleNpcBattle(in PacketObservationSource packet, int instanceId);

    void AppendNpc2136State(in PacketObservationSource packet, int instanceId, long sequence, long value0);

    void AppendNpc0140Value(in PacketObservationSource packet, int instanceId, long value0);

    void AppendNpc0240Value(in PacketObservationSource packet, int instanceId, long value0);

    void AppendNpc4636State(in PacketObservationSource packet, int instanceId, byte state0, byte state1);

    void AppendSummon(in PacketObservationSource packet, int ownerId, int summonInstanceId);
}

public readonly record struct RuntimeNpcStateSnapshot(int? NpcCode, long? Hp, long? MaxHp, long? HpObservedAtMilliseconds, bool? BattleToggledOn, NpcKind? Kind, long? Value2136, long? Sequence2136, long? Value0140, long? Value0240, (byte State0, byte State1)? State4636, (int SequenceId, int ResultCode)? Latest2C38);
