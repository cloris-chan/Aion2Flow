using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Scene.Observation;

namespace Cloris.Aion2Flow.Scene.Compatibility;

public sealed class LegacyRuntimeObservationSink(CombatMetricsStore store) : IRuntimeObservationSink
{
    public int CurrentTarget
    {
        get => store.CurrentTarget;
        set => store.CurrentTarget = value;
    }

    public int ResolveLifecycleId(int rawInstanceId) => store.ResolveLifecycleId(rawInstanceId);

    public int RebindInstanceLifecycle(int rawInstanceId) => store.RebindInstanceLifecycle(rawInstanceId);

    public bool IsKnownEntity(int id) => store.IsKnownEntity(id);

    public bool HasSummonOwner(int instanceId) => store.SummonOwnerByInstance.ContainsKey(instanceId);

    public bool TryGetNpcRuntimeState(int instanceId, out RuntimeNpcStateSnapshot state)
    {
        if (!store.TryGetNpcRuntimeState(instanceId, out var legacyState))
        {
            state = default;
            return false;
        }

        state = new RuntimeNpcStateSnapshot
        {
            NpcCode = legacyState.NpcCode,
            Hp = legacyState.Hp,
            MaxHp = legacyState.MaxHp,
            HpObservedAtMilliseconds = legacyState.HpObservedAtMilliseconds,
            BattleToggledOn = legacyState.BattleToggledOn,
            Kind = legacyState.Kind,
            Value2136 = legacyState.Value2136,
            Sequence2136 = legacyState.Sequence2136,
            Value0140 = legacyState.Value0140,
            Value0240 = legacyState.Value0240,
            State4636 = legacyState.State4636,
            Latest2C38 = legacyState.Latest2C38
        };
        return true;
    }

    public int ResolveNpcObservationSource() => store.ResolveNpcObservationSource();

    public void RememberNpcObservationSource(int instanceId) => store.RememberNpcObservationSource(instanceId);

    public void StageDestinationMap(uint mapId) => store.StageDestinationMap(mapId);

    public void StageDestinationMapInstance(uint instanceId) => store.StageDestinationMapInstance(instanceId);

    public void MarkSceneArrival() => store.MarkSceneArrival();

    public void AppendCombatPacket(ParsedCombatPacket packet) => store.AppendCombatPacket(packet);

    public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, long timestamp, long frameOrdinal, long batchOrdinal)
        => store.RegisterCompactValue0438(targetId, sourceId, skillCodeRaw, marker, layoutTag, type, timestamp, frameOrdinal, batchOrdinal);

    public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, int value, long timestamp, long frameOrdinal, long batchOrdinal)
        => store.RegisterCompactValue0438(targetId, sourceId, skillCodeRaw, marker, layoutTag, type, value, timestamp, frameOrdinal, batchOrdinal);

    public void RegisterCompactControl0238(int sourceId, int skillCodeRaw, int marker, long batchOrdinal)
        => store.RegisterCompactControl0238(sourceId, skillCodeRaw, marker, batchOrdinal);

    public void RegisterCompactControl0638(int sourceId, int skillCodeRaw, int marker, long timestamp, long frameOrdinal, long batchOrdinal)
        => store.RegisterCompactControl0638(sourceId, skillCodeRaw, marker, timestamp, frameOrdinal, batchOrdinal);

    public void RegisterPeriodicLink0538(int targetId, int sourceId, int linkId, int sequenceId, int tailRaw, long timestamp, long frameOrdinal, long batchOrdinal)
        => store.RegisterPeriodicLink0538(targetId, sourceId, linkId, sequenceId, tailRaw, timestamp, frameOrdinal, batchOrdinal);

    public void RegisterObservation2A38(int sourceId, int mode, int groupCode, int sequenceId, ushort headValue, uint buffCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal)
        => store.RegisterObservation2A38(sourceId, mode, groupCode, sequenceId, headValue, buffCodeRaw, timestamp, frameOrdinal, batchOrdinal);

    public void RegisterObservation2C38(int instanceId, int mode, int sequenceId, int resultCode, int tailSourceId, int tailSkillCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal)
        => store.RegisterObservation2C38(instanceId, mode, sequenceId, resultCode, tailSourceId, tailSkillCodeRaw, timestamp, frameOrdinal, batchOrdinal);

    public void AppendNickname(int uid, string nickname, int? originServerId = null) => store.AppendNickname(uid, nickname, originServerId);

    public void AppendNpcCode(int instanceId, int npcCode) => store.AppendNpcCode(instanceId, npcCode);

    public void AppendNpcName(int npcCode, string name) => store.AppendNpcName(npcCode, name);

    public void AppendNpcKind(int instanceId, NpcKind kind) => store.AppendNpcKind(instanceId, kind);

    public void AppendNpcHp(int instanceId, int hp, long observedAtMilliseconds) => store.AppendNpcHp(instanceId, hp, observedAtMilliseconds);

    public void AppendNpcHp(int instanceId, int hp, int maxHp, long observedAtMilliseconds) => store.AppendNpcHp(instanceId, hp, maxHp, observedAtMilliseconds);

    public void SetNpcBattle(int instanceId, bool isActive, long observedAtMilliseconds) => store.SetNpcBattle(instanceId, isActive, observedAtMilliseconds);

    public void ToggleNpcBattle(int instanceId) => store.ToggleNpcBattle(instanceId);

    public void AppendNpc2136State(int instanceId, uint sequence, uint value0) => store.AppendNpc2136State(instanceId, sequence, value0);

    public void AppendNpc0140Value(int instanceId, uint value0) => store.AppendNpc0140Value(instanceId, value0);

    public void AppendNpc0240Value(int instanceId, uint value0) => store.AppendNpc0240Value(instanceId, value0);

    public void AppendNpc4636State(int instanceId, byte state0, byte state1) => store.AppendNpc4636State(instanceId, state0, state1);

    public void AppendSummon(int ownerId, int summonInstanceId) => store.AppendSummon(ownerId, summonInstanceId);
}
