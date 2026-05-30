using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Runtime;

namespace Cloris.Aion2Flow.SceneRuntime.Observation;

public sealed class JournalingRuntimeObservationSink(ObservedEventJournal journal, SceneRuntimeClock clock, Func<Guid> sceneSessionId, Func<long>? nextBatchOrdinal = null) : IRuntimeObservationSink
{
    private readonly LifecycleRemapService _lifecycle = new();
    private readonly Dictionary<long, long> _mappedBatchOrdinals = [];
    private readonly Dictionary<int, RuntimeNpcState> _npcStates = [];
    private readonly HashSet<int> _knownEntities = [];
    private readonly Dictionary<int, int> _summonOwnerByInstance = [];

    public JournalingRuntimeObservationSink(ObservedEventJournal journal, SceneRuntimeClock clock, Guid sceneSessionId) : this(journal, clock, () => sceneSessionId)
    {
    }

    public ObservedEventJournal Journal => journal;

    public int CurrentTarget
    {
        get => _lifecycle.CurrentTarget;
        set => _lifecycle.CurrentTarget = value;
    }

    public int ResolveLifecycleId(int rawInstanceId) => _lifecycle.Resolve(rawInstanceId);

    public int RebindInstanceLifecycle(int rawInstanceId) => _lifecycle.Rebind(rawInstanceId);

    public void SetLifecycleId(int rawInstanceId, int mappedInstanceId) => _lifecycle.Set(rawInstanceId, mappedInstanceId);

    public bool IsKnownEntity(int id) => id > 0 && (_knownEntities.Contains(id) || _npcStates.ContainsKey(id) || _summonOwnerByInstance.ContainsKey(id));

    public bool HasSummonOwner(int instanceId) => instanceId > 0 && _summonOwnerByInstance.ContainsKey(ResolveLifecycleId(instanceId));

    public bool TryGetNpcRuntimeState(int instanceId, out RuntimeNpcStateSnapshot state)
    {
        if (_npcStates.TryGetValue(ResolveLifecycleId(instanceId), out var current))
        {
            state = current.ToSnapshot();
            return true;
        }

        state = default;
        return false;
    }

    public int ResolveNpcObservationSource() => _lifecycle.CurrentTarget > 0 ? _lifecycle.CurrentTarget : _lifecycle.LastObservedNpcSource;

    public void RememberNpcObservationSource(int instanceId)
    {
        instanceId = ResolveLifecycleId(instanceId);
        _lifecycle.RememberNpcObservationSource(instanceId);
        AddKnownEntity(instanceId);
    }

    public void StageDestinationMap(uint mapId) => StageDestinationMap(mapId, allowSameMapReload: false);

    public void StageDestinationMap(uint mapId, bool allowSameMapReload) => AppendSceneMapObservation(mapId, allowSameMapReload, "stage-destination-map");

    public void StagePendingDestinationMap(uint mapId, bool allowSameMapReload) => AppendSceneMapObservation(mapId, allowSameMapReload, "pending-destination-map");

    public void ConfirmDestinationMap(uint mapId, bool allowSameMapReload) => AppendSceneMapObservation(mapId, allowSameMapReload, "confirm-destination-map");

