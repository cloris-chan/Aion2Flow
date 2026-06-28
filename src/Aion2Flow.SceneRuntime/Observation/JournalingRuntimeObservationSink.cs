using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Runtime;

namespace Cloris.Aion2Flow.SceneRuntime.Observation;

public sealed class JournalingRuntimeObservationSink : IRuntimeObservationSink
{
    private readonly ObservedEventJournal journal;
    private readonly SceneRuntimeClock clock;
    private readonly Func<Guid> sceneSessionId;
    private readonly Func<long>? nextBatchOrdinal;
    private readonly ILiveSceneCollectionPolicy? collectionPolicy;
    private readonly LifecycleRemapService _lifecycle = new();
    private readonly Dictionary<long, long> _mappedBatchOrdinals = [];
    private readonly Dictionary<int, RuntimeNpcState> _npcStates = [];
    private readonly HashSet<int> _knownEntities = [];
    private readonly Dictionary<int, int> _summonOwnerByInstance = [];

    public JournalingRuntimeObservationSink(ObservedEventJournal journal, SceneRuntimeClock clock, Guid sceneSessionId) : this(journal, clock, () => sceneSessionId, null, null)
    {
    }

    public JournalingRuntimeObservationSink(ObservedEventJournal journal, SceneRuntimeClock clock, Func<Guid> sceneSessionId, Func<long>? nextBatchOrdinal = null)
        : this(journal, clock, sceneSessionId, nextBatchOrdinal, null)
    {
    }

