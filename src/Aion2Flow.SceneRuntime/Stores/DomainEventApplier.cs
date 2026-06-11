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
    private readonly PeriodicPoolCanonicalizer _periodicPool;
    private readonly OwnerTargetSummonResourceCanonicalizer _ownerTargetSummonResource;
    private readonly CompactAvoidanceCanonicalizer _compactAvoidance;
    private readonly BossFocusStore _bossFocus;

    public DomainEventApplier(EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat)
        : this(entities, boundary, metadataRegistry, combat, new SystemPeriodicRecoveryCanonicalizer(), new PeriodicPoolCanonicalizer(), new CompactAvoidanceCanonicalizer(), new BossFocusStore(entities))
    {
    }

    public DomainEventApplier(EntityStore entities, SceneBoundaryStore boundary, CombatStore combat)
        : this(entities, boundary, new RuntimeMetadataRegistry(), combat)
    {
    }

    internal DomainEventApplier(EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat, SystemPeriodicRecoveryCanonicalizer systemPeriodicRecovery, PeriodicPoolCanonicalizer periodicPool, CompactAvoidanceCanonicalizer compactAvoidance, BossFocusStore bossFocus)
    {
        _entities = entities;
        _boundary = boundary;
        _metadataRegistry = metadataRegistry;
        _combat = combat;
        _systemPeriodicRecovery = systemPeriodicRecovery;
        _periodicPool = periodicPool;
        _ownerTargetSummonResource = new OwnerTargetSummonResourceCanonicalizer(entities);
        _compactAvoidance = compactAvoidance;
        _bossFocus = bossFocus;
    }

    public EntityStore Entities => _entities;
    public SceneBoundaryStore Boundary => _boundary;
    public RuntimeMetadataRegistry MetadataRegistry => _metadataRegistry;
    public CombatStore Combat => _combat;
    public BossFocusStore BossFocus => _bossFocus;

    internal DomainEventApplierSnapshot CreateSnapshot() => new(
        _systemPeriodicRecovery.CreateSnapshot(),
        _periodicPool.CreateSnapshot(),
        _compactAvoidance.CreateSnapshot(),
        _bossFocus.CreateSnapshot());

    internal static DomainEventApplier FromSnapshot(EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat, DomainEventApplierSnapshot snapshot)
        => new(
            entities,
            boundary,
            metadataRegistry,
            combat,
            SystemPeriodicRecoveryCanonicalizer.FromSnapshot(snapshot.SystemPeriodicRecovery),
            PeriodicPoolCanonicalizer.FromSnapshot(snapshot.PeriodicPool),
            CompactAvoidanceCanonicalizer.FromSnapshot(snapshot.CompactAvoidance),
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
                _bossFocus.ApplyNpcHp(resource.EntityId, (int)(resource.CurrentValue ?? 0), (int)(resource.MaximumValue ?? 0), observedAtMilliseconds);
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
        foreach (var result in _compactAvoidance.CompleteBatch(batchOrdinal))
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
        var structurePath = entry.Raw.StructurePath;
        var observedAtMilliseconds = stamp.OffsetTicks / TimeSpan.TicksPerMillisecond;
        var rawResults = entry.Raw.Opcode switch
        {
            0x0438 => _compactAvoidance.ObserveCompactValue0438(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation, in structurePath, observedAtMilliseconds),
            0x0238 => _compactAvoidance.AdvanceBatch(in stamp),
            0x0638 => _compactAvoidance.AdvanceBatch(in stamp),
            _ => _compactAvoidance.NormalizeCombat(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation, observedAtMilliseconds)
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
    }

    private void ApplyAura(in AuraObservation aura)
    {
        if (aura.TargetEntityId > 0 && aura.SequenceId > 0)
            _entities.ApplyNpc2C38State(aura.TargetEntityId, aura.SequenceId, aura.ResultCode);

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
                _entities.ApplyNickname(entry.SourceEntityId, nickname);
                if (metadataClass is { } characterClass)
                    _entities.ApplyMetadataCharacterClass(entry.SourceEntityId, characterClass);

                if (!string.IsNullOrWhiteSpace(nickname) || metadataClass is not null)
                    _metadataRegistry.UpsertPcMetadata(entry.SourceEntityId, nickname, state.OriginServerId, state.Faction, metadataClass);
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
            _bossFocus.ApplyNpcKind(state.EntityId, kind, entry.Stamp.OffsetTicks / TimeSpan.TicksPerMillisecond);
            return;
        }

        if (state.StateCode == StateCodes.NpcBattle)
        {
            var isActive = state.Value0 != 0 && CanNpcBattleActivate(state.EntityId);
            _entities.ApplyBattleToggle(state.EntityId, isActive);
            _bossFocus.ApplyBattle(state.EntityId, isActive, entry.Stamp.OffsetTicks / TimeSpan.TicksPerMillisecond);
            return;
        }

        if (state.StateCode == StateCodes.NpcBattleToggle)
        {
            var isActive = !_entities.GetOrAdd(state.EntityId).NpcCombatActive && CanNpcBattleActivate(state.EntityId);
            _entities.ApplyBattleToggle(state.EntityId, isActive);
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
}

internal sealed record DomainEventApplierSnapshot(
    SystemPeriodicRecoveryCanonicalizerSnapshot SystemPeriodicRecovery,
    PeriodicPoolCanonicalizerSnapshot PeriodicPool,
    CompactAvoidanceCanonicalizerSnapshot CompactAvoidance,
    BossFocusStoreSnapshot BossFocus);