    public void ConfirmPendingDestinationMapArrival()
    {
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
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
                DiagnosticKey = "confirm-pending-destination-map-arrival"
            }
        });
    }

    private void AppendSceneMapObservation(uint mapId, bool allowSameMapReload, string diagnosticKey)
    {
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Scene,
            SourceEntityId = 0,
            TargetEntityId = 0,
            Raw = default,
            Scene = new SceneObservation
            {
                MapId = mapId,
                MapInstanceId = 0,
                Value0 = allowSameMapReload ? 1 : 0,
                Value1 = 0,
                DiagnosticKey = diagnosticKey
            }
        });
    }

    public void StageDestinationMapInstance(uint instanceId)
    {
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Scene,
            SourceEntityId = 0,
            TargetEntityId = 0,
            Raw = default,
            Scene = new SceneObservation
            {
                MapId = 0,
                MapInstanceId = instanceId,
                Value0 = 0,
                Value1 = 0,
                DiagnosticKey = "stage-destination-instance"
            }
        });
    }

    public void ConfirmDestinationMapInstance(uint instanceId)
    {
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Scene,
            SourceEntityId = 0,
            TargetEntityId = 0,
            Raw = default,
            Scene = new SceneObservation
            {
                MapId = 0,
                MapInstanceId = instanceId,
                Value0 = 0,
                Value1 = 0,
                DiagnosticKey = "confirm-destination-instance"
            }
        });
    }

    public void MarkSceneTransportBoundary()
    {
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
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
                DiagnosticKey = "scene-transport-boundary"
            }
        });
    }

    public void AppendCombatObservation(int sourceId, int targetId, long timestamp, long frameOrdinal, long batchOrdinal, in CombatObservation observation, ushort opcode = 0, int payloadLength = 0, long captureSequence = 0, PacketStructurePath structurePath = default)
    {
        var normalized = CombatResourceRegistry.NormalizeObservationForStorage(sourceId, targetId, in observation);
        sourceId = ResolveLifecycleId(sourceId);
        targetId = ResolveLifecycleId(targetId);
        AddKnownEntity(sourceId);
        AddKnownEntity(targetId);
        var stamp = clock.CreateStamp(timestamp, frameOrdinal, MapBatchOrdinal(batchOrdinal));
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference
            {
                Opcode = opcode,
                PayloadLength = payloadLength,
                CaptureSequence = captureSequence,
                TimestampMilliseconds = timestamp,
                StructurePath = structurePath
            },
            Combat = normalized
        });
    }

    public void CompleteBatch(long batchOrdinal) => journal.CompleteBatch(MapBatchOrdinal(batchOrdinal));

    public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, long timestamp, long frameOrdinal, long batchOrdinal, PacketStructurePath structurePath = default)
    {
        targetId = ResolveLifecycleId(targetId);
        sourceId = ResolveLifecycleId(sourceId);
        var stamp = clock.CreateStamp(timestamp, frameOrdinal, MapBatchOrdinal(batchOrdinal));
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference
            {
                Opcode = 0x0438,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = timestamp,
                StructurePath = structurePath
            },
            Combat = new CombatObservation
            {
                SkillCode = skillCodeRaw,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 0,
                DetailRaw = marker,
                Marker = marker,
                Type = type,
                LayoutTag = layoutTag
            }
        });
    }

    public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, int value, long timestamp, long frameOrdinal, long batchOrdinal, PacketStructurePath structurePath = default)
    {
        targetId = ResolveLifecycleId(targetId);
        sourceId = ResolveLifecycleId(sourceId);
        var stamp = clock.CreateStamp(timestamp, frameOrdinal, MapBatchOrdinal(batchOrdinal));
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference
            {
                Opcode = 0x0438,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = timestamp,
                StructurePath = structurePath
            },
            Combat = new CombatObservation
            {
                SkillCode = skillCodeRaw,
                Damage = value,
                HitCount = 0,
                AttemptCount = 0,
                DetailRaw = marker,
                Marker = marker,
                Type = type,
                LayoutTag = layoutTag
            }
        });
    }

    public void RegisterCompactControl0238(int sourceId, int skillCodeRaw, int marker, long batchOrdinal, PacketStructurePath structurePath = default)
    {
        sourceId = ResolveLifecycleId(sourceId);
        var stamp = clock.CreateStampFromOffset(0, 0, MapBatchOrdinal(batchOrdinal));
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = 0,
            Raw = new RawPacketReference
            {
                Opcode = 0x0238,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = 0,
                StructurePath = structurePath
            },
            Combat = new CombatObservation
            {
                SkillCode = skillCodeRaw,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 0,
                DetailRaw = marker,
                Marker = marker,
                Type = 0,
                LayoutTag = 0
            }
        });
    }

    public void RegisterCompactControl0638(int sourceId, int skillCodeRaw, int marker, long timestamp, long frameOrdinal, long batchOrdinal, PacketStructurePath structurePath = default)
    {
        sourceId = ResolveLifecycleId(sourceId);
        var stamp = clock.CreateStamp(timestamp, frameOrdinal, MapBatchOrdinal(batchOrdinal));
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = 0,
            Raw = new RawPacketReference
            {
                Opcode = 0x0638,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = timestamp,
                StructurePath = structurePath
            },
            Combat = new CombatObservation
            {
                SkillCode = skillCodeRaw,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 0,
                DetailRaw = marker,
                Marker = marker,
                Type = 0,
                LayoutTag = 0
            }
        });
    }

    public void RegisterObservation2A38(int sourceId, int mode, int groupCode, int sequenceId, ushort headValue, uint buffCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal, PacketStructurePath structurePath = default)
    {
        sourceId = ResolveLifecycleId(sourceId);
        AddKnownEntity(sourceId);
        var stamp = clock.CreateStamp(timestamp, frameOrdinal, MapBatchOrdinal(batchOrdinal));
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Aura,
            SourceEntityId = sourceId,
            TargetEntityId = 0,
            Raw = new RawPacketReference
            {
                Opcode = 0x2A38,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = timestamp,
                StructurePath = structurePath
            },
            Aura = new AuraObservation
            {
                SourceEntityId = sourceId,
                TargetEntityId = 0,
                SkillCode = (int)buffCodeRaw,
                StackCount = 0,
                SequenceId = sequenceId,
                ChainId = 0,
                ResultCode = 0,
                Mode = mode
            }
        });
    }

    public void RegisterObservation2C38(int instanceId, int mode, int sequenceId, int resultCode, int tailSourceId, int tailSkillCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal, PacketStructurePath structurePath = default)
    {
        instanceId = ResolveLifecycleId(instanceId);
        tailSourceId = ResolveLifecycleId(tailSourceId);
        AddKnownEntity(instanceId);
        AddKnownEntity(tailSourceId);
        var state = GetOrAddNpcState(instanceId);
        state.Latest2C38 = (sequenceId, resultCode);
        RememberNpcObservationSource(instanceId);
        var stamp = clock.CreateStamp(timestamp, frameOrdinal, MapBatchOrdinal(batchOrdinal));
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Aura,
            SourceEntityId = tailSourceId,
            TargetEntityId = instanceId,
            Raw = new RawPacketReference
            {
                Opcode = 0x2C38,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = timestamp,
                StructurePath = structurePath
            },
            Aura = new AuraObservation
            {
                SourceEntityId = tailSourceId,
                TargetEntityId = instanceId,
                SkillCode = tailSkillCodeRaw,
                StackCount = 0,
                SequenceId = sequenceId,
                ChainId = 0,
                ResultCode = resultCode,
                Mode = mode
            }
        });
    }

    public void AppendNickname(int uid, string nickname, int? originServerId = null, Faction faction = Faction.Unknown, CharacterClass? characterClass = null)
    {
        uid = ResolveLifecycleId(uid);
        AddKnownEntity(uid);
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = uid,
            TargetEntityId = 0,
            Raw = default,
            State = new StateObservation
            {
                EntityId = uid,
                StateCode = StateCodes.PlayerIdentity,
                Value0 = 0,
                Value1 = 0,
                DetailRaw = 0,
                Text = nickname,
                OriginServerId = originServerId,
                Faction = faction,
                CharacterClass = characterClass
            }
        });
    }

    public void AppendNpcCode(int instanceId, int npcCode)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.NpcCode = npcCode;
        AddKnownEntity(instanceId);
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
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

    public void AppendNpcName(int npcCode, string name)
    {
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = 0,
            TargetEntityId = 0,
            Raw = default,
            State = new StateObservation
            {
                EntityId = npcCode,
                StateCode = StateCodes.NpcName,
                Value0 = 0,
                Value1 = 0,
                DetailRaw = 0,
                Text = name
            }
        });
    }

    public void AppendNpcKind(int instanceId, NpcKind kind)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.Kind = kind;
        AddKnownEntity(instanceId);
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = default,
            State = new StateObservation
            {
                EntityId = instanceId,
                StateCode = StateCodes.NpcKind,
                Value0 = (int)kind,
                Value1 = 0,
                DetailRaw = 0
            }
        });
    }

    public void AppendNpcHp(int instanceId, int hp, long observedAtMilliseconds)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.Hp = hp;
        state.MaxHp = Math.Max(state.MaxHp ?? 0, hp);
        state.HpObservedAtMilliseconds = observedAtMilliseconds;
        if (hp == 0)
            state.BattleToggledOn = false;
        AddKnownEntity(instanceId);
        var stamp = clock.CreateStamp(observedAtMilliseconds, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Resource,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = new RawPacketReference
            {
                Opcode = 0x008D,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = observedAtMilliseconds
            },
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
        var state = GetOrAddNpcState(instanceId);
        state.Hp = hp;
        state.MaxHp = Math.Max(maxHp, hp);
        state.HpObservedAtMilliseconds = observedAtMilliseconds;
        if (hp == 0)
            state.BattleToggledOn = false;
        AddKnownEntity(instanceId);
        var stamp = clock.CreateStamp(observedAtMilliseconds, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Resource,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = new RawPacketReference
            {
                Opcode = 0x008D,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = observedAtMilliseconds
            },
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
        var state = GetOrAddNpcState(instanceId);
        state.BattleToggledOn = isActive && state.Hp != 0;
        AddKnownEntity(instanceId);
        var stamp = clock.CreateStamp(observedAtMilliseconds, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = new RawPacketReference
            {
                Opcode = 0x218D,
                PayloadLength = 0,
                CaptureSequence = 0,
                TimestampMilliseconds = observedAtMilliseconds
            },
            State = new StateObservation
            {
                EntityId = instanceId,
                StateCode = StateCodes.NpcBattle,
                Value0 = isActive ? 1 : 0,
                Value1 = 0,
                DetailRaw = 0
            }
        });
    }

    public void ToggleNpcBattle(int instanceId)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        var next = !(state.BattleToggledOn ?? false);
        state.BattleToggledOn = next && state.Hp != 0;
        AddKnownEntity(instanceId);
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = default,
            State = new StateObservation
            {
                EntityId = instanceId,
                StateCode = StateCodes.NpcBattleToggle,
                Value0 = 0,
                Value1 = 0,
                DetailRaw = 0
            }
        });
    }

    public void AppendNpc2136State(int instanceId, uint sequence, uint value0)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.Sequence2136 = sequence;
        state.Value2136 = value0;
        AddKnownEntity(instanceId);
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
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
        var state = GetOrAddNpcState(instanceId);
        state.Value0140 = value0;
        AddKnownEntity(instanceId);
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
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
        var state = GetOrAddNpcState(instanceId);
        state.Value0240 = value0;
        AddKnownEntity(instanceId);
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
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
        var state = GetOrAddNpcState(instanceId);
        state.State4636 = (state0, state1);
        AddKnownEntity(instanceId);
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
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
        AddKnownEntity(ownerId);
        AddKnownEntity(summonInstanceId);
        _summonOwnerByInstance[summonInstanceId] = ownerId;
        GetOrAddNpcState(summonInstanceId).Kind = NpcKind.Summon;
        var stamp = clock.CreateStampFromOffset(0, 0, 0);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
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

    private long MapBatchOrdinal(long batchOrdinal)
    {
        if (nextBatchOrdinal is null || batchOrdinal <= 0)
            return batchOrdinal;

        if (_mappedBatchOrdinals.TryGetValue(batchOrdinal, out var mapped))
            return mapped;

        mapped = nextBatchOrdinal();
        _mappedBatchOrdinals[batchOrdinal] = mapped;
        return mapped;
    }

    private RuntimeNpcState GetOrAddNpcState(int instanceId)
    {
        if (!_npcStates.TryGetValue(instanceId, out var state))
        {
            state = new RuntimeNpcState();
            _npcStates[instanceId] = state;
        }

        return state;
    }

    private void AddKnownEntity(int entityId)
    {
        if (entityId > 0)
            _knownEntities.Add(entityId);
    }

    private sealed class RuntimeNpcState
    {
        public int? NpcCode { get; set; }
        public int? Hp { get; set; }
        public int? MaxHp { get; set; }
        public long? HpObservedAtMilliseconds { get; set; }
        public bool? BattleToggledOn { get; set; }
        public NpcKind? Kind { get; set; }
        public uint? Value2136 { get; set; }
        public uint? Sequence2136 { get; set; }
        public uint? Value0140 { get; set; }
        public uint? Value0240 { get; set; }
        public (byte State0, byte State1)? State4636 { get; set; }
        public (int SequenceId, int ResultCode)? Latest2C38 { get; set; }

        public RuntimeNpcStateSnapshot ToSnapshot() => new(NpcCode, Hp, MaxHp, HpObservedAtMilliseconds, BattleToggledOn, Kind, Value2136, Sequence2136, Value0140, Value0240, State4636, Latest2C38);
    }
}
