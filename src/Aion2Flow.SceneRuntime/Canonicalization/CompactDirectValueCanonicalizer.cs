using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class CompactDirectValueCanonicalizer
{
    private const int MaxPendingOpeners = 128;
    private const int MaxPendingValues = 256;
    private const int MaxPendingSidecars = 256;
    private const int MaxConfirmedInlineRecoveryGroups = 128;

    internal readonly record struct PendingCompactOpener(int SourceId, int TargetId, uint BodyCodeRaw, int Marker, int Mode, int Flag, int EchoSourceId, TimelineStamp Stamp, long ObservedAtMilliseconds, RawPacketReference Raw, CombatObservation Observation, int MatchedValueCount);
    internal readonly record struct PendingCompactValue(int SourceId, int TargetId, uint BodyCodeRaw, int Marker, TimelineStamp Stamp, long ObservedAtMilliseconds, RawPacketReference Raw, CombatObservation Observation);
    internal readonly record struct PendingCompactSidecar(int SourceId, int TargetId, uint BodyCodeRaw, int Marker);
    internal readonly record struct PendingCompactInlineRecoveryGroup(int SourceId, uint BodyCodeRaw, int Marker);
    internal readonly record struct CompactControlHeader(int SourceId, int TargetId, TimelineStamp Stamp, long ObservedAtMilliseconds, RawPacketReference Raw, CombatObservation Observation)
    {
        public static CompactControlHeader Empty { get; } = default;

        public bool HasHeader => Raw.Opcode != 0;
    }

    private readonly List<PendingCompactOpener> _pendingOpeners = new(MaxPendingOpeners);
    private readonly List<PendingCompactOpener> _closedOpeners = new(MaxPendingOpeners);
    private readonly List<PendingCompactValue> _pendingValues = new(MaxPendingValues);
    private readonly List<PendingCompactSidecar> _pendingSidecars = new(MaxPendingSidecars);
    private readonly List<PendingCompactInlineRecoveryGroup> _inlineRecoveryGroups = new(MaxConfirmedInlineRecoveryGroups);

    internal bool TryObserveCompactValue0438(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds, RawPacketReference raw, out StampedCombatCanonicalizationBatch results, out CompactControlHeader header)
    {
        header = CompactControlHeader.Empty;
        if (!IsCompactDirectValueShape(in observation))
        {
            results = StampedCombatCanonicalizationBatch.Empty;
            return false;
        }

        var bodyCodeRaw = unchecked((uint)observation.BodySkillVariantRaw);
        if (TryFindMatchingOpener(sourceId, bodyCodeRaw, observation.Marker, out var opener, out var openerIndex))
        {
            var matchesRecovery = MatchesRecoveryOpener(in opener, sourceId);
            if (!matchesRecovery && targetId == sourceId)
                return ObservePendingCompactValue(sourceId, targetId, bodyCodeRaw, in stamp, in observation, observedAtMilliseconds, raw, out results);

            var normalized = matchesRecovery ? NormalizeAsHealing(in observation) : observation;
            var canonicalization = CombatContributionCanonicalization.CompactDirectValue;
            if (matchesRecovery)
                canonicalization |= CombatContributionCanonicalization.CompactRecoveryByOpener;
            results = StampedCombatCanonicalizationBatch.One(new StampedCombatCanonicalizationResult(sourceId, targetId, stamp, observedAtMilliseconds, raw, normalized, canonicalization));
            MarkOpenerMatched(openerIndex, results.Count);
            header = CreateHeader(in opener);
            return true;
        }

        if (IsDirectSupportValue(in observation) &&
            TryConsumeClosedRecoveryOpener(sourceId, bodyCodeRaw, observation.Marker, out var closedOpener))
        {
            results = StampedCombatCanonicalizationBatch.One(new StampedCombatCanonicalizationResult(sourceId, targetId, stamp, observedAtMilliseconds, raw, NormalizeAsHealing(in observation), CombatContributionCanonicalization.CompactDirectValue | CombatContributionCanonicalization.CompactRecoveryByOpener));
            header = CreateHeader(in closedOpener);
            return true;
        }

        if (TryFindInlineRecoveryGroup(sourceId, bodyCodeRaw, observation.Marker, out _))
        {
            results = StampedCombatCanonicalizationBatch.One(new StampedCombatCanonicalizationResult(sourceId, targetId, stamp, observedAtMilliseconds, raw, NormalizeAsHealing(in observation), CombatContributionCanonicalization.CompactDirectValue | CombatContributionCanonicalization.CompactRecoveryByInlineGroup));
            return true;
        }

        return ObservePendingCompactValue(sourceId, targetId, bodyCodeRaw, in stamp, in observation, observedAtMilliseconds, raw, out results);
    }

    private bool ObservePendingCompactValue(int sourceId, int targetId, uint bodyCodeRaw, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds, RawPacketReference raw, out StampedCombatCanonicalizationBatch results)
    {
        var pending = new PendingCompactValue(sourceId, targetId, bodyCodeRaw, observation.Marker, stamp, observedAtMilliseconds, raw, observation);
        _pendingValues.Add(pending);
        results = targetId == sourceId && TryConfirmInlineRecoveryGroupFromSelfValue(in pending, out var group)
            ? FlushValuesMatchedBy(in group)
            : StampedCombatCanonicalizationBatch.Empty;
        results = Append(results, TrimPendingValues());
        return true;
    }

    internal StampedCombatCanonicalizationBatch ObserveCompactValueSidecar0438(int sourceId, int targetId, in CombatObservation observation)
    {
        if (!IsInlineDirectValueSidecarShape(in observation))
            return StampedCombatCanonicalizationBatch.Empty;

        var sidecar = new PendingCompactSidecar(sourceId, targetId, unchecked((uint)observation.BodySkillVariantRaw), observation.Marker);
        _pendingSidecars.Add(sidecar);
        TrimPendingSidecars();
        return targetId == sourceId && TryConfirmInlineRecoveryGroupFromSelfSidecar(in sidecar, out var group)
            ? FlushValuesMatchedBy(in group)
            : StampedCombatCanonicalizationBatch.Empty;
    }

    internal StampedCombatCanonicalizationBatch ObserveCompactControl0238(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds, RawPacketReference raw)
    {
        if (!IsCompactControlOpener(sourceId, in observation))
            return StampedCombatCanonicalizationBatch.Empty;

        var opener = new PendingCompactOpener(sourceId, targetId, observation.BodyCodeRaw, observation.Marker, observation.Type, observation.Flag, observation.ChainId, stamp, observedAtMilliseconds, raw, observation, MatchedValueCount: 0);
        _pendingOpeners.Add(opener);
        TrimPendingOpeners();
        var results = FlushValuesMatchedBy(in opener);
        MarkOpenerMatched(_pendingOpeners.Count - 1, results.Count);
        return results;
    }

    internal StampedCombatCanonicalizationBatch ObserveCompactControl0638(int sourceId, in CombatObservation observation)
    {
        if (IsCompactControlCloser(sourceId, in observation))
            CloseUnmatchedPendingOpener(sourceId, observation.BodyResourceEffectRef.RawId, observation.Marker);

        return StampedCombatCanonicalizationBatch.Empty;
    }

    internal StampedCombatCanonicalizationBatch FlushPending()
    {
        ClearPendingAssociations();
        if (_pendingValues.Count == 0)
            return StampedCombatCanonicalizationBatch.Empty;

        var results = new StampedCombatCanonicalizationBatchBuilder(_pendingValues.Count);
        foreach (var pending in _pendingValues)
            results.Add(CreateResult(in pending, asHealing: false, CombatContributionCanonicalization.CompactDirectValue));

        _pendingValues.Clear();
        return results.ToBatch();
    }

    internal bool HasPrimaryControlEvidence(int sourceId, uint bodyCodeRaw, int marker)
    {
        if (sourceId <= 0 || bodyCodeRaw == 0 || marker <= 0)
            return false;

        for (var i = _pendingOpeners.Count - 1; i >= 0; i--)
        {
            var pending = _pendingOpeners[i];
            if (IsPrimaryControlOpener(in pending) && MatchesOpener(in pending, sourceId, bodyCodeRaw, marker))
                return true;
        }

        for (var i = _closedOpeners.Count - 1; i >= 0; i--)
        {
            var pending = _closedOpeners[i];
            if (IsPrimaryControlOpener(in pending) && MatchesOpener(in pending, sourceId, bodyCodeRaw, marker))
                return true;
        }

        return false;
    }

    internal CompactDirectValueCanonicalizerSnapshot CreateSnapshot() => new([.. _pendingOpeners], [.. _closedOpeners], [.. _pendingValues], [.. _pendingSidecars], [.. _inlineRecoveryGroups]);

    internal static CompactDirectValueCanonicalizer FromSnapshot(CompactDirectValueCanonicalizerSnapshot snapshot)
    {
        var canonicalizer = new CompactDirectValueCanonicalizer();
        canonicalizer._pendingOpeners.AddRange(snapshot.PendingOpeners);
        canonicalizer._closedOpeners.AddRange(snapshot.ClosedOpeners);
        canonicalizer._pendingValues.AddRange(snapshot.PendingValues);
        canonicalizer._pendingSidecars.AddRange(snapshot.PendingSidecars);
        canonicalizer._inlineRecoveryGroups.AddRange(snapshot.InlineRecoveryGroups);
        return canonicalizer;
    }

    private StampedCombatCanonicalizationBatch FlushValuesMatchedBy(in PendingCompactOpener opener)
    {
        if (_pendingValues.Count == 0)
            return StampedCombatCanonicalizationBatch.Empty;

        var results = new StampedCombatCanonicalizationBatchBuilder(_pendingValues.Count);
        for (var i = 0; i < _pendingValues.Count;)
        {
            var pending = _pendingValues[i];
            if (!MatchesOpener(in opener, pending.SourceId, pending.BodyCodeRaw, pending.Marker))
            {
                i++;
                continue;
            }

            var matchesRecovery = MatchesRecoveryOpener(in opener, pending.SourceId);
            var canonicalization = CombatContributionCanonicalization.CompactDirectValue;
            if (matchesRecovery)
                canonicalization |= CombatContributionCanonicalization.CompactRecoveryByOpener;
            results.Add(CreateResult(in pending, matchesRecovery, canonicalization));
            _pendingValues.RemoveAt(i);
        }

        return results.ToBatch();
    }

    private StampedCombatCanonicalizationBatch FlushValuesMatchedBy(in PendingCompactInlineRecoveryGroup group)
    {
        if (_pendingValues.Count == 0)
            return StampedCombatCanonicalizationBatch.Empty;

        var results = new StampedCombatCanonicalizationBatchBuilder(_pendingValues.Count);
        for (var i = 0; i < _pendingValues.Count;)
        {
            var pending = _pendingValues[i];
            if (!MatchesInlineRecoveryGroup(in group, pending.SourceId, pending.BodyCodeRaw, pending.Marker))
            {
                i++;
                continue;
            }

            results.Add(CreateResult(in pending, asHealing: true, CombatContributionCanonicalization.CompactDirectValue | CombatContributionCanonicalization.CompactRecoveryByInlineGroup));
            _pendingValues.RemoveAt(i);
        }

        return results.ToBatch();
    }

    private void MarkOpenerMatched(int openerIndex, int matchedValueCount)
    {
        if (matchedValueCount <= 0 || (uint)openerIndex >= (uint)_pendingOpeners.Count)
            return;

        var opener = _pendingOpeners[openerIndex];
        _pendingOpeners[openerIndex] = opener with { MatchedValueCount = checked(opener.MatchedValueCount + matchedValueCount) };
    }

    private void CloseUnmatchedPendingOpener(int sourceId, uint bodyCodeRaw, int marker)
    {
        for (var i = _pendingOpeners.Count - 1; i >= 0; i--)
        {
            var pending = _pendingOpeners[i];
            if (pending.MatchedValueCount == 0 && MatchesOpener(in pending, sourceId, bodyCodeRaw, marker))
            {
                _closedOpeners.Add(pending);
                TrimClosedOpeners();
                _pendingOpeners.RemoveAt(i);
                return;
            }
        }
    }

    private bool TryConsumeClosedRecoveryOpener(int sourceId, uint bodyCodeRaw, int marker, out PendingCompactOpener opener)
    {
        for (var i = _closedOpeners.Count - 1; i >= 0; i--)
        {
            var pending = _closedOpeners[i];
            if (MatchesOpener(in pending, sourceId, bodyCodeRaw, marker) &&
                MatchesRecoveryOpener(in pending, sourceId))
            {
                opener = pending;
                _closedOpeners.RemoveAt(i);
                return true;
            }
        }

        opener = default;
        return false;
    }

    private bool TryFindMatchingOpener(int sourceId, uint bodyCodeRaw, int marker, out PendingCompactOpener opener, out int openerIndex)
    {
        for (var i = _pendingOpeners.Count - 1; i >= 0; i--)
        {
            var pending = _pendingOpeners[i];
            if (MatchesOpener(in pending, sourceId, bodyCodeRaw, marker))
            {
                opener = pending;
                openerIndex = i;
                return true;
            }
        }

        opener = default;
        openerIndex = -1;
        return false;
    }

    private bool TryFindInlineRecoveryGroup(int sourceId, uint bodyCodeRaw, int marker, out PendingCompactInlineRecoveryGroup group)
    {
        for (var i = _inlineRecoveryGroups.Count - 1; i >= 0; i--)
        {
            var pending = _inlineRecoveryGroups[i];
            if (MatchesInlineRecoveryGroup(in pending, sourceId, bodyCodeRaw, marker))
            {
                group = pending;
                return true;
            }
        }

        group = default;
        return false;
    }

    private bool TryConfirmInlineRecoveryGroupFromSelfValue(in PendingCompactValue value, out PendingCompactInlineRecoveryGroup group)
    {
        if (!HasMatchingSelfSidecar(in value))
        {
            group = default;
            return false;
        }

        group = new PendingCompactInlineRecoveryGroup(value.SourceId, value.BodyCodeRaw, value.Marker);
        ConfirmInlineRecoveryGroup(in group);
        return true;
    }

    private bool TryConfirmInlineRecoveryGroupFromSelfSidecar(in PendingCompactSidecar sidecar, out PendingCompactInlineRecoveryGroup group)
    {
        if (!HasMatchingSelfValue(in sidecar))
        {
            group = default;
            return false;
        }

        group = new PendingCompactInlineRecoveryGroup(sidecar.SourceId, sidecar.BodyCodeRaw, sidecar.Marker);
        ConfirmInlineRecoveryGroup(in group);
        return true;
    }

    private void ConfirmInlineRecoveryGroup(in PendingCompactInlineRecoveryGroup group)
    {
        if (TryFindInlineRecoveryGroup(group.SourceId, group.BodyCodeRaw, group.Marker, out _))
            return;

        _inlineRecoveryGroups.Add(group);
        TrimInlineRecoveryGroups();
    }

    private bool HasMatchingSelfSidecar(in PendingCompactValue value)
    {
        for (var i = _pendingSidecars.Count - 1; i >= 0; i--)
        {
            var sidecar = _pendingSidecars[i];
            if (sidecar.SourceId == sidecar.TargetId &&
                sidecar.TargetId == value.SourceId &&
                MatchesInlineRecoveryGroup(sidecar.SourceId, sidecar.BodyCodeRaw, sidecar.Marker, value.SourceId, value.BodyCodeRaw, value.Marker))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasMatchingSelfValue(in PendingCompactSidecar sidecar)
    {
        for (var i = _pendingValues.Count - 1; i >= 0; i--)
        {
            var value = _pendingValues[i];
            if (value.SourceId == value.TargetId &&
                value.TargetId == sidecar.SourceId &&
                MatchesInlineRecoveryGroup(value.SourceId, value.BodyCodeRaw, value.Marker, sidecar.SourceId, sidecar.BodyCodeRaw, sidecar.Marker))
            {
                return true;
            }
        }

        return false;
    }

    private static StampedCombatCanonicalizationResult CreateResult(in PendingCompactValue pending, bool asHealing, CombatContributionCanonicalization canonicalization)
    {
        var original = pending.Observation;
        var observation = asHealing ? NormalizeAsHealing(in original) : original;
        return new StampedCombatCanonicalizationResult(pending.SourceId, pending.TargetId, pending.Stamp, pending.ObservedAtMilliseconds, pending.Raw, observation, canonicalization);
    }

    private static CompactControlHeader CreateHeader(in PendingCompactOpener opener) =>
        new(opener.SourceId, opener.TargetId, opener.Stamp, opener.ObservedAtMilliseconds, opener.Raw, opener.Observation);

    private static CombatObservation NormalizeAsHealing(in CombatObservation observation) => observation with
    {
        EventKind = CombatEventKind.Healing,
        ValueKind = CombatValueKind.Healing
    };

    private static bool IsDirectSupportValue(in CombatObservation observation) =>
        observation.EventKind == CombatEventKind.Support ||
        observation.ValueKind == CombatValueKind.Support;

    private static bool MatchesOpener(in PendingCompactOpener pending, int sourceId, uint bodyCodeRaw, int marker) =>
        pending.SourceId == sourceId &&
        pending.BodyCodeRaw == bodyCodeRaw &&
        pending.Marker == marker;

    private static bool MatchesInlineRecoveryGroup(in PendingCompactInlineRecoveryGroup pending, int sourceId, uint bodyCodeRaw, int marker) =>
        MatchesInlineRecoveryGroup(pending.SourceId, pending.BodyCodeRaw, pending.Marker, sourceId, bodyCodeRaw, marker);

    private static bool MatchesInlineRecoveryGroup(
        int pendingSourceId,
        uint pendingBodyCodeRaw,
        int pendingMarker,
        int sourceId,
        uint bodyCodeRaw,
        int marker) =>
        pendingSourceId == sourceId &&
        pendingBodyCodeRaw == bodyCodeRaw &&
        pendingMarker == marker;

    private static bool MatchesRecoveryOpener(in PendingCompactOpener pending, int sourceId) =>
        pending.Mode is 0 or 12 &&
        pending.Flag == 0 &&
        pending.EchoSourceId == sourceId;

    private static bool IsPrimaryControlOpener(in PendingCompactOpener pending) =>
        pending.Flag == 2;

    private static bool IsCompactControlOpener(int sourceId, in CombatObservation observation) =>
        sourceId > 0 &&
        observation.BodyCodeRaw > 0 &&
        observation.Marker > 0 &&
        observation.Damage == 0 &&
        observation.HitCount == 0 &&
        observation.AttemptCount == 0 &&
        observation.LayoutTag == 0;

    private static bool IsCompactControlCloser(int sourceId, in CombatObservation observation) =>
        sourceId > 0 &&
        observation.BodyResourceEffectRef.RawId > 0 &&
        observation.Marker > 0 &&
        observation.Damage == 0 &&
        observation.HitCount == 0 &&
        observation.AttemptCount == 0 &&
        observation.LayoutTag == 0;

    private static bool IsCompactDirectValueShape(in CombatObservation observation) =>
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

    private static bool IsInlineDirectValueSidecarShape(in CombatObservation observation) =>
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

    private void TrimPendingOpeners()
    {
        while (_pendingOpeners.Count > MaxPendingOpeners)
            _pendingOpeners.RemoveAt(0);
    }

    private void TrimClosedOpeners()
    {
        while (_closedOpeners.Count > MaxPendingOpeners)
            _closedOpeners.RemoveAt(0);
    }

    private void ClearPendingAssociations()
    {
        _pendingOpeners.Clear();
        _closedOpeners.Clear();
        _pendingSidecars.Clear();
        _inlineRecoveryGroups.Clear();
    }

    private StampedCombatCanonicalizationBatch TrimPendingValues()
    {
        if (_pendingValues.Count <= MaxPendingValues)
            return StampedCombatCanonicalizationBatch.Empty;

        var oldest = _pendingValues[0];
        _pendingValues.RemoveAt(0);
        return StampedCombatCanonicalizationBatch.One(CreateResult(in oldest, asHealing: false, CombatContributionCanonicalization.CompactDirectValue));
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

}

internal sealed record CompactDirectValueCanonicalizerSnapshot(
    CompactDirectValueCanonicalizer.PendingCompactOpener[] PendingOpeners,
    CompactDirectValueCanonicalizer.PendingCompactOpener[] ClosedOpeners,
    CompactDirectValueCanonicalizer.PendingCompactValue[] PendingValues,
    CompactDirectValueCanonicalizer.PendingCompactSidecar[] PendingSidecars,
    CompactDirectValueCanonicalizer.PendingCompactInlineRecoveryGroup[] InlineRecoveryGroups);
