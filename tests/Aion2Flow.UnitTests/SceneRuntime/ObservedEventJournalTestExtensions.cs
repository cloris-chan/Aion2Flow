using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

internal readonly record struct ObservedEventTestEntry<TObservation>(ObservedEventHeader Header, TObservation Observation)
    where TObservation : struct;

internal static class ObservedEventJournalTestExtensions
{
    public static void AppendCombat(this ObservedEventJournal journal, Guid sceneSessionId, TimelineStamp stamp, int sourceEntityId, int targetEntityId, in CombatWireObservation observation, RawPacketReference raw = default)
    {
        var header = new ObservedEventHeader(sceneSessionId, stamp, sourceEntityId, targetEntityId, raw);
        journal.Append(in header, in observation);
    }

    public static void AppendAction(this ObservedEventJournal journal, Guid sceneSessionId, TimelineStamp stamp, int sourceEntityId, int targetEntityId, in ActionObservation observation, RawPacketReference raw = default)
    {
        var header = new ObservedEventHeader(sceneSessionId, stamp, sourceEntityId, targetEntityId, raw);
        journal.Append(in header, in observation);
    }

    public static void AppendState(this ObservedEventJournal journal, Guid sceneSessionId, TimelineStamp stamp, int sourceEntityId, int targetEntityId, in StateObservation observation, RawPacketReference raw = default)
    {
        var header = new ObservedEventHeader(sceneSessionId, stamp, sourceEntityId, targetEntityId, raw);
        journal.Append(in header, in observation);
    }

    public static void AppendEntityVital(this ObservedEventJournal journal, Guid sceneSessionId, TimelineStamp stamp, int sourceEntityId, int targetEntityId, in EntityVitalObservation observation, RawPacketReference raw = default)
    {
        var header = new ObservedEventHeader(sceneSessionId, stamp, sourceEntityId, targetEntityId, raw);
        journal.Append(in header, in observation);
    }

    public static void AppendAura(this ObservedEventJournal journal, Guid sceneSessionId, TimelineStamp stamp, int sourceEntityId, int targetEntityId, in AuraObservation observation, RawPacketReference raw = default)
    {
        var header = new ObservedEventHeader(sceneSessionId, stamp, sourceEntityId, targetEntityId, raw);
        journal.Append(in header, in observation);
    }

    public static void AppendScene(this ObservedEventJournal journal, Guid sceneSessionId, TimelineStamp stamp, int sourceEntityId, int targetEntityId, in SceneObservation observation, RawPacketReference raw = default)
    {
        var header = new ObservedEventHeader(sceneSessionId, stamp, sourceEntityId, targetEntityId, raw);
        journal.Append(in header, in observation);
    }

    public static void Append(this ObservedEventJournal journal, in ObservedEventTestEntry<CombatWireObservation> entry)
    {
        var header = entry.Header;
        var observation = entry.Observation;
        journal.Append(in header, in observation);
    }

    public static void Append(this ObservedEventJournal journal, in ObservedEventTestEntry<ActionObservation> entry)
    {
        var header = entry.Header;
        var observation = entry.Observation;
        journal.Append(in header, in observation);
    }

    public static void Append(this ObservedEventJournal journal, in ObservedEventTestEntry<StateObservation> entry)
    {
        var header = entry.Header;
        var observation = entry.Observation;
        journal.Append(in header, in observation);
    }

    public static void Append(this ObservedEventJournal journal, in ObservedEventTestEntry<EntityVitalObservation> entry)
    {
        var header = entry.Header;
        var observation = entry.Observation;
        journal.Append(in header, in observation);
    }

    public static void Append(this ObservedEventJournal journal, in ObservedEventTestEntry<AuraObservation> entry)
    {
        var header = entry.Header;
        var observation = entry.Observation;
        journal.Append(in header, in observation);
    }

    public static void Append(this ObservedEventJournal journal, in ObservedEventTestEntry<SceneObservation> entry)
    {
        var header = entry.Header;
        var observation = entry.Observation;
        journal.Append(in header, in observation);
    }

    public static ObservedEventTestSnapshot ReadSnapshot(this ObservedEventJournal journal, long observationOrdinal)
    {
        var snapshot = default(ObservedEventTestSnapshot);
        journal.ReadEntry(observationOrdinal, entry => snapshot = ObservedEventTestSnapshot.Create(entry));
        return snapshot;
    }
}

internal readonly record struct ObservedEventTestSnapshot(
    Guid SceneSessionId,
    TimelineStamp Stamp,
    ObservedEventDomain Domain,
    int SourceEntityId,
    int TargetEntityId,
    RawPacketReference Raw,
    CombatWireObservation? Combat,
    ActionObservation? Action,
    StateObservation? State,
    EntityVitalObservation? EntityVital,
    AuraObservation? Aura,
    SceneObservation? Scene)
{
    public static ObservedEventTestSnapshot Create(ObservedEventEntry entry)
        => new(
            entry.SceneSessionId,
            entry.Stamp,
            entry.Domain,
            entry.SourceEntityId,
            entry.TargetEntityId,
            entry.Raw,
            entry.Domain == ObservedEventDomain.Combat ? entry.Combat : null,
            entry.Domain == ObservedEventDomain.Action ? entry.Action : null,
            entry.Domain == ObservedEventDomain.State ? entry.State : null,
            entry.Domain == ObservedEventDomain.EntityVital ? entry.EntityVital : null,
            entry.Domain == ObservedEventDomain.Aura ? entry.Aura : null,
            entry.Domain == ObservedEventDomain.Scene ? entry.Scene : null);
}
