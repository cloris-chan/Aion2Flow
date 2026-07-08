using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class CompactAvoidanceCanonicalizer
{
    private const int MaxPendingAvoidances = 32;

    private readonly record struct CompactAvoidanceKey(int SourceId, int TargetId, int BodySkillVariantRaw, int Marker);
    internal readonly record struct PendingCompactAvoidance(int SourceId, int TargetId, int BodySkillVariantRaw, int Marker, TimelineStamp Stamp, long ObservedAtMilliseconds, RawPacketReference Raw);

    private readonly List<PendingCompactAvoidance> _pendingCompact = new(MaxPendingAvoidances);
    private CompactAvoidanceKey _lastCompactAvoidanceKey;
    private int _lastCompactAvoidanceLayoutTag;
    private bool _hasLastCompactAvoidanceKey;

    public StampedCombatCanonicalizationBatch NormalizeCombat(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds, RawPacketReference raw)
    {
        var result = new StampedCombatCanonicalizationResult(sourceId, targetId, stamp, observedAtMilliseconds, raw, observation);
        return StampedCombatCanonicalizationBatch.One(result);
    }

    public StampedCombatCanonicalizationBatch ObserveCompactValue0438(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds, RawPacketReference raw)
    {
        var isCompactSignal = IsCompactSignalShape(in observation) &&
                              observation.EventKind == CombatEventKind.Unknown &&
                              observation.ValueKind == CombatValueKind.Unknown;
        if (!isCompactSignal)
        {
            ClearCompactAvoidanceRun();
            return NormalizeCombat(sourceId, targetId, in stamp, in observation, observedAtMilliseconds, raw);
        }

        if (IsCompactType2Sidecar(in observation))
        {
            CancelPendingCompactEvade(sourceId, targetId, observation.BodySkillVariantRaw, observation.Marker);
            ClearCompactAvoidanceRun();
            return StampedCombatCanonicalizationBatch.Empty;
        }

        if (TryObserveCompactAvoidance(sourceId, targetId, in stamp, in observation, observedAtMilliseconds, raw))
            return StampedCombatCanonicalizationBatch.Empty;

        ClearCompactAvoidanceRun();
        return StampedCombatCanonicalizationBatch.Empty;
    }

    public StampedCombatCanonicalizationBatch FlushPending()
    {
        if (_pendingCompact.Count == 0)
            return StampedCombatCanonicalizationBatch.Empty;

        return FlushPendingCompact();
    }

    internal CompactAvoidanceCanonicalizerSnapshot CreateSnapshot() => new([.. _pendingCompact], _hasLastCompactAvoidanceKey, _lastCompactAvoidanceKey.SourceId, _lastCompactAvoidanceKey.TargetId, _lastCompactAvoidanceKey.BodySkillVariantRaw, _lastCompactAvoidanceKey.Marker, _lastCompactAvoidanceLayoutTag);

    internal static CompactAvoidanceCanonicalizer FromSnapshot(CompactAvoidanceCanonicalizerSnapshot snapshot)
    {
        var canonicalizer = new CompactAvoidanceCanonicalizer
        {
            _hasLastCompactAvoidanceKey = snapshot.HasLastCompactAvoidanceKey,
            _lastCompactAvoidanceKey = new CompactAvoidanceKey(snapshot.LastSourceId, snapshot.LastTargetId, snapshot.LastBodySkillVariantRaw, snapshot.LastMarker),
            _lastCompactAvoidanceLayoutTag = snapshot.LastLayoutTag
        };
        canonicalizer._pendingCompact.AddRange(snapshot.Pending);
        return canonicalizer;
    }

    private bool TryObserveCompactAvoidance(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds, RawPacketReference raw)
    {
        if (!IsCompactEvadeSignal(sourceId, targetId, in observation) || observation.Marker <= 0)
            return false;

        var key = new CompactAvoidanceKey(sourceId, targetId, observation.BodySkillVariantRaw, observation.Marker);
        if (_hasLastCompactAvoidanceKey &&
            _lastCompactAvoidanceKey == key &&
            IsDuplicateCompactAvoidanceSignal(_lastCompactAvoidanceLayoutTag, observation.LayoutTag))
        {
            _lastCompactAvoidanceLayoutTag = observation.LayoutTag;
            return true;
        }

        _pendingCompact.Add(new PendingCompactAvoidance(sourceId, targetId, observation.BodySkillVariantRaw, observation.Marker, stamp, observedAtMilliseconds, raw));
        _lastCompactAvoidanceKey = key;
        _lastCompactAvoidanceLayoutTag = observation.LayoutTag;
        _hasLastCompactAvoidanceKey = true;
        TrimPending();
        return true;
    }

    private StampedCombatCanonicalizationBatch FlushPendingCompact()
    {
        var results = new StampedCombatCanonicalizationBatchBuilder(_pendingCompact.Count);
        foreach (var pending in _pendingCompact)
            results.Add(new StampedCombatCanonicalizationResult(pending.SourceId, pending.TargetId, pending.Stamp, pending.ObservedAtMilliseconds, pending.Raw, CreateCompactEvade(in pending), CombatContributionCanonicalization.CompactAvoidance));

        _pendingCompact.Clear();
        ClearCompactAvoidanceRun();
        return results.ToBatch();
    }

    private void CancelPendingCompactEvade(int sourceId, int targetId, int bodySkillVariantRaw, int marker)
    {
        for (var i = _pendingCompact.Count - 1; i >= 0; i--)
        {
            var pending = _pendingCompact[i];
            if (pending.SourceId == sourceId &&
                pending.TargetId == targetId &&
                pending.Marker == marker &&
                (bodySkillVariantRaw <= 0 || pending.BodySkillVariantRaw == bodySkillVariantRaw))
            {
                _pendingCompact.RemoveAt(i);
            }
        }
    }

    private void TrimPending()
    {
        while (_pendingCompact.Count > MaxPendingAvoidances)
            _pendingCompact.RemoveAt(0);
    }

    private void ClearCompactAvoidanceRun()
    {
        _lastCompactAvoidanceKey = default;
        _lastCompactAvoidanceLayoutTag = 0;
        _hasLastCompactAvoidanceKey = false;
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

    private static bool IsDuplicateCompactAvoidanceSignal(int previousLayoutTag, int currentLayoutTag) =>
        previousLayoutTag == 2 && currentLayoutTag == 0;

    private static CombatObservation CreateCompactEvade(in PendingCompactAvoidance pending)
    {
        var observation = new CombatObservation
        {
            SkillCode = pending.BodySkillVariantRaw,
            BodySkillVariantRaw = pending.BodySkillVariantRaw,
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

}

internal sealed record CompactAvoidanceCanonicalizerSnapshot(CompactAvoidanceCanonicalizer.PendingCompactAvoidance[] Pending, bool HasLastCompactAvoidanceKey, int LastSourceId, int LastTargetId, int LastBodySkillVariantRaw, int LastMarker, int LastLayoutTag);

public readonly record struct StampedCombatCanonicalizationResult(int SourceId, int TargetId, TimelineStamp Stamp, long ObservedAtMilliseconds, RawPacketReference Raw, CombatObservation Observation, CombatContributionCanonicalization Canonicalization)
{
    public StampedCombatCanonicalizationResult(int sourceId, int targetId, TimelineStamp stamp, long observedAtMilliseconds, RawPacketReference raw, CombatObservation observation)
        : this(sourceId, targetId, stamp, observedAtMilliseconds, raw, observation, CombatContributionCanonicalization.None)
    {
    }

    public StampedCombatCanonicalizationResult WithCanonicalization(CombatContributionCanonicalization canonicalization) => this with { Canonicalization = Canonicalization | canonicalization };
}

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
