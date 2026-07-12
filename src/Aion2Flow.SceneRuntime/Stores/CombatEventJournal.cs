using System.Collections;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

internal sealed class CombatEventJournal(int capacity = 0) : IReadOnlyList<CombatEventRecord>
{
    internal const int SegmentCapacity = 512;

    private readonly List<CombatEventStorageSegment> _segments = capacity > 0 ? new(ResolveSegmentCount(capacity)) : [];
    private int _count;

    public int Count => _count;

    public CombatEventRecord this[int index] => GetEvent(index);

    public int Append(in CombatEventRecord record)
    {
        if (_segments.Count == 0 || _segments[^1].Count == SegmentCapacity)
            _segments.Add(new CombatEventStorageSegment());

        var ordinal = _count++;
        _segments[^1].Add(in record);
        return ordinal;
    }

    public void EnsureCapacity(int capacity)
    {
        if (capacity > 0)
            _segments.EnsureCapacity(ResolveSegmentCount(capacity));
    }

    public ref readonly CombatEventRecord GetEvent(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        if (ordinal >= _count)
            throw new ArgumentOutOfRangeException(nameof(ordinal));

        return ref _segments[ordinal / SegmentCapacity][ordinal % SegmentCapacity];
    }

    public CombatEventRange AsRange() => new(this, 0, _count);

    public CombatEventSegment Freeze()
    {
        if (_count == 0)
            return default;

        var segments = new CombatEventStorageSegment[_segments.Count];
        _segments.CopyTo(segments);
        return new CombatEventSegment(segments, 0, _count);
    }

    public CombatEventRecord[] ToArray()
    {
        if (_count == 0)
            return [];

        var result = new CombatEventRecord[_count];
        var copied = 0;
        for (var i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            var count = Math.Min(segment.Count, result.Length - copied);
            segment.CopyTo(result.AsSpan(copied, count));
            copied += count;
        }
        return result;
    }

    public IEnumerator<CombatEventRecord> GetEnumerator()
    {
        for (var i = 0; i < _count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static int ResolveSegmentCount(int capacity)
        => Math.Max(1, (capacity + SegmentCapacity - 1) / SegmentCapacity);
}

public readonly ref struct CombatEventRange
{
    private readonly CombatEventJournal _journal;
    private readonly int _start;

    internal CombatEventRange(CombatEventJournal journal, int start, int length)
    {
        _journal = journal;
        _start = start;
        Length = length;
    }

    public int Length { get; }

    public ref readonly CombatEventRecord this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (index >= Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return ref _journal.GetEvent(_start + index);
        }
    }

    public CombatEventRange this[Range range]
    {
        get
        {
            var (offset, length) = range.GetOffsetAndLength(Length);
            return new CombatEventRange(_journal, _start + offset, length);
        }
    }

    public Enumerator GetEnumerator() => new(this);

    public ref struct Enumerator
    {
        private readonly CombatEventRange _range;
        private int _index = -1;

        internal Enumerator(CombatEventRange range)
        {
            _range = range;
        }

        public readonly ref readonly CombatEventRecord Current => ref _range[_index];

        public bool MoveNext() => ++_index < _range.Length;
    }
}

public readonly struct CombatEventSegment
{
    private readonly CombatEventStorageSegment[]? _segments;

    internal CombatEventSegment(CombatEventStorageSegment[] segments, long startEventOrdinal, long endEventOrdinalExclusive)
    {
        _segments = segments;
        StartEventOrdinal = startEventOrdinal;
        EndEventOrdinalExclusive = endEventOrdinalExclusive;
    }

    public long StartEventOrdinal { get; }
    public long EndEventOrdinalExclusive { get; }
    public int Count => checked((int)(EndEventOrdinalExclusive - StartEventOrdinal));
    public bool IsEmpty => Count == 0;

    internal ref readonly CombatEventRecord GetEvent(long eventOrdinal)
    {
        if (eventOrdinal < StartEventOrdinal || eventOrdinal >= EndEventOrdinalExclusive)
            throw new ArgumentOutOfRangeException(nameof(eventOrdinal));

        var offset = checked((int)(eventOrdinal - StartEventOrdinal));
        return ref _segments![offset / CombatEventJournal.SegmentCapacity][offset % CombatEventJournal.SegmentCapacity];
    }
}

internal sealed class CombatEventStorageSegment
{
    private readonly CombatEventRecord[] _events = new CombatEventRecord[CombatEventJournal.SegmentCapacity];

    public int Count { get; private set; }

    public ref readonly CombatEventRecord this[int index] => ref _events[index];

    public void Add(in CombatEventRecord record) => _events[Count++] = record;

    public void CopyTo(Span<CombatEventRecord> destination) => _events.AsSpan(0, destination.Length).CopyTo(destination);
}
