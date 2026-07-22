namespace Cloris.Aion2Flow.SceneRuntime.Journal;

public readonly record struct SceneJournalSegment
{
    private readonly long _endObservationOrdinalExclusive;
    private readonly SceneJournalLiveBoundary? _liveBoundary;
    private readonly bool _isLiveGrowing;

    public SceneJournalSegment(
        ObservedEventJournal? journal,
        long startObservationOrdinal,
        long endObservationOrdinalExclusive)
        : this(journal, startObservationOrdinal, endObservationOrdinalExclusive, IsLiveGrowing: false)
    {
    }

    internal SceneJournalSegment(
        ObservedEventJournal? journal,
        long startObservationOrdinal,
        long endObservationOrdinalExclusive,
        bool IsLiveGrowing)
    {
        Journal = journal;
        StartObservationOrdinal = startObservationOrdinal;
        _endObservationOrdinalExclusive = endObservationOrdinalExclusive;
        _liveBoundary = IsLiveGrowing && journal is not null
            ? new SceneJournalLiveBoundary(journal, endObservationOrdinalExclusive)
            : null;
        _isLiveGrowing = false;
    }

    internal SceneJournalSegment(
        ObservedEventJournal journal,
        long startObservationOrdinal,
        SceneJournalLiveBoundary liveBoundary)
    {
        Journal = journal;
        StartObservationOrdinal = startObservationOrdinal;
        _endObservationOrdinalExclusive = liveBoundary.EndObservationOrdinalExclusive;
        _liveBoundary = liveBoundary;
        _isLiveGrowing = false;
    }

    private SceneJournalSegment(
        ObservedEventJournal? journal,
        long startObservationOrdinal,
        long endObservationOrdinalExclusive,
        bool isLiveGrowing,
        byte _)
    {
        Journal = journal;
        StartObservationOrdinal = startObservationOrdinal;
        _endObservationOrdinalExclusive = endObservationOrdinalExclusive;
        _liveBoundary = null;
        _isLiveGrowing = isLiveGrowing;
    }

    public static SceneJournalSegment Empty => default;

    public ObservedEventJournal? Journal { get; }

    public long StartObservationOrdinal { get; }

    public long EndObservationOrdinalExclusive => _liveBoundary?.EndObservationOrdinalExclusive ?? _endObservationOrdinalExclusive;

    public bool IsLiveGrowing => _liveBoundary?.IsGrowing ?? _isLiveGrowing;

    public bool IsEmpty => Journal is null;

    public long CurrentEndObservationOrdinalExclusive => Math.Max(StartObservationOrdinal, EndObservationOrdinalExclusive);

    public JournalCursor CreateCursor() => new(StartObservationOrdinal);

    public SceneJournalSegment CreateBoundedSnapshot()
    {
        var endObservationOrdinalExclusive = CurrentEndObservationOrdinalExclusive;
        var isLiveGrowing = IsLiveGrowing;
        return new SceneJournalSegment(
            Journal,
            StartObservationOrdinal,
            endObservationOrdinalExclusive,
            isLiveGrowing,
            0);
    }

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

internal sealed class SceneJournalLiveBoundary(ObservedEventJournal journal, long endObservationOrdinalExclusive)
{
    private long _frozenEndObservationOrdinalExclusive = endObservationOrdinalExclusive;
    private int _isGrowing = 1;

    public long EndObservationOrdinalExclusive
    {
        get
        {
            if (!IsGrowing)
                return Volatile.Read(ref _frozenEndObservationOrdinalExclusive);

            var liveEnd = journal.NextObservationOrdinal;
            return IsGrowing
                ? Math.Max(Volatile.Read(ref _frozenEndObservationOrdinalExclusive), liveEnd)
                : Volatile.Read(ref _frozenEndObservationOrdinalExclusive);
        }
    }

    public bool IsGrowing => Volatile.Read(ref _isGrowing) != 0;

    public void Freeze(long endObservationOrdinalExclusive)
    {
        Volatile.Write(ref _frozenEndObservationOrdinalExclusive, endObservationOrdinalExclusive);
        Volatile.Write(ref _isGrowing, 0);
    }
}