    internal JournalingRuntimeObservationSink(ObservedEventJournal journal, SceneRuntimeClock clock, Func<Guid> sceneSessionId, Func<long>? nextBatchOrdinal, ILiveSceneCollectionPolicy? collectionPolicy)
    {
        this.journal = journal;
        this.clock = clock;
        this.sceneSessionId = sceneSessionId;
        this.nextBatchOrdinal = nextBatchOrdinal;
        this.collectionPolicy = collectionPolicy;
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

    public void SeedNpcRuntimeState(in PacketObservationSource packet, int instanceId, in RuntimeNpcStateSnapshot state)
    {
        instanceId = ResolveLifecycleId(instanceId);
        _npcStates.TryGetValue(instanceId, out var cached);
        AddKnownEntity(instanceId);

        if (state.NpcCode is int npcCode)
        {
            var stamp = CreateStamp(in packet);
            journal.Append(new ObservedEventEnvelope
            {
                SceneSessionId = sceneSessionId(),
                Stamp = stamp,
                Domain = ObservedEventDomain.State,
                SourceEntityId = instanceId,
                TargetEntityId = 0,
                Raw = cached?.NpcCodeRaw ?? packet.Raw,
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

        if (state.Kind is NpcKind kind)
        {
            var stamp = CreateStamp(in packet);
            journal.Append(new ObservedEventEnvelope
            {
                SceneSessionId = sceneSessionId(),
                Stamp = stamp,
                Domain = ObservedEventDomain.State,
                SourceEntityId = instanceId,
                TargetEntityId = 0,
                Raw = cached?.KindRaw ?? packet.Raw,
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

        if (state.Hp is int hp)
        {
            var stamp = CreateStamp(in packet);
            journal.Append(new ObservedEventEnvelope
            {
                SceneSessionId = sceneSessionId(),
                Stamp = stamp,
                Domain = ObservedEventDomain.Resource,
                SourceEntityId = instanceId,
                TargetEntityId = 0,
                Raw = cached?.HpRaw ?? packet.Raw,
                Resource = new ResourceObservation
                {
                    EntityId = instanceId,
                    CurrentValue = hp,
                    MaximumValue = state.MaxHp,
                    Delta = null,
                    ResourceKind = 0
                }
            });
        }
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
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendCombat(in packet, sourceId, targetId, this))
            return;
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

    public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, int bodySkillVariantRaw, int marker, int layoutTag, int type)
    {
        targetId = ResolveLifecycleId(targetId);
        sourceId = ResolveLifecycleId(sourceId);
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendCombat(in packet, sourceId, targetId, this))
            return;
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
                SkillCode = bodySkillVariantRaw,
                BodySkillVariantRaw = bodySkillVariantRaw,
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

    public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, int bodySkillVariantRaw, int marker, int layoutTag, int type, int value)
    {
        targetId = ResolveLifecycleId(targetId);
        sourceId = ResolveLifecycleId(sourceId);
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendCombat(in packet, sourceId, targetId, this))
            return;
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
                SkillCode = bodySkillVariantRaw,
                BodySkillVariantRaw = bodySkillVariantRaw,
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

    public void RegisterCompactControl0238(in PacketObservationSource packet, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int echoSourceId)
    {
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendExtendedObservation())
            return;
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
                SkillCode = 0,
                BodySkillVariantRaw = 0,
                BodyResourceEffectRef = bodyResourceEffectRef,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 0,
                DetailRaw = marker,
                Marker = marker,
                ChainId = echoSourceId,
                Type = 0,
                LayoutTag = 0
            }
        });
    }

    public void RegisterCompactControl0638(in PacketObservationSource packet, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int flag)
    {
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendExtendedObservation())
            return;
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
                SkillCode = 0,
                BodySkillVariantRaw = 0,
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

    public void RegisterObservation2A38(in PacketObservationSource packet, int entityId, int mode, int groupCode, int instanceSequenceId, uint headCode, ushort headValue, ulong headMiddleRaw, uint timelineValue, uint stableValue, int echoSourceId, int stackValue, ResourceEffectRef buffResourceEffectRef, int tailLength, ulong tailLow64, ulong tailHigh64)
    {
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendExtendedObservation())
            return;
        entityId = ResolveLifecycleId(entityId);
        AddKnownEntity(entityId);
        var stamp = CreateStamp(in packet);
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneSessionId(),
            Stamp = stamp,
            Domain = ObservedEventDomain.Aura,
            SourceEntityId = entityId,
            TargetEntityId = 0,
            Raw = packet.Raw,
            Aura = new AuraObservation
            {
                Kind = AuraObservationKind.Open,
                EntityId = entityId,
                BuffResourceEffectRef = buffResourceEffectRef,
                StackCount = stackValue,
                InstanceSequenceId = instanceSequenceId,
                ResultCode = 0,
                OpenMode = mode,
                GroupCode = groupCode,
                HeadCode = headCode,
                HeadValue = headValue,
                HeadMiddleRaw = headMiddleRaw,
                TimelineValue = timelineValue,
                StableValue = stableValue,
                EchoSourceEntityId = echoSourceId,
                TailLength = tailLength,
                TailLow64 = tailLow64,
                TailHigh64 = tailHigh64
            }
        });
    }

    public void RegisterObservation2B38(in PacketObservationSource packet, int sourceId, int sourceIdCopy, int phase, int instanceSequenceId, ResourceEffectRef actionResourceEffectRef, int sequenceValue, int stateValue, int detailValue, int tailLength)
    {
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendExtendedObservation())
            return;
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
                InstanceSequenceId = instanceSequenceId,
                ActionResourceEffectRef = actionResourceEffectRef,
                SequenceValue = sequenceValue,
                StateValue = stateValue,
                DetailValue = detailValue,
                TailLength = tailLength
            }
        });
    }

    public void RegisterObservation2C38(in PacketObservationSource packet, int entityId, scoped ReadOnlySpan<AuraResultRecord> results)
    {
        entityId = ResolveLifecycleId(entityId);
        AddKnownEntity(entityId);
        var state = GetOrAddNpcState(entityId);
        RememberNpcObservationSource(entityId);
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendExtendedObservation())
            return;
        for (var resultIndex = 0; resultIndex < results.Length; resultIndex++)
        {
            ref readonly var result = ref results[resultIndex];
            state.Latest2C38 = (result.InstanceSequenceId, result.ResultCode);
            var stamp = CreateStamp(in packet);
            journal.Append(new ObservedEventEnvelope
            {
                SceneSessionId = sceneSessionId(),
                Stamp = stamp,
                Domain = ObservedEventDomain.Aura,
                SourceEntityId = 0,
                TargetEntityId = entityId,
                Raw = packet.Raw,
                Aura = new AuraObservation
                {
                    Kind = AuraObservationKind.Result,
                    EntityId = entityId,
                    StackCount = 0,
                    InstanceSequenceId = result.InstanceSequenceId,
                    ResultCode = result.ResultCode,
                    ResultCount = results.Length,
                    ResultIndex = resultIndex,
                    StateCode = result.StateCode,
                    ResultDetailEntityId = result.DetailEntityId,
                    ResultDetailValue0 = result.DetailValue0,
                    ResultDetailValue1 = result.DetailValue1
                }
            });
        }
    }

    public void AppendNickname(in PacketObservationSource packet, int uid, string nickname, Faction faction = Faction.Unknown, CharacterClass? characterClass = null, bool isLocalPlayer = false, int? originServerId = null, string legionName = "")
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
                Faction = faction,
                CharacterClass = characterClass,
                IsLocalPlayer = isLocalPlayer,
                OriginServerId = originServerId,
                LegionName = legionName
            }
        });
    }

    public void AppendNpcCode(in PacketObservationSource packet, int instanceId, int npcCode)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.NpcCode = npcCode;
        state.NpcCodeRaw = packet.Raw;
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
        state.KindRaw = packet.Raw;
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
        collectionPolicy?.OnBossMetadataChanged();
    }

    public void AppendNpcHp(in PacketObservationSource packet, int instanceId, int hp)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.Hp = hp;
        state.HpRaw = packet.Raw;
        state.HpObservedAtMilliseconds = ResolvePacketOffsetMilliseconds(in packet);
        if (hp == 0)
            state.BattleToggledOn = false;
        AddKnownEntity(instanceId);
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendResourceObservation())
            return;
        var stamp = CreateStamp(in packet);
        state.HpObservedAtMilliseconds = stamp.OffsetTicks / TimeSpan.TicksPerMillisecond;
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
        collectionPolicy?.OnBossMetadataChanged();
    }

    public void AppendNpcHp(in PacketObservationSource packet, int instanceId, int hp, int maxHp)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.Hp = hp;
        state.MaxHp = maxHp;
        state.HpRaw = packet.Raw;
        state.HpObservedAtMilliseconds = ResolvePacketOffsetMilliseconds(in packet);
        if (hp == 0)
            state.BattleToggledOn = false;
        AddKnownEntity(instanceId);
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendResourceObservation())
            return;
        var stamp = CreateStamp(in packet);
        state.HpObservedAtMilliseconds = stamp.OffsetTicks / TimeSpan.TicksPerMillisecond;
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
        collectionPolicy?.OnBossMetadataChanged();
    }

    public void SetNpcBattle(in PacketObservationSource packet, int instanceId, bool isActive)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.BattleToggledOn = isActive && state.Hp != 0;
        AddKnownEntity(instanceId);
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendExtendedObservation())
            return;
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
        collectionPolicy?.OnBossMetadataChanged();
    }

    public void ToggleNpcBattle(in PacketObservationSource packet, int instanceId)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        var next = !(state.BattleToggledOn ?? false);
        state.BattleToggledOn = next && state.Hp != 0;
        AddKnownEntity(instanceId);
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendExtendedObservation())
            return;
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
        collectionPolicy?.OnBossMetadataChanged();
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

    private long ResolvePacketOffsetMilliseconds(in PacketObservationSource packet) =>
        Math.Max(0, packet.CaptureTimestampMilliseconds - clock.SceneStartedAtMilliseconds);

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
        public RawPacketReference? NpcCodeRaw { get; set; }
        public RawPacketReference? KindRaw { get; set; }
        public RawPacketReference? HpRaw { get; set; }
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
