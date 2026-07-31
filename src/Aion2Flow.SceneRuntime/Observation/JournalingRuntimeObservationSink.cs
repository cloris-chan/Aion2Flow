using Cloris.Aion2Flow.Protocol.Combat;
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
    private readonly Func<long>? nextFlushId;
    private readonly ILiveSceneCollectionPolicy? collectionPolicy;
    private readonly MapRuntimeObservationContext _mapContext;
    private readonly Dictionary<long, long> _mappedFlushIds = [];

    public JournalingRuntimeObservationSink(ObservedEventJournal journal, SceneRuntimeClock clock, Guid sceneSessionId)
        : this(journal, clock, () => sceneSessionId, null, null, new MapRuntimeObservationContext())
    {
    }

    public JournalingRuntimeObservationSink(ObservedEventJournal journal, SceneRuntimeClock clock, Func<Guid> sceneSessionId, Func<long>? nextFlushId = null)
        : this(journal, clock, sceneSessionId, nextFlushId, null, new MapRuntimeObservationContext())
    {
    }

    internal JournalingRuntimeObservationSink(
        ObservedEventJournal journal,
        SceneRuntimeClock clock,
        Func<Guid> sceneSessionId,
        Func<long>? nextFlushId,
        ILiveSceneCollectionPolicy? collectionPolicy,
        MapRuntimeObservationContext mapContext)
    {
        this.journal = journal;
        this.clock = clock;
        this.sceneSessionId = sceneSessionId;
        this.nextFlushId = nextFlushId;
        this.collectionPolicy = collectionPolicy;
        _mapContext = mapContext;
    }

    public ObservedEventJournal Journal => journal;

    public int CurrentTarget
    {
        get => EntityIngress.Lifecycle.CurrentTarget;
        set => EntityIngress.Lifecycle.CurrentTarget = value;
    }

    public int ResolveLifecycleId(int rawInstanceId) => EntityIngress.Lifecycle.Resolve(rawInstanceId);

    public int RebindInstanceLifecycle(int rawInstanceId) => EntityIngress.Lifecycle.Rebind(rawInstanceId);

    public void SetLifecycleId(int rawInstanceId, int mappedInstanceId) => EntityIngress.Lifecycle.Set(rawInstanceId, mappedInstanceId);

    public bool IsKnownEntity(int id) =>
        id > 0 &&
        (EntityIngress.KnownEntities.Contains(id) ||
         EntityIngress.NpcStates.ContainsKey(id) ||
         EntityIngress.SummonOwnerByInstance.ContainsKey(id));

    public bool HasSummonOwner(int instanceId) =>
        instanceId > 0 &&
        EntityIngress.SummonOwnerByInstance.ContainsKey(ResolveLifecycleId(instanceId));

    public bool TryGetNpcRuntimeState(int instanceId, out RuntimeNpcStateSnapshot state)
    {
        if (EntityIngress.NpcStates.TryGetValue(ResolveLifecycleId(instanceId), out var current))
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
        EntityIngress.NpcStates.TryGetValue(instanceId, out var cached);
        AddKnownEntity(instanceId);

        if (state.NpcCode is int npcCode)
        {
            var stamp = CreateStamp(in packet);
            journal.Append(
                CreateHeader(in stamp, instanceId, 0, cached?.NpcCodeRaw ?? packet.Raw),
                new StateObservation
                {
                    EntityId = instanceId,
                    StateCode = npcCode,
                    Value0 = 0,
                    Value1 = 0,
                    DetailRaw = 0
                });
        }

        if (state.Kind is NpcKind kind)
        {
            var stamp = CreateStamp(in packet);
            journal.Append(
                CreateHeader(in stamp, instanceId, 0, cached?.KindRaw ?? packet.Raw),
                new StateObservation
                {
                    EntityId = instanceId,
                    StateCode = StateCodes.NpcKind,
                    Value0 = (int)kind,
                    Value1 = 0,
                    DetailRaw = 0
                });
        }

        if (state.Hp is long hp)
        {
            var stamp = CreateStamp(in packet);
            journal.Append(
                CreateHeader(in stamp, instanceId, 0, cached?.HpRaw ?? packet.Raw),
                new EntityVitalObservation
                {
                    EntityId = instanceId,
                    CurrentHp = hp,
                    MaxHp = state.MaxHp
                });
        }
    }

    public int ResolveNpcObservationSource() =>
        EntityIngress.Lifecycle.CurrentTarget > 0
            ? EntityIngress.Lifecycle.CurrentTarget
            : EntityIngress.Lifecycle.LastObservedNpcSource;

    public void RememberNpcObservationSource(int instanceId)
    {
        instanceId = ResolveLifecycleId(instanceId);
        EntityIngress.Lifecycle.RememberNpcObservationSource(instanceId);
        AddKnownEntity(instanceId);
    }

    public void SetCurrentMap(in PacketObservationSource packet, uint mapId)
    {
        if (_mapContext.TryConfirmCurrentMap(mapId, out var boundaryMapId))
        {
            StartMapContext(in packet, boundaryMapId);
            return;
        }

        AppendSceneMapObservation(in packet, mapId, SceneObservationKind.CurrentMap);
    }

    public void EnsureUnknownMapScope(in PacketObservationSource packet)
    {
        if (_mapContext.HasMapScope)
        {
            return;
        }

        StartMapContext(in packet, 0, provisional: true);
    }

    public bool StageMapCandidate(in PacketObservationSource packet, uint mapId)
    {
        switch (_mapContext.StageMapCandidate(mapId))
        {
            case MapScopeCandidateResult.CurrentMapAdopted:
                AppendSceneMapObservation(in packet, mapId, SceneObservationKind.CurrentMap);
                return false;
            case MapScopeCandidateResult.CandidateStaged:
                AppendSceneMapObservation(in packet, mapId, SceneObservationKind.MapCandidateObserved);
                return false;
            case MapScopeCandidateResult.ArrivalBoundary:
                var hadMapScope = _mapContext.HasMapScope;
                StartMapContext(in packet, mapId);
                return hadMapScope;
            default:
                return false;
        }
    }

    public bool ConfirmDestinationMapArrival(in PacketObservationSource packet)
    {
        if (_mapContext.TryCommitArrival(out var mapId))
        {
            StartMapContext(in packet, mapId);
            return true;
        }

        AppendSceneMapObservation(in packet, mapId, SceneObservationKind.DestinationMapArrival);
        return false;
    }

    private void AppendSceneMapObservation(
        in PacketObservationSource packet,
        uint mapId,
        SceneObservationKind kind)
    {
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, 0, 0, packet.Raw),
            new SceneObservation
            {
                MapId = mapId,
                MapInstanceId = 0,
                Kind = kind
            });
    }

    public void RegisterMapEvent(in PacketObservationSource packet, uint instanceId)
    {
        _mapContext.RegisterMapEvent(instanceId);
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, 0, 0, packet.Raw),
            new SceneObservation
            {
                MapId = 0,
                MapInstanceId = instanceId,
                Kind = SceneObservationKind.MapEventRegistered
            });
    }

    public void UnregisterMapEvent(in PacketObservationSource packet, uint instanceId)
    {
        _mapContext.UnregisterMapEvent(instanceId);
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, 0, 0, packet.Raw),
            new SceneObservation
            {
                MapId = 0,
                MapInstanceId = instanceId,
                Kind = SceneObservationKind.MapEventUnregistered
            });
    }

    public void MarkTransportStreamActivated(in PacketObservationSource packet)
    {
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, 0, 0, packet.Raw),
            new SceneObservation
            {
                MapId = 0,
                MapInstanceId = 0,
                Kind = SceneObservationKind.TransportStreamActivated
            });
    }

    private void StartMapContext(
        in PacketObservationSource packet,
        uint mapId,
        bool provisional = false)
    {
        var hadMapScope = _mapContext.HasMapScope;
        collectionPolicy?.StartMapContext(in packet, mapId);
        _mapContext.StartMapContext(mapId, provisional, preserveEntities: !hadMapScope);
        if (provisional)
            return;

        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, 0, 0, packet.Raw),
            new SceneObservation
            {
                MapId = mapId,
                MapInstanceId = 0,
                Kind = SceneObservationKind.MapContextStarted
            });
    }

    public void AppendCombatWireObservation(in PacketObservationSource packet, int sourceId, int targetId, in CombatWireObservation observation)
    {
        EnsureUnknownMapScope(in packet);
        sourceId = ResolveLifecycleId(sourceId);
        targetId = ResolveLifecycleId(targetId);
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendCombat(in packet, sourceId, targetId, this))
            return;
        _mapContext.MarkCombatObserved();
        AddKnownEntity(sourceId);
        AddKnownEntity(targetId);
        var stamp = CreateStamp(in packet);
        journal.Append(CreateHeader(in stamp, sourceId, targetId, packet.Raw), in observation);
    }

    public void CompleteFlush(long flushId) => journal.CompleteFlush(MapFlushId(flushId));

    public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, int bodySkillVariantRaw, int marker, int layoutTag, int type)
    {
        EnsureUnknownMapScope(in packet);
        targetId = ResolveLifecycleId(targetId);
        sourceId = ResolveLifecycleId(sourceId);
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendCombat(in packet, sourceId, targetId, this))
            return;
        _mapContext.MarkCombatObserved();
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, sourceId, targetId, packet.Raw),
            new CombatWireObservation
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
            });
    }

    public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, int bodySkillVariantRaw, int marker, int layoutTag, int type, int value)
    {
        EnsureUnknownMapScope(in packet);
        targetId = ResolveLifecycleId(targetId);
        sourceId = ResolveLifecycleId(sourceId);
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendCombat(in packet, sourceId, targetId, this))
            return;
        _mapContext.MarkCombatObserved();
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, sourceId, targetId, packet.Raw),
            new CombatWireObservation
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
            });
    }

    public void RegisterCompactControl0238(in PacketObservationSource packet, int sourceId, int mode, uint bodyCodeRaw, int marker, int flag, int echoSourceId)
    {
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendExtendedObservation())
            return;
        sourceId = ResolveLifecycleId(sourceId);
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, sourceId, 0, packet.Raw),
            new CombatWireObservation
            {
                SkillCode = 0,
                BodySkillVariantRaw = 0,
                BodyCodeRaw = bodyCodeRaw,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 0,
                DetailRaw = marker,
                Marker = marker,
                Flag = flag,
                ChainId = echoSourceId,
                Type = mode,
                LayoutTag = 0
            });
    }

    public void RegisterCompactControl0638(in PacketObservationSource packet, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int flag)
    {
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendExtendedObservation())
            return;
        sourceId = ResolveLifecycleId(sourceId);
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, sourceId, 0, packet.Raw),
            new CombatWireObservation
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
            });
    }

    public void RegisterObservation2A38(in PacketObservationSource packet, int entityId, int mode, int groupCode, int instanceSequenceId, uint headCode, ushort headValue, ulong headMiddleRaw, uint timelineValue, uint stableValue, int echoSourceId, int stackValue, ResourceEffectRef buffResourceEffectRef, int tailLength, ulong tailLow64, ulong tailHigh64)
    {
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendExtendedObservation())
            return;
        entityId = ResolveLifecycleId(entityId);
        AddKnownEntity(entityId);
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, entityId, 0, packet.Raw),
            new AuraObservation
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
        journal.Append(
            CreateHeader(in stamp, sourceId, 0, packet.Raw),
            new ActionObservation
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
            journal.Append(
                CreateHeader(in stamp, 0, entityId, packet.Raw),
                new AuraObservation
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
                });
        }
    }

    public void AppendNickname(in PacketObservationSource packet, int uid, string nickname, Faction faction = Faction.Unknown, CharacterClass? characterClass = null, bool isLocalPlayer = false, int? originServerId = null, string legionName = "")
    {
        uid = ResolveLifecycleId(uid);
        AddKnownEntity(uid);
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, uid, 0, packet.Raw),
            new StateObservation
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
            });
    }

    public void AppendPlayerGroupMember(in PacketObservationSource packet, int uid, in PlayerGroupMembership membership)
    {
        uid = ResolveLifecycleId(uid);
        AddKnownEntity(uid);
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, uid, 0, packet.Raw),
            new StateObservation
            {
                EntityId = uid,
                StateCode = StateCodes.PlayerGroupMembership,
                Value0 = (int)membership.Kind,
                Value1 = membership.SubPartyIndex,
                DetailRaw = ((long)membership.GroupId << 8) | membership.MemberSlotIndex,
                GroupMembership = membership
            });
    }

    public void AppendPlayerGroupProfile(in PacketObservationSource packet, int originServerId, string nickname, in PlayerGroupMembership membership)
    {
        if (originServerId <= 0 || string.IsNullOrWhiteSpace(nickname) || !membership.IsKnown)
            return;

        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, 0, 0, packet.Raw),
            new StateObservation
            {
                EntityId = 0,
                StateCode = StateCodes.PlayerGroupMembership,
                Value0 = (int)membership.Kind,
                Value1 = membership.SubPartyIndex,
                DetailRaw = ((long)membership.GroupId << 8) | membership.MemberSlotIndex,
                Text = nickname,
                OriginServerId = originServerId,
                GroupMembership = membership
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
        journal.Append(
            CreateHeader(in stamp, instanceId, 0, packet.Raw),
            new StateObservation
            {
                EntityId = instanceId,
                StateCode = npcCode,
                Value0 = 0,
                Value1 = 0,
                DetailRaw = 0
            });
    }

    public void AppendNpcName(in PacketObservationSource packet, int npcCode, string name)
    {
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, 0, 0, packet.Raw),
            new StateObservation
            {
                EntityId = npcCode,
                StateCode = StateCodes.LocalizedNpcName,
                Value0 = 0,
                Value1 = 0,
                DetailRaw = 0,
                Text = name
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
        journal.Append(
            CreateHeader(in stamp, instanceId, 0, packet.Raw),
            new StateObservation
            {
                EntityId = instanceId,
                StateCode = StateCodes.NpcKind,
                Value0 = (int)kind,
                Value1 = 0,
                DetailRaw = 0
            });
        collectionPolicy?.OnBossMetadataChanged();
    }

    public void AppendNpcHp(in PacketObservationSource packet, int instanceId, long hp)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.Hp = hp;
        state.HpRaw = packet.Raw;
        state.HpObservedAtMilliseconds = ResolvePacketOffsetMilliseconds(in packet);
        if (hp == 0)
            state.BattleToggledOn = false;
        AddKnownEntity(instanceId);
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendEntityVitalObservation())
            return;
        var stamp = CreateStamp(in packet);
        state.HpObservedAtMilliseconds = stamp.OffsetTicks / TimeSpan.TicksPerMillisecond;
        journal.Append(
            CreateHeader(in stamp, instanceId, 0, packet.Raw),
            new EntityVitalObservation
            {
                EntityId = instanceId,
                CurrentHp = hp,
                MaxHp = null
            });
        collectionPolicy?.OnBossMetadataChanged();
    }

    public void AppendNpcHp(in PacketObservationSource packet, int instanceId, long hp, long maxHp)
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
        if (collectionPolicy is not null && !collectionPolicy.ShouldAppendEntityVitalObservation())
            return;
        var stamp = CreateStamp(in packet);
        state.HpObservedAtMilliseconds = stamp.OffsetTicks / TimeSpan.TicksPerMillisecond;
        journal.Append(
            CreateHeader(in stamp, instanceId, 0, packet.Raw),
            new EntityVitalObservation
            {
                EntityId = instanceId,
                CurrentHp = hp,
                MaxHp = maxHp
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
        journal.Append(
            CreateHeader(in stamp, instanceId, 0, packet.Raw),
            new StateObservation
            {
                EntityId = instanceId,
                StateCode = StateCodes.NpcBattle,
                Value0 = isActive ? 1 : 0,
                Value1 = 0,
                DetailRaw = 0
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
        journal.Append(
            CreateHeader(in stamp, instanceId, 0, packet.Raw),
            new StateObservation
            {
                EntityId = instanceId,
                StateCode = StateCodes.NpcBattleToggle,
                Value0 = 0,
                Value1 = 0,
                DetailRaw = 0
            });
        collectionPolicy?.OnBossMetadataChanged();
    }

    public void AppendNpc2136State(in PacketObservationSource packet, int instanceId, long sequence, long value0)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.Sequence2136 = sequence;
        state.Value2136 = value0;
        AddKnownEntity(instanceId);
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, instanceId, 0, packet.Raw),
            new StateObservation
            {
                EntityId = instanceId,
                StateCode = 2136,
                Value0 = sequence,
                Value1 = value0,
                DetailRaw = 0
            });
    }

    public void AppendNpc0140Value(in PacketObservationSource packet, int instanceId, long value0)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.Value0140 = value0;
        AddKnownEntity(instanceId);
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, instanceId, 0, packet.Raw),
            new StateObservation
            {
                EntityId = instanceId,
                StateCode = 140,
                Value0 = value0,
                Value1 = 0,
                DetailRaw = 0
            });
    }

    public void AppendNpc0240Value(in PacketObservationSource packet, int instanceId, long value0)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.Value0240 = value0;
        AddKnownEntity(instanceId);
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, instanceId, 0, packet.Raw),
            new StateObservation
            {
                EntityId = instanceId,
                StateCode = 240,
                Value0 = value0,
                Value1 = 0,
                DetailRaw = 0
            });
    }

    public void AppendNpc4636State(in PacketObservationSource packet, int instanceId, byte state0, byte state1)
    {
        instanceId = ResolveLifecycleId(instanceId);
        var state = GetOrAddNpcState(instanceId);
        state.State4636 = (state0, state1);
        AddKnownEntity(instanceId);
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, instanceId, 0, packet.Raw),
            new StateObservation
            {
                EntityId = instanceId,
                StateCode = 4636,
                Value0 = state0,
                Value1 = state1,
                DetailRaw = 0
            });
    }

    public void AppendSummon(in PacketObservationSource packet, int ownerId, int summonInstanceId)
    {
        ownerId = ResolveLifecycleId(ownerId);
        summonInstanceId = ResolveLifecycleId(summonInstanceId);
        AddKnownEntity(ownerId);
        AddKnownEntity(summonInstanceId);
        EntityIngress.SummonOwnerByInstance[summonInstanceId] = ownerId;
        GetOrAddNpcState(summonInstanceId).Kind = NpcKind.Summon;
        var stamp = CreateStamp(in packet);
        journal.Append(
            CreateHeader(in stamp, ownerId, summonInstanceId, packet.Raw),
            new StateObservation
            {
                EntityId = summonInstanceId,
                StateCode = 0,
                Value0 = ownerId,
                Value1 = 0,
                DetailRaw = 0
            });
    }

    private long MapFlushId(long flushId)
    {
        if (nextFlushId is null || flushId <= 0)
            return flushId;

        if (_mappedFlushIds.TryGetValue(flushId, out var mapped))
            return mapped;

        mapped = nextFlushId();
        _mappedFlushIds[flushId] = mapped;
        return mapped;
    }

    private TimelineStamp CreateStamp(in PacketObservationSource packet) =>
        clock.CreateStamp(packet.CaptureTimestampMilliseconds, MapFlushId(packet.FlushId));

    private ObservedEventHeader CreateHeader(in TimelineStamp stamp, int sourceEntityId, int targetEntityId, RawPacketReference raw)
        => new(sceneSessionId(), stamp, sourceEntityId, targetEntityId, raw);

    private long ResolvePacketOffsetMilliseconds(in PacketObservationSource packet) =>
        Math.Max(0, packet.CaptureTimestampMilliseconds - clock.SceneStartedAtMilliseconds);

    private MapEntityIngressState EntityIngress => _mapContext.Entities;

    private RuntimeNpcState GetOrAddNpcState(int instanceId)
    {
        if (!EntityIngress.NpcStates.TryGetValue(instanceId, out var state))
        {
            state = new RuntimeNpcState();
            EntityIngress.NpcStates[instanceId] = state;
        }

        return state;
    }

    private void AddKnownEntity(int entityId)
    {
        if (entityId > 0)
            EntityIngress.KnownEntities.Add(entityId);
    }
}
