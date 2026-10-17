using System.Collections;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

internal sealed class EventJournal<T>(int capacity = 0) : IReadOnlyList<T> where T : struct
{
    internal const int SegmentCapacity = 512;

    private readonly List<EventStorageSegment<T>> _segments = capacity > 0 ? new(ResolveSegmentCount(capacity)) : [];
    private int _count;

    public int Count => _count;

    public T this[int index] => GetEvent(index);

    public int Append(in T record)
    {
        if (_segments.Count == 0 || _segments[^1].Count == SegmentCapacity)
            _segments.Add(new EventStorageSegment<T>());

        var ordinal = _count++;
        _segments[^1].Add(in record);
        return ordinal;
    }

    public void EnsureCapacity(int capacity)
    {
        if (capacity > 0)
            _segments.EnsureCapacity(ResolveSegmentCount(capacity));
    }

    public ref readonly T GetEvent(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(ordinal, _count);

        return ref _segments[ordinal / SegmentCapacity][ordinal % SegmentCapacity];
    }

    public EventRange<T> AsRange() => new(this, 0, _count);

    public EventSegment<T> Freeze()
    {
        if (_count == 0)
            return default;

        var segments = new EventStorageSegment<T>[_segments.Count];
        _segments.CopyTo(segments);
        return new EventSegment<T>(segments, 0, _count);
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < _count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static int ResolveSegmentCount(int capacity)
        => (capacity - 1) / SegmentCapacity + 1;
}

public readonly ref struct EventRange<T> where T : struct
{
    private readonly EventJournal<T> _journal;
    private readonly int _start;

    internal EventRange(EventJournal<T> journal, int start, int length)
    {
        _journal = journal;
        _start = start;
        Length = length;
    }

    public int Length { get; }

    public ref readonly T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Length);
            return ref _journal.GetEvent(_start + index);
        }
    }

    public EventRange<T> this[Range range]
    {
        get
        {
            var (offset, length) = range.GetOffsetAndLength(Length);
            return new EventRange<T>(_journal, _start + offset, length);
        }
    }

    public Enumerator GetEnumerator() => new(this);

    public ref struct Enumerator
    {
        private readonly EventRange<T> _range;
        private int _index = -1;

        internal Enumerator(EventRange<T> range)
        {
            _range = range;
        }

        public readonly ref readonly T Current => ref _range[_index];

        public bool MoveNext() => ++_index < _range.Length;
    }
}

// A frozen segment owns only the segment references and fixed bounds. Appending
// to the journal never replaces published records; resetting replaces the journal.
public readonly struct EventSegment<T> : IReadOnlyList<T> where T : struct
{
    private readonly EventStorageSegment<T>[]? _segments;

    internal EventSegment(EventStorageSegment<T>[] segments, long startEventOrdinal, long endEventOrdinalExclusive)
    {
        _segments = segments;
        StartEventOrdinal = startEventOrdinal;
        EndEventOrdinalExclusive = endEventOrdinalExclusive;
    }

    public long StartEventOrdinal { get; }
    public long EndEventOrdinalExclusive { get; }
    public int Count => checked((int)(EndEventOrdinalExclusive - StartEventOrdinal));
    public bool IsEmpty => Count == 0;

    public ref readonly T this[int index] => ref GetEvent(StartEventOrdinal + index);

    T IReadOnlyList<T>.this[int index] => this[index];

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

    public struct Enumerator
    {
        private readonly EventSegment<T> _segment;
        private int _index;

        internal Enumerator(EventSegment<T> segment)
        {
            _segment = segment;
            _index = -1;
        }

        public readonly ref readonly T Current => ref _segment[_index];

        public bool MoveNext() => ++_index < _segment.Count;
    }

    internal ref readonly T GetEvent(long eventOrdinal)
    {
        if (eventOrdinal < StartEventOrdinal || eventOrdinal >= EndEventOrdinalExclusive)
            throw new ArgumentOutOfRangeException(nameof(eventOrdinal));

        var offset = checked((int)(eventOrdinal - StartEventOrdinal));
        return ref _segments![offset / EventJournal<T>.SegmentCapacity][offset % EventJournal<T>.SegmentCapacity];
    }
}

internal sealed class EventStorageSegment<T> where T : struct
{
    private readonly T[] _events = new T[EventJournal<T>.SegmentCapacity];

    public int Count { get; private set; }

    public ref readonly T this[int index] => ref _events[index];

    public void Add(in T record) => _events[Count++] = record;
}
