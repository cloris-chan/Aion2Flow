using Cloris.Aion2Flow.SceneRuntime.Canonicalization;
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
    private readonly PeriodicChainCanonicalizer _periodicChain;
    private readonly OwnerTargetSummonRestoreCanonicalizer _ownerTargetSummonRestore;
    private readonly MultiHitAttributionService _multiHitAttribution;
    private readonly CompactOutcomeCanonicalizer _compactOutcome;
    private readonly PeriodicLinkCanonicalizer _periodicLink;
    private readonly BossFocusStore _bossFocus;

    public DomainEventApplier(EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat)
        : this(
            entities,
            boundary,
            metadataRegistry,
            combat,
            new SystemPeriodicRecoveryCanonicalizer(),
            new PeriodicChainCanonicalizer(),
            new MultiHitAttributionService(),
            new CompactOutcomeCanonicalizer(),
            new PeriodicLinkCanonicalizer(),
            new BossFocusStore(entities))
    {
    }

    public DomainEventApplier(EntityStore entities, SceneBoundaryStore boundary, CombatStore combat)
        : this(entities, boundary, new RuntimeMetadataRegistry(), combat)
    {
    }

    private DomainEventApplier(
        EntityStore entities,
        SceneBoundaryStore boundary,
        RuntimeMetadataRegistry metadataRegistry,
        CombatStore combat,
        SystemPeriodicRecoveryCanonicalizer systemPeriodicRecovery,
        PeriodicChainCanonicalizer periodicChain,
        MultiHitAttributionService multiHitAttribution,
        CompactOutcomeCanonicalizer compactOutcome,
        PeriodicLinkCanonicalizer periodicLink,
        BossFocusStore bossFocus)
    {
        _entities = entities;
        _boundary = boundary;
        _metadataRegistry = metadataRegistry;
        _combat = combat;
        _systemPeriodicRecovery = systemPeriodicRecovery;
        _periodicChain = periodicChain;
        _ownerTargetSummonRestore = new OwnerTargetSummonRestoreCanonicalizer(entities);
        _multiHitAttribution = multiHitAttribution;
        _compactOutcome = compactOutcome;
        _periodicLink = periodicLink;
        _bossFocus = bossFocus;
    }

    public EntityStore Entities => _entities;
    public SceneBoundaryStore Boundary => _boundary;
    public RuntimeMetadataRegistry MetadataRegistry => _metadataRegistry;
    public CombatStore Combat => _combat;
    public BossFocusStore BossFocus => _bossFocus;

    public DomainEventApplier DeepClone(EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat)
        => new(
            entities,
            boundary,
            metadataRegistry,
            combat,
            _systemPeriodicRecovery.DeepClone(),
            _periodicChain.DeepClone(),
            _multiHitAttribution.DeepClone(),
            _compactOutcome.DeepClone(),
            _periodicLink.DeepClone(),
            _bossFocus.DeepClone(entities));

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
                _bossFocus.ApplyNpcHp(resource.EntityId, (int)(resource.CurrentValue ?? 0), (int)(resource.MaximumValue ?? 0), entry.Raw.TimestampMilliseconds);
                break;
            case ObservedEventDomain.Scene when entry.Scene is { } scene:
                ApplyScene(in scene);
                break;
            case ObservedEventDomain.Aura when entry.Aura is { } aura:
                ApplyAura(in entry, in aura);
                break;
        }
    }

    public void CompleteBatch(long batchOrdinal)
    {
        foreach (var result in _compactOutcome.CompleteBatch(batchOrdinal))
        {
            var stamp = result.Stamp;
            var observation = result.Observation;
            var canonicalized = new CombatCanonicalizationResult(result.SourceId, result.TargetId, observation);
            ApplyCanonicalizedCombatResult(in stamp, in canonicalized, result.ObservedAtMilliseconds);
        }
    }

    public void FlushPendingOutcomeSidecars() => CompleteBatch(long.MaxValue);

    private void ApplyCombat(in ObservedEventEnvelope entry, in CombatObservation combatObservation)
    {
        var stamp = entry.Stamp;
        if (entry.Raw.Opcode == 0x0538 && PeriodicLinkCanonicalizer.IsLinkObservation(in combatObservation))
        {
            if (_periodicLink.Normalize(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation) is { } periodicLinkResult)
                ApplyCanonicalizedCombatResult(in stamp, in periodicLinkResult, entry.Raw.TimestampMilliseconds);
            return;
        }

        var rawResults = entry.Raw.Opcode switch
        {
            0x0438 => _compactOutcome.ObserveCompactValue0438(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation, entry.Raw.TimestampMilliseconds),
            0x0238 => _compactOutcome.ObserveCompactControl0238(entry.SourceEntityId, in stamp, in combatObservation),
            0x0638 => _compactOutcome.ObserveCompactControl0638(entry.SourceEntityId, in stamp, in combatObservation, entry.Raw.TimestampMilliseconds),
            _ => _compactOutcome.NormalizeCombat(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation, entry.Raw.TimestampMilliseconds)
        };

        foreach (var rawResult in rawResults)
        {
            var observation = rawResult.Observation;
            var resultStamp = rawResult.Stamp;
            var result = new CombatCanonicalizationResult(rawResult.SourceId, rawResult.TargetId, observation);
            ApplyCanonicalizedCombatResult(in resultStamp, in result, rawResult.ObservedAtMilliseconds);
        }
    }

    private void ApplyCanonicalizedCombatResult(in TimelineStamp stamp, in CombatCanonicalizationResult result, long observedAtMilliseconds)
    {
        var resultObservation = result.Observation;
        var ownerTargetSummonRestoreResult = _ownerTargetSummonRestore.Normalize(result.SourceId, result.TargetId, in resultObservation);
        var observation = ownerTargetSummonRestoreResult.Observation;
        var systemRecoveryResult = _systemPeriodicRecovery.Normalize(ownerTargetSummonRestoreResult.SourceId, ownerTargetSummonRestoreResult.TargetId, in stamp, in observation);
        var systemRecoveryObservation = systemRecoveryResult.Observation;
        foreach (var normalized in _periodicChain.Normalize(systemRecoveryResult.SourceId, systemRecoveryResult.TargetId, in systemRecoveryObservation))
            ApplyCombatResult(in stamp, in normalized, observedAtMilliseconds);
    }

    private void ApplyCombatResult(in TimelineStamp stamp, in CombatCanonicalizationResult result, long observedAtMilliseconds)
    {
        var observation = result.Observation;
        if (_entities.ApplyCharacterClassEvidence(result.SourceId, in observation) &&
            _entities.TryGet(result.SourceId, out var sourceEntity))
        {
            _metadataRegistry.UpsertPcClass(result.SourceId, sourceEntity.CharacterClass);
        }

        _combat.ApplyCombat(result.SourceId, result.TargetId, in observation, observedAtMilliseconds);
        _multiHitAttribution.ObserveCombat(result.SourceId, result.TargetId, in stamp, in observation);
    }

    private void ApplyAura(in ObservedEventEnvelope entry, in AuraObservation aura)
    {
        if (aura.TargetEntityId > 0 && aura.SequenceId > 0)
            _entities.ApplyNpc2C38State(aura.TargetEntityId, aura.SequenceId, aura.ResultCode);

        if (_multiHitAttribution.TrySynthesize2C38Invincible(in entry, in aura) is { } result)
        {
            var stamp = entry.Stamp;
            ApplyCombatResult(in stamp, in result, entry.Raw.TimestampMilliseconds);
        }
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
                _entities.ApplyNickname(entry.SourceEntityId, nickname);
                if (!string.IsNullOrWhiteSpace(nickname))
                    _metadataRegistry.UpsertPcMetadata(entry.SourceEntityId, nickname, state.OriginServerId, state.Faction);
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
            _bossFocus.ApplyNpcKind(state.EntityId, kind, entry.Raw.TimestampMilliseconds);
            return;
        }

        if (state.StateCode == StateCodes.NpcBattle)
        {
            var isActive = state.Value0 != 0 && CanNpcBattleActivate(state.EntityId);
            _entities.ApplyBattleToggle(state.EntityId, isActive);
            _bossFocus.ApplyBattle(state.EntityId, isActive, entry.Raw.TimestampMilliseconds);
            return;
        }

        if (state.StateCode == StateCodes.NpcBattleToggle)
        {
            var isActive = !_entities.GetOrAdd(state.EntityId).NpcCombatActive && CanNpcBattleActivate(state.EntityId);
            _entities.ApplyBattleToggle(state.EntityId, isActive);
            _bossFocus.ApplyBattleToggle(state.EntityId, isActive, entry.Raw.TimestampMilliseconds);
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

    private bool CanNpcBattleActivate(int instanceId) =>
        !_entities.TryGet(instanceId, out var entity) || entity.CurrentHp != 0;
}
