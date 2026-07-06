using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public sealed class SceneCombatSnapshot
{
    private static readonly CombatantSnapshotEntry[] EmptyCombatants = [];
    private static readonly SceneBossFocusSnapshot[] EmptyBossFocuses = [];
    private static readonly int[] EmptyBossNpcCodes = [];

    public static SceneCombatSnapshot Empty { get; } = new(
        encounterId: Guid.Empty,
        kind: SceneKind.Standard,
        readModelRevision: 0,
        sceneTransitionRevision: 0,
        mapId: 0,
        mapInstanceId: 0,
        encounterStartTime: 0,
        encounterEndTime: 0,
        encounterTime: 0,
        combatants: EmptyCombatants,
        targetObservation: null,
        encounter: EncounterSummarySnapshot.Empty,
        bossFocuses: EmptyBossFocuses,
        bossNpcCodes: EmptyBossNpcCodes);

    public SceneCombatSnapshot()
        : this(
            encounterId: Guid.NewGuid(),
            kind: SceneKind.Standard,
            readModelRevision: 0,
            sceneTransitionRevision: 0,
            mapId: 0,
            mapInstanceId: 0,
            encounterStartTime: 0,
            encounterEndTime: 0,
            encounterTime: 0,
            combatants: EmptyCombatants,
            targetObservation: null,
            encounter: EncounterSummarySnapshot.Empty,
            bossFocuses: EmptyBossFocuses,
            bossNpcCodes: EmptyBossNpcCodes)
    {
    }

    internal SceneCombatSnapshot(
        Guid encounterId,
        SceneKind kind,
        long readModelRevision,
        long sceneTransitionRevision,
        uint mapId,
        uint mapInstanceId,
        long encounterStartTime,
        long encounterEndTime,
        long encounterTime,
        CombatantSnapshotEntry[] combatants,
        NpcRuntimeObservationSnapshot? targetObservation,
        EncounterSummarySnapshot encounter,
        SceneBossFocusSnapshot[] bossFocuses,
        int[] bossNpcCodes)
    {
        EncounterId = encounterId;
        Kind = kind;
        ReadModelRevision = readModelRevision;
        SceneTransitionRevision = sceneTransitionRevision;
        MapId = mapId;
        MapInstanceId = mapInstanceId;
        EncounterStartTime = encounterStartTime;
        EncounterEndTime = encounterEndTime;
        EncounterTime = encounterTime;
        Combatants = new CombatantSnapshotMap(combatants);
        TargetObservation = targetObservation;
        Encounter = encounter;
        BossFocuses = new SnapshotList<SceneBossFocusSnapshot>(bossFocuses);
        BossNpcCodes = new SnapshotList<int>(bossNpcCodes);
    }

    public Guid EncounterId { get; }

    public SceneKind Kind { get; }

    public long ReadModelRevision { get; }

    public long SceneTransitionRevision { get; }

    public uint MapId { get; }

    public uint MapInstanceId { get; }

    public long EncounterStartTime { get; }

    public long EncounterEndTime { get; }

    public long EncounterTime { get; }

    public CombatantSnapshotMap Combatants { get; }

    public NpcRuntimeObservationSnapshot? TargetObservation { get; }

    public EncounterSummarySnapshot Encounter { get; }

    public SnapshotList<SceneBossFocusSnapshot> BossFocuses { get; }

    public SnapshotList<int> BossNpcCodes { get; }

    public SceneCombatSnapshot DeepClone()
    {
        return this;
    }
}

public readonly record struct CombatantSnapshotEntry(int Id, SceneCombatantMetrics Metrics)
{
    public void Deconstruct(out int id, out SceneCombatantMetrics metrics)
    {
        id = Id;
        metrics = Metrics;
    }
}

