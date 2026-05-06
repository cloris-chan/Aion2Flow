using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Scene.Canonicalization;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Model;
using Cloris.Aion2Flow.Scene.Observation;

namespace Cloris.Aion2Flow.Scene.Stores;

public sealed class DomainEventApplier(EntityStore entities, MetadataStore metadata, CombatStore combat)
{
    private readonly SystemPeriodicRecoveryCanonicalizer _systemPeriodicRecovery = new();
    private readonly PeriodicChainCanonicalizer _periodicChain = new();
    private readonly MultiHitAttributionService _multiHitAttribution = new();
    private readonly CompactOutcomeCanonicalizer _compactOutcome = new();
    private readonly PeriodicLinkCanonicalizer _periodicLink = new();
    private readonly BossFocusStore _bossFocus = new(entities);

    public EntityStore Entities => entities;
    public MetadataStore Metadata => metadata;
    public CombatStore Combat => combat;
    public BossFocusStore BossFocus => _bossFocus;

    public void ApplyJournal(ObservedEventJournal journal)
    {
        if (journal.Count == 0)
            return;

        var cursor = journal.CreateCursor(0);
        while (true)
        {
            var entries = journal.GetEntries(cursor, 256);
            if (entries.Length == 0)
                break;

            foreach (ref readonly var entry in entries)
                ApplyEntry(in entry);

            cursor = new JournalCursor(cursor.Position + entries.Length, cursor.StartOrdinal);
        }

        FlushPendingOutcomeSidecars();
    }

    private void ApplyEntry(in ObservedEventEnvelope entry)
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
                entities.ApplyNpcHp(resource.EntityId, (int)(resource.CurrentValue ?? 0), (int)(resource.MaximumValue ?? 0));
                _bossFocus.ApplyNpcHp(resource.EntityId, (int)(resource.CurrentValue ?? 0), (int)(resource.MaximumValue ?? 0), entry.Raw.TimestampMilliseconds);
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
            ApplyCanonicalizedCombatResult(in stamp, in canonicalized);
        }
    }

    public void FlushPendingOutcomeSidecars() => CompleteBatch(long.MaxValue);

    private void ApplyCombat(in ObservedEventEnvelope entry, in CombatObservation combatObservation)
    {
        var stamp = entry.Stamp;
        if (entry.Raw.Opcode == 0x0538 && PeriodicLinkCanonicalizer.IsLinkObservation(in combatObservation))
        {
            if (_periodicLink.Normalize(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation) is { } periodicLinkResult)
                ApplyCanonicalizedCombatResult(in stamp, in periodicLinkResult);
            return;
        }

        var rawResults = entry.Raw.Opcode switch
        {
            0x0238 => _compactOutcome.ObserveCompactControl0238(entry.SourceEntityId, in stamp, in combatObservation),
            0x0638 => _compactOutcome.ObserveCompactControl0638(entry.SourceEntityId, in stamp, in combatObservation),
            _ => _compactOutcome.NormalizeCombat(entry.SourceEntityId, entry.TargetEntityId, in stamp, in combatObservation)
        };

        foreach (var rawResult in rawResults)
        {
            var observation = rawResult.Observation;
            var resultStamp = rawResult.Stamp;
            var result = new CombatCanonicalizationResult(rawResult.SourceId, rawResult.TargetId, observation);
            ApplyCanonicalizedCombatResult(in resultStamp, in result);
        }
    }

    private void ApplyCanonicalizedCombatResult(in TimelineStamp stamp, in CombatCanonicalizationResult result)
    {
        var observation = result.Observation;
        var systemRecoveryResult = _systemPeriodicRecovery.Normalize(result.SourceId, result.TargetId, in stamp, in observation);
        var systemRecoveryObservation = systemRecoveryResult.Observation;
        foreach (var normalized in _periodicChain.Normalize(systemRecoveryResult.SourceId, systemRecoveryResult.TargetId, in systemRecoveryObservation))
            ApplyCombatResult(in stamp, in normalized);
    }

    private void ApplyCombatResult(in TimelineStamp stamp, in CombatCanonicalizationResult result)
    {
        var observation = result.Observation;
        combat.ApplyCombat(result.SourceId, result.TargetId, in observation);
        _multiHitAttribution.ObserveCombat(result.SourceId, result.TargetId, in stamp, in observation);
    }

    private void ApplyAura(in ObservedEventEnvelope entry, in AuraObservation aura)
    {
        if (aura.TargetEntityId > 0 && aura.SequenceId > 0)
            entities.ApplyNpc2C38State(aura.TargetEntityId, aura.SequenceId, aura.ResultCode);

        if (_multiHitAttribution.TrySynthesize2C38Invincible(in entry, in aura) is { } result)
        {
            var stamp = entry.Stamp;
            ApplyCombatResult(in stamp, in result);
        }
    }

    private void ApplyState(in ObservedEventEnvelope entry, in StateObservation state)
    {
        if (entry.TargetEntityId != 0 && state.EntityId == entry.TargetEntityId && entry.SourceEntityId != entry.TargetEntityId)
        {
            entities.ApplySummon(entry.SourceEntityId, entry.TargetEntityId);
            return;
        }

        if (state.StateCode == StateCodes.PlayerIdentity)
        {
            if (entry.SourceEntityId > 0)
                entities.ApplyNickname(entry.SourceEntityId, string.Empty);
            return;
        }

        if (state.StateCode == StateCodes.NpcKind)
        {
            var kind = Enum.IsDefined((NpcKind)state.Value0) ? (NpcKind)state.Value0 : NpcKind.Unknown;
            entities.ApplyNpcKind(state.EntityId, kind);
            _bossFocus.ApplyNpcKind(state.EntityId, kind, entry.Raw.TimestampMilliseconds);
            return;
        }

        if (state.StateCode == StateCodes.NpcBattle)
        {
            var isActive = state.Value0 != 0 && CanNpcBattleActivate(state.EntityId);
            entities.ApplyBattleToggle(state.EntityId, isActive);
            _bossFocus.ApplyBattle(state.EntityId, isActive, entry.Raw.TimestampMilliseconds);
            return;
        }

        if (state.StateCode == StateCodes.NpcBattleToggle)
        {
            var isActive = !entities.GetOrAdd(state.EntityId).BattleActive && CanNpcBattleActivate(state.EntityId);
            entities.ApplyBattleToggle(state.EntityId, isActive);
            _bossFocus.ApplyBattleToggle(state.EntityId, isActive, entry.Raw.TimestampMilliseconds);
            return;
        }

        if (state.StateCode is >= 2_000_000 and <= 2_999_999)
        {
            entities.ApplyNpcCode(state.EntityId, state.StateCode);
            return;
        }

        if (state.StateCode == 2136)
        {
            entities.ApplyNpc2136State(state.EntityId, checked((uint)state.Value0), checked((uint)state.Value1));
            return;
        }

        if (state.StateCode == 140)
        {
            entities.ApplyNpc0140Value(state.EntityId, checked((uint)state.Value0));
            return;
        }

        if (state.StateCode == 240)
        {
            entities.ApplyNpc0240Value(state.EntityId, checked((uint)state.Value0));
            return;
        }

        if (state.StateCode == 4636)
        {
            entities.ApplyNpc4636State(state.EntityId, checked((byte)state.Value0), checked((byte)state.Value1));
            return;
        }

        _ = entities.GetOrAdd(state.EntityId);
    }

    private bool CanNpcBattleActivate(int instanceId) =>
        !entities.TryGet(instanceId, out var entity) || entity.CurrentHp != 0;
}
