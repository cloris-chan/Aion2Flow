namespace Cloris.Aion2Flow.SceneRuntime.Journal;

public readonly record struct SceneJournalSegment(ObservedEventJournal? Journal, long StartObservationOrdinal, long EndObservationOrdinalExclusive, bool IsLiveGrowing)
{
    public static SceneJournalSegment Empty => default;

    public bool IsEmpty => Journal is null;

    public long CurrentEndObservationOrdinalExclusive
    {
        get
        {
            if (Journal is null)
                return Math.Max(StartObservationOrdinal, EndObservationOrdinalExclusive);

            return IsLiveGrowing
                ? Math.Max(StartObservationOrdinal, Journal.NextObservationOrdinal)
                : Math.Max(StartObservationOrdinal, EndObservationOrdinalExclusive);
        }
    }

    public JournalCursor CreateCursor() => new(StartObservationOrdinal);

    public JournalReadResult ReadEntries(JournalCursor cursor, int maxCount, JournalEntriesReader reader)
    {
        if (Journal is null)
            return new JournalReadResult(0, cursor);

        var boundedCursor = cursor.NextObservationOrdinal < StartObservationOrdinal
            ? new JournalCursor(StartObservationOrdinal)
            : cursor;
        return Journal.ReadEntries(boundedCursor, CurrentEndObservationOrdinalExclusive, maxCount, reader);
    }
}
