using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Combat.Metrics;

namespace Cloris.Aion2Flow.Scene.Observation;

public sealed class CompositeRuntimeObservationSink(IRuntimeObservationSink legacy, JournalingRuntimeObservationSink journaling) : IRuntimeObservationSink
{
    public IRuntimeObservationSink Legacy => legacy;

    public JournalingRuntimeObservationSink Journaling => journaling;

    public int CurrentTarget
    {
        get => legacy.CurrentTarget;
        set
        {
            legacy.CurrentTarget = value;
            journaling.CurrentTarget = value;
        }
    }

    public int ResolveLifecycleId(int rawInstanceId) => legacy.ResolveLifecycleId(rawInstanceId);

    public int RebindInstanceLifecycle(int rawInstanceId) => legacy.RebindInstanceLifecycle(rawInstanceId);

    public bool IsKnownEntity(int id) => legacy.IsKnownEntity(id);

    public bool HasSummonOwner(int instanceId) => legacy.HasSummonOwner(instanceId);

    public bool TryGetNpcRuntimeState(int instanceId, out RuntimeNpcStateSnapshot state) => legacy.TryGetNpcRuntimeState(instanceId, out state);

    public int ResolveNpcObservationSource() => legacy.ResolveNpcObservationSource();

    public void RememberNpcObservationSource(int instanceId)
    {
        legacy.RememberNpcObservationSource(instanceId);
        journaling.RememberNpcObservationSource(instanceId);
    }

    public void StageDestinationMap(uint mapId)
    {
        legacy.StageDestinationMap(mapId);
        journaling.StageDestinationMap(mapId);
    }

    public void StageDestinationMapInstance(uint instanceId)
    {
        legacy.StageDestinationMapInstance(instanceId);
        journaling.StageDestinationMapInstance(instanceId);
    }

    public void MarkSceneArrival()
    {
        legacy.MarkSceneArrival();
        journaling.MarkSceneArrival();
    }

    public void AppendCombatPacket(ParsedCombatPacket packet)
    {
        legacy.AppendCombatPacket(packet);
        journaling.AppendCombatPacket(packet);
    }

