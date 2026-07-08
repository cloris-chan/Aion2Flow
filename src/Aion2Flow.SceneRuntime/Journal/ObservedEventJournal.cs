using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Journal;

public sealed class ObservedEventJournal(int capacity = 0)
{
    private readonly Lock _gate = new();
    private readonly List<ObservedEventEnvelope> _entries = capacity > 0 ? new(capacity) : [];
    private long _firstObservationOrdinal = 0;
    private long _nextObservationOrdinal;
    private long _lastCompletedFlushId = -1;

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public long FirstObservationOrdinal
    {
        get { lock (_gate) return _firstObservationOrdinal; }
    }

    public long NextObservationOrdinal
    {
        get { lock (_gate) return _nextObservationOrdinal; }
    }

    public long LastCompletedFlushId
    {
        get { lock (_gate) return _lastCompletedFlushId; }
    }

    public static ObservedEventJournal FromEntries(IReadOnlyList<ObservedEventEnvelope> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var journal = new ObservedEventJournal(entries.Count);
        if (entries.Count == 0)
            return journal;

        journal._firstObservationOrdinal = entries[0].Stamp.ObservationOrdinal;
        journal._nextObservationOrdinal = journal._firstObservationOrdinal;
        var lastFlushId = -1L;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Stamp.ObservationOrdinal != journal._nextObservationOrdinal)
                throw new ArgumentException("Entries must be ordered by contiguous observation ordinal.", nameof(entries));

            journal._entries.Add(entry);
            journal._nextObservationOrdinal++;
            lastFlushId = Math.Max(lastFlushId, entry.Stamp.FlushId);
        }

        journal._lastCompletedFlushId = lastFlushId;
        return journal;
    }

    public void Append(in ObservedEventEnvelope observedEvent)
    {
        lock (_gate)
        {
            if (observedEvent.Stamp.ObservationOrdinal != _nextObservationOrdinal)
                throw new ArgumentException(
                    $"ObservationOrdinal must be {_nextObservationOrdinal}, got {observedEvent.Stamp.ObservationOrdinal}.");

            _entries.Add(observedEvent);
            _nextObservationOrdinal++;
        }
    }

    public void EnsureCapacity(int capacity)
    {
        if (capacity <= 0)
            return;

        lock (_gate)
            _entries.EnsureCapacity(capacity);
    }

    public void CompleteFlush(long flushId)
    {
        lock (_gate)
        {
            if (flushId <= _lastCompletedFlushId)
                throw new ArgumentException(
                    $"FlushId must be > {_lastCompletedFlushId}, " +
                    $"got {flushId}.");
            _lastCompletedFlushId = flushId;
        }
    }

#pragma warning disable CA1822
    public JournalCursor CreateCursor(long startOrdinal) => new(startOrdinal);
#pragma warning restore CA1822

    public ObservedEventEnvelope Read(long observationOrdinal)
    {
        lock (_gate)
        {
            var index = FindPosition(observationOrdinal);
            if (index >= _entries.Count || _entries[index].Stamp.ObservationOrdinal != observationOrdinal)
                throw new ArgumentOutOfRangeException(nameof(observationOrdinal));
            return _entries[index];
        }
    }

    public bool TryRead(long observationOrdinal, out ObservedEventEnvelope observedEvent)
    {
        lock (_gate)
        {
            var index = FindPosition(observationOrdinal);
            if (index >= _entries.Count || _entries[index].Stamp.ObservationOrdinal != observationOrdinal)
            {
                observedEvent = default;
                return false;
            }

            observedEvent = _entries[index];
            return true;
        }
    }

    public JournalReadResult CopyEntries(JournalCursor cursor, Span<ObservedEventEnvelope> destination)
    {
        lock (_gate)
        {
            var start = FindPosition(cursor.NextObservationOrdinal);
            if (start >= _entries.Count || destination.Length == 0)
                return new JournalReadResult(0, cursor);

            var count = Math.Min(destination.Length, _entries.Count - start);
            var entries = CollectionsMarshal.AsSpan(_entries).Slice(start, count);
            entries.CopyTo(destination);
            return new JournalReadResult(count, new JournalCursor(entries[^1].Stamp.ObservationOrdinal + 1));
        }
    }

    public JournalReadResult ReadEntries(JournalCursor cursor, int maxCount, JournalEntriesReader reader)
        => ReadEntries(cursor, long.MaxValue, maxCount, reader);

    public JournalReadResult ReadEntries(JournalCursor cursor, long endObservationOrdinalExclusive, int maxCount, JournalEntriesReader reader)
    {
        lock (_gate)
        {
            var start = FindPosition(cursor.NextObservationOrdinal);
            if (start >= _entries.Count || maxCount <= 0)
                return new JournalReadResult(0, cursor);

            var end = endObservationOrdinalExclusive >= _nextObservationOrdinal
                ? _entries.Count
                : FindPosition(endObservationOrdinalExclusive);
            if (end <= start)
                return new JournalReadResult(0, cursor);

            var count = Math.Min(maxCount, end - start);
            var entries = CollectionsMarshal.AsSpan(_entries).Slice(start, count);
            reader(entries);
            return new JournalReadResult(count, new JournalCursor(entries[^1].Stamp.ObservationOrdinal + 1));
        }
    }

    private int FindPosition(long observationOrdinal)
    {
        if (observationOrdinal <= _firstObservationOrdinal)
            return 0;

        if (observationOrdinal >= _nextObservationOrdinal)
            return _entries.Count;

        var offset = observationOrdinal - _firstObservationOrdinal;
        if ((ulong)offset < (uint)_entries.Count && _entries[(int)offset].Stamp.ObservationOrdinal == observationOrdinal)
            return (int)offset;

        int lo = 0, hi = _entries.Count;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (_entries[mid].Stamp.ObservationOrdinal < observationOrdinal)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }
}

public delegate void JournalEntriesReader(ReadOnlySpan<ObservedEventEnvelope> entries);

public readonly record struct JournalCursor(long NextObservationOrdinal);

public readonly record struct JournalReadResult(int Count, JournalCursor Cursor);
