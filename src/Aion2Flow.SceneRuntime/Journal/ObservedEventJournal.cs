using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Journal;

public sealed class ObservedEventJournal(int capacity = 0)
{
    internal const int SegmentCapacity = 512;

    private readonly Lock _gate = new();
    private readonly List<ObservedEventStorageSegment> _segments = capacity > 0 ? new(ResolveSegmentCapacity(capacity)) : [];
    private readonly List<RawPacketReference> _rawReferences = [];
    private readonly Dictionary<RawPacketReference, int> _rawReferenceIndices = [];
    private readonly List<Guid> _sceneSessionIds = [];
    private readonly Dictionary<Guid, int> _sceneSessionIndices = [];
    private int _count;
    private long _firstObservationOrdinal;
    private long _nextObservationOrdinal;
    private long _lastCompletedFlushId = -1;

    public int Count
    {
        get { lock (_gate) return _count; }
    }

    public int SegmentCount
    {
        get { lock (_gate) return _segments.Count; }
    }

    public int RawReferenceCount
    {
        get { lock (_gate) return _rawReferences.Count; }
    }

    public int SceneSessionCount
    {
        get { lock (_gate) return _sceneSessionIds.Count; }
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

    public void Append(in ObservedEventHeader header, in CombatWireObservation observation)
    {
        lock (_gate)
        {
            var segment = PrepareAppend(in header, out var sessionIndex, out var rawIndex);
            var payloadIndex = segment.AddCombat(in observation);
            CommitAppend(segment, in header, sessionIndex, rawIndex, payloadIndex, ObservedEventDomain.Combat);
        }
    }

    public void Append(in ObservedEventHeader header, in ActionObservation observation)
    {
        lock (_gate)
        {
            var segment = PrepareAppend(in header, out var sessionIndex, out var rawIndex);
            var payloadIndex = segment.AddAction(in observation);
            CommitAppend(segment, in header, sessionIndex, rawIndex, payloadIndex, ObservedEventDomain.Action);
        }
    }

    public void Append(in ObservedEventHeader header, in StateObservation observation)
    {
        lock (_gate)
        {
            var segment = PrepareAppend(in header, out var sessionIndex, out var rawIndex);
            var payloadIndex = segment.AddState(in observation);
            CommitAppend(segment, in header, sessionIndex, rawIndex, payloadIndex, ObservedEventDomain.State);
        }
    }

    public void Append(in ObservedEventHeader header, in EntityVitalObservation observation)
    {
        lock (_gate)
        {
            var segment = PrepareAppend(in header, out var sessionIndex, out var rawIndex);
            var payloadIndex = segment.AddEntityVital(in observation);
            CommitAppend(segment, in header, sessionIndex, rawIndex, payloadIndex, ObservedEventDomain.EntityVital);
        }
    }

    public void Append(in ObservedEventHeader header, in AuraObservation observation)
    {
        lock (_gate)
        {
            var segment = PrepareAppend(in header, out var sessionIndex, out var rawIndex);
            var payloadIndex = segment.AddAura(in observation);
            CommitAppend(segment, in header, sessionIndex, rawIndex, payloadIndex, ObservedEventDomain.Aura);
        }
    }

    public void Append(in ObservedEventHeader header, in SceneObservation observation)
    {
        lock (_gate)
        {
            var segment = PrepareAppend(in header, out var sessionIndex, out var rawIndex);
            var payloadIndex = segment.AddScene(in observation);
            CommitAppend(segment, in header, sessionIndex, rawIndex, payloadIndex, ObservedEventDomain.Scene);
        }
    }

    public void AppendDiagnostic(in ObservedEventHeader header)
    {
        lock (_gate)
        {
            var segment = PrepareAppend(in header, out var sessionIndex, out var rawIndex);
            CommitAppend(segment, in header, sessionIndex, rawIndex, -1, ObservedEventDomain.Diagnostic);
        }
    }

    public void EnsureCapacity(int capacity)
    {
        if (capacity <= 0)
            return;

        lock (_gate)
            _segments.EnsureCapacity(ResolveSegmentCapacity(capacity));
    }

    public void CompleteFlush(long flushId)
    {
        lock (_gate)
        {
            if (flushId <= _lastCompletedFlushId)
                throw new ArgumentException($"FlushId must be > {_lastCompletedFlushId}, got {flushId}.");
            _lastCompletedFlushId = flushId;
        }
    }

#pragma warning disable CA1822
    public JournalCursor CreateCursor(long startOrdinal) => new(startOrdinal);
#pragma warning restore CA1822

    public void ReadEntry(long observationOrdinal, JournalEntryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        lock (_gate)
        {
            if (!TryResolvePosition(observationOrdinal, out var segmentIndex, out var entryIndex))
                throw new ArgumentOutOfRangeException(nameof(observationOrdinal));

            reader(_segments[segmentIndex].GetEntry(this, entryIndex));
        }
    }

    public bool TryReadEntry(long observationOrdinal, JournalEntryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        lock (_gate)
        {
            if (!TryResolvePosition(observationOrdinal, out var segmentIndex, out var entryIndex))
                return false;

            reader(_segments[segmentIndex].GetEntry(this, entryIndex));
            return true;
        }
    }

    public JournalReadResult ReadEntries(JournalCursor cursor, int maxCount, JournalEntriesReader reader)
        => ReadEntries(cursor, long.MaxValue, maxCount, reader);

    public JournalReadResult ReadEntries(JournalCursor cursor, long endObservationOrdinalExclusive, int maxCount, JournalEntriesReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        lock (_gate)
        {
            if (_count == 0 || maxCount <= 0)
                return new JournalReadResult(0, cursor);

            var startOrdinal = Math.Max(cursor.NextObservationOrdinal, _firstObservationOrdinal);
            var endOrdinal = Math.Min(endObservationOrdinalExclusive, _nextObservationOrdinal);
            if (startOrdinal >= endOrdinal || !TryResolvePosition(startOrdinal, out var segmentIndex, out var entryIndex))
                return new JournalReadResult(0, cursor);

            var segment = _segments[segmentIndex];
            var count = (int)Math.Min(Math.Min((long)maxCount, endOrdinal - startOrdinal), segment.Count - entryIndex);
            if (count <= 0)
                return new JournalReadResult(0, cursor);

            reader(new JournalEntryBatch(this, segment, entryIndex, count));
            return new JournalReadResult(count, new JournalCursor(startOrdinal + count));
        }
    }

    internal ref readonly RawPacketReference GetRawReference(int index) => ref CollectionsMarshal.AsSpan(_rawReferences)[index];

    internal Guid GetSceneSessionId(int index) => _sceneSessionIds[index];

    private ObservedEventStorageSegment PrepareAppend(in ObservedEventHeader header, out int sessionIndex, out int rawIndex)
    {
        if (header.Stamp.ObservationOrdinal != _nextObservationOrdinal)
            throw new ArgumentException($"ObservationOrdinal must be {_nextObservationOrdinal}, got {header.Stamp.ObservationOrdinal}.", nameof(header));

        if (_segments.Count == 0 || _segments[^1].Count == SegmentCapacity)
            _segments.Add(new ObservedEventStorageSegment(_nextObservationOrdinal));

        sessionIndex = InternSceneSession(header.SceneSessionId);
        rawIndex = InternRawReference(header.Raw);
        return _segments[^1];
    }

    private void CommitAppend(
        ObservedEventStorageSegment segment,
        in ObservedEventHeader header,
        int sessionIndex,
        int rawIndex,
        int payloadIndex,
        ObservedEventDomain domain)
    {
        segment.AddHeader(new StoredObservedEventHeader(
            header.Stamp.OffsetTicks,
            header.Stamp.FlushId,
            sessionIndex,
            header.SourceEntityId,
            header.TargetEntityId,
            rawIndex,
            payloadIndex,
            domain));
        if (_count == 0)
            _firstObservationOrdinal = header.Stamp.ObservationOrdinal;
        _count++;
        _nextObservationOrdinal++;
    }

    private int InternRawReference(RawPacketReference raw)
    {
        if (_rawReferenceIndices.TryGetValue(raw, out var index))
            return index;

        index = _rawReferences.Count;
        _rawReferences.Add(raw);
        _rawReferenceIndices.Add(raw, index);
        return index;
    }

    private int InternSceneSession(Guid sceneSessionId)
    {
        if (_sceneSessionIndices.TryGetValue(sceneSessionId, out var index))
            return index;

        index = _sceneSessionIds.Count;
        _sceneSessionIds.Add(sceneSessionId);
        _sceneSessionIndices.Add(sceneSessionId, index);
        return index;
    }

    private bool TryResolvePosition(long observationOrdinal, out int segmentIndex, out int entryIndex)
    {
        var offset = observationOrdinal - _firstObservationOrdinal;
        if ((ulong)offset >= (uint)_count)
        {
            segmentIndex = 0;
            entryIndex = 0;
            return false;
        }

        segmentIndex = (int)(offset / SegmentCapacity);
        entryIndex = (int)(offset % SegmentCapacity);
        return true;
    }

    private static int ResolveSegmentCapacity(int entryCapacity)
        => Math.Max(1, (entryCapacity + SegmentCapacity - 1) / SegmentCapacity);
}

public readonly ref struct ObservedEventEntry
{
    private readonly ObservedEventJournal _journal;
    private readonly ObservedEventStorageSegment _segment;
    private readonly StoredObservedEventHeader _header;
    private readonly long _observationOrdinal;

    internal ObservedEventEntry(
        ObservedEventJournal journal,
        ObservedEventStorageSegment segment,
        in StoredObservedEventHeader header,
        long observationOrdinal)
    {
        _journal = journal;
        _segment = segment;
        _header = header;
        _observationOrdinal = observationOrdinal;
    }

    public Guid SceneSessionId => _journal.GetSceneSessionId(_header.SceneSessionIndex);
    public TimelineStamp Stamp => new(_header.OffsetTicks, _observationOrdinal, _header.FlushId);
    public ObservedEventDomain Domain => _header.Domain;
    public int SourceEntityId => _header.SourceEntityId;
    public int TargetEntityId => _header.TargetEntityId;
    public long ObservedAtMilliseconds => _header.OffsetTicks / TimeSpan.TicksPerMillisecond;
    public ref readonly RawPacketReference Raw => ref _journal.GetRawReference(_header.RawReferenceIndex);

    public ref readonly CombatWireObservation Combat
    {
        get
        {
            EnsureDomain(ObservedEventDomain.Combat);
            return ref _segment.GetCombat(_header.PayloadIndex);
        }
    }

    public ref readonly ActionObservation Action
    {
        get
        {
            EnsureDomain(ObservedEventDomain.Action);
            return ref _segment.GetAction(_header.PayloadIndex);
        }
    }

    public ref readonly StateObservation State
    {
        get
        {
            EnsureDomain(ObservedEventDomain.State);
            return ref _segment.GetState(_header.PayloadIndex);
        }
    }

    public ref readonly EntityVitalObservation EntityVital
    {
        get
        {
            EnsureDomain(ObservedEventDomain.EntityVital);
            return ref _segment.GetEntityVital(_header.PayloadIndex);
        }
    }

    public ref readonly AuraObservation Aura
    {
        get
        {
            EnsureDomain(ObservedEventDomain.Aura);
            return ref _segment.GetAura(_header.PayloadIndex);
        }
    }

    public ref readonly SceneObservation Scene
    {
        get
        {
            EnsureDomain(ObservedEventDomain.Scene);
            return ref _segment.GetScene(_header.PayloadIndex);
        }
    }

    private void EnsureDomain(ObservedEventDomain expected)
    {
        if (_header.Domain != expected)
            throw new InvalidOperationException($"Journal entry domain is {_header.Domain}, not {expected}.");
    }
}

public readonly ref struct JournalEntryBatch
{
    private readonly ObservedEventJournal _journal;
    private readonly ObservedEventStorageSegment _segment;
    private readonly int _start;

    internal JournalEntryBatch(ObservedEventJournal journal, ObservedEventStorageSegment segment, int start, int count)
    {
        _journal = journal;
        _segment = segment;
        _start = start;
        Count = count;
    }

    public int Count { get; }

    public ObservedEventEntry this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _segment.GetEntry(_journal, _start + index);
        }
    }

    public Enumerator GetEnumerator() => new(this);

    public ref struct Enumerator
    {
        private readonly JournalEntryBatch _entries;
        private int _index = -1;

        internal Enumerator(JournalEntryBatch entries)
        {
            _entries = entries;
        }

        public readonly ObservedEventEntry Current => _entries[_index];

        public bool MoveNext() => ++_index < _entries.Count;
    }
}

