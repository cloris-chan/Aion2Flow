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
    private readonly CompactDirectValueCanonicalizer _compactDirectValue;
    private readonly OwnerTargetSummonResourceCanonicalizer _ownerTargetSummonResource;
    private readonly CompactAvoidanceCanonicalizer _compactAvoidance;
    private readonly TransientEffectOwnerTracker _transientEffectOwners;
    private readonly BossFocusStore _bossFocus;
    public DomainEventApplier(EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat)
        : this(entities, boundary, metadataRegistry, combat, new SystemPeriodicRecoveryCanonicalizer(), new PeriodicPoolCanonicalizer(), new CompactDirectValueCanonicalizer(), new CompactAvoidanceCanonicalizer(), new TransientEffectOwnerTracker(), new BossFocusStore(entities))
    {
    }

    public DomainEventApplier(EntityStore entities, SceneBoundaryStore boundary, CombatStore combat)
        : this(entities, boundary, new RuntimeMetadataRegistry(), combat)
    {
    }

    internal DomainEventApplier(EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat, SystemPeriodicRecoveryCanonicalizer systemPeriodicRecovery, PeriodicPoolCanonicalizer periodicPool, CompactDirectValueCanonicalizer compactDirectValue, CompactAvoidanceCanonicalizer compactAvoidance, TransientEffectOwnerTracker transientEffectOwners, BossFocusStore bossFocus)
    {
        _entities = entities;
        _boundary = boundary;
        _metadataRegistry = metadataRegistry;
        _combat = combat;
        _systemPeriodicRecovery = systemPeriodicRecovery;
        _periodicPool = periodicPool;
        _compactDirectValue = compactDirectValue;
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
        _compactDirectValue.CreateSnapshot(),
        _compactAvoidance.CreateSnapshot(),
        _transientEffectOwners.CreateSnapshot(),
        _bossFocus.CreateSnapshot());

    internal static DomainEventApplier FromSnapshot(EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat, DomainEventApplierSnapshot snapshot)
    {
        var applier = new DomainEventApplier(
            entities,
            boundary,
            metadataRegistry,
            combat,
            SystemPeriodicRecoveryCanonicalizer.FromSnapshot(snapshot.SystemPeriodicRecovery),
            PeriodicPoolCanonicalizer.FromSnapshot(snapshot.PeriodicPool),
            CompactDirectValueCanonicalizer.FromSnapshot(snapshot.CompactDirectValue),
            CompactAvoidanceCanonicalizer.FromSnapshot(snapshot.CompactAvoidance),
            TransientEffectOwnerTracker.FromSnapshot(snapshot.TransientEffectOwners),
            BossFocusStore.FromSnapshot(entities, snapshot.BossFocus));
        return applier;
    }

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
                _entities.ApplyNpcHp(resource.EntityId, resource.CurrentValue ?? 0, resource.MaximumValue ?? 0);
                if (TrackBossFocus)
                    _bossFocus.ApplyNpcHp(resource.EntityId, resource.CurrentValue ?? 0, resource.MaximumValue ?? 0, observedAtMilliseconds);
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

    public void CompleteFlush()
    {
        FlushPendingOutcomeSidecars();
    }

    public void FlushPendingOutcomeSidecars()
    {
        foreach (var result in _compactDirectValue.FlushPending())
            ApplyStampedCombatResult(in result);

        foreach (var result in _compactAvoidance.FlushPending())
            ApplyStampedCombatResult(in result);
    }

    private void ApplyCombat(in ObservedEventEnvelope entry, in CombatObservation combatObservation)
    {
        var stamp = entry.Stamp;
        var observedAtMilliseconds = stamp.OffsetTicks / TimeSpan.TicksPerMillisecond;
        ObserveTransientEffectOwnerPacket(in entry, in combatObservation, observedAtMilliseconds);
        if (entry.Raw.Opcode == 0x0238)
        {
            var controlResults = _compactDirectValue.ObserveCompactControl0238(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation, observedAtMilliseconds, entry.Raw);
            if (controlResults.Count > 0)
            {
                ApplyStampedCombatResults(controlResults);
            }

            return;
        }

        if (entry.Raw.Opcode == 0x0638)
        {
            ApplyStampedCombatResults(in entry, _compactDirectValue.ObserveCompactControl0638(entry.SourceEntityId, in combatObservation));
            return;
        }

        if (entry.Raw.Opcode == 0x0438)
        {
            var sidecarResults = _compactDirectValue.ObserveCompactValueSidecar0438(entry.SourceEntityId, entry.TargetEntityId, in combatObservation);
            if (sidecarResults.Count > 0)
            {
                ApplyStampedCombatResults(in entry, sidecarResults);
            }
        }

        if (entry.Raw.Opcode == 0x0438 &&
            _compactDirectValue.TryObserveCompactValue0438(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation, observedAtMilliseconds, entry.Raw, out var compactResults, out var compactHeader))
        {
            if (compactResults.Count == 0)
                return;

            if (compactHeader.HasHeader)
            {
                ApplyStampedCombatResults(compactResults);
            }
            else
            {
                ApplyStampedCombatResults(in entry, compactResults);
            }

            return;
        }

        var rawResults = entry.Raw.Opcode switch
        {
            0x0438 => _compactAvoidance.ObserveCompactValue0438(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation, observedAtMilliseconds, entry.Raw),
            0x0238 => StampedCombatCanonicalizationBatch.Empty,
            0x0638 => StampedCombatCanonicalizationBatch.Empty,
            _ => _compactAvoidance.NormalizeCombat(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation, observedAtMilliseconds, entry.Raw)
        };

        ApplyStampedCombatResults(in entry, rawResults);
    }

    private void ApplyStampedCombatResults(StampedCombatCanonicalizationBatch results)
    {
        if (results.Count == 0)
            return;

        foreach (var result in results)
            ApplyStampedCombatResult(in result);
    }

    private void ApplyStampedCombatResults(in ObservedEventEnvelope entry, StampedCombatCanonicalizationBatch results)
    {
        if (results.Count == 0)
            return;

        ApplyStampedCombatResults(results);
    }

    private void ApplyStampedCombatResult(in StampedCombatCanonicalizationResult rawResult)
    {
        var observation = rawResult.Observation;
        var resultStamp = rawResult.Stamp;
        var result = new CombatCanonicalizationResult(rawResult.SourceId, rawResult.TargetId, observation, rawResult.Canonicalization);
        ApplyCanonicalizedCombatResult(in resultStamp, in result, rawResult.ObservedAtMilliseconds, rawResult.Raw);
    }

    private void ApplyCanonicalizedCombatResult(in TimelineStamp stamp, in CombatCanonicalizationResult result, long observedAtMilliseconds, RawPacketReference raw)
    {
        var resultObservation = result.Observation;
        TryApplyTransientEffectOwner(result.SourceId, result.TargetId, in resultObservation, observedAtMilliseconds);
        var ownerTargetSummonResourceResult = _ownerTargetSummonResource.Normalize(result.SourceId, result.TargetId, in resultObservation);
        var ownerTargetCanonicalization = result.Canonicalization | ownerTargetSummonResourceResult.Canonicalization;
        var observation = ownerTargetSummonResourceResult.Observation;
        var systemRecoveryResult = _systemPeriodicRecovery.Normalize(ownerTargetSummonResourceResult.SourceId, ownerTargetSummonResourceResult.TargetId, in observation);
        var systemRecoveryCanonicalization = ownerTargetCanonicalization | systemRecoveryResult.Canonicalization;
        var systemRecoveryObservation = systemRecoveryResult.Observation;
        foreach (var normalized in _periodicPool.Normalize(systemRecoveryResult.SourceId, systemRecoveryResult.TargetId, in systemRecoveryObservation))
        {
            var final = normalized.WithCanonicalization(systemRecoveryCanonicalization);
            ApplyCombatResult(in final, observedAtMilliseconds, stamp.ObservationOrdinal, raw);
        }
    }

    private void ApplyCombatResult(in CombatCanonicalizationResult result, long observedAtMilliseconds, long sourceObservationOrdinal, RawPacketReference raw)
    {
        var observation = result.Observation;
        _entities.ApplyCharacterClassEvidence(result.SourceId, in observation);

        _combat.ApplyCombat(result.SourceId, result.TargetId, in observation, observedAtMilliseconds, sourceObservationOrdinal, raw, result.Canonicalization);
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

        if (state.StateCode == StateCodes.PlayerGroupMembership)
        {
            if (entry.SourceEntityId > 0 && state.GroupMembership.IsKnown)
            {
                _metadataRegistry.UpsertPlayerGroupMembership(entry.SourceEntityId, state.GroupMembership);
            }
            else if (state.GroupMembership.IsKnown && state.OriginServerId is > 0 && !string.IsNullOrWhiteSpace(state.Text))
            {
                _metadataRegistry.UpsertPlayerGroupProfile(state.OriginServerId.Value, state.Text, state.GroupMembership);
            }
            return;
        }

        if (state.StateCode == StateCodes.LocalizedNpcName)
        {
            return;
        }

        if (state.StateCode == StateCodes.NpcKind)
        {
            var kind = state.Value0 is >= int.MinValue and <= int.MaxValue && Enum.IsDefined((NpcKind)(int)state.Value0) ? (NpcKind)(int)state.Value0 : NpcKind.Unknown;
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
            _entities.ApplyNpc2136State(state.EntityId, state.Value0, state.Value1);
            return;
        }

        if (state.StateCode == 140)
        {
            _entities.ApplyNpc0140Value(state.EntityId, state.Value0);
            return;
        }

        if (state.StateCode == 240)
        {
            _entities.ApplyNpc0240Value(state.EntityId, state.Value0);
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
    CompactDirectValueCanonicalizerSnapshot CompactDirectValue,
    CompactAvoidanceCanonicalizerSnapshot CompactAvoidance,
    TransientEffectOwnerTrackerSnapshot TransientEffectOwners,
    BossFocusStoreSnapshot BossFocus);
