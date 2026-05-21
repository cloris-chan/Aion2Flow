using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class CompactAvoidanceCanonicalizer
{
    private const int MaxPendingAvoidances = 32;

    internal readonly record struct PendingCompactAvoidance(int SourceId, int TargetId, int OriginalSkillCode, int Marker, TimelineStamp Stamp, long ObservedAtMilliseconds);

    private readonly List<PendingCompactAvoidance> _pendingCompact = new(MaxPendingAvoidances);
    private long _currentBatchOrdinal;

    public StampedCombatCanonicalizationBatch NormalizeCombat(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds = 0)
    {
        var prefix = EnsureBatch(stamp.BatchOrdinal);
        var result = new StampedCombatCanonicalizationResult(sourceId, targetId, stamp, observedAtMilliseconds, observation);
        return Append(prefix, result);
    }

    public StampedCombatCanonicalizationBatch ObserveCompactValue0438(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds = 0)
    {
        var isCompactSignal = IsCompactSignalShape(in observation) &&
                              observation.EventKind == CombatEventKind.Unknown &&
                              observation.ValueKind == CombatValueKind.Unknown;
        if (!isCompactSignal)
            return NormalizeCombat(sourceId, targetId, in stamp, in observation, observedAtMilliseconds);

        if (IsCompactType2Sidecar(in observation))
        {
            CancelPendingCompactEvade(targetId, observation.SkillCode);
            return EnsureBatch(stamp.BatchOrdinal);
        }

        var prefix = EnsureBatch(stamp.BatchOrdinal);
        if (TryObserveCompactAvoidance(sourceId, targetId, in stamp, in observation, observedAtMilliseconds))
            return prefix;

        return prefix;
    }

    public StampedCombatCanonicalizationBatch AdvanceBatch(in TimelineStamp stamp) => EnsureBatch(stamp.BatchOrdinal);

    public StampedCombatCanonicalizationBatch CompleteBatch(long batchOrdinal)
    {
        if (_currentBatchOrdinal == 0)
            return StampedCombatCanonicalizationBatch.Empty;

        if (batchOrdinal > 0 && _currentBatchOrdinal > 0 && batchOrdinal < _currentBatchOrdinal)
            return StampedCombatCanonicalizationBatch.Empty;

        return FinalizeBatch();
    }

    private bool TryObserveCompactAvoidance(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds)
    {
        if (!IsCompactEvadeSignal(sourceId, targetId, in observation) || observation.Marker <= 0)
            return false;

        var trackedSkillCode = ResolveTrackedSkillCode(observation.SkillCode);
        if (trackedSkillCode <= 0)
            return false;

        _pendingCompact.Add(new PendingCompactAvoidance(sourceId, targetId, observation.SkillCode, observation.Marker, stamp, observedAtMilliseconds));
        TrimPending();
        return true;
    }

    private StampedCombatCanonicalizationBatch FinalizeBatch()
    {
        var results = new StampedCombatCanonicalizationBatchBuilder(_pendingCompact.Count);
        foreach (var pending in _pendingCompact)
            results.Add(new StampedCombatCanonicalizationResult(pending.SourceId, pending.TargetId, pending.Stamp, pending.ObservedAtMilliseconds, CreateCompactEvade(in pending)));

        _pendingCompact.Clear();
        _currentBatchOrdinal = 0;
        return results.ToBatch();
    }

    private StampedCombatCanonicalizationBatch EnsureBatch(long batchOrdinal)
    {
        var resolvedBatchOrdinal = batchOrdinal > 0 ? batchOrdinal : 0;
        if (_currentBatchOrdinal == 0)
        {
            _currentBatchOrdinal = resolvedBatchOrdinal;
            return StampedCombatCanonicalizationBatch.Empty;
        }

        if (resolvedBatchOrdinal == 0 || resolvedBatchOrdinal == _currentBatchOrdinal)
            return StampedCombatCanonicalizationBatch.Empty;

        var results = FinalizeBatch();
        _currentBatchOrdinal = resolvedBatchOrdinal;
        return results;
    }

    private static StampedCombatCanonicalizationBatch Append(StampedCombatCanonicalizationBatch prefix, in StampedCombatCanonicalizationResult result)
    {
        if (prefix.Count == 0)
            return StampedCombatCanonicalizationBatch.One(result);

        var results = new StampedCombatCanonicalizationBatchBuilder(prefix.Count + 1);
        results.AddRange(prefix);
        results.Add(result);
        return results.ToBatch();
    }

    private void CancelPendingCompactEvade(int targetId, int skillCode)
    {
        for (var i = _pendingCompact.Count - 1; i >= 0; i--)
        {
            var pending = _pendingCompact[i];
            if (pending.TargetId == targetId && pending.OriginalSkillCode == skillCode)
                _pendingCompact.RemoveAt(i);
        }
    }

    private void TrimPending()
    {
        while (_pendingCompact.Count > MaxPendingAvoidances)
            _pendingCompact.RemoveAt(0);
    }

    private static bool IsCompactEvadeSignal(int sourceId, int targetId, in CombatObservation observation) =>
        IsCompactSignalShape(in observation) &&
        targetId > 0 &&
        sourceId > 0 &&
        targetId != sourceId &&
        observation.Type == 1 &&
        observation.LayoutTag is 0 or 2;

    private static bool IsCompactSignalShape(in CombatObservation observation) => observation.HitCount == 0 && observation.AttemptCount == 0;

    private static bool IsCompactType2Sidecar(in CombatObservation observation) => IsCompactSignalShape(in observation) && observation.Type == 2;

    private static CombatObservation CreateCompactEvade(in PendingCompactAvoidance pending)
    {
        var observation = new CombatObservation
        {
            SkillCode = pending.OriginalSkillCode,
            OriginalSkillCode = pending.OriginalSkillCode,
            Damage = 0,
            HitCount = 0,
            AttemptCount = 1,
            Marker = pending.Marker,
            Modifiers = DamageModifiers.Evade,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage,
            EffectTag = PacketEffectTag.CompactEvade
        };
        return CombatResourceRegistry.NormalizeObservationForStorage(pending.SourceId, pending.TargetId, in observation);
    }

    private static int ResolveTrackedSkillCode(int skillCode)
    {
        if (skillCode <= 0)
            return 0;

        var variant = CombatResourceRegistry.ParseSkillVariant(skillCode);
        return CombatResourceRegistry.InferOriginalSkillCode(skillCode) ?? variant.NormalizedSkillCode;
    }
}

