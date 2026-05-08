using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Journal;

public sealed class ObservedEventJournal
{
    private readonly Lock _gate = new();
    private readonly List<ObservedEventEnvelope> _entries = [];
    private long _nextObservationOrdinal;
    private long _lastCompletedBatchOrdinal = -1;

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public long NextObservationOrdinal
    {
        get { lock (_gate) return _nextObservationOrdinal; }
    }

    public long LastCompletedBatchOrdinal
    {
        get { lock (_gate) return _lastCompletedBatchOrdinal; }
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

    public void CompleteBatch(long batchOrdinal)
    {
        lock (_gate)
        {
            if (batchOrdinal <= _lastCompletedBatchOrdinal)
                throw new ArgumentException(
                    $"BatchOrdinal must be > {_lastCompletedBatchOrdinal}, " +
                    $"got {batchOrdinal}.");
            _lastCompletedBatchOrdinal = batchOrdinal;
        }
    }

    public JournalCursor CreateCursor(long startOrdinal)
    {
        lock (_gate)
        {
            int lo = 0, hi = _entries.Count;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (_entries[mid].Stamp.ObservationOrdinal < startOrdinal)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return new JournalCursor(lo, startOrdinal);
        }
    }

    public ObservedEventEnvelope Read(long observationOrdinal)
    {
        lock (_gate)
        {
            if (observationOrdinal < 0 || observationOrdinal >= _entries.Count)
                throw new ArgumentOutOfRangeException(nameof(observationOrdinal));
            return _entries[(int)observationOrdinal];
        }
    }

    public ReadOnlySpan<ObservedEventEnvelope> GetEntries(JournalCursor cursor, int maxCount)
    {
        lock (_gate)
        {
            int start = cursor.Position;
            if (start >= _entries.Count)
                return [];

            int count = Math.Min(maxCount, _entries.Count - start);
            return CollectionsMarshal.AsSpan(_entries).Slice(start, count);
        }
    }

    public int CopyEntries(JournalCursor cursor, Span<ObservedEventEnvelope> destination)
    {
        lock (_gate)
        {
            int start = cursor.Position;
            if (start >= _entries.Count || destination.Length == 0)
                return 0;

            int count = Math.Min(destination.Length, _entries.Count - start);
            CollectionsMarshal.AsSpan(_entries).Slice(start, count).CopyTo(destination);
            return count;
        }
    }
}

public readonly record struct JournalCursor(int Position, long StartOrdinal);
