using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Runtime;

namespace Cloris.Aion2Flow.Scene.Observation;

public sealed class JournalingRuntimeObservationSink(ObservedEventJournal journal, SceneRuntimeClock clock, Guid sceneSessionId) : IRuntimeObservationSink
{
    private readonly LifecycleRemapService _lifecycle = new();

    public ObservedEventJournal Journal => journal;

    public int CurrentTarget
    {
        get => _lifecycle.CurrentTarget;
        set => _lifecycle.CurrentTarget = value;
    }

    public int ResolveLifecycleId(int rawInstanceId) => _lifecycle.Resolve(rawInstanceId);

    public int RebindInstanceLifecycle(int rawInstanceId) => _lifecycle.Rebind(rawInstanceId);

    public void SetLifecycleId(int rawInstanceId, int mappedInstanceId) => _lifecycle.Set(rawInstanceId, mappedInstanceId);

    public bool IsKnownEntity(int id) => false;

    public bool HasSummonOwner(int instanceId) => false;

    public bool TryGetNpcRuntimeState(int instanceId, out RuntimeNpcStateSnapshot state)
    {
        state = default;
        return false;
    }

    public int ResolveNpcObservationSource() => _lifecycle.CurrentTarget > 0 ? _lifecycle.CurrentTarget : _lifecycle.LastObservedNpcSource;

    public void RememberNpcObservationSource(int instanceId) => _lifecycle.RememberNpcObservationSource(instanceId);

