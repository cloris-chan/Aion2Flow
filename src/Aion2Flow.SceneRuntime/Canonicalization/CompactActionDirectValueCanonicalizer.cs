using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class CompactActionDirectValueCanonicalizer
{
    private const int MaxPendingOpeners = 128;
    private const int MaxPendingValues = 256;
    private const int MaxPendingSidecars = 256;
    private const int MaxConfirmedInlineRecoveryGroups = 128;

    internal readonly record struct PendingCompactActionOpener(int SourceId, uint BodyCodeRaw, int Marker, int Mode, int Flag, int EchoSourceId, long BatchOrdinal, int ScopeId);
    internal readonly record struct PendingCompactActionValue(int SourceId, int TargetId, uint BodyCodeRaw, int Marker, long BatchOrdinal, int ScopeId, TimelineStamp Stamp, long ObservedAtMilliseconds, CombatObservation Observation);
    internal readonly record struct PendingCompactActionSidecar(int SourceId, int TargetId, uint BodyCodeRaw, int Marker, long BatchOrdinal, int ScopeId);
    internal readonly record struct PendingCompactActionInlineRecoveryGroup(int SourceId, uint BodyCodeRaw, int Marker, long BatchOrdinal, int ScopeId);

    private readonly List<PendingCompactActionOpener> _pendingOpeners = new(MaxPendingOpeners);
    private readonly List<PendingCompactActionValue> _pendingValues = new(MaxPendingValues);
    private readonly List<PendingCompactActionSidecar> _pendingSidecars = new(MaxPendingSidecars);
    private readonly List<PendingCompactActionInlineRecoveryGroup> _inlineRecoveryGroups = new(MaxConfirmedInlineRecoveryGroups);

    public bool TryObserveCompactValue0438(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation, in PacketStructurePath structurePath, long observedAtMilliseconds, out StampedCombatCanonicalizationBatch results)
    {
        if (!IsCompactDirectActionValueShape(in observation))
        {
            results = StampedCombatCanonicalizationBatch.Empty;
            return false;
        }

        var bodyCodeRaw = unchecked((uint)observation.BodySkillVariantRaw);
        var scopeId = ResolveAssociationScope(in structurePath);
        if (TryFindActionOpener(sourceId, bodyCodeRaw, observation.Marker, stamp.BatchOrdinal, scopeId, out var opener))
        {
            if (!MatchesRecoveryOpener(in opener, sourceId) && targetId == sourceId)
                return ObservePendingCompactValue(sourceId, targetId, bodyCodeRaw, in stamp, in observation, scopeId, observedAtMilliseconds, out results);

            var normalized = MatchesRecoveryOpener(in opener, sourceId) ? NormalizeAsHealing(in observation) : observation;
            results = StampedCombatCanonicalizationBatch.One(new StampedCombatCanonicalizationResult(sourceId, targetId, stamp, observedAtMilliseconds, normalized));
            return true;
        }

        if (TryFindInlineRecoveryGroup(sourceId, bodyCodeRaw, observation.Marker, stamp.BatchOrdinal, scopeId, out _))
        {
            results = StampedCombatCanonicalizationBatch.One(new StampedCombatCanonicalizationResult(sourceId, targetId, stamp, observedAtMilliseconds, NormalizeAsHealing(in observation)));
            return true;
        }

        return ObservePendingCompactValue(sourceId, targetId, bodyCodeRaw, in stamp, in observation, scopeId, observedAtMilliseconds, out results);
    }

    private bool ObservePendingCompactValue(int sourceId, int targetId, uint bodyCodeRaw, in TimelineStamp stamp, in CombatObservation observation, int scopeId, long observedAtMilliseconds, out StampedCombatCanonicalizationBatch results)
    {
        var pending = new PendingCompactActionValue(sourceId, targetId, bodyCodeRaw, observation.Marker, stamp.BatchOrdinal, scopeId, stamp, observedAtMilliseconds, observation);
        _pendingValues.Add(pending);
        results = targetId == sourceId && TryConfirmInlineRecoveryGroupFromSelfValue(in pending, out var group)
            ? FlushValuesMatchedBy(in group)
            : StampedCombatCanonicalizationBatch.Empty;
        results = Append(results, TrimPendingValues());
        return true;
    }

    public StampedCombatCanonicalizationBatch ObserveCompactValueSidecar0438(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation, in PacketStructurePath structurePath)
    {
        if (!IsInlineDirectActionSidecarShape(in observation))
            return StampedCombatCanonicalizationBatch.Empty;

        var sidecar = new PendingCompactActionSidecar(sourceId, targetId, unchecked((uint)observation.BodySkillVariantRaw), observation.Marker, stamp.BatchOrdinal, ResolveAssociationScope(in structurePath));
        _pendingSidecars.Add(sidecar);
        TrimPendingSidecars();
        return targetId == sourceId && TryConfirmInlineRecoveryGroupFromSelfSidecar(in sidecar, out var group)
            ? FlushValuesMatchedBy(in group)
            : StampedCombatCanonicalizationBatch.Empty;
    }

    public StampedCombatCanonicalizationBatch ObserveCompactControl0238(int sourceId, in CombatObservation observation, in TimelineStamp stamp, in PacketStructurePath structurePath)
    {
        if (!IsCompactActionOpener(sourceId, in observation))
            return StampedCombatCanonicalizationBatch.Empty;

        var opener = new PendingCompactActionOpener(sourceId, observation.BodyCodeRaw, observation.Marker, observation.Type, observation.Flag, observation.ChainId, stamp.BatchOrdinal, ResolveAssociationScope(in structurePath));
        _pendingOpeners.Add(opener);
        TrimPendingOpeners();
        return FlushValuesMatchedBy(in opener);
    }

    public StampedCombatCanonicalizationBatch ObserveCompactControl0638(int sourceId, in CombatObservation observation, in TimelineStamp stamp, in PacketStructurePath structurePath)
    {
        if (IsCompactActionCloser(sourceId, in observation))
            ClosePendingActionOpener(sourceId, observation.BodyResourceEffectRef.RawId, observation.Marker, stamp.BatchOrdinal, ResolveAssociationScope(in structurePath));

        return StampedCombatCanonicalizationBatch.Empty;
    }

    public StampedCombatCanonicalizationBatch CompleteBatch(long batchOrdinal)
    {
        if (_pendingValues.Count == 0)
        {
            TrimCompletedOpeners(batchOrdinal);
            return StampedCombatCanonicalizationBatch.Empty;
        }

        var results = new StampedCombatCanonicalizationBatchBuilder(_pendingValues.Count);
        for (var i = 0; i < _pendingValues.Count;)
        {
            var pending = _pendingValues[i];
            if (!IsBatchComplete(in pending, batchOrdinal))
            {
                i++;
                continue;
            }

            results.Add(CreateResult(in pending, asHealing: false));
            _pendingValues.RemoveAt(i);
        }

        TrimCompletedOpeners(batchOrdinal);
        return results.ToBatch();
    }

    internal CompactActionDirectValueCanonicalizerSnapshot CreateSnapshot() => new([.. _pendingOpeners], [.. _pendingValues], [.. _pendingSidecars], [.. _inlineRecoveryGroups]);

    internal static CompactActionDirectValueCanonicalizer FromSnapshot(CompactActionDirectValueCanonicalizerSnapshot snapshot)
    {
        var canonicalizer = new CompactActionDirectValueCanonicalizer();
        canonicalizer._pendingOpeners.AddRange(snapshot.PendingOpeners);
        canonicalizer._pendingValues.AddRange(snapshot.PendingValues);
        canonicalizer._pendingSidecars.AddRange(snapshot.PendingSidecars);
        canonicalizer._inlineRecoveryGroups.AddRange(snapshot.InlineRecoveryGroups);
        return canonicalizer;
    }

    private StampedCombatCanonicalizationBatch FlushValuesMatchedBy(in PendingCompactActionOpener opener)
    {
        if (_pendingValues.Count == 0)
            return StampedCombatCanonicalizationBatch.Empty;

        var results = new StampedCombatCanonicalizationBatchBuilder(_pendingValues.Count);
        for (var i = 0; i < _pendingValues.Count;)
        {
            var pending = _pendingValues[i];
            if (!MatchesAction(in opener, pending.SourceId, pending.BodyCodeRaw, pending.Marker, pending.BatchOrdinal, pending.ScopeId))
            {
                i++;
                continue;
            }

            results.Add(CreateResult(in pending, MatchesRecoveryOpener(in opener, pending.SourceId)));
            _pendingValues.RemoveAt(i);
        }

        return results.ToBatch();
    }

    private void ClosePendingActionOpener(int sourceId, uint bodyCodeRaw, int marker, long batchOrdinal, int scopeId)
    {
        for (var i = _pendingOpeners.Count - 1; i >= 0; i--)
        {
            var pending = _pendingOpeners[i];
            if (MatchesAction(in pending, sourceId, bodyCodeRaw, marker, batchOrdinal, scopeId))
            {
                _pendingOpeners.RemoveAt(i);
                return;
            }
        }
    }

    private StampedCombatCanonicalizationBatch FlushValuesMatchedBy(in PendingCompactActionInlineRecoveryGroup group)
    {
        if (_pendingValues.Count == 0)
            return StampedCombatCanonicalizationBatch.Empty;

        var results = new StampedCombatCanonicalizationBatchBuilder(_pendingValues.Count);
        for (var i = 0; i < _pendingValues.Count;)
        {
            var pending = _pendingValues[i];
            if (!MatchesInlineRecoveryGroup(in group, pending.SourceId, pending.BodyCodeRaw, pending.Marker, pending.BatchOrdinal, pending.ScopeId))
            {
                i++;
                continue;
            }

            results.Add(CreateResult(in pending, asHealing: true));
            _pendingValues.RemoveAt(i);
        }

        return results.ToBatch();
    }

    private bool TryFindActionOpener(int sourceId, uint bodyCodeRaw, int marker, long batchOrdinal, int scopeId, out PendingCompactActionOpener opener)
    {
        for (var i = _pendingOpeners.Count - 1; i >= 0; i--)
        {
            var pending = _pendingOpeners[i];
            if (MatchesAction(in pending, sourceId, bodyCodeRaw, marker, batchOrdinal, scopeId))
            {
                opener = pending;
                return true;
            }
        }

        opener = default;
        return false;
    }

    private bool TryFindInlineRecoveryGroup(int sourceId, uint bodyCodeRaw, int marker, long batchOrdinal, int scopeId, out PendingCompactActionInlineRecoveryGroup group)
    {
        for (var i = _inlineRecoveryGroups.Count - 1; i >= 0; i--)
        {
            var pending = _inlineRecoveryGroups[i];
            if (MatchesInlineRecoveryGroup(in pending, sourceId, bodyCodeRaw, marker, batchOrdinal, scopeId))
            {
                group = pending;
                return true;
            }
        }

        group = default;
        return false;
    }

    private bool TryConfirmInlineRecoveryGroupFromSelfValue(in PendingCompactActionValue value, out PendingCompactActionInlineRecoveryGroup group)
    {
        if (!HasMatchingSelfSidecar(in value))
        {
            group = default;
            return false;
        }

        group = new PendingCompactActionInlineRecoveryGroup(value.SourceId, value.BodyCodeRaw, value.Marker, value.BatchOrdinal, value.ScopeId);
        ConfirmInlineRecoveryGroup(in group);
        return true;
    }

    private bool TryConfirmInlineRecoveryGroupFromSelfSidecar(in PendingCompactActionSidecar sidecar, out PendingCompactActionInlineRecoveryGroup group)
    {
        if (!HasMatchingSelfValue(in sidecar))
        {
            group = default;
            return false;
        }

        group = new PendingCompactActionInlineRecoveryGroup(sidecar.SourceId, sidecar.BodyCodeRaw, sidecar.Marker, sidecar.BatchOrdinal, sidecar.ScopeId);
        ConfirmInlineRecoveryGroup(in group);
        return true;
    }

    private void ConfirmInlineRecoveryGroup(in PendingCompactActionInlineRecoveryGroup group)
    {
        if (TryFindInlineRecoveryGroup(group.SourceId, group.BodyCodeRaw, group.Marker, group.BatchOrdinal, group.ScopeId, out _))
            return;

        _inlineRecoveryGroups.Add(group);
        TrimInlineRecoveryGroups();
    }

    private bool HasMatchingSelfSidecar(in PendingCompactActionValue value)
    {
        for (var i = _pendingSidecars.Count - 1; i >= 0; i--)
        {
            var sidecar = _pendingSidecars[i];
            if (sidecar.SourceId == sidecar.TargetId &&
                sidecar.TargetId == value.SourceId &&
                MatchesInlineRecoveryGroup(sidecar.SourceId, sidecar.BodyCodeRaw, sidecar.Marker, sidecar.BatchOrdinal, sidecar.ScopeId, value.SourceId, value.BodyCodeRaw, value.Marker, value.BatchOrdinal, value.ScopeId))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasMatchingSelfValue(in PendingCompactActionSidecar sidecar)
    {
        for (var i = _pendingValues.Count - 1; i >= 0; i--)
        {
            var value = _pendingValues[i];
            if (value.SourceId == value.TargetId &&
                value.TargetId == sidecar.SourceId &&
                MatchesInlineRecoveryGroup(value.SourceId, value.BodyCodeRaw, value.Marker, value.BatchOrdinal, value.ScopeId, sidecar.SourceId, sidecar.BodyCodeRaw, sidecar.Marker, sidecar.BatchOrdinal, sidecar.ScopeId))
            {
                return true;
            }
        }

        return false;
    }

    private static StampedCombatCanonicalizationResult CreateResult(in PendingCompactActionValue pending, bool asHealing)
    {
        var original = pending.Observation;
        var observation = asHealing ? NormalizeAsHealing(in original) : original;
        return new StampedCombatCanonicalizationResult(pending.SourceId, pending.TargetId, pending.Stamp, pending.ObservedAtMilliseconds, observation);
    }

    private static CombatObservation NormalizeAsHealing(in CombatObservation observation) => observation with
    {
        EventKind = CombatEventKind.Healing,
        ValueKind = CombatValueKind.Healing
    };

    private static bool MatchesAction(in PendingCompactActionOpener pending, int sourceId, uint bodyCodeRaw, int marker, long batchOrdinal, int scopeId) =>
        pending.SourceId == sourceId &&
        pending.BodyCodeRaw == bodyCodeRaw &&
        pending.Marker == marker &&
        MatchesAssociation(pending.BatchOrdinal, pending.ScopeId, batchOrdinal, scopeId);

    private static bool MatchesInlineRecoveryGroup(in PendingCompactActionInlineRecoveryGroup pending, int sourceId, uint bodyCodeRaw, int marker, long batchOrdinal, int scopeId) =>
        MatchesInlineRecoveryGroup(pending.SourceId, pending.BodyCodeRaw, pending.Marker, pending.BatchOrdinal, pending.ScopeId, sourceId, bodyCodeRaw, marker, batchOrdinal, scopeId);

    private static bool MatchesInlineRecoveryGroup(
        int pendingSourceId,
        uint pendingBodyCodeRaw,
        int pendingMarker,
        long pendingBatchOrdinal,
        int pendingScopeId,
        int sourceId,
        uint bodyCodeRaw,
        int marker,
        long batchOrdinal,
        int scopeId) =>
        pendingSourceId == sourceId &&
        pendingBodyCodeRaw == bodyCodeRaw &&
        pendingMarker == marker &&
        MatchesAssociation(pendingBatchOrdinal, pendingScopeId, batchOrdinal, scopeId);

    private static bool MatchesRecoveryOpener(in PendingCompactActionOpener pending, int sourceId) =>
        pending.Mode is 0 or 12 &&
        pending.Flag == 0 &&
        pending.EchoSourceId == sourceId;

    private static bool IsCompactActionOpener(int sourceId, in CombatObservation observation) =>
        sourceId > 0 &&
        observation.BodyCodeRaw > 0 &&
        observation.Marker > 0 &&
        observation.Damage == 0 &&
        observation.HitCount == 0 &&
        observation.AttemptCount == 0 &&
        observation.LayoutTag == 0;

    private static bool IsCompactActionCloser(int sourceId, in CombatObservation observation) =>
        sourceId > 0 &&
        observation.BodyResourceEffectRef.RawId > 0 &&
        observation.Marker > 0 &&
        observation.Damage == 0 &&
        observation.HitCount == 0 &&
        observation.AttemptCount == 0 &&
        observation.LayoutTag == 0;

    private static bool IsCompactDirectActionValueShape(in CombatObservation observation) =>
        observation.Damage > 0 &&
        observation.HitCount == 1 &&
        observation.AttemptCount == 1 &&
        observation.ResourceKind == CombatResourceKind.Unknown &&
        observation.PeriodicRelation == PeriodicEffectRelation.None &&
        observation.EffectTag == PacketEffectTag.None &&
        observation.LayoutTag == 4 &&
        observation.Flag == 0 &&
        observation.Type == 2 &&
        observation.Loop is 1 or 2 &&
        observation.ChainId > 0 &&
        observation.BodySkillVariantRaw > 0 &&
        observation.Marker > 0 &&
        observation.ValueKind is CombatValueKind.Unknown or CombatValueKind.Damage or CombatValueKind.Support;

    private static bool IsInlineDirectActionSidecarShape(in CombatObservation observation) =>
        observation.Damage == 0 &&
        observation.HitCount == 0 &&
        observation.AttemptCount == 0 &&
        observation.ResourceKind == CombatResourceKind.Unknown &&
        observation.PeriodicRelation == PeriodicEffectRelation.None &&
        observation.EffectTag == PacketEffectTag.None &&
        observation.LayoutTag == 0 &&
        observation.Flag == 0 &&
        observation.Type is 0 or 2 &&
        observation.Loop == 0 &&
        observation.ChainId == 0 &&
        observation.BodySkillVariantRaw > 0 &&
        observation.Marker > 0 &&
        observation.EventKind == CombatEventKind.Unknown &&
        observation.ValueKind == CombatValueKind.Unknown;

    private static bool MatchesAssociation(long leftBatchOrdinal, int leftScopeId, long rightBatchOrdinal, int rightScopeId) =>
        leftBatchOrdinal > 0 && rightBatchOrdinal > 0 && leftBatchOrdinal == rightBatchOrdinal ||
        leftScopeId > 0 && rightScopeId > 0 && leftScopeId == rightScopeId;

    private static bool IsBatchComplete(in PendingCompactActionValue pending, long batchOrdinal)
    {
        if (batchOrdinal == long.MaxValue)
            return true;

        return pending.BatchOrdinal > 0 && batchOrdinal > pending.BatchOrdinal;
    }

    private void TrimPendingOpeners()
    {
        while (_pendingOpeners.Count > MaxPendingOpeners)
            _pendingOpeners.RemoveAt(0);
    }

    private void TrimCompletedOpeners(long batchOrdinal)
    {
        if (batchOrdinal != long.MaxValue)
            return;

        _pendingOpeners.Clear();
        _pendingSidecars.Clear();
        _inlineRecoveryGroups.Clear();
    }

    private StampedCombatCanonicalizationBatch TrimPendingValues()
    {
        if (_pendingValues.Count <= MaxPendingValues)
            return StampedCombatCanonicalizationBatch.Empty;

        var oldest = _pendingValues[0];
        _pendingValues.RemoveAt(0);
        return StampedCombatCanonicalizationBatch.One(CreateResult(in oldest, asHealing: false));
    }

    private void TrimPendingSidecars()
    {
        while (_pendingSidecars.Count > MaxPendingSidecars)
            _pendingSidecars.RemoveAt(0);
    }

    private void TrimInlineRecoveryGroups()
    {
        while (_inlineRecoveryGroups.Count > MaxConfirmedInlineRecoveryGroups)
            _inlineRecoveryGroups.RemoveAt(0);
    }

    private static StampedCombatCanonicalizationBatch Append(StampedCombatCanonicalizationBatch first, StampedCombatCanonicalizationBatch second)
    {
        if (first.Count == 0)
            return second;

        if (second.Count == 0)
            return first;

        var results = new StampedCombatCanonicalizationBatchBuilder(first.Count + second.Count);
        results.AddRange(first);
        results.AddRange(second);
        return results.ToBatch();
    }

    private static int ResolveAssociationScope(in PacketStructurePath structurePath)
    {
        if (structurePath.IsEmpty)
            return 0;

        var parent = structurePath.Parent;
        if (parent.ScopeId > 0)
            return parent.ScopeId;

        if (structurePath.Leaf.ParentScopeId > 0)
            return structurePath.Leaf.ParentScopeId;

        return structurePath.Leaf.ScopeId;
    }
}

internal sealed record CompactActionDirectValueCanonicalizerSnapshot(
    CompactActionDirectValueCanonicalizer.PendingCompactActionOpener[] PendingOpeners,
    CompactActionDirectValueCanonicalizer.PendingCompactActionValue[] PendingValues,
    CompactActionDirectValueCanonicalizer.PendingCompactActionSidecar[] PendingSidecars,
    CompactActionDirectValueCanonicalizer.PendingCompactActionInlineRecoveryGroup[] InlineRecoveryGroups);
