using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Canonicalization;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public sealed class DomainEventApplier
{
    private readonly ObservedEventEnvelope[] _journalBuffer = new ObservedEventEnvelope[256];
    private readonly EntityStore _entities;
    private readonly SceneBoundaryStore _boundary;
    private readonly RuntimeMetadataRegistry _metadataRegistry;
    private readonly CombatStore _combat;
    private readonly SystemPeriodicRecoveryCanonicalizer _systemPeriodicRecovery;
    private readonly PeriodicPoolCanonicalizer _periodicPool;
    private readonly CompactActionDirectValueCanonicalizer _compactActionDirectValue;
    private readonly OwnerTargetSummonResourceCanonicalizer _ownerTargetSummonResource;
    private readonly CompactAvoidanceCanonicalizer _compactAvoidance;
    private readonly TransientEffectOwnerTracker _transientEffectOwners;
    private readonly BossFocusStore _bossFocus;

    public DomainEventApplier(EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat)
        : this(entities, boundary, metadataRegistry, combat, new SystemPeriodicRecoveryCanonicalizer(), new PeriodicPoolCanonicalizer(), new CompactActionDirectValueCanonicalizer(), new CompactAvoidanceCanonicalizer(), new TransientEffectOwnerTracker(), new BossFocusStore(entities))
    {
    }

    public DomainEventApplier(EntityStore entities, SceneBoundaryStore boundary, CombatStore combat)
        : this(entities, boundary, new RuntimeMetadataRegistry(), combat)
    {
    }

    internal DomainEventApplier(EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat, SystemPeriodicRecoveryCanonicalizer systemPeriodicRecovery, PeriodicPoolCanonicalizer periodicPool, CompactActionDirectValueCanonicalizer compactActionDirectValue, CompactAvoidanceCanonicalizer compactAvoidance, TransientEffectOwnerTracker transientEffectOwners, BossFocusStore bossFocus)
    {
        _entities = entities;
        _boundary = boundary;
        _metadataRegistry = metadataRegistry;
        _combat = combat;
        _systemPeriodicRecovery = systemPeriodicRecovery;
        _periodicPool = periodicPool;
        _compactActionDirectValue = compactActionDirectValue;
        _ownerTargetSummonResource = new OwnerTargetSummonResourceCanonicalizer(entities);
        _compactAvoidance = compactAvoidance;
        _transientEffectOwners = transientEffectOwners;
        _bossFocus = bossFocus;
    }

    public EntityStore Entities => _entities;
    public SceneBoundaryStore Boundary => _boundary;
    public RuntimeMetadataRegistry MetadataRegistry => _metadataRegistry;
    public CombatStore Combat => _combat;
    public BossFocusStore BossFocus => _bossFocus;
    public bool TrackBossFocus { get; set; } = true;

    internal DomainEventApplierSnapshot CreateSnapshot() => new(
        _systemPeriodicRecovery.CreateSnapshot(),
        _periodicPool.CreateSnapshot(),
        _compactActionDirectValue.CreateSnapshot(),
        _compactAvoidance.CreateSnapshot(),
        _transientEffectOwners.CreateSnapshot(),
        _bossFocus.CreateSnapshot());

    internal static DomainEventApplier FromSnapshot(EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat, DomainEventApplierSnapshot snapshot)
        => new(
            entities,
            boundary,
            metadataRegistry,
            combat,
            SystemPeriodicRecoveryCanonicalizer.FromSnapshot(snapshot.SystemPeriodicRecovery),
            PeriodicPoolCanonicalizer.FromSnapshot(snapshot.PeriodicPool),
            CompactActionDirectValueCanonicalizer.FromSnapshot(snapshot.CompactActionDirectValue),
            CompactAvoidanceCanonicalizer.FromSnapshot(snapshot.CompactAvoidance),
            TransientEffectOwnerTracker.FromSnapshot(snapshot.TransientEffectOwners),
            BossFocusStore.FromSnapshot(entities, snapshot.BossFocus));

    public void ApplyJournal(ObservedEventJournal journal)
    {
        var count = journal.Count;
        if (count == 0)
            return;

        _combat.EnsureCapacity(count);
        var cursor = journal.CreateCursor(0);
        while (true)
        {
            var result = journal.CopyEntries(cursor, _journalBuffer);
            if (result.Count == 0)
                break;

            var entries = _journalBuffer.AsSpan(0, result.Count);
            foreach (ref readonly var entry in entries)
                ApplyEntry(in entry);

            cursor = result.Cursor;
        }

        FlushPendingOutcomeSidecars();
    }

    public void ApplyEntry(in ObservedEventEnvelope entry)
    {
        var observedAtMilliseconds = entry.Stamp.OffsetTicks / TimeSpan.TicksPerMillisecond;
        switch (entry.Domain)
        {
            case ObservedEventDomain.Combat when entry.Combat is { } c:
                ApplyCombat(in entry, in c);
                break;
            case ObservedEventDomain.State when entry.State is { } state:
                ApplyState(in entry, in state);
                break;
            case ObservedEventDomain.Resource when entry.Resource is { } resource:
                _entities.ApplyNpcHp(resource.EntityId, (int)(resource.CurrentValue ?? 0), (int)(resource.MaximumValue ?? 0));
                if (TrackBossFocus)
                    _bossFocus.ApplyNpcHp(resource.EntityId, (int)(resource.CurrentValue ?? 0), (int)(resource.MaximumValue ?? 0), observedAtMilliseconds);
                TryApplyBossCombatActivity(resource.EntityId, observedAtMilliseconds);
                break;
            case ObservedEventDomain.Scene when entry.Scene is { } scene:
                ApplyScene(in scene);
                break;
            case ObservedEventDomain.Aura when entry.Aura is { } aura:
                ApplyAura(in aura);
                break;
        }
    }

    public void CompleteBatch(long batchOrdinal)
    {
        foreach (var result in _compactActionDirectValue.CompleteBatch(batchOrdinal))
            ApplyStampedCombatResult(in result);

        foreach (var result in _compactAvoidance.CompleteBatch(batchOrdinal))
            ApplyStampedCombatResult(in result);
    }

    public void FlushPendingOutcomeSidecars() => CompleteBatch(long.MaxValue);

    private void ApplyCombat(in ObservedEventEnvelope entry, in CombatObservation combatObservation)
    {
        var stamp = entry.Stamp;
        var structurePath = entry.Raw.StructurePath;
        var observedAtMilliseconds = stamp.OffsetTicks / TimeSpan.TicksPerMillisecond;
        ObserveTransientEffectOwnerPacket(in entry, in combatObservation, observedAtMilliseconds);
        if (entry.Raw.Opcode == 0x0238)
            ApplyStampedCombatResults(_compactActionDirectValue.ObserveCompactControl0238(entry.SourceEntityId, in combatObservation, in stamp, in structurePath));

        if (entry.Raw.Opcode == 0x0438)
            ApplyStampedCombatResults(_compactActionDirectValue.ObserveCompactValueSidecar0438(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation, in structurePath));

        if (entry.Raw.Opcode == 0x0438 &&
            _compactActionDirectValue.TryObserveCompactValue0438(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation, in structurePath, observedAtMilliseconds, out var actionResults))
        {
            ApplyStampedCombatResults(actionResults);
            return;
        }

        var rawResults = entry.Raw.Opcode switch
        {
            0x0438 => _compactAvoidance.ObserveCompactValue0438(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation, in structurePath, observedAtMilliseconds),
            0x0238 => _compactAvoidance.AdvanceBatch(in stamp),
            0x0638 => _compactAvoidance.AdvanceBatch(in stamp),
            _ => _compactAvoidance.NormalizeCombat(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation, observedAtMilliseconds)
        };

        ApplyStampedCombatResults(rawResults);
    }

    private void ApplyStampedCombatResults(StampedCombatCanonicalizationBatch results)
    {
        foreach (var result in results)
            ApplyStampedCombatResult(in result);
    }

    private void ApplyStampedCombatResult(in StampedCombatCanonicalizationResult rawResult)
    {
        var observation = rawResult.Observation;
        var resultStamp = rawResult.Stamp;
        var result = new CombatCanonicalizationResult(rawResult.SourceId, rawResult.TargetId, observation);
        ApplyCanonicalizedCombatResult(in resultStamp, in result, rawResult.ObservedAtMilliseconds);
    }

    private void ApplyCanonicalizedCombatResult(in TimelineStamp stamp, in CombatCanonicalizationResult result, long observedAtMilliseconds)
    {
        var resultObservation = result.Observation;
        TryApplyTransientEffectOwner(result.SourceId, result.TargetId, in resultObservation, observedAtMilliseconds);
        var ownerTargetSummonResourceResult = _ownerTargetSummonResource.Normalize(result.SourceId, result.TargetId, in resultObservation);
        var observation = ownerTargetSummonResourceResult.Observation;
        var systemRecoveryResult = _systemPeriodicRecovery.Normalize(ownerTargetSummonResourceResult.SourceId, ownerTargetSummonResourceResult.TargetId, in stamp, in observation);
        var systemRecoveryObservation = systemRecoveryResult.Observation;
        foreach (var normalized in _periodicPool.Normalize(systemRecoveryResult.SourceId, systemRecoveryResult.TargetId, in systemRecoveryObservation))
            ApplyCombatResult(in normalized, observedAtMilliseconds);
    }

    private void ApplyCombatResult(in CombatCanonicalizationResult result, long observedAtMilliseconds)
    {
        var observation = result.Observation;
        _entities.ApplyCharacterClassEvidence(result.SourceId, in observation);

        _combat.ApplyCombat(result.SourceId, result.TargetId, in observation, observedAtMilliseconds);
        TryApplyBossCombatActivity(result.SourceId, observedAtMilliseconds);
        TryApplyBossCombatActivity(result.TargetId, observedAtMilliseconds);
    }

    private void ObserveTransientEffectOwnerPacket(in ObservedEventEnvelope entry, in CombatObservation observation, long observedAtMilliseconds)
    {
        if (IsTransientEffectOwnerSeed(in entry, in observation) && CanObserveTransientEffectOwnerSeed(entry.SourceEntityId))
        {
            _transientEffectOwners.ObserveOwnerSkill(entry.SourceEntityId, in observation, observedAtMilliseconds);
            return;
        }

        if (entry.Raw.Opcode is 0x0238 or 0x0638)
            TryApplyTransientEffectOwner(entry.SourceEntityId, observation.ChainId, in observation, observedAtMilliseconds);
    }

    private void TryApplyTransientEffectOwner(int sourceId, int targetId, in CombatObservation observation, long observedAtMilliseconds)
    {
        if (!CanApplyTransientEffectOwner(sourceId))
            return;

        var ownerId = _transientEffectOwners.ResolveOwner(sourceId, targetId, in observation, observedAtMilliseconds);
        if (CanOwnTransientEffect(ownerId, sourceId))
            _entities.ApplyTransientEffectOwner(ownerId, sourceId);
    }

    private bool CanObserveTransientEffectOwnerSeed(int sourceId) => CanOwnTransientEffect(sourceId, 0);

    private static bool IsTransientEffectOwnerSeed(in ObservedEventEnvelope entry, in CombatObservation observation)
    {
        if (entry.Raw.Opcode == 0x0238)
            return true;

        return entry.Raw.Opcode == 0x0438 &&
               observation.Damage == 0 &&
               observation.HitCount == 0 &&
               observation.AttemptCount == 0 &&
               observation.ResourceKind == CombatResourceKind.Unknown &&
               observation.PeriodicRelation == PeriodicEffectRelation.None &&
               observation.EffectTag == PacketEffectTag.None &&
               observation.EventKind == CombatEventKind.Unknown &&
               observation.ValueKind == CombatValueKind.Unknown &&
               observation.LayoutTag == 0 &&
               observation.Flag == 0 &&
               observation.ChainId == 0 &&
               observation.Type is 2 or 3 &&
               observation.Marker > 0;
    }

    private bool CanApplyTransientEffectOwner(int sourceId)
    {
        if (sourceId <= 0)
            return false;

        if (!_entities.TryGet(sourceId, out var entity))
            return true;

        return !entity.IsPlayer &&
               entity.OwnerKind != EntityOwnerKind.Summon &&
               entity.Kind is not (NpcKind.Monster or NpcKind.Boss or NpcKind.Friendly or NpcKind.Summon or NpcKind.TrainingDummy);
    }

    private bool CanOwnTransientEffect(int ownerId, int effectSourceId)
    {
        if (ownerId <= 0 || ownerId == effectSourceId)
            return false;

        if (!_entities.TryGet(ownerId, out var entity))
            return true;

        return !entity.NpcCode.HasValue &&
               entity.OwnerEntityId is null &&
               entity.Kind is not (NpcKind.Monster or NpcKind.Boss or NpcKind.Friendly or NpcKind.Summon or NpcKind.TrainingDummy);
    }

    private void ApplyAura(in AuraObservation aura)
    {
        if (aura.Kind == AuraObservationKind.Result && aura.EntityId > 0 && aura.InstanceSequenceId > 0)
            _entities.ApplyNpc2C38State(aura.EntityId, aura.InstanceSequenceId, aura.ResultCode);
    }

    private void ApplyScene(in SceneObservation scene)
    {
        if (scene.DiagnosticKey == "stage-destination-map")
        {
            _boundary.StageDestinationMap(scene.MapId, scene.Value0 != 0);
            return;
        }

        if (scene.DiagnosticKey == "pending-destination-map")
        {
            _boundary.StagePendingDestinationMap(scene.MapId, scene.Value0 != 0);
            return;
        }

        if (scene.DiagnosticKey == "confirm-destination-map")
        {
            _boundary.ConfirmDestinationMap(scene.MapId, scene.Value0 != 0);
            _metadataRegistry.UpsertMapCode(_boundary.CurrentMapInstanceId, _boundary.CurrentMapId);
            return;
        }

        if (scene.DiagnosticKey == "confirm-pending-destination-map-arrival")
        {
            _boundary.ConfirmPendingDestinationMapArrival();
            _metadataRegistry.UpsertMapCode(_boundary.CurrentMapInstanceId, _boundary.CurrentMapId);
            return;
        }

        if (scene.DiagnosticKey == "stage-destination-instance")
        {
            _boundary.StageDestinationMapInstance(scene.MapInstanceId);
            _metadataRegistry.UpsertMapCode(_boundary.CurrentMapInstanceId, _boundary.CurrentMapId);
            return;
        }

        if (scene.DiagnosticKey == "confirm-destination-instance")
        {
            _boundary.ConfirmDestinationMapInstance(scene.MapInstanceId);
            _metadataRegistry.UpsertMapCode(_boundary.CurrentMapInstanceId, _boundary.CurrentMapId);
            return;
        }

        if (scene.DiagnosticKey == "scene-transport-boundary")
        {
            _boundary.MarkSceneTransportBoundary();
            _metadataRegistry.UpsertMapCode(_boundary.CurrentMapInstanceId, _boundary.CurrentMapId);
        }
    }

    private void ApplyState(in ObservedEventEnvelope entry, in StateObservation state)
    {
        if (entry.TargetEntityId != 0 && state.EntityId == entry.TargetEntityId && entry.SourceEntityId != entry.TargetEntityId)
        {
            _entities.ApplySummon(entry.SourceEntityId, entry.TargetEntityId);
            return;
        }

        if (state.StateCode == StateCodes.PlayerIdentity)
        {
            if (entry.SourceEntityId > 0)
            {
                var nickname = state.Text ?? string.Empty;
                var metadataClass = state.CharacterClass is CharacterClass.None ? null : state.CharacterClass;
                if (!string.IsNullOrWhiteSpace(nickname))
                    _entities.ApplyNickname(entry.SourceEntityId, nickname);
                else
                    _entities.ApplyPlayerIdentity(entry.SourceEntityId);
                if (metadataClass is { } characterClass)
                    _entities.ApplyMetadataCharacterClass(entry.SourceEntityId, characterClass);

                var legionName = state.LegionName ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(nickname) || metadataClass is not null || state.IsLocalPlayer || state.OriginServerId is not null || !string.IsNullOrWhiteSpace(legionName))
                    _metadataRegistry.UpsertPcMetadata(entry.SourceEntityId, nickname, state.Faction, metadataClass, state.IsLocalPlayer, state.OriginServerId, legionName);
            }
            return;
        }

        if (state.StateCode == StateCodes.NpcName)
        {
            return;
        }

        if (state.StateCode == StateCodes.NpcKind)
        {
            var kind = Enum.IsDefined((NpcKind)state.Value0) ? (NpcKind)state.Value0 : NpcKind.Unknown;
            _entities.ApplyNpcKind(state.EntityId, kind);
            if (TrackBossFocus)
                _bossFocus.ApplyNpcKind(state.EntityId, kind, entry.Stamp.OffsetTicks / TimeSpan.TicksPerMillisecond);
            TryApplyBossCombatActivity(state.EntityId, entry.Stamp.OffsetTicks / TimeSpan.TicksPerMillisecond);
            return;
        }

        if (state.StateCode == StateCodes.NpcBattle)
        {
            var isActive = state.Value0 != 0 && CanNpcBattleActivate(state.EntityId);
            _entities.ApplyBattleToggle(state.EntityId, isActive);
            if (TrackBossFocus)
                _bossFocus.ApplyBattle(state.EntityId, isActive, entry.Stamp.OffsetTicks / TimeSpan.TicksPerMillisecond);
            return;
        }

        if (state.StateCode == StateCodes.NpcBattleToggle)
        {
            var isActive = !_entities.GetOrAdd(state.EntityId).NpcCombatActive && CanNpcBattleActivate(state.EntityId);
            _entities.ApplyBattleToggle(state.EntityId, isActive);
            if (TrackBossFocus)
                _bossFocus.ApplyBattleToggle(state.EntityId, isActive, entry.Stamp.OffsetTicks / TimeSpan.TicksPerMillisecond);
            return;
        }

        if (state.StateCode is >= 2_000_000 and <= 2_999_999)
        {
            _entities.ApplyNpcCode(state.EntityId, state.StateCode);
            _metadataRegistry.UpsertNpcCode(state.EntityId, state.StateCode);
            return;
        }

        if (state.StateCode == 2136)
        {
            _entities.ApplyNpc2136State(state.EntityId, checked((uint)state.Value0), checked((uint)state.Value1));
            return;
        }

        if (state.StateCode == 140)
        {
            _entities.ApplyNpc0140Value(state.EntityId, checked((uint)state.Value0));
            return;
        }

        if (state.StateCode == 240)
        {
            _entities.ApplyNpc0240Value(state.EntityId, checked((uint)state.Value0));
            return;
        }

        if (state.StateCode == 4636)
        {
            _entities.ApplyNpc4636State(state.EntityId, checked((byte)state.Value0), checked((byte)state.Value1));
            return;
        }

        _ = _entities.GetOrAdd(state.EntityId);
    }

    private bool CanNpcBattleActivate(int instanceId) => !_entities.TryGet(instanceId, out var entity) || entity.CurrentHp != 0;

    private void TryApplyBossCombatActivity(int instanceId, long observedAtMilliseconds)
    {
        if (!TrackBossFocus ||
            instanceId <= 0 ||
            !_entities.TryGet(instanceId, out var entity) ||
            !BossModeFocusTargets.IsFocusTarget(entity.Kind) ||
            entity.CurrentHp == 0 ||
            !_combat.TryGetLastCombatActivityObservedAt(instanceId, out var activityObservedAtMilliseconds))
        {
            return;
        }

        _bossFocus.ApplyCombatActivity(instanceId, activityObservedAtMilliseconds, observedAtMilliseconds);
    }
}

internal sealed record DomainEventApplierSnapshot(
    SystemPeriodicRecoveryCanonicalizerSnapshot SystemPeriodicRecovery,
    PeriodicPoolCanonicalizerSnapshot PeriodicPool,
    CompactActionDirectValueCanonicalizerSnapshot CompactActionDirectValue,
    CompactAvoidanceCanonicalizerSnapshot CompactAvoidance,
    TransientEffectOwnerTrackerSnapshot TransientEffectOwners,
    BossFocusStoreSnapshot BossFocus);