public readonly struct CombatantSnapshotMap : IReadOnlyDictionary<int, SceneCombatantMetrics>
{
    private readonly CombatantSnapshotEntry[]? _entries;

    internal CombatantSnapshotMap(CombatantSnapshotEntry[] entries)
    {
        _entries = entries;
    }

    public int Count => Entries.Length;

    public KeyCollection Keys => new(_entries);

    public ValueCollection Values => new(_entries);

    IEnumerable<int> IReadOnlyDictionary<int, SceneCombatantMetrics>.Keys => Keys;

    IEnumerable<SceneCombatantMetrics> IReadOnlyDictionary<int, SceneCombatantMetrics>.Values => Values;

    private ReadOnlySpan<CombatantSnapshotEntry> Entries => _entries ?? [];

    public SceneCombatantMetrics this[int key]
    {
        get
        {
            if (TryGetValue(key, out var value))
            {
                return value;
            }

            throw new KeyNotFoundException($"The combatant id '{key}' was not found in the snapshot.");
        }
    }

    public bool ContainsKey(int key)
    {
        return FindIndex(key) >= 0;
    }

    public bool Contains(int key)
    {
        return ContainsKey(key);
    }

    public bool TryGetValue(int key, [MaybeNullWhen(false)] out SceneCombatantMetrics value)
    {
        var index = FindIndex(key);
        if (index >= 0)
        {
            value = Entries[index].Metrics;
            return true;
        }

        value = default;
        return false;
    }

    public ReadOnlySpan<CombatantSnapshotEntry> AsSpan()
    {
        return Entries;
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(Entries);
    }

    IEnumerator<KeyValuePair<int, SceneCombatantMetrics>> IEnumerable<KeyValuePair<int, SceneCombatantMetrics>>.GetEnumerator()
    {
        var entries = _entries ?? [];
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            yield return new KeyValuePair<int, SceneCombatantMetrics>(entry.Id, entry.Metrics);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<KeyValuePair<int, SceneCombatantMetrics>>)this).GetEnumerator();
    }

    private int FindIndex(int key)
    {
        var span = Entries;
        var low = 0;
        var high = span.Length - 1;

        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var midKey = span[mid].Id;
            if (midKey == key)
            {
                return mid;
            }

            if (midKey < key)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return -1;
    }

    public ref struct Enumerator
    {
        private readonly ReadOnlySpan<CombatantSnapshotEntry> _entries;
        private int _index;

        internal Enumerator(ReadOnlySpan<CombatantSnapshotEntry> entries)
        {
            _entries = entries;
            _index = -1;
            Current = default;
        }

        public CombatantSnapshotEntry Current { get; private set; }

        public bool MoveNext()
        {
            var next = _index + 1;
            if ((uint)next >= (uint)_entries.Length)
            {
                return false;
            }

            _index = next;
            Current = _entries[next];
            return true;
        }
    }

    public readonly struct KeyCollection : IReadOnlyCollection<int>
    {
        private readonly CombatantSnapshotEntry[]? _entries;

        internal KeyCollection(CombatantSnapshotEntry[]? entries)
        {
            _entries = entries;
        }

        public int Count => Entries.Length;

        private ReadOnlySpan<CombatantSnapshotEntry> Entries => _entries ?? [];

        public Enumerator GetEnumerator()
        {
            return new Enumerator(Entries);
        }

        IEnumerator<int> IEnumerable<int>.GetEnumerator()
        {
            var entries = _entries ?? [];
            for (var i = 0; i < entries.Length; i++)
            {
                yield return entries[i].Id;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<int>)this).GetEnumerator();
        }

        public ref struct Enumerator
        {
            private readonly ReadOnlySpan<CombatantSnapshotEntry> _entries;
            private int _index;

            internal Enumerator(ReadOnlySpan<CombatantSnapshotEntry> entries)
            {
                _entries = entries;
                _index = -1;
                Current = default;
            }

            public int Current { get; private set; }

            public bool MoveNext()
            {
                var next = _index + 1;
                if ((uint)next >= (uint)_entries.Length)
                {
                    return false;
                }

                _index = next;
                Current = _entries[next].Id;
                return true;
            }
        }
    }

    public readonly struct ValueCollection : IReadOnlyCollection<SceneCombatantMetrics>
    {
        private readonly CombatantSnapshotEntry[]? _entries;

        internal ValueCollection(CombatantSnapshotEntry[]? entries)
        {
            _entries = entries;
        }

        public int Count => Entries.Length;

        private ReadOnlySpan<CombatantSnapshotEntry> Entries => _entries ?? [];

        public Enumerator GetEnumerator()
        {
            return new Enumerator(Entries);
        }

        IEnumerator<SceneCombatantMetrics> IEnumerable<SceneCombatantMetrics>.GetEnumerator()
        {
            var entries = _entries ?? [];
            for (var i = 0; i < entries.Length; i++)
            {
                yield return entries[i].Metrics;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<SceneCombatantMetrics>)this).GetEnumerator();
        }

        public ref struct Enumerator
        {
            private readonly ReadOnlySpan<CombatantSnapshotEntry> _entries;
            private int _index;

            internal Enumerator(ReadOnlySpan<CombatantSnapshotEntry> entries)
            {
                _entries = entries;
                _index = -1;
                Current = default;
            }

            public SceneCombatantMetrics Current { get; private set; }

            public bool MoveNext()
            {
                var next = _index + 1;
                if ((uint)next >= (uint)_entries.Length)
                {
                    return false;
                }

                _index = next;
                Current = _entries[next].Metrics;
                return true;
            }
        }
    }
}

