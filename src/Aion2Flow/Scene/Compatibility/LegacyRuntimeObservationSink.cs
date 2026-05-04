using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Scene.Observation;

namespace Cloris.Aion2Flow.Scene.Compatibility;

public sealed class LegacyRuntimeObservationSink : IRuntimeObservationSink
{
    private readonly CombatMetricsStore _store;

    public LegacyRuntimeObservationSink(CombatMetricsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public int CurrentTarget
    {
        get => _store.CurrentTarget;
        set => _store.CurrentTarget = value;
    }

    public int ResolveLifecycleId(int rawInstanceId) => _store.ResolveLifecycleId(rawInstanceId);

    public int RebindInstanceLifecycle(int rawInstanceId) => _store.RebindInstanceLifecycle(rawInstanceId);

    public bool IsKnownEntity(int id) => _store.IsKnownEntity(id);

    public bool HasSummonOwner(int instanceId) => _store.SummonOwnerByInstance.ContainsKey(instanceId);

    public bool TryGetNpcRuntimeState(int instanceId, out RuntimeNpcStateSnapshot state)
    {
        if (!_store.TryGetNpcRuntimeState(instanceId, out var legacyState))
        {
            state = default;
            return false;
        }

        state = new RuntimeNpcStateSnapshot(
            legacyState.NpcCode,
            legacyState.Hp,
            legacyState.MaxHp,
            legacyState.HpObservedAtMilliseconds,
            legacyState.BattleToggledOn,
            legacyState.Kind,
            legacyState.Value2136,
            legacyState.Sequence2136,
            legacyState.Value0140,
            legacyState.Value0240,
            legacyState.State4636,
            legacyState.Latest2C38);
        return true;
    }

    public int ResolveNpcObservationSource() => _store.ResolveNpcObservationSource();

    public void RememberNpcObservationSource(int instanceId) => _store.RememberNpcObservationSource(instanceId);

    public void StageDestinationMap(uint mapId) => _store.StageDestinationMap(mapId);

    public void StageDestinationMapInstance(uint instanceId) => _store.StageDestinationMapInstance(instanceId);

    public void MarkSceneArrival() => _store.MarkSceneArrival();

    public void AppendCombatPacket(ParsedCombatPacket packet) => _store.AppendCombatPacket(packet);

    public void RegisterCompactValue0438(
        int targetId,
        int sourceId,
        int skillCodeRaw,
        int marker,
        int layoutTag,
        int type,
        long timestamp,
        long frameOrdinal,
        long batchOrdinal)
        => _store.RegisterCompactValue0438(targetId, sourceId, skillCodeRaw, marker, layoutTag, type, timestamp, frameOrdinal, batchOrdinal);

    public void RegisterCompactValue0438(
        int targetId,
        int sourceId,
        int skillCodeRaw,
        int marker,
        int layoutTag,
        int type,
        int value,
        long timestamp,
        long frameOrdinal,
        long batchOrdinal)
        => _store.RegisterCompactValue0438(targetId, sourceId, skillCodeRaw, marker, layoutTag, type, value, timestamp, frameOrdinal, batchOrdinal);

    public void RegisterCompactControl0238(int sourceId, int skillCodeRaw, int marker, long batchOrdinal)
        => _store.RegisterCompactControl0238(sourceId, skillCodeRaw, marker, batchOrdinal);

    public void RegisterCompactControl0638(
        int sourceId,
        int skillCodeRaw,
        int marker,
        long timestamp,
        long frameOrdinal,
        long batchOrdinal)
        => _store.RegisterCompactControl0638(sourceId, skillCodeRaw, marker, timestamp, frameOrdinal, batchOrdinal);

    public void RegisterPeriodicLink0538(
        int targetId,
        int sourceId,
        int linkId,
        int sequenceId,
        int tailRaw,
        long timestamp,
        long frameOrdinal,
        long batchOrdinal)
        => _store.RegisterPeriodicLink0538(targetId, sourceId, linkId, sequenceId, tailRaw, timestamp, frameOrdinal, batchOrdinal);

    public void RegisterObservation2A38(
        int sourceId,
        int mode,
        int groupCode,
        int sequenceId,
        ushort headValue,
        uint buffCodeRaw,
        long timestamp,
        long frameOrdinal,
        long batchOrdinal)
        => _store.RegisterObservation2A38(sourceId, mode, groupCode, sequenceId, headValue, buffCodeRaw, timestamp, frameOrdinal, batchOrdinal);

    public void RegisterObservation2C38(
        int instanceId,
        int mode,
        int sequenceId,
        int resultCode,
        int tailSourceId,
        int tailSkillCodeRaw,
        long timestamp,
        long frameOrdinal,
        long batchOrdinal)
        => _store.RegisterObservation2C38(instanceId, mode, sequenceId, resultCode, tailSourceId, tailSkillCodeRaw, timestamp, frameOrdinal, batchOrdinal);

    public void AppendNickname(int uid, string nickname, int? originServerId = null)
        => _store.AppendNickname(uid, nickname, originServerId);

    public void AppendNpcCode(int instanceId, int npcCode) => _store.AppendNpcCode(instanceId, npcCode);

    public void AppendNpcName(int npcCode, string name) => _store.AppendNpcName(npcCode, name);

    public void AppendNpcKind(int instanceId, NpcKind kind) => _store.AppendNpcKind(instanceId, kind);

    public void AppendNpcHp(int instanceId, int hp, long observedAtMilliseconds)
        => _store.AppendNpcHp(instanceId, hp, observedAtMilliseconds);

    public void AppendNpcHp(int instanceId, int hp, int maxHp, long observedAtMilliseconds)
        => _store.AppendNpcHp(instanceId, hp, maxHp, observedAtMilliseconds);

    public void SetNpcBattle(int instanceId, bool isActive, long observedAtMilliseconds)
        => _store.SetNpcBattle(instanceId, isActive, observedAtMilliseconds);

    public void ToggleNpcBattle(int instanceId) => _store.ToggleNpcBattle(instanceId);

    public void AppendNpc2136State(int instanceId, uint sequence, uint value0)
        => _store.AppendNpc2136State(instanceId, sequence, value0);

    public void AppendNpc0140Value(int instanceId, uint value0) => _store.AppendNpc0140Value(instanceId, value0);

    public void AppendNpc0240Value(int instanceId, uint value0) => _store.AppendNpc0240Value(instanceId, value0);

    public void AppendNpc4636State(int instanceId, byte state0, byte state1)
        => _store.AppendNpc4636State(instanceId, state0, state1);

    public void AppendSummon(int ownerId, int summonInstanceId) => _store.AppendSummon(ownerId, summonInstanceId);
}
