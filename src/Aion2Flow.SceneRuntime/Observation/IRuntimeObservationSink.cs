using Cloris.Aion2Flow.SceneRuntime.Combat;
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

    void StageDestinationMap(uint mapId);

    void StageDestinationMap(uint mapId, bool allowSameMapReload);

    void StagePendingDestinationMap(uint mapId, bool allowSameMapReload);

    void ConfirmDestinationMap(uint mapId, bool allowSameMapReload);

    void ConfirmPendingDestinationMapArrival();

    void StageDestinationMapInstance(uint instanceId);

    void ConfirmDestinationMapInstance(uint instanceId);

    void MarkSceneTransportBoundary();

    void AppendCombatObservation(int sourceId, int targetId, long timestamp, long frameOrdinal, long batchOrdinal, in CombatObservation observation, ushort opcode = 0, int payloadLength = 0, long captureSequence = 0, PacketStructureReference structure = default);

    void CompleteBatch(long batchOrdinal);

    void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, long timestamp, long frameOrdinal, long batchOrdinal, PacketStructureReference structure = default);

    void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, int value, long timestamp, long frameOrdinal, long batchOrdinal, PacketStructureReference structure = default);

    void RegisterCompactControl0238(int sourceId, int skillCodeRaw, int marker, long batchOrdinal, PacketStructureReference structure = default);

    void RegisterCompactControl0638(int sourceId, int skillCodeRaw, int marker, long timestamp, long frameOrdinal, long batchOrdinal, PacketStructureReference structure = default);

    void RegisterObservation2A38(int sourceId, int mode, int groupCode, int sequenceId, ushort headValue, uint buffCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal, PacketStructureReference structure = default);

    void RegisterObservation2C38(int instanceId, int mode, int sequenceId, int resultCode, int tailSourceId, int tailSkillCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal, PacketStructureReference structure = default);

    void AppendNickname(int uid, string nickname, int? originServerId = null, Faction faction = Faction.Unknown);

    void AppendNpcCode(int instanceId, int npcCode);

    void AppendNpcName(int npcCode, string name);

    void AppendNpcKind(int instanceId, NpcKind kind);

    void AppendNpcHp(int instanceId, int hp, long observedAtMilliseconds);

    void AppendNpcHp(int instanceId, int hp, int maxHp, long observedAtMilliseconds);

    void SetNpcBattle(int instanceId, bool isActive, long observedAtMilliseconds);

    void ToggleNpcBattle(int instanceId);

    void AppendNpc2136State(int instanceId, uint sequence, uint value0);

    void AppendNpc0140Value(int instanceId, uint value0);

    void AppendNpc0240Value(int instanceId, uint value0);

    void AppendNpc4636State(int instanceId, byte state0, byte state1);

    void AppendSummon(int ownerId, int summonInstanceId);
}

public static class RuntimeObservationSinkExtensions
{
    public static void AppendCombatPacket(this IRuntimeObservationSink sink, ParsedCombatPacket packet)
    {
        var observation = packet.ToObservation();
        sink.AppendCombatObservation(packet.SourceId, packet.TargetId, packet.Timestamp, packet.FrameOrdinal, packet.BatchOrdinal, in observation);
    }
}

public readonly record struct RuntimeNpcStateSnapshot(int? NpcCode, int? Hp, int? MaxHp, long? HpObservedAtMilliseconds, bool? BattleToggledOn, NpcKind? Kind, uint? Value2136, uint? Sequence2136, uint? Value0140, uint? Value0240, (byte State0, byte State1)? State4636, (int SequenceId, int ResultCode)? Latest2C38);
