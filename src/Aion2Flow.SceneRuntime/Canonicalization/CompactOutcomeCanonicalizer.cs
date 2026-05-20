using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class CompactOutcomeCanonicalizer
{
    private const int MaxPendingAvoidances = 32;
    private const int PendingCompactInitialCapacity = 1024;
    internal readonly record struct PendingDirectBlockedDamage(int SourceId, int TargetId, TimelineStamp Stamp, long ObservedAtMilliseconds, CombatObservation Observation);
    internal readonly record struct PendingCompactAvoidance(int SourceId, int TargetId, int OriginalSkillCode, int Marker, TimelineStamp Stamp, long ObservedAtMilliseconds);
    internal readonly record struct AvoidedSignature(int SourceId, int TargetId, int Marker);
    internal readonly record struct StoredDamageKey(long BatchOrdinal, int SourceId, int TargetId, int Marker);
    internal readonly record struct SourceMarker(int SourceId, int Marker);
    internal readonly record struct SourceBaseSkill(int SourceId, int BaseSkillCode);
    private readonly List<PendingDirectBlockedDamage> _pendingDirect = new(MaxPendingAvoidances);
    private readonly List<PendingCompactAvoidance> _pendingCompact = new(MaxPendingAvoidances);
    private readonly List<PendingCompactAvoidance> _pendingCompactDamage = new(PendingCompactInitialCapacity);
    private readonly List<PendingCompactAvoidance> _pendingCompactControls0638 = new(PendingCompactInitialCapacity);
    private readonly HashSet<StoredDamageKey> _storedDamageKeys = [];
    private readonly HashSet<SourceMarker> _damageMarkersBySource = [];
    private readonly Dictionary<SourceBaseSkill, int> _lastDamageTargetBySourceBaseSkill = [];
    private readonly Dictionary<SourceBaseSkill, int> _damageHitsBySourceBaseSkill = [];
    private readonly HashSet<SourceMarker> _compactDamageMarkersBySource = [];
    private readonly Dictionary<SourceBaseSkill, int> _compactControlsBySourceBaseSkill = [];
    private readonly HashSet<int> _currentBatchDodgeTargets = [];
    private readonly HashSet<AvoidedSignature> _resolvedAvoidanceSignatures = [];
    private readonly HashSet<(int TargetId, int SkillCode)> _confirmedCompactDamage = [];
    private long _currentBatchOrdinal;

    public StampedCombatCanonicalizationBatch NormalizeCombat(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds = 0)
    {
        var prefix = EnsureBatch(stamp.BatchOrdinal);

        if (TryObserveDirectBlockedDamage(sourceId, targetId, in stamp, in observation, observedAtMilliseconds))
            return prefix;

        var result = new StampedCombatCanonicalizationResult(sourceId, targetId, stamp, observedAtMilliseconds, observation);
        TrackStored(in result);
        return Append(prefix, result);
    }

    public StampedCombatCanonicalizationBatch ObserveCompactValue0438(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds = 0)
    {
        var isCompactSignal = IsCompactSignalShape(in observation) && observation.EventKind == CombatEventKind.Unknown && observation.ValueKind == CombatValueKind.Unknown;
        if (!isCompactSignal)
        {
            var directPrefix = EnsureBatch(stamp.BatchOrdinal);
            var directResult = new StampedCombatCanonicalizationResult(sourceId, targetId, stamp, observedAtMilliseconds, observation);
            TrackStored(in directResult);
            return Append(directPrefix, directResult);
        }

        if (IsCompactType2Sidecar(in observation) && IsCompactDamageConfirmation(sourceId, targetId))
        {
            if (sourceId > 0 && observation.Marker > 0)
                _compactDamageMarkersBySource.Add(new SourceMarker(sourceId, observation.Marker));

            if (IsPlayerOrphanItemSkillCandidate(observation.SkillCode))
                _pendingCompactDamage.Add(new PendingCompactAvoidance(sourceId, targetId, observation.SkillCode, observation.Marker, stamp, observedAtMilliseconds));

            _confirmedCompactDamage.Add((targetId, observation.SkillCode));
            CancelPendingCompactEvade(targetId, observation.SkillCode);
        }

        var prefix = EnsureBatch(stamp.BatchOrdinal);
        if (IsCompactType2Sidecar(in observation))
            return prefix;

        if (TryObserveCompactAvoidance(sourceId, targetId, in stamp, in observation, observedAtMilliseconds))
            return prefix;

        return prefix;
    }

    public StampedCombatCanonicalizationBatch ObserveCompactControl0238(int sourceId, in TimelineStamp stamp, in CombatObservation observation)
    {
        var prefix = EnsureBatch(stamp.BatchOrdinal);
        ObserveDodgeSignal(sourceId, in observation);
        return prefix;
    }

    public StampedCombatCanonicalizationBatch ObserveCompactControl0638(int sourceId, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds = 0)
    {
        var prefix = EnsureBatch(stamp.BatchOrdinal);
        if (sourceId > 0 && observation.Marker > 0 && observation.SkillCode > 0 && IsPlayerOrphanItemSkillCandidate(observation.SkillCode))
        {
            _pendingCompactControls0638.Add(new PendingCompactAvoidance(sourceId, 0, observation.SkillCode, observation.Marker, stamp, observedAtMilliseconds));
            var baseSkill = ResolveBaseSkillCode(observation.SkillCode);
            if (baseSkill > 0)
                Increment(_compactControlsBySourceBaseSkill, new SourceBaseSkill(sourceId, baseSkill));
        }

        ObserveDodgeSignal(sourceId, in observation);
        return prefix;
    }

    public StampedCombatCanonicalizationBatch CompleteBatch(long batchOrdinal)
    {
        if (_currentBatchOrdinal == 0)
            return batchOrdinal == long.MaxValue ? FlushOrphanCompactHits() : StampedCombatCanonicalizationBatch.Empty;

        if (batchOrdinal > 0 && _currentBatchOrdinal > 0 && batchOrdinal < _currentBatchOrdinal)
            return StampedCombatCanonicalizationBatch.Empty;

        if (batchOrdinal == long.MaxValue)
            return FinalizeAll();

        var results = FinalizeBatch();
        TrackStored(results);
        return results;
    }

    private bool TryObserveCompactAvoidance(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds)
    {
        if (!IsCompactEvadeSignal(sourceId, targetId, in observation) || observation.Marker <= 0)
            return false;

        var trackedSkillCode = ResolveTrackedSkillCode(observation.SkillCode);
        if (trackedSkillCode <= 0 || sourceId <= 0 || targetId <= 0 || sourceId == targetId)
            return false;

        var signature = new AvoidedSignature(sourceId, targetId, observation.Marker);
        if (_resolvedAvoidanceSignatures.Contains(signature))
            return true;

        _pendingCompact.Add(new PendingCompactAvoidance(sourceId, targetId, observation.SkillCode, observation.Marker, stamp, observedAtMilliseconds));
        TrimPending();
        return true;
    }

    private bool TryObserveDirectBlockedDamage(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds)
    {
        if (!IsDirectBlockedDamageCandidate(sourceId, targetId, in observation))
            return false;

        var signature = new AvoidedSignature(sourceId, targetId, observation.Marker);
        if (_resolvedAvoidanceSignatures.Contains(signature))
            return true;

        _pendingDirect.Add(new PendingDirectBlockedDamage(sourceId, targetId, stamp, observedAtMilliseconds, observation));
        TrimPending();
        return true;
    }

    private StampedCombatCanonicalizationBatch FinalizeBatch()
    {
        var results = new StampedCombatCanonicalizationBatchBuilder(_pendingDirect.Count + _pendingCompact.Count);

        foreach (var pending in _pendingDirect)
        {
            var signature = new AvoidedSignature(pending.SourceId, pending.TargetId, pending.Observation.Marker);
            if (_resolvedAvoidanceSignatures.Contains(signature))
                continue;

            if (_currentBatchDodgeTargets.Contains(pending.TargetId))
            {
                _resolvedAvoidanceSignatures.Add(signature);
                var observation = pending.Observation;
                results.Add(new StampedCombatCanonicalizationResult(pending.SourceId, pending.TargetId, pending.Stamp, pending.ObservedAtMilliseconds, ApplyAvoidedModifier(pending.SourceId, pending.TargetId, in observation, DamageModifiers.Evade, PacketEffectTag.ActiveDodgeEvade)));
            }
            else
            {
                var observation = pending.Observation;
                results.Add(new StampedCombatCanonicalizationResult(pending.SourceId, pending.TargetId, pending.Stamp, pending.ObservedAtMilliseconds, NormalizeBaseObservation(pending.SourceId, pending.TargetId, in observation)));
            }
        }

        foreach (var pending in _pendingCompact)
        {
            var signature = new AvoidedSignature(pending.SourceId, pending.TargetId, pending.Marker);
            if (_resolvedAvoidanceSignatures.Contains(signature))
                continue;

            if (_confirmedCompactDamage.Contains((pending.TargetId, pending.OriginalSkillCode)))
            {
                _resolvedAvoidanceSignatures.Add(signature);
                continue;
            }

            _resolvedAvoidanceSignatures.Add(signature);
            results.Add(new StampedCombatCanonicalizationResult(pending.SourceId, pending.TargetId, pending.Stamp, pending.ObservedAtMilliseconds, CreateCompactEvade(in pending)));
        }

        _pendingDirect.Clear();
        _pendingCompact.Clear();
        _currentBatchDodgeTargets.Clear();
        _resolvedAvoidanceSignatures.Clear();
        _currentBatchOrdinal = 0;
        return results.ToBatch();
    }

    private StampedCombatCanonicalizationBatch FinalizeAll()
    {
        var results = new StampedCombatCanonicalizationBatchBuilder();
        var finalized = FinalizeBatch();
        TrackStored(finalized);
        results.AddRange(finalized);
        results.AddRange(FlushOrphanCompactHits());
        return results.ToBatch();
    }

    private StampedCombatCanonicalizationBatch FlushOrphanCompactHits()
    {
        var results = new StampedCombatCanonicalizationBatchBuilder();
        foreach (var pending in _pendingCompactDamage)
        {
            if (pending.Marker <= 0)
                continue;

            if (HasStoredDamageNear(in pending))
                continue;

            if (!IsPlayerOrphanItemSkillCandidate(pending.OriginalSkillCode))
                continue;

            results.Add(CreateOrphanCompactHit(in pending, pending.TargetId));
        }

        foreach (var marker in _compactDamageMarkersBySource)
            _damageMarkersBySource.Add(marker);

        foreach (var pending in _pendingCompact)
        {
            if (pending.SourceId > 0 && pending.Marker > 0)
                _damageMarkersBySource.Add(new SourceMarker(pending.SourceId, pending.Marker));
        }

        var seen0638Markers = new HashSet<SourceMarker>();
        var emittedBySourceBaseSkill = new Dictionary<SourceBaseSkill, int>();
        foreach (var pending in _pendingCompactControls0638)
        {
            if (pending.Marker <= 0 || pending.SourceId <= 0)
                continue;

            if (_damageMarkersBySource.Contains(new SourceMarker(pending.SourceId, pending.Marker)))
                continue;

            if (!IsPlayerOrphanItemSkillCandidate(pending.OriginalSkillCode))
                continue;

            if (!seen0638Markers.Add(new SourceMarker(pending.SourceId, pending.Marker)))
                continue;

            var baseSkill = ResolveBaseSkillCode(pending.OriginalSkillCode);
            if (baseSkill <= 0)
                continue;

            var key = new SourceBaseSkill(pending.SourceId, baseSkill);
            if (!_lastDamageTargetBySourceBaseSkill.TryGetValue(key, out var targetId) || targetId <= 0)
                continue;

            _damageHitsBySourceBaseSkill.TryGetValue(key, out var damageCount);
            emittedBySourceBaseSkill.TryGetValue(key, out var emittedCount);
            _compactControlsBySourceBaseSkill.TryGetValue(key, out var totalControls);
            if (damageCount + emittedCount >= totalControls)
                continue;

            results.Add(CreateOrphanCompactHit(in pending, targetId));
            emittedBySourceBaseSkill[key] = emittedCount + 1;
        }

        _pendingCompactDamage.Clear();
        _pendingCompactControls0638.Clear();
        _storedDamageKeys.Clear();
        _damageMarkersBySource.Clear();
        _lastDamageTargetBySourceBaseSkill.Clear();
        _damageHitsBySourceBaseSkill.Clear();
        _compactDamageMarkersBySource.Clear();
        _compactControlsBySourceBaseSkill.Clear();
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
        TrackStored(results);
        _currentBatchOrdinal = resolvedBatchOrdinal;
        return results;
    }

    private void TrackStored(StampedCombatCanonicalizationBatch results)
    {
        foreach (var result in results)
            TrackStored(in result);
    }

    private void TrackStored(in StampedCombatCanonicalizationResult result)
    {
        var observation = result.Observation;
        if (observation.EventKind != CombatEventKind.Damage || result.SourceId <= 0 || observation.Marker <= 0)
            return;

        _damageMarkersBySource.Add(new SourceMarker(result.SourceId, observation.Marker));
        var baseSkill = observation.BaseSkillCode > 0 ? observation.BaseSkillCode : ResolveBaseSkillCode(OriginalSkillCode(in observation));
        if (baseSkill > 0 && result.TargetId > 0)
        {
            var sourceSkill = new SourceBaseSkill(result.SourceId, baseSkill);
            _lastDamageTargetBySourceBaseSkill[sourceSkill] = result.TargetId;
            Increment(_damageHitsBySourceBaseSkill, sourceSkill);
        }

        if (result.TargetId > 0 && result.SourceId != result.TargetId)
            _storedDamageKeys.Add(new StoredDamageKey(result.Stamp.BatchOrdinal, result.SourceId, result.TargetId, observation.Marker));
    }

    private bool HasStoredDamageNear(in PendingCompactAvoidance pending) =>
        _storedDamageKeys.Contains(new StoredDamageKey(pending.Stamp.BatchOrdinal, pending.SourceId, pending.TargetId, pending.Marker)) ||
        _storedDamageKeys.Contains(new StoredDamageKey(pending.Stamp.BatchOrdinal - 1, pending.SourceId, pending.TargetId, pending.Marker)) ||
        _storedDamageKeys.Contains(new StoredDamageKey(pending.Stamp.BatchOrdinal + 1, pending.SourceId, pending.TargetId, pending.Marker));

    private static void Increment(Dictionary<SourceBaseSkill, int> counts, SourceBaseSkill key)
    {
        ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(counts, key, out _);
        count++;
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

    private void ObserveDodgeSignal(int sourceId, in CombatObservation observation)
    {
        if (sourceId <= 0 || observation.Marker <= 0)
            return;

        var trackedSkillCode = ResolveTrackedSkillCode(observation.SkillCode);
        if (trackedSkillCode <= 0 || !IsDodgeSkill(trackedSkillCode))
            return;

        _currentBatchDodgeTargets.Add(sourceId);
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

        while (_pendingDirect.Count > MaxPendingAvoidances)
            _pendingDirect.RemoveAt(0);
    }

    private static bool IsCompactDamageConfirmation(int sourceId, int targetId) =>
        sourceId > 0 && targetId > 0 && sourceId != targetId;

    private static bool IsCompactEvadeSignal(int sourceId, int targetId, in CombatObservation observation) =>
        IsCompactSignalShape(in observation) && targetId > 0 && sourceId > 0 && targetId != sourceId && observation.Type == 1 && observation.LayoutTag is 0 or 2;

    private static bool IsCompactSignalShape(in CombatObservation observation) =>
        observation.HitCount == 0 && observation.AttemptCount == 0;

    private static bool IsCompactType2Sidecar(in CombatObservation observation) =>
        IsCompactSignalShape(in observation) && observation.Type == 2;

    private static bool IsDirectBlockedDamageCandidate(int sourceId, int targetId, in CombatObservation observation)
    {
        if (observation.Damage != 1 || sourceId <= 0 || targetId <= 0 || sourceId == targetId)
            return false;

        return observation.ValueKind is CombatValueKind.Damage or CombatValueKind.DrainDamage or CombatValueKind.Unknown || observation.EventKind == CombatEventKind.Damage;
    }

    private static CombatObservation ApplyAvoidedModifier(int sourceId, int targetId, in CombatObservation observation, DamageModifiers modifier, PacketEffectTag effectTag)
    {
        var modified = observation with
        {
            Damage = 0,
            HitCount = 0,
            AttemptCount = Math.Max(observation.AttemptCount, 1),
            Modifiers = (observation.Modifiers & ~(DamageModifiers.Evade | DamageModifiers.Invincible | DamageModifiers.Critical)) | modifier,
            EffectTag = effectTag,
            PeriodicRelation = PeriodicEffectRelation.None,
            PeriodicMode = 0
        };
        return CombatResourceRegistry.NormalizeObservationForStorage(sourceId, targetId, in modified);
    }

    private static CombatObservation NormalizeBaseObservation(int sourceId, int targetId, in CombatObservation observation)
        => CombatResourceRegistry.NormalizeObservationForStorage(sourceId, targetId, in observation);

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

    private static bool IsDodgeSkill(int trackedSkillCode)
    {
        var suffix = trackedSkillCode % 1000000;
        if (suffix != 100)
            return false;
        var classPrefix = trackedSkillCode / 1000000;
        return classPrefix is >= 11 and <= 18;
    }

    private static StampedCombatCanonicalizationResult CreateOrphanCompactHit(in PendingCompactAvoidance pending, int targetId)
    {
        var observation = new CombatObservation
        {
            SkillCode = pending.OriginalSkillCode,
            OriginalSkillCode = pending.OriginalSkillCode,
            Damage = 0,
            HitCount = 1,
            AttemptCount = 1,
            Marker = pending.Marker,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };
        return new StampedCombatCanonicalizationResult(pending.SourceId, targetId, pending.Stamp, pending.ObservedAtMilliseconds, NormalizeBaseObservation(pending.SourceId, targetId, in observation));
    }

    private static bool IsPlayerOrphanItemSkillCandidate(int originalSkillCode)
    {
        var resolvedSkillCode = CombatResourceRegistry.InferOriginalSkillCode(originalSkillCode);
        if (resolvedSkillCode is null)
            return false;

        return CombatResourceRegistry.SkillMap.TryGetValue(resolvedSkillCode.Value, out var skill) && skill.SourceType == SkillSourceType.ItemSkill && skill.Category != SkillCategory.Npc;
    }

    private static int ResolveBaseSkillCode(int skillCodeRaw) =>
        skillCodeRaw > 0 ? CombatResourceRegistry.ParseSkillVariant(skillCodeRaw).BaseSkillCode : 0;

    private static int OriginalSkillCode(in CombatObservation observation) =>
        observation.OriginalSkillCode != 0 ? observation.OriginalSkillCode : observation.SkillCode;

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

    public static StampedCombatCanonicalizationBatch Two(in StampedCombatCanonicalizationResult first, in StampedCombatCanonicalizationResult second) =>
        new(2, in first, in second, null);

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
