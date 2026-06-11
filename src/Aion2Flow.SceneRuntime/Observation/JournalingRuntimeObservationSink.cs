using Cloris.Aion2Flow.Protocol.Combat;
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

    public void StageDestinationMap(in PacketObservationSource packet, uint mapId) => StageDestinationMap(in packet, mapId, allowSameMapReload: false);

    public void StageDestinationMap(in PacketObservationSource packet, uint mapId, bool allowSameMapReload) => AppendSceneMapObservation(in packet, mapId, allowSameMapReload, "stage-destination-map");

    public void StagePendingDestinationMap(in PacketObservationSource packet, uint mapId, bool allowSameMapReload) => AppendSceneMapObservation(in packet, mapId, allowSameMapReload, "pending-destination-map");

    public void ConfirmDestinationMap(in PacketObservationSource packet, uint mapId, bool allowSameMapReload) => AppendSceneMapObservation(in packet, mapId, allowSameMapReload, "confirm-destination-map");

    public void ConfirmPendingDestinationMapArrival(in PacketObservationSource packet)
    {
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Scene,
            SourceEntityId = 0,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    private void AppendSceneMapObservation(in PacketObservationSource packet, uint mapId, bool allowSameMapReload, string diagnosticKey)
    {
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Scene,
            SourceEntityId = 0,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    public void StageDestinationMapInstance(in PacketObservationSource packet, uint instanceId)
    {
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Scene,
            SourceEntityId = 0,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    public void ConfirmDestinationMapInstance(in PacketObservationSource packet, uint instanceId)
    {
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Scene,
            SourceEntityId = 0,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    public void MarkSceneTransportBoundary(in PacketObservationSource packet)
    {
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Scene,
            SourceEntityId = 0,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    public void AppendCombatObservation(in PacketObservationSource packet, int sourceId, int targetId, in CombatObservation observation)
    {
        var normalized = CombatResourceRegistry.NormalizeObservationForStorage(sourceId, targetId, in observation);
        sourceId = ResolveLifecycleId(sourceId);
        targetId = ResolveLifecycleId(targetId);
        AddKnownEntity(sourceId);
        AddKnownEntity(targetId);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = packet.Raw,
            Combat = normalized
        });
    }

    public void CompleteBatch(long batchOrdinal) => journal.CompleteBatch(MapBatchOrdinal(batchOrdinal));

    public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int layoutTag, int type)
    {
        targetId = ResolveLifecycleId(targetId);
        sourceId = ResolveLifecycleId(sourceId);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = packet.Raw,
            Combat = new CombatObservation
            {
                BodyResourceEffectRef = bodyResourceEffectRef,
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

    public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int layoutTag, int type, int value)
    {
        targetId = ResolveLifecycleId(targetId);
        sourceId = ResolveLifecycleId(sourceId);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = packet.Raw,
            Combat = new CombatObservation
            {
                BodyResourceEffectRef = bodyResourceEffectRef,
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

    public void RegisterCompactControl0238(in PacketObservationSource packet, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker)
    {
        sourceId = ResolveLifecycleId(sourceId);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = 0,
            Raw = packet.Raw,
            Combat = new CombatObservation
            {
                BodyResourceEffectRef = bodyResourceEffectRef,
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

    public void RegisterCompactControl0638(in PacketObservationSource packet, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int flag)
    {
        sourceId = ResolveLifecycleId(sourceId);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = 0,
            Raw = packet.Raw,
            Combat = new CombatObservation
            {
                BodyResourceEffectRef = bodyResourceEffectRef,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 0,
                DetailRaw = marker,
                Marker = marker,
                Flag = flag,
                Type = 0,
                LayoutTag = 0
            }
        });
    }

    public void RegisterObservation2A38(in PacketObservationSource packet, int sourceId, int mode, int groupCode, int sequenceId, ushort headValue, ResourceEffectRef buffResourceEffectRef)
    {
        sourceId = ResolveLifecycleId(sourceId);
        AddKnownEntity(sourceId);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Aura,
            SourceEntityId = sourceId,
            TargetEntityId = 0,
            Raw = packet.Raw,
            Aura = new AuraObservation
            {
                SourceEntityId = sourceId,
                TargetEntityId = 0,
                BuffResourceEffectRef = buffResourceEffectRef,
                StackCount = 0,
                SequenceId = sequenceId,
                ChainId = 0,
                ResultCode = 0,
                Mode = mode
            }
        });
    }

    public void RegisterObservation2B38(in PacketObservationSource packet, int sourceId, int sourceIdCopy, int phase, int marker, ResourceEffectRef actionResourceEffectRef, int sequenceId, int stateValue, int detailValue, int tailLength)
    {
        sourceId = ResolveLifecycleId(sourceId);
        sourceIdCopy = ResolveLifecycleId(sourceIdCopy);
        AddKnownEntity(sourceId);
        AddKnownEntity(sourceIdCopy);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Action,
            SourceEntityId = sourceId,
            TargetEntityId = 0,
            Raw = packet.Raw,
            Action = new ActionObservation
            {
                SourceEntityId = sourceId,
                SourceEntityIdCopy = sourceIdCopy,
                Phase = phase,
                Marker = marker,
                ActionResourceEffectRef = actionResourceEffectRef,
                SequenceId = sequenceId,
                StateValue = stateValue,
                DetailValue = detailValue,
                TailLength = tailLength
            }
        });
    }

    public void RegisterObservation2C38(in PacketObservationSource packet, int instanceId, int mode, int sequenceId, int resultCode, int tailFirstValue, int tailUInt32Raw)
    {
        instanceId = ResolveLifecycleId(instanceId);
        AddKnownEntity(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.Latest2C38 = (sequenceId, resultCode);
        RememberNpcObservationSource(instanceId);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Aura,
            SourceEntityId = 0,
            TargetEntityId = instanceId,
            Raw = packet.Raw,
            Aura = new AuraObservation
            {
                SourceEntityId = 0,
                TargetEntityId = instanceId,
                StackCount = 0,
                SequenceId = sequenceId,
                ChainId = 0,
                ResultCode = resultCode,
                Mode = mode,
                TailFirstValue = tailFirstValue,
                TailUInt32Raw = tailUInt32Raw
            }
        });
    }

    public void AppendNickname(in PacketObservationSource packet, int uid, string nickname, int? originServerId = null, Faction faction = Faction.Unknown, CharacterClass? characterClass = null)
    {
        uid = ResolveLifecycleId(uid);
        AddKnownEntity(uid);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = uid,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    public void AppendNpcCode(in PacketObservationSource packet, int instanceId, int npcCode)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.NpcCode = npcCode;
        AddKnownEntity(instanceId);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    public void AppendNpcName(in PacketObservationSource packet, int npcCode, string name)
    {
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = 0,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    public void AppendNpcKind(in PacketObservationSource packet, int instanceId, NpcKind kind)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.Kind = kind;
        AddKnownEntity(instanceId);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    public void AppendNpcHp(in PacketObservationSource packet, int instanceId, int hp)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var stamp = CreateStamp(in packet);
        var state = GetOrAddNpcState(instanceId);
        state.Hp = hp;
        state.MaxHp = Math.Max(state.MaxHp ?? 0, hp);
        state.HpObservedAtMilliseconds = stamp.OffsetTicks / TimeSpan.TicksPerMillisecond;
        if (hp == 0)
            state.BattleToggledOn = false;
        AddKnownEntity(instanceId);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Resource,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    public void AppendNpcHp(in PacketObservationSource packet, int instanceId, int hp, int maxHp)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var stamp = CreateStamp(in packet);
        var state = GetOrAddNpcState(instanceId);
        state.Hp = hp;
        state.MaxHp = Math.Max(maxHp, hp);
        state.HpObservedAtMilliseconds = stamp.OffsetTicks / TimeSpan.TicksPerMillisecond;
        if (hp == 0)
            state.BattleToggledOn = false;
        AddKnownEntity(instanceId);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Resource,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    public void SetNpcBattle(in PacketObservationSource packet, int instanceId, bool isActive)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.BattleToggledOn = isActive && state.Hp != 0;
        AddKnownEntity(instanceId);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    public void ToggleNpcBattle(in PacketObservationSource packet, int instanceId)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        var next = !(state.BattleToggledOn ?? false);
        state.BattleToggledOn = next && state.Hp != 0;
        AddKnownEntity(instanceId);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    public void AppendNpc2136State(in PacketObservationSource packet, int instanceId, uint sequence, uint value0)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.Sequence2136 = sequence;
        state.Value2136 = value0;
        AddKnownEntity(instanceId);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    public void AppendNpc0140Value(in PacketObservationSource packet, int instanceId, uint value0)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.Value0140 = value0;
        AddKnownEntity(instanceId);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    public void AppendNpc0240Value(in PacketObservationSource packet, int instanceId, uint value0)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.Value0240 = value0;
        AddKnownEntity(instanceId);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    public void AppendNpc4636State(in PacketObservationSource packet, int instanceId, byte state0, byte state1)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.State4636 = (state0, state1);
        AddKnownEntity(instanceId);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = instanceId,
            TargetEntityId = 0,
            Raw = packet.Raw,
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

    public void AppendSummon(in PacketObservationSource packet, int ownerId, int summonInstanceId)
    {
        ownerId = ResolveLifecycleId(ownerId);
        summonInstanceId = ResolveLifecycleId(summonInstanceId);
        AddKnownEntity(ownerId);
        AddKnownEntity(summonInstanceId);
        _summonOwnerByInstance[summonInstanceId] = ownerId;
        GetOrAddNpcState(summonInstanceId).Kind = NpcKind.Summon;
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.State,
            SourceEntityId = ownerId,
            TargetEntityId = summonInstanceId,
            Raw = packet.Raw,
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

    private TimelineStamp CreateStamp(in PacketObservationSource packet)
        => clock.CreateStamp(packet.CaptureTimestampMilliseconds, packet.FrameOrdinal, MapBatchOrdinal(packet.BatchOrdinal));

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