internal sealed class ObservedEventStorageSegment(long firstObservationOrdinal)
{
    private readonly StoredObservedEventHeader[] _headers = new StoredObservedEventHeader[ObservedEventJournal.SegmentCapacity];
    private PayloadBuffer<CombatWireObservation> _combat;
    private PayloadBuffer<ActionObservation> _actions;
    private PayloadBuffer<StateObservation> _states;
    private PayloadBuffer<EntityVitalObservation> _entityVitals;
    private PayloadBuffer<AuraObservation> _auras;
    private PayloadBuffer<SceneObservation> _scenes;

    public int Count { get; private set; }

    public void AddHeader(in StoredObservedEventHeader header) => _headers[Count++] = header;
    public int AddCombat(in CombatWireObservation value) => _combat.Add(in value);
    public int AddAction(in ActionObservation value) => _actions.Add(in value);
    public int AddState(in StateObservation value) => _states.Add(in value);
    public int AddEntityVital(in EntityVitalObservation value) => _entityVitals.Add(in value);
    public int AddAura(in AuraObservation value) => _auras.Add(in value);
    public int AddScene(in SceneObservation value) => _scenes.Add(in value);
    public ref readonly CombatWireObservation GetCombat(int index) => ref _combat[index];
    public ref readonly ActionObservation GetAction(int index) => ref _actions[index];
    public ref readonly StateObservation GetState(int index) => ref _states[index];
    public ref readonly EntityVitalObservation GetEntityVital(int index) => ref _entityVitals[index];
    public ref readonly AuraObservation GetAura(int index) => ref _auras[index];
    public ref readonly SceneObservation GetScene(int index) => ref _scenes[index];

    public ObservedEventEntry GetEntry(ObservedEventJournal journal, int index)
        => new(journal, this, in _headers[index], firstObservationOrdinal + index);
}

internal readonly record struct StoredObservedEventHeader(
    long OffsetTicks,
    long FlushId,
    int SceneSessionIndex,
    int SourceEntityId,
    int TargetEntityId,
    int RawReferenceIndex,
    int PayloadIndex,
    ObservedEventDomain Domain);

internal struct PayloadBuffer<T>
{
    private T[]? _items;
    private int _count;

    public int Add(in T value)
    {
        var items = _items;
        if (items is null)
        {
            items = new T[8];
            _items = items;
        }
        else if (_count == items.Length)
        {
            Array.Resize(ref items, items.Length << 1);
            _items = items;
        }

        var index = _count++;
        items[index] = value;
        return index;
    }

    public readonly ref readonly T this[int index] => ref _items![index];
}

public delegate void JournalEntryReader(ObservedEventEntry entry);

public delegate void JournalEntriesReader(JournalEntryBatch entries);

public readonly record struct JournalCursor(long NextObservationOrdinal);

public readonly record struct JournalReadResult(int Count, JournalCursor Cursor);