    public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        legacy.RegisterCompactValue0438(targetId, sourceId, skillCodeRaw, marker, layoutTag, type, timestamp, frameOrdinal, batchOrdinal);
        journaling.RegisterCompactValue0438(targetId, sourceId, skillCodeRaw, marker, layoutTag, type, timestamp, frameOrdinal, batchOrdinal);
    }

    public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, int value, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        legacy.RegisterCompactValue0438(targetId, sourceId, skillCodeRaw, marker, layoutTag, type, value, timestamp, frameOrdinal, batchOrdinal);
        journaling.RegisterCompactValue0438(targetId, sourceId, skillCodeRaw, marker, layoutTag, type, value, timestamp, frameOrdinal, batchOrdinal);
    }

    public void RegisterCompactControl0238(int sourceId, int skillCodeRaw, int marker, long batchOrdinal)
    {
        legacy.RegisterCompactControl0238(sourceId, skillCodeRaw, marker, batchOrdinal);
        journaling.RegisterCompactControl0238(sourceId, skillCodeRaw, marker, batchOrdinal);
    }

    public void RegisterCompactControl0638(int sourceId, int skillCodeRaw, int marker, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        legacy.RegisterCompactControl0638(sourceId, skillCodeRaw, marker, timestamp, frameOrdinal, batchOrdinal);
        journaling.RegisterCompactControl0638(sourceId, skillCodeRaw, marker, timestamp, frameOrdinal, batchOrdinal);
    }

    public void RegisterPeriodicLink0538(int targetId, int sourceId, int linkId, int sequenceId, int tailRaw, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        legacy.RegisterPeriodicLink0538(targetId, sourceId, linkId, sequenceId, tailRaw, timestamp, frameOrdinal, batchOrdinal);
        journaling.RegisterPeriodicLink0538(targetId, sourceId, linkId, sequenceId, tailRaw, timestamp, frameOrdinal, batchOrdinal);
    }

    public void RegisterObservation2A38(int sourceId, int mode, int groupCode, int sequenceId, ushort headValue, uint buffCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        legacy.RegisterObservation2A38(sourceId, mode, groupCode, sequenceId, headValue, buffCodeRaw, timestamp, frameOrdinal, batchOrdinal);
        journaling.RegisterObservation2A38(sourceId, mode, groupCode, sequenceId, headValue, buffCodeRaw, timestamp, frameOrdinal, batchOrdinal);
    }

    public void RegisterObservation2C38(int instanceId, int mode, int sequenceId, int resultCode, int tailSourceId, int tailSkillCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        legacy.RegisterObservation2C38(instanceId, mode, sequenceId, resultCode, tailSourceId, tailSkillCodeRaw, timestamp, frameOrdinal, batchOrdinal);
        journaling.RegisterObservation2C38(instanceId, mode, sequenceId, resultCode, tailSourceId, tailSkillCodeRaw, timestamp, frameOrdinal, batchOrdinal);
    }

    public void AppendNickname(int uid, string nickname, int? originServerId = null)
    {
        legacy.AppendNickname(uid, nickname, originServerId);
        journaling.AppendNickname(uid, nickname, originServerId);
    }

    public void AppendNpcCode(int instanceId, int npcCode)
    {
        legacy.AppendNpcCode(instanceId, npcCode);
        journaling.AppendNpcCode(instanceId, npcCode);
    }

    public void AppendNpcName(int npcCode, string name)
    {
        legacy.AppendNpcName(npcCode, name);
        journaling.AppendNpcName(npcCode, name);
    }

    public void AppendNpcKind(int instanceId, NpcKind kind)
    {
        legacy.AppendNpcKind(instanceId, kind);
        journaling.AppendNpcKind(instanceId, kind);
    }

    public void AppendNpcHp(int instanceId, int hp, long observedAtMilliseconds)
    {
        legacy.AppendNpcHp(instanceId, hp, observedAtMilliseconds);
        journaling.AppendNpcHp(instanceId, hp, observedAtMilliseconds);
    }

    public void AppendNpcHp(int instanceId, int hp, int maxHp, long observedAtMilliseconds)
    {
        legacy.AppendNpcHp(instanceId, hp, maxHp, observedAtMilliseconds);
        journaling.AppendNpcHp(instanceId, hp, maxHp, observedAtMilliseconds);
    }

    public void SetNpcBattle(int instanceId, bool isActive, long observedAtMilliseconds)
    {
        legacy.SetNpcBattle(instanceId, isActive, observedAtMilliseconds);
        journaling.SetNpcBattle(instanceId, isActive, observedAtMilliseconds);
    }

    public void ToggleNpcBattle(int instanceId)
    {
        legacy.ToggleNpcBattle(instanceId);
        journaling.ToggleNpcBattle(instanceId);
    }

    public void AppendNpc2136State(int instanceId, uint sequence, uint value0)
    {
        legacy.AppendNpc2136State(instanceId, sequence, value0);
        journaling.AppendNpc2136State(instanceId, sequence, value0);
    }

    public void AppendNpc0140Value(int instanceId, uint value0)
    {
        legacy.AppendNpc0140Value(instanceId, value0);
        journaling.AppendNpc0140Value(instanceId, value0);
    }

    public void AppendNpc0240Value(int instanceId, uint value0)
    {
        legacy.AppendNpc0240Value(instanceId, value0);
        journaling.AppendNpc0240Value(instanceId, value0);
    }

    public void AppendNpc4636State(int instanceId, byte state0, byte state1)
    {
        legacy.AppendNpc4636State(instanceId, state0, state1);
        journaling.AppendNpc4636State(instanceId, state0, state1);
    }

    public void AppendSummon(int ownerId, int summonInstanceId)
    {
        legacy.AppendSummon(ownerId, summonInstanceId);
        journaling.AppendSummon(ownerId, summonInstanceId);
    }
}