public readonly record struct StampedCombatCanonicalizationResult(int SourceId, int TargetId, TimelineStamp Stamp, long ObservedAtMilliseconds, CombatObservation Observation);

public readonly struct StampedCombatCanonicalizationBatch
{
    private readonly StampedCombatCanonicalizationResult _first;
    private readonly StampedCombatCanonicalizationResult _second;
    private readonly StampedCombatCanonicalizationResult[]? _overflow;

    internal StampedCombatCanonicalizationBatch(int count, in StampedCombatCanonicalizationResult first, in StampedCombatCanonicalizationResult second, StampedCombatCanonicalizationResult[]? overflow)
    {
        Count = count;
        _first = first;
        _second = second;
        _overflow = overflow;
    }

    public static StampedCombatCanonicalizationBatch Empty => default;

    public int Count { get; }

    public static StampedCombatCanonicalizationBatch One(in StampedCombatCanonicalizationResult result) =>
        new(1, in result, default, null);

    public static StampedCombatCanonicalizationBatch Two(in StampedCombatCanonicalizationResult first, in StampedCombatCanonicalizationResult second) => new(2, in first, in second, null);

    public StampedCombatCanonicalizationResult this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (_overflow is not null)
                return _overflow[index];

            return index == 0 ? _first : _second;
        }
    }

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator
    {
        private readonly StampedCombatCanonicalizationBatch _batch;
        private int _index;

        internal Enumerator(StampedCombatCanonicalizationBatch batch)
        {
            _batch = batch;
            _index = -1;
            Current = default;
        }

        public StampedCombatCanonicalizationResult Current { get; private set; }

        public bool MoveNext()
        {
            var next = _index + 1;
            if ((uint)next >= (uint)_batch.Count)
                return false;

            _index = next;
            Current = _batch[next];
            return true;
        }
    }
}

internal struct StampedCombatCanonicalizationBatchBuilder(int capacity = 0)
{
    private StampedCombatCanonicalizationResult _first = default;
    private StampedCombatCanonicalizationResult _second = default;
    private StampedCombatCanonicalizationResult[]? _overflow = null;
    private readonly int _initialCapacity = capacity;
    private int _count = 0;

    public void Add(in StampedCombatCanonicalizationResult result)
    {
        if (_count == 0)
        {
            _first = result;
            _count = 1;
            return;
        }

        if (_count == 1)
        {
            _second = result;
            _count = 2;
            return;
        }

        var overflow = EnsureOverflow();
        overflow[_count++] = result;
    }

    public void AddRange(StampedCombatCanonicalizationBatch batch)
    {
        foreach (var result in batch)
            Add(in result);
    }

    public readonly StampedCombatCanonicalizationBatch ToBatch()
    {
        if (_count == 0)
            return StampedCombatCanonicalizationBatch.Empty;

        if (_overflow is { } overflow)
            return new StampedCombatCanonicalizationBatch(_count, default, default, overflow);

        return _count == 1
            ? StampedCombatCanonicalizationBatch.One(_first)
            : StampedCombatCanonicalizationBatch.Two(_first, _second);
    }

    private StampedCombatCanonicalizationResult[] EnsureOverflow()
    {
        if (_overflow is null)
        {
            var capacity = Math.Max(Math.Max(_initialCapacity, 4), _count + 1);
            _overflow = new StampedCombatCanonicalizationResult[capacity];
            _overflow[0] = _first;
            _overflow[1] = _second;
            return _overflow;
        }

        if (_count < _overflow.Length)
            return _overflow;

        Array.Resize(ref _overflow, checked(_overflow.Length * 2));
        return _overflow;
    }
}
