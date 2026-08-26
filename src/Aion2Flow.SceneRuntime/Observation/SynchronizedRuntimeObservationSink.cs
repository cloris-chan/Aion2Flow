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

    public void SetCurrentMap(in PacketObservationSource packet, uint mapId)
    {
        lock (gate) inner.SetCurrentMap(in packet, mapId);
    }

    public void EnsureUnknownMapScope(in PacketObservationSource packet)
    {
        lock (gate) inner.EnsureUnknownMapScope(in packet);
    }

    public bool StageMapCandidate(in PacketObservationSource packet, uint mapId)
    {
        lock (gate) return inner.StageMapCandidate(in packet, mapId);
    }

    public bool ConfirmDestinationMapArrival(in PacketObservationSource packet)
    {
        lock (gate) return inner.ConfirmDestinationMapArrival(in packet);
    }

    public void RegisterMapEvent(in PacketObservationSource packet, uint instanceId)
    {
        lock (gate) inner.RegisterMapEvent(in packet, instanceId);
    }

    public void UnregisterMapEvent(in PacketObservationSource packet, uint instanceId)
    {
        lock (gate) inner.UnregisterMapEvent(in packet, instanceId);
    }

    public void MarkTransportStreamActivated(in PacketObservationSource packet)
    {
        lock (gate) inner.MarkTransportStreamActivated(in packet);
    }

    public void AppendCombatWireObservation(in PacketObservationSource packet, int sourceId, int targetId, in CombatWireObservation observation)
    {
        lock (gate) inner.AppendCombatWireObservation(in packet, sourceId, targetId, in observation);
    }

    public void CompleteFlush(long flushId)
    {
        lock (gate) inner.CompleteFlush(flushId);
    }

    public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, int bodySkillVariantRaw, int marker, int layoutTag, int type)
    {
        lock (gate) inner.RegisterCompactValue0438(in packet, targetId, sourceId, bodySkillVariantRaw, marker, layoutTag, type);
    }

    public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, int bodySkillVariantRaw, int marker, int layoutTag, int type, int value)
    {
        lock (gate) inner.RegisterCompactValue0438(in packet, targetId, sourceId, bodySkillVariantRaw, marker, layoutTag, type, value);
    }

    public void RegisterCompactControl0238(in PacketObservationSource packet, int sourceId, int mode, uint bodyCodeRaw, int marker, int flag, int echoSourceId, int? availableCountAfterControl = null, int? cooldownMilliseconds = null)
    {
        lock (gate) inner.RegisterCompactControl0238(in packet, sourceId, mode, bodyCodeRaw, marker, flag, echoSourceId, availableCountAfterControl, cooldownMilliseconds);
    }

    public void RegisterCompactControl0638(in PacketObservationSource packet, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int flag)
    {
        lock (gate) inner.RegisterCompactControl0638(in packet, sourceId, bodyResourceEffectRef, marker, flag);
    }

    public void RegisterCooldownCharge2238(in PacketObservationSource packet, byte state, int packetSkillCode, int availableCount, int nextChargeRemainingMilliseconds)
    {
        lock (gate) inner.RegisterCooldownCharge2238(in packet, state, packetSkillCode, availableCount, nextChargeRemainingMilliseconds);
    }

    public void RegisterCooldown4738(in PacketObservationSource packet, int rowBaseSkillId, int remainingMilliseconds)
    {
        lock (gate) inner.RegisterCooldown4738(in packet, rowBaseSkillId, remainingMilliseconds);
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

    public void AppendPlayerGroupProfile(in PacketObservationSource packet, int originServerId, string nickname, in PlayerGroupMembership membership)
    {
        lock (gate) inner.AppendPlayerGroupProfile(in packet, originServerId, nickname, in membership);
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

    public void AppendNpcHp(in PacketObservationSource packet, int instanceId, long hp)
    {
        lock (gate) inner.AppendNpcHp(in packet, instanceId, hp);
    }

    public void AppendNpcHp(in PacketObservationSource packet, int instanceId, long hp, long maxHp)
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

    public void AppendNpc2136State(in PacketObservationSource packet, int instanceId, long sequence, long value0)
    {
        lock (gate) inner.AppendNpc2136State(in packet, instanceId, sequence, value0);
    }

    public void AppendNpc0140Value(in PacketObservationSource packet, int instanceId, long value0)
    {
        lock (gate) inner.AppendNpc0140Value(in packet, instanceId, value0);
    }

    public void AppendNpc0240Value(in PacketObservationSource packet, int instanceId, long value0)
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
