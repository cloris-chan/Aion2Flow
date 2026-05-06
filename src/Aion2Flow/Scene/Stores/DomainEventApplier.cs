using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Observation;

namespace Cloris.Aion2Flow.Scene.Stores;

public sealed class DomainEventApplier(EntityStore entities, MetadataStore metadata, CombatStore combat)
{
    public EntityStore Entities => entities;
    public MetadataStore Metadata => metadata;
    public CombatStore Combat => combat;

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
    }

    private void ApplyEntry(in ObservedEventEnvelope entry)
    {
        switch (entry.Domain)
        {
            case ObservedEventDomain.Combat when entry.Combat is { } c:
                combat.ApplyCombat(entry.SourceEntityId, entry.TargetEntityId, c.Damage, c.HitCount, c.AttemptCount, c.SkillCode);
                break;
            case ObservedEventDomain.State when entry.State is { } state:
                ApplyState(in entry, in state);
                break;
            case ObservedEventDomain.Resource when entry.Resource is { } resource:
                entities.ApplyNpcHp(resource.EntityId, (int)(resource.CurrentValue ?? 0), (int)(resource.MaximumValue ?? 0));
                break;
            case ObservedEventDomain.Aura when entry.Aura is { } aura:
                ApplyAura(in aura);
                break;
        }
    }

    private void ApplyAura(in AuraObservation aura)
    {
        if (aura.TargetEntityId > 0 && aura.SequenceId > 0)
            entities.ApplyNpc2C38State(aura.TargetEntityId, aura.SequenceId, aura.ResultCode);
    }

    private void ApplyState(in ObservedEventEnvelope entry, in StateObservation state)
    {
        if (entry.TargetEntityId != 0 && state.EntityId == entry.TargetEntityId && entry.SourceEntityId != entry.TargetEntityId)
        {
            entities.ApplySummon(entry.SourceEntityId, entry.TargetEntityId);
            return;
        }

        if (state.StateCode == 0)
        {
            if (entry.SourceEntityId > 0)
                entities.ApplyNickname(entry.SourceEntityId, string.Empty);
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
}
