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
    private readonly EntityStore _entities;
    private readonly SceneBoundaryStore _boundary;
    private readonly RuntimeMetadataRegistry _metadataRegistry;
    private readonly CombatStore _combat;
    private readonly MechanicStore _mechanics;
    private readonly ResourceStore _resources;
    private readonly ICombatOccurrenceObserver? _combatOccurrenceObserver;
    private readonly IAuraLifecycleObserver? _auraLifecycleObserver;
    private readonly CombatContributionPathResolver _contributionPathResolver;
    private readonly SystemPeriodicRecoveryCanonicalizer _systemPeriodicRecovery;
    private readonly PeriodicPoolCanonicalizer _periodicPool;
    private readonly CompactDirectValueCanonicalizer _compactDirectValue;
    private readonly OwnerTargetSummonResourceCanonicalizer _ownerTargetSummonResource;
    private readonly CompactAvoidanceCanonicalizer _compactAvoidance;
    private readonly EntityVitalStore _entityVitals;
    private readonly AuraStore _auras;
    private readonly BossFocusStore _bossFocus;
    public DomainEventApplier(EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat, ICombatOccurrenceObserver? combatOccurrenceObserver = null, IAuraLifecycleObserver? auraLifecycleObserver = null)
        : this(entities, boundary, metadataRegistry, combat, new SystemPeriodicRecoveryCanonicalizer(), new PeriodicPoolCanonicalizer(), new CompactDirectValueCanonicalizer(), new CompactAvoidanceCanonicalizer(), new EntityVitalStore(), new AuraStore(), new MechanicStore(), new ResourceStore(), combatOccurrenceObserver, auraLifecycleObserver)
    {
    }

    public DomainEventApplier(EntityStore entities, SceneBoundaryStore boundary, CombatStore combat)
        : this(entities, boundary, new RuntimeMetadataRegistry(), combat)
    {
    }

    public DomainEventApplier(EntityStore entities, SceneBoundaryStore boundary, CombatStore combat, ICombatOccurrenceObserver combatOccurrenceObserver)
        : this(entities, boundary, new RuntimeMetadataRegistry(), combat, combatOccurrenceObserver)
    {
    }

    public DomainEventApplier(EntityStore entities, SceneBoundaryStore boundary, CombatStore combat, ISceneEventObserver sceneEventObserver)
        : this(entities, boundary, new RuntimeMetadataRegistry(), combat, sceneEventObserver, sceneEventObserver)
    {
    }

    internal DomainEventApplier(EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat, SystemPeriodicRecoveryCanonicalizer systemPeriodicRecovery, PeriodicPoolCanonicalizer periodicPool, CompactDirectValueCanonicalizer compactDirectValue, CompactAvoidanceCanonicalizer compactAvoidance, EntityVitalStore entityVitals, AuraStore auras, MechanicStore mechanics, ResourceStore resources, ICombatOccurrenceObserver? combatOccurrenceObserver = null, IAuraLifecycleObserver? auraLifecycleObserver = null, CombatContributionPathResolver? contributionPathResolver = null)
    {
        _entities = entities;
        _boundary = boundary;
        _metadataRegistry = metadataRegistry;
        _combat = combat;
        _mechanics = mechanics;
        _resources = resources;
        _combatOccurrenceObserver = combatOccurrenceObserver;
        _auraLifecycleObserver = auraLifecycleObserver;
        _contributionPathResolver = contributionPathResolver ?? new CombatContributionPathResolver(CombatContributionPath.ProductionFallback);
        _systemPeriodicRecovery = systemPeriodicRecovery;
        _periodicPool = periodicPool;
        _compactDirectValue = compactDirectValue;
        _ownerTargetSummonResource = new OwnerTargetSummonResourceCanonicalizer(entities);
        _compactAvoidance = compactAvoidance;
        _entityVitals = entityVitals;
        _auras = auras;
        _bossFocus = new BossFocusStore(entities, entityVitals);
    }

    public EntityStore Entities => _entities;
    public SceneBoundaryStore Boundary => _boundary;
    public RuntimeMetadataRegistry MetadataRegistry => _metadataRegistry;
    public CombatStore Combat => _combat;
    public MechanicStore Mechanics => _mechanics;
    public ResourceStore Resources => _resources;
    public EntityVitalStore EntityVitals => _entityVitals;
    public AuraStore Auras => _auras;
    public BossFocusStore BossFocus => _bossFocus;
    public bool TrackBossFocus { get; set; } = true;

    internal DomainEventApplierSnapshot CreateSnapshot() => new(
        _systemPeriodicRecovery.CreateSnapshot(),
        _periodicPool.CreateSnapshot(),
        _compactDirectValue.CreateSnapshot(),
        _compactAvoidance.CreateSnapshot(),
        _entityVitals.CreateSnapshot(),
        _auras.CreateSnapshot(),
        _mechanics.CreateSnapshot(),
        _resources.CreateSnapshot(),
        _contributionPathResolver.CreateSnapshot(),
        _bossFocus.CreateSnapshot());

    internal static DomainEventApplier FromSnapshot(EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat, DomainEventApplierSnapshot snapshot)
    {
        var entityVitals = EntityVitalStore.FromSnapshot(snapshot.EntityVitals);
        var applier = new DomainEventApplier(
            entities,
            boundary,
            metadataRegistry,
            combat,
            SystemPeriodicRecoveryCanonicalizer.FromSnapshot(snapshot.SystemPeriodicRecovery),
            PeriodicPoolCanonicalizer.FromSnapshot(snapshot.PeriodicPool),
            CompactDirectValueCanonicalizer.FromSnapshot(snapshot.CompactDirectValue),
            CompactAvoidanceCanonicalizer.FromSnapshot(snapshot.CompactAvoidance),
            entityVitals,
            AuraStore.FromSnapshot(snapshot.Auras),
            MechanicStore.FromSnapshot(snapshot.Mechanics),
            ResourceStore.FromSnapshot(snapshot.Resources),
            contributionPathResolver: CombatContributionPathResolver.FromSnapshot(snapshot.ContributionPath));
        applier._bossFocus.RestoreSnapshot(snapshot.BossFocus);
        return applier;
    }

    public void ApplyJournal(ObservedEventJournal journal)
    {
        var count = journal.Count;
        if (count == 0)
            return;

        _combat.EnsureCapacity(count);
        _mechanics.EnsureCapacity(count);
        _resources.EnsureCapacity(count);
        var cursor = journal.CreateCursor(0);
        while (true)
        {
            var result = journal.ReadEntries(cursor, 256, entries =>
            {
                for (var i = 0; i < entries.Count; i++)
                    ApplyEntry(entries[i]);
            });
            if (result.Count == 0)
                break;

            cursor = result.Cursor;
        }

        FlushPendingOutcomeSidecars();
    }

    public DomainEventMaterialization ApplyEntry(ObservedEventEntry entry)
    {
        var observedAtMilliseconds = entry.ObservedAtMilliseconds;
        var auraLifecycle = default(AuraLifecycleTransition);
        switch (entry.Domain)
        {
            case ObservedEventDomain.Combat:
                ApplyCombat(entry, in entry.Combat);
                break;
            case ObservedEventDomain.State:
                ApplyState(entry, in entry.State);
                break;
            case ObservedEventDomain.EntityVital:
                var vital = _entityVitals.Apply(entry);
                if (vital.CurrentHp == 0)
                    _entities.ApplyBattleToggle(vital.EntityId, false);
                if (TrackBossFocus)
                    _bossFocus.ApplyNpcHp(vital.EntityId, vital.CurrentHp, vital.MaxHp ?? 0, observedAtMilliseconds);
                TryApplyBossCombatActivity(vital.EntityId, observedAtMilliseconds);
                break;
            case ObservedEventDomain.Scene:
                ApplyScene(in entry.Scene);
                break;
            case ObservedEventDomain.Aura:
                auraLifecycle = _auras.Apply(entry);
                ObserveAuraLifecycle(entry, in auraLifecycle);
                ApplyAura(in entry.Aura);
                break;
            case ObservedEventDomain.Action:
                auraLifecycle = _auras.Apply(entry);
                ObserveAuraLifecycle(entry, in auraLifecycle);
                break;
        }

        return new DomainEventMaterialization(auraLifecycle);
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

    private void ApplyCombat(ObservedEventEntry entry, in CombatWireObservation combatObservation)
    {
        var stamp = entry.Stamp;
        var observedAtMilliseconds = stamp.OffsetTicks / TimeSpan.TicksPerMillisecond;
        if (entry.Raw.Opcode == 0x0238)
        {
            var controlResults = _compactDirectValue.ObserveCompactControl0238(entry.SourceEntityId, in combatObservation);
            if (controlResults.Count > 0)
            {
                ApplyStampedCombatResults(controlResults);
            }

            return;
        }

        if (entry.Raw.Opcode == 0x0638)
        {
            ApplyStampedCombatResults(_compactDirectValue.ObserveCompactControl0638(entry.SourceEntityId, in combatObservation));
            return;
        }

        if (entry.Raw.Opcode == 0x0438)
        {
            var sidecarResults = _compactDirectValue.ObserveCompactValueSidecar0438(entry.SourceEntityId, entry.TargetEntityId, in combatObservation);
            if (sidecarResults.Count > 0)
            {
                ApplyStampedCombatResults(sidecarResults);
            }
        }

        if (entry.Raw.Opcode == 0x0438 &&
            _compactDirectValue.TryObserveCompactValue0438(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation, observedAtMilliseconds, entry.Raw, out var compactResults))
        {
            if (compactResults.Count == 0)
                return;

            ApplyStampedCombatResults(compactResults);
            return;
        }

        var rawResults = entry.Raw.Opcode switch
        {
            0x0438 => _compactAvoidance.ObserveCompactValue0438(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation, observedAtMilliseconds, entry.Raw),
            0x0238 => StampedCombatCanonicalizationBatch.Empty,
            0x0638 => StampedCombatCanonicalizationBatch.Empty,
            _ => _compactAvoidance.NormalizeCombat(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation, observedAtMilliseconds, entry.Raw)
        };

        ApplyStampedCombatResults(rawResults);
    }

    private void ApplyStampedCombatResults(StampedCombatCanonicalizationBatch results)
    {
        if (results.Count == 0)
            return;

        foreach (var result in results)
            ApplyStampedCombatResult(in result);
    }

    private void ApplyStampedCombatResult(in StampedCombatCanonicalizationResult rawResult)
    {
        var observation = rawResult.Observation;
        var resultStamp = rawResult.Stamp;
        var result = new CombatCanonicalizationResult(rawResult.SourceId, rawResult.TargetId, observation, rawResult.Resolution);
        ApplyCanonicalizedCombatResult(in resultStamp, in result, rawResult.ObservedAtMilliseconds, rawResult.Raw);
    }

    private void ApplyCanonicalizedCombatResult(in TimelineStamp stamp, in CombatCanonicalizationResult result, long observedAtMilliseconds, RawPacketReference raw)
    {
        var resultObservation = result.Observation;
        var resultResolution = result.Resolution;
        var ownerTargetSummonResourceResult = _ownerTargetSummonResource
            .Normalize(result.SourceId, result.TargetId, in resultObservation)
            .Inherit(in resultResolution);
        var observation = ownerTargetSummonResourceResult.Observation;
        var ownerTargetResolution = ownerTargetSummonResourceResult.Resolution;
        var systemRecoveryResult = _systemPeriodicRecovery
            .Normalize(ownerTargetSummonResourceResult.SourceId, ownerTargetSummonResourceResult.TargetId, in observation)
            .Inherit(in ownerTargetResolution);
        var systemRecoveryObservation = systemRecoveryResult.Observation;
        var systemRecoveryResolution = systemRecoveryResult.Resolution;
        foreach (var normalized in _periodicPool.Normalize(systemRecoveryResult.SourceId, systemRecoveryResult.TargetId, in systemRecoveryObservation))
        {
            var final = normalized.Inherit(in systemRecoveryResolution);
            ApplyCombatResult(in final, observedAtMilliseconds, in stamp, raw);
        }
    }

    private void ApplyCombatResult(in CombatCanonicalizationResult result, long observedAtMilliseconds, in TimelineStamp stamp, RawPacketReference raw)
    {
        var occurrence = result.Resolution;
        var observation = result.Observation;
        var materialization = ResolveCombatOccurrence(result.SourceId, result.TargetId, in observation, in occurrence, observedAtMilliseconds, in stamp, raw);
        if (!materialization.IsAdmitted)
            return;

        ApplyMaterialization(
            result.SourceId,
            result.TargetId,
            in observation,
            in materialization,
            observedAtMilliseconds,
            stamp.ObservationOrdinal,
            raw,
            applyClassEvidence: true);

        MaterializeSecondaryContributions(in result, observedAtMilliseconds, in stamp, raw);
    }

    private void ApplyMaterialization(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceMaterialization materialization,
        long observedAtMilliseconds,
        long sourceObservationOrdinal,
        RawPacketReference raw,
        bool applyClassEvidence)
    {
        if (!materialization.HasAny)
            return;

        if (materialization.Mechanic is { } mechanic)
            _mechanics.Apply(sourceId, targetId, in observation, in mechanic, observedAtMilliseconds, sourceObservationOrdinal, raw);

        if (materialization.Resource is { } resource)
            _resources.Apply(sourceId, targetId, in observation, in resource, observedAtMilliseconds, sourceObservationOrdinal, raw);

        if (materialization.Contribution is { } contribution)
        {
            if (applyClassEvidence)
                _entities.ApplyCharacterClassEvidence(sourceId, in observation, in contribution);
            _combat.ApplyCombat(sourceId, targetId, in observation, in contribution, observedAtMilliseconds, sourceObservationOrdinal, raw);
        }

        TryApplyBossCombatActivity(sourceId, observedAtMilliseconds);
        TryApplyBossCombatActivity(targetId, observedAtMilliseconds);
    }

    private void MaterializeSecondaryContributions(in CombatCanonicalizationResult result, long observedAtMilliseconds, in TimelineStamp stamp, RawPacketReference raw)
    {
        var observation = result.Observation;
        if (observation.DrainHealAmount > 0 && result.SourceId > 0 && result.SourceId != result.TargetId)
        {
            var drain = observation with
            {
                Damage = observation.DrainHealAmount,
                HitCount = 0,
                AttemptCount = 0,
                DrainHealAmount = 0,
                RegenerationAmount = 0,
                ResourceKind = CombatResourceKind.Unknown,
                Modifiers = DamageModifiers.None,
                PeriodicRelation = PeriodicEffectRelation.None,
                PeriodicMode = 0
            };
            var drainOccurrence = new CombatOccurrenceResolution(
                CombatPacketRule.DrainSecondary,
                CombatMaterializationKind.DrainSecondary,
                CombatAssociationKind.None,
                CombatSuppressionReason.None);
            var drainMaterialization = ResolveCombatOccurrence(result.SourceId, result.SourceId, in drain, in drainOccurrence, observedAtMilliseconds, in stamp, raw);
            ApplyMaterialization(result.SourceId, result.SourceId, in drain, in drainMaterialization, observedAtMilliseconds, stamp.ObservationOrdinal, raw, applyClassEvidence: false);
        }

        if (observation.RegenerationAmount <= 0 || result.TargetId <= 0 || IsSummon(result.TargetId))
            return;

        var regeneration = observation with
        {
            Damage = observation.RegenerationAmount,
            HitCount = 0,
            AttemptCount = 0,
            DrainHealAmount = 0,
            RegenerationAmount = 0,
            ResourceKind = CombatResourceKind.Unknown,
            Modifiers = DamageModifiers.None,
            PeriodicRelation = PeriodicEffectRelation.None,
            PeriodicMode = 0
        };
        var regenerationOccurrence = new CombatOccurrenceResolution(
            CombatPacketRule.RegenerationSecondary,
            CombatMaterializationKind.RegenerationSecondary,
            CombatAssociationKind.None,
            CombatSuppressionReason.None);
        var regenerationMaterialization = ResolveCombatOccurrence(result.TargetId, result.TargetId, in regeneration, in regenerationOccurrence, observedAtMilliseconds, in stamp, raw);
        ApplyMaterialization(result.TargetId, result.TargetId, in regeneration, in regenerationMaterialization, observedAtMilliseconds, stamp.ObservationOrdinal, raw, applyClassEvidence: false);
    }

    private CombatOccurrenceMaterialization ResolveCombatOccurrence(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence,
        long observedAtMilliseconds,
        in TimelineStamp stamp,
        RawPacketReference raw)
    {
        var materialization = CombatOccurrenceMaterializer.Resolve(sourceId, targetId, in observation, in occurrence, _contributionPathResolver);
        if (_combatOccurrenceObserver is { } observer)
        {
            var context = new CombatOccurrenceContext(
                sourceId,
                targetId,
                observation,
                occurrence,
                observedAtMilliseconds,
                stamp.ObservationOrdinal,
                stamp.FlushId,
                raw,
                materialization);
            observer.Observe(in context);
        }

        return materialization;
    }

    private bool IsSummon(int entityId) =>
        _entities.TryGet(entityId, out var entity) &&
        (entity.OwnerKind == EntityOwnerKind.Summon || entity.Kind == NpcKind.Summon);

    private void ObserveAuraLifecycle(ObservedEventEntry entry, in AuraLifecycleTransition transition)
    {
        if (_auraLifecycleObserver is not { } observer)
            return;

        var stamp = entry.Stamp;
        var context = entry.Domain == ObservedEventDomain.Aura
            ? new AuraLifecycleObservationContext(
                AuraLifecycleSourceKind.Aura,
                entry.SourceEntityId,
                entry.TargetEntityId,
                entry.Aura,
                default,
                transition,
                entry.ObservedAtMilliseconds,
                stamp.ObservationOrdinal,
                stamp.FlushId,
                entry.Raw)
            : new AuraLifecycleObservationContext(
                AuraLifecycleSourceKind.Action,
                entry.SourceEntityId,
                entry.TargetEntityId,
                default,
                entry.Action,
                transition,
                entry.ObservedAtMilliseconds,
                stamp.ObservationOrdinal,
                stamp.FlushId,
                entry.Raw);
        observer.Observe(in context);
    }

    private void ApplyAura(in AuraObservation aura)
    {
        if (aura.Kind == AuraObservationKind.Result && aura.EntityId > 0 && aura.InstanceSequenceId > 0)
            _entities.ApplyNpc2C38State(aura.EntityId, aura.InstanceSequenceId, aura.ResultCode);
    }

    private void ApplyScene(in SceneObservation scene)
    {
        _boundary.ApplySceneObservation(in scene);

        switch (scene.Kind)
        {
            case SceneObservationKind.CurrentMap:
            case SceneObservationKind.DestinationMapArrival:
            case SceneObservationKind.MapEventRegistered:
                _metadataRegistry.UpsertMapCode(_boundary.CurrentMapInstanceId, _boundary.CurrentMapId);
                break;
            case SceneObservationKind.MapContextStarted:
            case SceneObservationKind.TransportStreamActivated:
                _compactDirectValue.ResetPendingAssociations();
                _metadataRegistry.UpsertMapCode(_boundary.CurrentMapInstanceId, _boundary.CurrentMapId);
                break;
        }
    }

    private void ApplyState(ObservedEventEntry entry, in StateObservation state)
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

    private bool CanNpcBattleActivate(int instanceId) => !_entityVitals.TryGet(instanceId, out var vital) || vital.CurrentHp != 0;

    private void TryApplyBossCombatActivity(int instanceId, long observedAtMilliseconds)
    {
        var hasActivity = _combat.TryGetLastCombatActivityObservedAt(instanceId, out var activityObservedAtMilliseconds);
        if (_mechanics.TryGetCombatant(instanceId, out var mechanicCombatant))
        {
            activityObservedAtMilliseconds = hasActivity
                ? Math.Max(activityObservedAtMilliseconds, mechanicCombatant!.LastObserved)
                : mechanicCombatant!.LastObserved;
            hasActivity = true;
        }

        if (!TrackBossFocus ||
            instanceId <= 0 ||
            !_entities.TryGet(instanceId, out var entity) ||
            !BossModeFocusTargets.IsFocusTarget(entity.Kind) ||
            _entityVitals.TryGet(instanceId, out var vital) && vital.CurrentHp == 0 ||
            !hasActivity)
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
    EntityVitalStoreSnapshot EntityVitals,
    AuraStoreSnapshot Auras,
    MechanicStoreSnapshot Mechanics,
    ResourceStoreSnapshot Resources,
    CombatContributionPathResolverSnapshot ContributionPath,
    BossFocusStoreSnapshot BossFocus);

public readonly record struct DomainEventMaterialization(AuraLifecycleTransition AuraLifecycle);