    public void StageDestinationMap(uint mapId)
    {
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.Scene,
            SourceEntityId = 0,
            TargetEntityId = 0,
            Raw = default,
            Scene = new SceneObservation
            {
                MapId = (int)mapId,
                MapInstanceId = 0,
                Value0 = 0,
                Value1 = 0,
                DiagnosticKey = "stage-destination-map"
            }
        });
    }

    public void StageDestinationMapInstance(uint instanceId)
    {
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.Scene,
            SourceEntityId = 0,
            TargetEntityId = 0,
            Raw = default,
            Scene = new SceneObservation
            {
                MapId = 0,
                MapInstanceId = (int)instanceId,
                Value0 = 0,
                Value1 = 0,
                DiagnosticKey = "stage-destination-instance"
            }
        });
    }

    public void MarkSceneArrival()
    {
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.Scene,
            SourceEntityId = 0,
            TargetEntityId = 0,
            Raw = default,
            Scene = new SceneObservation
            {
                MapId = 0,
                MapInstanceId = 0,
                Value0 = 0,
                Value1 = 0,
                DiagnosticKey = "scene-arrival"
            }
        });
    }

    public void AppendCombatPacket(ParsedCombatPacket packet)
    {
        var sourceId = ResolveLifecycleId(packet.SourceId);
        var targetId = ResolveLifecycleId(packet.TargetId);
        var stamp = clock.CreateStamp(packet.Timestamp, packet.FrameOrdinal, packet.BatchOrdinal);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference
            {
                Opcode = 0,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = packet.Timestamp
            },
            Combat = new CombatObservation
            {
                SkillCode = packet.SkillCode,
                OriginalSkillCode = packet.OriginalSkillCode,
                BaseSkillCode = packet.BaseSkillCode,
                Damage = packet.Damage,
                HitCount = packet.HitContribution,
                AttemptCount = packet.AttemptContribution,
                DetailRaw = packet.DetailRaw,
                Marker = packet.Marker,
                Type = packet.Type,
                Flag = packet.Flag,
                LayoutTag = packet.LayoutTag,
                Loop = packet.Loop,
                DrainHealAmount = packet.DrainHealAmount,
                RegenerationAmount = packet.RegenerationAmount,
                Modifiers = packet.Modifiers,
                ResourceKind = packet.ResourceKind,
                EventKind = packet.EventKind,
                ValueKind = packet.ValueKind,
                EffectTag = packet.EffectTag,
                PeriodicRelation = packet.PeriodicRelation,
                PeriodicMode = packet.PeriodicMode,
                ChainId = packet.Unknown
            }
        });
    }

    public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        targetId = ResolveLifecycleId(targetId);
        sourceId = ResolveLifecycleId(sourceId);
        var stamp = clock.CreateStamp(timestamp, frameOrdinal, batchOrdinal);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference
            {
                Opcode = 0x0438,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = timestamp
            },
            Combat = new CombatObservation
            {
                SkillCode = skillCodeRaw,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 0,
                DetailRaw = marker
            }
        });
    }

    public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, int value, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        targetId = ResolveLifecycleId(targetId);
        sourceId = ResolveLifecycleId(sourceId);
        var stamp = clock.CreateStamp(timestamp, frameOrdinal, batchOrdinal);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference
            {
                Opcode = 0x0438,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = timestamp
            },
            Combat = new CombatObservation
            {
                SkillCode = skillCodeRaw,
                Damage = value,
                HitCount = 0,
                AttemptCount = 0,
                DetailRaw = marker
            }
        });
    }

    public void RegisterCompactControl0238(int sourceId, int skillCodeRaw, int marker, long batchOrdinal)
    {
        sourceId = ResolveLifecycleId(sourceId);
        var stamp = clock.CreateStampFromOffset(0, 0, batchOrdinal);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = 0,
            Raw = new RawPacketReference
            {
                Opcode = 0x0238,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = 0
            },
            Combat = new CombatObservation
            {
                SkillCode = skillCodeRaw,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 0,
                DetailRaw = marker
            }
        });
    }

    public void RegisterCompactControl0638(int sourceId, int skillCodeRaw, int marker, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        sourceId = ResolveLifecycleId(sourceId);
        var stamp = clock.CreateStamp(timestamp, frameOrdinal, batchOrdinal);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = 0,
            Raw = new RawPacketReference
            {
                Opcode = 0x0638,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = timestamp
            },
            Combat = new CombatObservation
            {
                SkillCode = skillCodeRaw,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 0,
                DetailRaw = marker
            }
        });
    }

    public void RegisterPeriodicLink0538(int targetId, int sourceId, int linkId, int sequenceId, int tailRaw, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        targetId = ResolveLifecycleId(targetId);
        sourceId = ResolveLifecycleId(sourceId);
        var stamp = clock.CreateStamp(timestamp, frameOrdinal, batchOrdinal);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference
            {
                Opcode = 0x0538,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = timestamp
            },
            Combat = new CombatObservation
            {
                SkillCode = tailRaw,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 0,
                DetailRaw = linkId
            }
        });
    }

    public void RegisterObservation2A38(int sourceId, int mode, int groupCode, int sequenceId, ushort headValue, uint buffCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        sourceId = ResolveLifecycleId(sourceId);
        var stamp = clock.CreateStamp(timestamp, frameOrdinal, batchOrdinal);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.Aura,
            SourceEntityId = sourceId,
            TargetEntityId = 0,
            Raw = new RawPacketReference
            {
                Opcode = 0x2A38,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = timestamp
            },
            Aura = new AuraObservation
            {
                SourceEntityId = sourceId,
                TargetEntityId = 0,
                SkillCode = (int)buffCodeRaw,
                StackCount = 0,
                SequenceId = sequenceId,
                ChainId = 0,
                ResultCode = 0
            }
        });
    }

    public void RegisterObservation2C38(int instanceId, int mode, int sequenceId, int resultCode, int tailSourceId, int tailSkillCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        instanceId = ResolveLifecycleId(instanceId);
        tailSourceId = ResolveLifecycleId(tailSourceId);
        var stamp = clock.CreateStamp(timestamp, frameOrdinal, batchOrdinal);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.Aura,
            SourceEntityId = tailSourceId,
            TargetEntityId = instanceId,
            Raw = new RawPacketReference
            {
                Opcode = 0x2C38,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = timestamp
            },
            Aura = new AuraObservation
            {
                SourceEntityId = tailSourceId,
                TargetEntityId = instanceId,
                SkillCode = tailSkillCodeRaw,
                StackCount = 0,
                SequenceId = sequenceId,
                ChainId = 0,
                ResultCode = resultCode
            }
        });
    }

    public void AppendNickname(int uid, string nickname, int? originServerId = null)
    {
        uid = ResolveLifecycleId(uid);
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = uid,
            TargetEntityId = 0,
            Raw = default,
            State = new StateObservation
            {
                EntityId = uid,
                StateCode = 0,
                Value0 = 0,
                Value1 = 0,
                DetailRaw = 0
            }
        });
    }

    public void AppendNpcCode(int instanceId, int npcCode)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = default,
            State = new StateObservation
            {
                EntityId = instanceId,
                StateCode = npcCode,
                Value0 = 0,
                Value1 = 0,
                DetailRaw = 0
            }
        });
    }

    public void AppendNpcName(int npcCode, string name) { }
    public void AppendNpcKind(int instanceId, NpcKind kind) { }

    public void AppendNpcHp(int instanceId, int hp, long observedAtMilliseconds)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var stamp = clock.CreateStamp(observedAtMilliseconds, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.Resource,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = default,
            Resource = new ResourceObservation
            {
                EntityId = instanceId,
                CurrentValue = hp,
                MaximumValue = null,
                Delta = null,
                ResourceKind = 0
            }
        });
    }

    public void AppendNpcHp(int instanceId, int hp, int maxHp, long observedAtMilliseconds)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var stamp = clock.CreateStamp(observedAtMilliseconds, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.Resource,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = default,
            Resource = new ResourceObservation
            {
                EntityId = instanceId,
                CurrentValue = hp,
                MaximumValue = maxHp,
                Delta = null,
                ResourceKind = 0
            }
        });
    }

    public void SetNpcBattle(int instanceId, bool isActive, long observedAtMilliseconds)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var stamp = clock.CreateStamp(observedAtMilliseconds, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = default,
            State = new StateObservation
            {
                EntityId = instanceId,
                StateCode = isActive ? 1 : 0,
                Value0 = 0,
                Value1 = 0,
                DetailRaw = 0
            }
        });
    }

    public void ToggleNpcBattle(int instanceId) { }

    public void AppendNpc2136State(int instanceId, uint sequence, uint value0)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = default,
            State = new StateObservation
            {
                EntityId = instanceId,
                StateCode = 2136,
                Value0 = (int)sequence,
                Value1 = (int)value0,
                DetailRaw = 0
            }
        });
    }

    public void AppendNpc0140Value(int instanceId, uint value0)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = default,
            State = new StateObservation
            {
                EntityId = instanceId,
                StateCode = 140,
                Value0 = (int)value0,
                Value1 = 0,
                DetailRaw = 0
            }
        });
    }

    public void AppendNpc0240Value(int instanceId, uint value0)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = default,
            State = new StateObservation
            {
                EntityId = instanceId,
                StateCode = 240,
                Value0 = (int)value0,
                Value1 = 0,
                DetailRaw = 0
            }
        });
    }

    public void AppendNpc4636State(int instanceId, byte state0, byte state1)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = default,
            State = new StateObservation
            {
                EntityId = instanceId,
                StateCode = 4636,
                Value0 = state0,
                Value1 = state1,
                DetailRaw = 0
            }
        });
    }

    public void AppendSummon(int ownerId, int summonInstanceId)
    {
        ownerId = ResolveLifecycleId(ownerId);
        summonInstanceId = ResolveLifecycleId(summonInstanceId);
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId,
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = ownerId,
            TargetEntityId = summonInstanceId,
            Raw = default,
            State = new StateObservation
            {
                EntityId = summonInstanceId,
                StateCode = 0,
                Value0 = ownerId,
                Value1 = 0,
                DetailRaw = 0
            }
        });
    }
}
