using Cloris.Aion2Flow.Protocol.Combat;
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

    public void SeedNpcRuntimeState(in PacketObservationSource packet, int instanceId, in RuntimeNpcStateSnapshot state)
    {
        lock (gate) inner.SeedNpcRuntimeState(in packet, instanceId, in state);
    }

    public int ResolveNpcObservationSource()
    {
        lock (gate) return inner.ResolveNpcObservationSource();
    }

    public void RememberNpcObservationSource(int instanceId)
    {
        lock (gate) inner.RememberNpcObservationSource(instanceId);
    }

    public void StageDestinationMap(in PacketObservationSource packet, uint mapId)
    {
        lock (gate) inner.StageDestinationMap(in packet, mapId);
    }

    public void StageDestinationMap(in PacketObservationSource packet, uint mapId, bool allowSameMapReload)
    {
        lock (gate) inner.StageDestinationMap(in packet, mapId, allowSameMapReload);
    }

    public void StagePendingDestinationMap(in PacketObservationSource packet, uint mapId, bool allowSameMapReload)
    {
        lock (gate) inner.StagePendingDestinationMap(in packet, mapId, allowSameMapReload);
    }

    public void ConfirmDestinationMap(in PacketObservationSource packet, uint mapId, bool allowSameMapReload)
    {
        lock (gate) inner.ConfirmDestinationMap(in packet, mapId, allowSameMapReload);
    }

    public void ConfirmPendingDestinationMapArrival(in PacketObservationSource packet)
    {
        lock (gate) inner.ConfirmPendingDestinationMapArrival(in packet);
    }

    public void StageDestinationMapInstance(in PacketObservationSource packet, uint instanceId)
    {
        lock (gate) inner.StageDestinationMapInstance(in packet, instanceId);
    }

    public void ConfirmDestinationMapInstance(in PacketObservationSource packet, uint instanceId)
    {
        lock (gate) inner.ConfirmDestinationMapInstance(in packet, instanceId);
    }

    public void MarkSceneTransportBoundary(in PacketObservationSource packet)
    {
        lock (gate) inner.MarkSceneTransportBoundary(in packet);
    }

    public void AppendCombatObservation(in PacketObservationSource packet, int sourceId, int targetId, in CombatObservation observation)
    {
        lock (gate) inner.AppendCombatObservation(in packet, sourceId, targetId, in observation);
    }

    public void CompleteBatch(long batchOrdinal)
    {
        lock (gate) inner.CompleteBatch(batchOrdinal);
    }

    public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, int bodySkillVariantRaw, int marker, int layoutTag, int type)
    {
        lock (gate) inner.RegisterCompactValue0438(in packet, targetId, sourceId, bodySkillVariantRaw, marker, layoutTag, type);
    }

    public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, int bodySkillVariantRaw, int marker, int layoutTag, int type, int value)
    {
        lock (gate) inner.RegisterCompactValue0438(in packet, targetId, sourceId, bodySkillVariantRaw, marker, layoutTag, type, value);
    }

    public void RegisterCompactControl0238(in PacketObservationSource packet, int sourceId, int mode, uint bodyCodeRaw, int marker, int flag, int echoSourceId)
    {
        lock (gate) inner.RegisterCompactControl0238(in packet, sourceId, mode, bodyCodeRaw, marker, flag, echoSourceId);
    }

    public void RegisterCompactControl0638(in PacketObservationSource packet, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int flag)
    {
        lock (gate) inner.RegisterCompactControl0638(in packet, sourceId, bodyResourceEffectRef, marker, flag);
    }

    public void RegisterObservation2A38(in PacketObservationSource packet, int entityId, int mode, int groupCode, int instanceSequenceId, uint headCode, ushort headValue, ulong headMiddleRaw, uint timelineValue, uint stableValue, int echoSourceId, int stackValue, ResourceEffectRef buffResourceEffectRef, int tailLength, ulong tailLow64, ulong tailHigh64)
    {
        lock (gate) inner.RegisterObservation2A38(in packet, entityId, mode, groupCode, instanceSequenceId, headCode, headValue, headMiddleRaw, timelineValue, stableValue, echoSourceId, stackValue, buffResourceEffectRef, tailLength, tailLow64, tailHigh64);
    }

    public void RegisterObservation2B38(in PacketObservationSource packet, int sourceId, int sourceIdCopy, int phase, int instanceSequenceId, ResourceEffectRef actionResourceEffectRef, int sequenceValue, int stateValue, int detailValue, int tailLength)
    {
        lock (gate) inner.RegisterObservation2B38(in packet, sourceId, sourceIdCopy, phase, instanceSequenceId, actionResourceEffectRef, sequenceValue, stateValue, detailValue, tailLength);
    }

    public void RegisterObservation2C38(in PacketObservationSource packet, int entityId, scoped ReadOnlySpan<AuraResultRecord> results)
    {
        lock (gate) inner.RegisterObservation2C38(in packet, entityId, results);
    }

    public void AppendNickname(in PacketObservationSource packet, int uid, string nickname, Faction faction = Faction.Unknown, CharacterClass? characterClass = null, bool isLocalPlayer = false, int? originServerId = null, string legionName = "")
    {
        lock (gate) inner.AppendNickname(in packet, uid, nickname, faction, characterClass, isLocalPlayer, originServerId, legionName);
    }

    public void AppendPlayerGroupMember(in PacketObservationSource packet, int uid, in PlayerGroupMembership membership)
    {
        lock (gate) inner.AppendPlayerGroupMember(in packet, uid, in membership);
    }

    public void AppendNpcCode(in PacketObservationSource packet, int instanceId, int npcCode)
    {
        lock (gate) inner.AppendNpcCode(in packet, instanceId, npcCode);
    }

    public void AppendNpcName(in PacketObservationSource packet, int npcCode, string name)
    {
        lock (gate) inner.AppendNpcName(in packet, npcCode, name);
    }

    public void AppendNpcKind(in PacketObservationSource packet, int instanceId, NpcKind kind)
    {
        lock (gate) inner.AppendNpcKind(in packet, instanceId, kind);
    }

    public void AppendNpcHp(in PacketObservationSource packet, int instanceId, int hp)
    {
        lock (gate) inner.AppendNpcHp(in packet, instanceId, hp);
    }

    public void AppendNpcHp(in PacketObservationSource packet, int instanceId, int hp, int maxHp)
    {
        lock (gate) inner.AppendNpcHp(in packet, instanceId, hp, maxHp);
    }

    public void SetNpcBattle(in PacketObservationSource packet, int instanceId, bool isActive)
    {
        lock (gate) inner.SetNpcBattle(in packet, instanceId, isActive);
    }

    public void ToggleNpcBattle(in PacketObservationSource packet, int instanceId)
    {
        lock (gate) inner.ToggleNpcBattle(in packet, instanceId);
    }

    public void AppendNpc2136State(in PacketObservationSource packet, int instanceId, uint sequence, uint value0)
    {
        lock (gate) inner.AppendNpc2136State(in packet, instanceId, sequence, value0);
    }

    public void AppendNpc0140Value(in PacketObservationSource packet, int instanceId, uint value0)
    {
        lock (gate) inner.AppendNpc0140Value(in packet, instanceId, value0);
    }

    public void AppendNpc0240Value(in PacketObservationSource packet, int instanceId, uint value0)
    {
        lock (gate) inner.AppendNpc0240Value(in packet, instanceId, value0);
    }

    public void AppendNpc4636State(in PacketObservationSource packet, int instanceId, byte state0, byte state1)
    {
        lock (gate) inner.AppendNpc4636State(in packet, instanceId, state0, state1);
    }

    public void AppendSummon(in PacketObservationSource packet, int ownerId, int summonInstanceId)
    {
        lock (gate) inner.AppendSummon(in packet, ownerId, summonInstanceId);
    }
}