public readonly struct SnapshotList<T> : IReadOnlyList<T>
{
    private readonly T[]? _items;

    internal SnapshotList(T[] items)
    {
        _items = items;
    }

    public int Count => Items.Length;

    private ReadOnlySpan<T> Items => _items ?? [];

    public T this[int index] => Items[index];

    public ReadOnlySpan<T> AsSpan()
    {
        return Items;
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(Items);
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        var items = _items ?? [];
        for (var i = 0; i < items.Length; i++)
        {
            yield return items[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<T>)this).GetEnumerator();
    }

    public ref struct Enumerator
    {
        private readonly ReadOnlySpan<T> _items;
        private int _index;

        internal Enumerator(ReadOnlySpan<T> items)
        {
            _items = items;
            _index = -1;
            Current = default!;
        }

        public T Current { get; private set; }

        public bool MoveNext()
        {
            var next = _index + 1;
            if ((uint)next >= (uint)_items.Length)
            {
                return false;
            }

            _index = next;
            Current = _items[next];
            return true;
        }
    }
}

public readonly record struct EncounterSummarySnapshot(
    int TrackingTargetId,
    NpcRuntimePhaseHint PhaseHint,
    bool IsActive,
    bool ShouldArchive,
    string Reason)
{
    public static EncounterSummarySnapshot Empty { get; } = new(0, NpcRuntimePhaseHint.Unknown, false, false, string.Empty);
}

public readonly record struct NpcRuntimeObservationSnapshot(
    int InstanceId,
    long? Value2136,
    long? Sequence2136,
    long? Value0140,
    long? Value0240,
    byte? State4636Value0,
    byte? State4636Value1,
    int? Sequence2C38,
    int? Result2C38,
    long? Hp,
    bool? BattleToggledOn,
    NpcRuntimePhaseHint PhaseHint);

public readonly record struct SceneBossFocusSnapshot
{
    public int InstanceId { get; init; }

    public NpcKind Kind { get; init; }

    public long Hp { get; init; }

    public long MaxHp { get; init; }

    public long CumulativeLostHp { get; init; }

    public long EffectiveHp
    {
        get
        {
            if (!HasMaxHp)
                return 0;

            var maxHp = Math.Max(1L, MaxHp);
            return HasHp ? Math.Max(maxHp, Math.Max(0, Hp) + CumulativeLostHp) : maxHp;
        }
    }

    public long LastObservedAtMilliseconds { get; init; }

    public bool HasHp { get; init; }

    public bool HasMaxHp { get; init; }
}
