using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.SceneRuntime.Observation;

public interface IRuntimeObservationSynchronization
{
    Lock Gate { get; }
}

public sealed class SynchronizedRuntimeObservationSink(IRuntimeObservationSink inner, Lock gate) : IRuntimeObservationSink, IRuntimeObservationSynchronization
{
    public Lock Gate => gate;

    public int CurrentTarget
    {
        get
        {
            lock (gate) return inner.CurrentTarget;
        }
        set
        {
            lock (gate) inner.CurrentTarget = value;
        }
    }

    public int ResolveLifecycleId(int rawInstanceId)
    {
        lock (gate) return inner.ResolveLifecycleId(rawInstanceId);
    }

    public int RebindInstanceLifecycle(int rawInstanceId)
    {
        lock (gate) return inner.RebindInstanceLifecycle(rawInstanceId);
    }

    public bool IsKnownEntity(int id)
    {
        lock (gate) return inner.IsKnownEntity(id);
    }

    public bool HasSummonOwner(int instanceId)
    {
        lock (gate) return inner.HasSummonOwner(instanceId);
    }

    public bool TryGetNpcRuntimeState(int instanceId, out RuntimeNpcStateSnapshot state)
    {
        lock (gate) return inner.TryGetNpcRuntimeState(instanceId, out state);
    }

    public int ResolveNpcObservationSource()
    {
        lock (gate) return inner.ResolveNpcObservationSource();
    }

    public void RememberNpcObservationSource(int instanceId)
    {
        lock (gate) inner.RememberNpcObservationSource(instanceId);
    }

    public void StageDestinationMap(uint mapId)
    {
        lock (gate) inner.StageDestinationMap(mapId);
    }

    public void StageDestinationMap(uint mapId, bool allowSameMapReload)
    {
        lock (gate) inner.StageDestinationMap(mapId, allowSameMapReload);
    }

    public void StagePendingDestinationMap(uint mapId, bool allowSameMapReload)
    {
        lock (gate) inner.StagePendingDestinationMap(mapId, allowSameMapReload);
    }

    public void ConfirmDestinationMap(uint mapId, bool allowSameMapReload)
    {
        lock (gate) inner.ConfirmDestinationMap(mapId, allowSameMapReload);
    }

    public void ConfirmPendingDestinationMapArrival()
    {
        lock (gate) inner.ConfirmPendingDestinationMapArrival();
    }

    public void StageDestinationMapInstance(uint instanceId)
    {
        lock (gate) inner.StageDestinationMapInstance(instanceId);
    }

    public void ConfirmDestinationMapInstance(uint instanceId)
    {
        lock (gate) inner.ConfirmDestinationMapInstance(instanceId);
    }

    public void MarkSceneTransportBoundary()
    {
        lock (gate) inner.MarkSceneTransportBoundary();
    }

    public void AppendCombatObservation(int sourceId, int targetId, long timestamp, long frameOrdinal, long batchOrdinal, in CombatObservation observation, ushort opcode = 0, int payloadLength = 0, long captureSequence = 0)
    {
        lock (gate) inner.AppendCombatObservation(sourceId, targetId, timestamp, frameOrdinal, batchOrdinal, in observation, opcode, payloadLength, captureSequence);
    }

    public void CompleteBatch(long batchOrdinal)
    {
        lock (gate) inner.CompleteBatch(batchOrdinal);
    }

    public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        lock (gate) inner.RegisterCompactValue0438(targetId, sourceId, skillCodeRaw, marker, layoutTag, type, timestamp, frameOrdinal, batchOrdinal);
    }

    public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, int value, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        lock (gate) inner.RegisterCompactValue0438(targetId, sourceId, skillCodeRaw, marker, layoutTag, type, value, timestamp, frameOrdinal, batchOrdinal);
    }

    public void RegisterCompactControl0238(int sourceId, int skillCodeRaw, int marker, long batchOrdinal)
    {
        lock (gate) inner.RegisterCompactControl0238(sourceId, skillCodeRaw, marker, batchOrdinal);
    }

    public void RegisterCompactControl0638(int sourceId, int skillCodeRaw, int marker, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        lock (gate) inner.RegisterCompactControl0638(sourceId, skillCodeRaw, marker, timestamp, frameOrdinal, batchOrdinal);
    }

    public void RegisterObservation2A38(int sourceId, int mode, int groupCode, int sequenceId, ushort headValue, uint buffCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        lock (gate) inner.RegisterObservation2A38(sourceId, mode, groupCode, sequenceId, headValue, buffCodeRaw, timestamp, frameOrdinal, batchOrdinal);
    }

    public void RegisterObservation2C38(int instanceId, int mode, int sequenceId, int resultCode, int tailSourceId, int tailSkillCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        lock (gate) inner.RegisterObservation2C38(instanceId, mode, sequenceId, resultCode, tailSourceId, tailSkillCodeRaw, timestamp, frameOrdinal, batchOrdinal);
    }

    public void AppendNickname(int uid, string nickname, int? originServerId = null, Faction faction = Faction.Unknown)
    {
        lock (gate) inner.AppendNickname(uid, nickname, originServerId, faction);
    }

    public void AppendNpcCode(int instanceId, int npcCode)
    {
        lock (gate) inner.AppendNpcCode(instanceId, npcCode);
    }

    public void AppendNpcName(int npcCode, string name)
    {
        lock (gate) inner.AppendNpcName(npcCode, name);
    }

    public void AppendNpcKind(int instanceId, NpcKind kind)
    {
        lock (gate) inner.AppendNpcKind(instanceId, kind);
    }

    public void AppendNpcHp(int instanceId, int hp, long observedAtMilliseconds)
    {
        lock (gate) inner.AppendNpcHp(instanceId, hp, observedAtMilliseconds);
    }

    public void AppendNpcHp(int instanceId, int hp, int maxHp, long observedAtMilliseconds)
    {
        lock (gate) inner.AppendNpcHp(instanceId, hp, maxHp, observedAtMilliseconds);
    }

    public void SetNpcBattle(int instanceId, bool isActive, long observedAtMilliseconds)
    {
        lock (gate) inner.SetNpcBattle(instanceId, isActive, observedAtMilliseconds);
    }

    public void ToggleNpcBattle(int instanceId)
    {
        lock (gate) inner.ToggleNpcBattle(instanceId);
    }

    public void AppendNpc2136State(int instanceId, uint sequence, uint value0)
    {
        lock (gate) inner.AppendNpc2136State(instanceId, sequence, value0);
    }

    public void AppendNpc0140Value(int instanceId, uint value0)
    {
        lock (gate) inner.AppendNpc0140Value(instanceId, value0);
    }

    public void AppendNpc0240Value(int instanceId, uint value0)
    {
        lock (gate) inner.AppendNpc0240Value(instanceId, value0);
    }

    public void AppendNpc4636State(int instanceId, byte state0, byte state1)
    {
        lock (gate) inner.AppendNpc4636State(instanceId, state0, state1);
    }

    public void AppendSummon(int ownerId, int summonInstanceId)
    {
        lock (gate) inner.AppendSummon(ownerId, summonInstanceId);
    }
}
