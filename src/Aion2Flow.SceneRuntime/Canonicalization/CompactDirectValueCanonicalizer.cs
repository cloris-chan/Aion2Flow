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
    private const int MaxSamePayloadSelfRecoveryGroups = 128;
    private const int MaxSamePayloadSelfPairRecoveryGroups = 128;

    internal readonly record struct PendingCompactOpener(int SourceId, uint BodyCodeRaw, int Marker, int Mode, int Flag, int EchoSourceId, int MatchedValueCount);
    internal readonly record struct PendingCompactValue(int SourceId, int TargetId, uint BodyCodeRaw, int Marker, TimelineStamp Stamp, long ObservedAtMilliseconds, RawPacketReference Raw, CombatWireObservation Observation);
    internal readonly record struct PendingCompactSidecar(int SourceId, int TargetId, uint BodyCodeRaw, int Marker);
    internal readonly record struct PendingCompactInlineRecoveryGroup(int SourceId, uint BodyCodeRaw, int Marker);
    internal readonly record struct PendingSamePayloadSelfRecoveryGroup(int SourceId, uint BodyCodeRaw, int Marker, uint DetailRefBase, PacketStructureKind ParentKind, int ParentScopeId);
    internal readonly record struct PendingSamePayloadSelfPairRecoveryGroup(int SourceId, uint BodyCodeRaw, int Marker, uint FirstDetailRef, uint SecondDetailRef, PacketStructureKind ParentKind, int ParentScopeId);
    private readonly List<PendingCompactOpener> _pendingOpeners = new(MaxPendingOpeners);
    private readonly List<PendingCompactOpener> _closedOpeners = new(MaxPendingOpeners);
    private readonly List<PendingCompactValue> _pendingValues = new(MaxPendingValues);
    private readonly List<PendingCompactSidecar> _pendingSidecars = new(MaxPendingSidecars);
    private readonly List<PendingCompactInlineRecoveryGroup> _inlineRecoveryGroups = new(MaxConfirmedInlineRecoveryGroups);
    private readonly List<PendingSamePayloadSelfRecoveryGroup> _samePayloadSelfRecoveryGroups = new(MaxSamePayloadSelfRecoveryGroups);
    private readonly List<PendingSamePayloadSelfPairRecoveryGroup> _samePayloadSelfPairRecoveryGroups = new(MaxSamePayloadSelfPairRecoveryGroups);

    internal bool TryObserveCompactValue0438(int sourceId, int targetId, in TimelineStamp stamp, in CombatWireObservation observation, long observedAtMilliseconds, RawPacketReference raw, out StampedCombatCanonicalizationBatch results)
    {
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

            var packetRule = matchesRecovery ? CombatPacketRule.CompactRecovery : CombatPacketRule.CompactDirectValue;
            results = StampedCombatCanonicalizationBatch.One(new StampedCombatCanonicalizationResult(
                sourceId,
                targetId,
                stamp,
                observedAtMilliseconds,
                raw,
                observation,
                packetRule,
                CombatMaterializationKind.CompactAssociated,
                CombatAssociationKind.CompactOpener));
            MarkOpenerMatched(openerIndex, results.Count);
            return true;
        }

        if (targetId == sourceId &&
            TryConsumeClosedRecoveryOpener(sourceId, bodyCodeRaw, observation.Marker))
        {
            results = StampedCombatCanonicalizationBatch.One(new StampedCombatCanonicalizationResult(sourceId, targetId, stamp, observedAtMilliseconds, raw, observation, CombatPacketRule.CompactRecovery, CombatMaterializationKind.CompactAssociated, CombatAssociationKind.CompactOpener));
            return true;
        }

        if (TryFindInlineRecoveryGroup(sourceId, bodyCodeRaw, observation.Marker, out _))
        {
            results = StampedCombatCanonicalizationBatch.One(new StampedCombatCanonicalizationResult(sourceId, targetId, stamp, observedAtMilliseconds, raw, observation, CombatPacketRule.CompactRecovery, CombatMaterializationKind.CompactAssociated, CombatAssociationKind.CompactInlineRecoveryGroup));
            return true;
        }

        if (TryFindSamePayloadSelfRecoveryGroup(sourceId, bodyCodeRaw, observation.Marker, in observation, raw, out _))
        {
            results = StampedCombatCanonicalizationBatch.One(new StampedCombatCanonicalizationResult(sourceId, targetId, stamp, observedAtMilliseconds, raw, observation, CombatPacketRule.CompactRecovery, CombatMaterializationKind.CompactAssociated, CombatAssociationKind.CompactSelfValueGroup));
            return true;
        }

        if (TryFindSamePayloadSelfPairRecoveryGroup(sourceId, targetId, bodyCodeRaw, observation.Marker, in observation, raw, out _))
        {
            results = StampedCombatCanonicalizationBatch.One(new StampedCombatCanonicalizationResult(sourceId, targetId, stamp, observedAtMilliseconds, raw, observation, CombatPacketRule.CompactRecovery, CombatMaterializationKind.CompactAssociated, CombatAssociationKind.CompactSelfValueGroup));
            return true;
        }

        return ObservePendingCompactValue(sourceId, targetId, bodyCodeRaw, in stamp, in observation, observedAtMilliseconds, raw, out results);
    }

    private bool ObservePendingCompactValue(int sourceId, int targetId, uint bodyCodeRaw, in TimelineStamp stamp, in CombatWireObservation observation, long observedAtMilliseconds, RawPacketReference raw, out StampedCombatCanonicalizationBatch results)
    {
        var pending = new PendingCompactValue(sourceId, targetId, bodyCodeRaw, observation.Marker, stamp, observedAtMilliseconds, raw, observation);
        _pendingValues.Add(pending);
        results = targetId == sourceId && TryConfirmInlineRecoveryGroupFromSelfValue(in pending, out var inlineGroup)
            ? FlushValuesMatchedBy(in inlineGroup)
            : StampedCombatCanonicalizationBatch.Empty;
        if (TryConfirmSamePayloadSelfRecoveryGroup(in pending, out var selfValueGroup))
            results = Append(results, FlushValuesMatchedBy(in selfValueGroup));
        if (TryConfirmSamePayloadSelfPairRecoveryGroup(in pending, out var selfPairGroup))
            results = Append(results, FlushValuesMatchedBy(in selfPairGroup));
        results = Append(results, TrimPendingValues());
        return true;
    }

    internal StampedCombatCanonicalizationBatch ObserveCompactValueSidecar0438(int sourceId, int targetId, in CombatWireObservation observation)
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

    internal StampedCombatCanonicalizationBatch ObserveCompactControl0238(int sourceId, in CombatWireObservation observation)
    {
        if (!IsCompactControlOpener(sourceId, in observation))
            return StampedCombatCanonicalizationBatch.Empty;

        var opener = new PendingCompactOpener(sourceId, observation.BodyCodeRaw, observation.Marker, observation.Type, observation.Flag, observation.ChainId, MatchedValueCount: 0);
        _pendingOpeners.Add(opener);
        TrimPendingOpeners();
        var results = FlushValuesMatchedBy(in opener);
        MarkOpenerMatched(_pendingOpeners.Count - 1, results.Count);
        return results;
    }

    internal StampedCombatCanonicalizationBatch ObserveCompactControl0638(int sourceId, in CombatWireObservation observation)
    {
        if (IsCompactControlCloser(sourceId, in observation))
            ClosePendingOpener(sourceId, observation.BodyResourceEffectRef.RawId, observation.Marker);

        return StampedCombatCanonicalizationBatch.Empty;
    }

    internal StampedCombatCanonicalizationBatch FlushPending()
    {
        var results = StampedCombatCanonicalizationBatch.Empty;
        if (_pendingValues.Count > 0)
        {
            var builder = new StampedCombatCanonicalizationBatchBuilder(_pendingValues.Count);
            foreach (var pending in _pendingValues)
                builder.Add(CreateResult(in pending, asHealing: false, CombatAssociationKind.None));

            _pendingValues.Clear();
            results = builder.ToBatch();
        }

        ClearFlushScopedAssociations();
        return results;
    }

    internal void ResetPendingAssociations()
    {
        ClearPendingAssociations();
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

    internal CompactDirectValueCanonicalizerSnapshot CreateSnapshot() => new([.. _pendingOpeners], [.. _closedOpeners], [.. _pendingValues], [.. _pendingSidecars], [.. _inlineRecoveryGroups], [.. _samePayloadSelfRecoveryGroups], [.. _samePayloadSelfPairRecoveryGroups]);

    internal static CompactDirectValueCanonicalizer FromSnapshot(CompactDirectValueCanonicalizerSnapshot snapshot)
    {
        var canonicalizer = new CompactDirectValueCanonicalizer();
        canonicalizer._pendingOpeners.AddRange(snapshot.PendingOpeners);
        canonicalizer._closedOpeners.AddRange(snapshot.ClosedOpeners);
        canonicalizer._pendingValues.AddRange(snapshot.PendingValues);
        canonicalizer._pendingSidecars.AddRange(snapshot.PendingSidecars);
        canonicalizer._inlineRecoveryGroups.AddRange(snapshot.InlineRecoveryGroups);
        canonicalizer._samePayloadSelfRecoveryGroups.AddRange(snapshot.SamePayloadSelfRecoveryGroups);
        canonicalizer._samePayloadSelfPairRecoveryGroups.AddRange(snapshot.SamePayloadSelfPairRecoveryGroups);
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
            results.Add(CreateResult(in pending, matchesRecovery, CombatAssociationKind.CompactOpener));
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

            results.Add(CreateResult(in pending, asHealing: true, CombatAssociationKind.CompactInlineRecoveryGroup));
            _pendingValues.RemoveAt(i);
        }

        return results.ToBatch();
    }

    private StampedCombatCanonicalizationBatch FlushValuesMatchedBy(in PendingSamePayloadSelfRecoveryGroup group)
    {
        if (_pendingValues.Count == 0)
            return StampedCombatCanonicalizationBatch.Empty;

        var results = new StampedCombatCanonicalizationBatchBuilder(_pendingValues.Count);
        for (var i = 0; i < _pendingValues.Count;)
        {
            var pending = _pendingValues[i];
            if (!MatchesSamePayloadSelfRecoveryGroup(in group, in pending))
            {
                i++;
                continue;
            }

            results.Add(CreateResult(in pending, asHealing: true, CombatAssociationKind.CompactSelfValueGroup));
            _pendingValues.RemoveAt(i);
        }

        return results.ToBatch();
    }

    private StampedCombatCanonicalizationBatch FlushValuesMatchedBy(in PendingSamePayloadSelfPairRecoveryGroup group)
    {
        if (_pendingValues.Count == 0)
            return StampedCombatCanonicalizationBatch.Empty;

        var results = new StampedCombatCanonicalizationBatchBuilder(_pendingValues.Count);
        for (var i = 0; i < _pendingValues.Count;)
        {
            var pending = _pendingValues[i];
            if (!MatchesSamePayloadSelfPairRecoveryGroup(in group, in pending))
            {
                i++;
                continue;
            }

            results.Add(CreateResult(in pending, asHealing: true, CombatAssociationKind.CompactSelfValueGroup));
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

    private void ClosePendingOpener(int sourceId, uint bodyCodeRaw, int marker)
    {
        for (var i = _pendingOpeners.Count - 1; i >= 0; i--)
        {
            var pending = _pendingOpeners[i];
            if (!MatchesOpener(in pending, sourceId, bodyCodeRaw, marker))
                continue;

            if (pending.MatchedValueCount == 0)
            {
                _closedOpeners.Add(pending);
                TrimClosedOpeners();
            }

            _pendingOpeners.RemoveAt(i);
            return;
        }
    }

    private bool TryConsumeClosedRecoveryOpener(int sourceId, uint bodyCodeRaw, int marker)
    {
        for (var i = _closedOpeners.Count - 1; i >= 0; i--)
        {
            var pending = _closedOpeners[i];
            if (MatchesOpener(in pending, sourceId, bodyCodeRaw, marker) &&
                MatchesRecoveryOpener(in pending, sourceId))
            {
                _closedOpeners.RemoveAt(i);
                return true;
            }
        }

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

    private bool TryFindSamePayloadSelfRecoveryGroup(int sourceId, uint bodyCodeRaw, int marker, in CombatWireObservation observation, RawPacketReference raw, out PendingSamePayloadSelfRecoveryGroup group)
    {
        if (!TryCreateSamePayloadSelfRecoveryGroup(sourceId, bodyCodeRaw, marker, in observation, raw, out var candidateGroup))
        {
            group = default;
            return false;
        }

        for (var i = _samePayloadSelfRecoveryGroups.Count - 1; i >= 0; i--)
        {
            var pending = _samePayloadSelfRecoveryGroups[i];
            if (pending == candidateGroup)
            {
                group = pending;
                return true;
            }
        }

        group = default;
        return false;
    }

    private bool TryFindSamePayloadSelfPairRecoveryGroup(int sourceId, int targetId, uint bodyCodeRaw, int marker, in CombatWireObservation observation, RawPacketReference raw, out PendingSamePayloadSelfPairRecoveryGroup group)
    {
        if (!IsSelfLoop2Value(sourceId, targetId, in observation))
        {
            group = default;
            return false;
        }

        for (var i = _samePayloadSelfPairRecoveryGroups.Count - 1; i >= 0; i--)
        {
            var pending = _samePayloadSelfPairRecoveryGroups[i];
            if (MatchesSamePayloadSelfPairRecoveryGroup(in pending, sourceId, targetId, bodyCodeRaw, marker, in observation, raw))
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

    private bool TryConfirmSamePayloadSelfRecoveryGroup(in PendingCompactValue value, out PendingSamePayloadSelfRecoveryGroup group)
    {
        var observation = value.Observation;
        if (!TryCreateSamePayloadSelfRecoveryGroup(value.SourceId, value.BodyCodeRaw, value.Marker, in observation, value.Raw, out group) ||
            !HasMatchingSamePayloadSelfRecoveryCounterpart(in value, in group))
        {
            group = default;
            return false;
        }

        ConfirmSamePayloadSelfRecoveryGroup(in group);
        return true;
    }

    private bool TryConfirmSamePayloadSelfPairRecoveryGroup(in PendingCompactValue value, out PendingSamePayloadSelfPairRecoveryGroup group)
    {
        if (!IsSelfLoop2Value(in value))
        {
            group = default;
            return false;
        }

        for (var i = _pendingValues.Count - 1; i >= 0; i--)
        {
            var candidate = _pendingValues[i];
            if (candidate.Stamp == value.Stamp ||
                !IsSelfLoop2Value(in candidate) ||
                !TryCreateSamePayloadSelfPairRecoveryGroup(in value, in candidate, out group))
            {
                continue;
            }

            ConfirmSamePayloadSelfPairRecoveryGroup(in group);
            var observation = value.Observation;
            if (TryCreateSamePayloadSelfRecoveryGroup(value.SourceId, value.BodyCodeRaw, value.Marker, in observation, value.Raw, out var samePayloadGroup))
                ConfirmSamePayloadSelfRecoveryGroup(in samePayloadGroup);
            return true;
        }

        group = default;
        return false;
    }

    private void ConfirmInlineRecoveryGroup(in PendingCompactInlineRecoveryGroup group)
    {
        if (TryFindInlineRecoveryGroup(group.SourceId, group.BodyCodeRaw, group.Marker, out _))
            return;

        _inlineRecoveryGroups.Add(group);
        TrimInlineRecoveryGroups();
    }

    private void ConfirmSamePayloadSelfRecoveryGroup(in PendingSamePayloadSelfRecoveryGroup group)
    {
        if (TryFindSamePayloadSelfRecoveryGroup(in group, out _))
            return;

        _samePayloadSelfRecoveryGroups.Add(group);
        TrimSamePayloadSelfRecoveryGroups();
    }

    private void ConfirmSamePayloadSelfPairRecoveryGroup(in PendingSamePayloadSelfPairRecoveryGroup group)
    {
        if (TryFindSamePayloadSelfPairRecoveryGroup(in group, out _))
            return;

        _samePayloadSelfPairRecoveryGroups.Add(group);
        TrimSamePayloadSelfPairRecoveryGroups();
    }

    private bool TryFindSamePayloadSelfRecoveryGroup(in PendingSamePayloadSelfRecoveryGroup group, out PendingSamePayloadSelfRecoveryGroup match)
    {
        for (var i = _samePayloadSelfRecoveryGroups.Count - 1; i >= 0; i--)
        {
            var pending = _samePayloadSelfRecoveryGroups[i];
            if (pending == group)
            {
                match = pending;
                return true;
            }
        }

        match = default;
        return false;
    }

    private bool TryFindSamePayloadSelfPairRecoveryGroup(in PendingSamePayloadSelfPairRecoveryGroup group, out PendingSamePayloadSelfPairRecoveryGroup match)
    {
        for (var i = _samePayloadSelfPairRecoveryGroups.Count - 1; i >= 0; i--)
        {
            var pending = _samePayloadSelfPairRecoveryGroups[i];
            if (pending == group)
            {
                match = pending;
                return true;
            }
        }

        match = default;
        return false;
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

    private bool HasMatchingSamePayloadSelfRecoveryCounterpart(in PendingCompactValue value, in PendingSamePayloadSelfRecoveryGroup group)
    {
        var valueIsSelf = value.TargetId == value.SourceId;
        for (var i = _pendingValues.Count - 1; i >= 0; i--)
        {
            var candidate = _pendingValues[i];
            var candidateIsSelf = candidate.TargetId == candidate.SourceId;
            if (candidateIsSelf == valueIsSelf ||
                !MatchesSamePayloadSelfRecoveryGroup(in group, in candidate))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static StampedCombatCanonicalizationResult CreateResult(in PendingCompactValue pending, bool asHealing, CombatAssociationKind association)
    {
        var observation = pending.Observation;
        var packetRule = asHealing ? CombatPacketRule.CompactRecovery : CombatPacketRule.CompactDirectValue;
        return new StampedCombatCanonicalizationResult(
            pending.SourceId,
            pending.TargetId,
            pending.Stamp,
            pending.ObservedAtMilliseconds,
            pending.Raw,
            observation,
            packetRule,
            CombatMaterializationKind.CompactAssociated,
            association);
    }

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

    private static bool TryCreateSamePayloadSelfRecoveryGroup(int sourceId, uint bodyCodeRaw, int marker, in CombatWireObservation observation, RawPacketReference raw, out PendingSamePayloadSelfRecoveryGroup group)
    {
        var parent = raw.StructurePath.Parent;
        var detailRefBase = observation.DetailResourceEffectRef.RawId / 10;
        if (sourceId <= 0 ||
            bodyCodeRaw == 0 ||
            marker <= 0 ||
            detailRefBase == 0 ||
            parent.Kind != PacketStructureKind.CompressedPayload ||
            parent.ScopeId == 0)
        {
            group = default;
            return false;
        }

        group = new PendingSamePayloadSelfRecoveryGroup(sourceId, bodyCodeRaw, marker, detailRefBase, parent.Kind, parent.ScopeId);
        return true;
    }

    private static bool MatchesSamePayloadSelfRecoveryGroup(in PendingSamePayloadSelfRecoveryGroup group, in PendingCompactValue pending)
    {
        var parent = pending.Raw.StructurePath.Parent;
        return pending.SourceId == group.SourceId &&
               pending.BodyCodeRaw == group.BodyCodeRaw &&
               pending.Marker == group.Marker &&
               pending.Observation.DetailResourceEffectRef.RawId / 10 == group.DetailRefBase &&
               parent.Kind == group.ParentKind &&
               parent.ScopeId == group.ParentScopeId;
    }

    private static bool TryCreateSamePayloadSelfPairRecoveryGroup(in PendingCompactValue first, in PendingCompactValue second, out PendingSamePayloadSelfPairRecoveryGroup group)
    {
        var firstParent = first.Raw.StructurePath.Parent;
        var secondParent = second.Raw.StructurePath.Parent;
        var firstDetailRef = first.Observation.DetailResourceEffectRef.RawId;
        var secondDetailRef = second.Observation.DetailResourceEffectRef.RawId;
        if (first.SourceId != second.SourceId ||
            first.BodyCodeRaw != second.BodyCodeRaw ||
            first.Marker != second.Marker ||
            firstDetailRef == 0 ||
            secondDetailRef == 0 ||
            firstDetailRef == secondDetailRef ||
            firstDetailRef / 10 != secondDetailRef / 10 ||
            firstParent.Kind != PacketStructureKind.CompressedPayload ||
            firstParent.ScopeId == 0 ||
            firstParent.Kind != secondParent.Kind ||
            firstParent.ScopeId != secondParent.ScopeId)
        {
            group = default;
            return false;
        }

        group = firstDetailRef < secondDetailRef
            ? new PendingSamePayloadSelfPairRecoveryGroup(first.SourceId, first.BodyCodeRaw, first.Marker, firstDetailRef, secondDetailRef, firstParent.Kind, firstParent.ScopeId)
            : new PendingSamePayloadSelfPairRecoveryGroup(first.SourceId, first.BodyCodeRaw, first.Marker, secondDetailRef, firstDetailRef, firstParent.Kind, firstParent.ScopeId);
        return true;
    }

    private static bool MatchesSamePayloadSelfPairRecoveryGroup(in PendingSamePayloadSelfPairRecoveryGroup group, in PendingCompactValue pending)
    {
        var observation = pending.Observation;
        return MatchesSamePayloadSelfPairRecoveryGroup(in group, pending.SourceId, pending.TargetId, pending.BodyCodeRaw, pending.Marker, in observation, pending.Raw);
    }

    private static bool MatchesSamePayloadSelfPairRecoveryGroup(in PendingSamePayloadSelfPairRecoveryGroup group, int sourceId, int targetId, uint bodyCodeRaw, int marker, in CombatWireObservation observation, RawPacketReference raw)
    {
        var parent = raw.StructurePath.Parent;
        var detailRef = observation.DetailResourceEffectRef.RawId;
        return IsSelfLoop2Value(sourceId, targetId, in observation) &&
               sourceId == group.SourceId &&
               bodyCodeRaw == group.BodyCodeRaw &&
               marker == group.Marker &&
               (detailRef == group.FirstDetailRef || detailRef == group.SecondDetailRef) &&
               parent.Kind == group.ParentKind &&
               parent.ScopeId == group.ParentScopeId;
    }

    private static bool IsSelfLoop2Value(in PendingCompactValue value)
    {
        var observation = value.Observation;
        return IsSelfLoop2Value(value.SourceId, value.TargetId, in observation);
    }

    private static bool IsSelfLoop2Value(int sourceId, int targetId, in CombatWireObservation observation) =>
        sourceId > 0 &&
        targetId == sourceId &&
        observation.Loop == 2;

    private static bool MatchesRecoveryOpener(in PendingCompactOpener pending, int sourceId) =>
        pending.Mode is 0 or 8 or 12 &&
        pending.Flag == 0 &&
        pending.EchoSourceId == sourceId;

    private static bool IsPrimaryControlOpener(in PendingCompactOpener pending) =>
        pending.Flag == 2;

    private static bool IsCompactControlOpener(int sourceId, in CombatWireObservation observation) =>
        sourceId > 0 &&
        observation.BodyCodeRaw > 0 &&
        observation.Marker > 0 &&
        observation.Damage == 0 &&
        observation.HitCount == 0 &&
        observation.AttemptCount == 0 &&
        observation.LayoutTag == 0;

    private static bool IsCompactControlCloser(int sourceId, in CombatWireObservation observation) =>
        sourceId > 0 &&
        observation.BodyResourceEffectRef.RawId > 0 &&
        observation.Marker > 0 &&
        observation.Damage == 0 &&
        observation.HitCount == 0 &&
        observation.AttemptCount == 0 &&
        observation.LayoutTag == 0;

    private static bool IsCompactDirectValueShape(in CombatWireObservation observation) =>
        observation.Damage > 0 &&
        observation.HitCount == 1 &&
        observation.AttemptCount == 1 &&
        observation.ResourceKind == CombatResourceKind.Unknown &&
        observation.PeriodicRelation == PeriodicEffectRelation.None &&
        observation.OutcomeKind == CombatWireOutcomeKind.None &&
        observation.LayoutTag == 4 &&
        observation.Flag == 0 &&
        observation.Type == 2 &&
        observation.Loop is 1 or 2 &&
        observation.ChainId > 0 &&
        observation.BodySkillVariantRaw > 0 &&
        observation.Marker > 0;

    private static bool IsInlineDirectValueSidecarShape(in CombatWireObservation observation) =>
        observation.Damage == 0 &&
        observation.HitCount == 0 &&
        observation.AttemptCount == 0 &&
        observation.ResourceKind == CombatResourceKind.Unknown &&
        observation.PeriodicRelation == PeriodicEffectRelation.None &&
        observation.OutcomeKind == CombatWireOutcomeKind.None &&
        observation.LayoutTag == 0 &&
        observation.Flag == 0 &&
        observation.Type is 0 or 2 &&
        observation.Loop == 0 &&
        observation.ChainId == 0 &&
        observation.BodySkillVariantRaw > 0 &&
        observation.Marker > 0;

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
        ClearFlushScopedAssociations();
    }

    private void ClearFlushScopedAssociations()
    {
        _pendingSidecars.Clear();
        _inlineRecoveryGroups.Clear();
        _samePayloadSelfRecoveryGroups.Clear();
        _samePayloadSelfPairRecoveryGroups.Clear();
    }

    private StampedCombatCanonicalizationBatch TrimPendingValues()
    {
        if (_pendingValues.Count <= MaxPendingValues)
            return StampedCombatCanonicalizationBatch.Empty;

        var oldest = _pendingValues[0];
        _pendingValues.RemoveAt(0);
        return StampedCombatCanonicalizationBatch.One(CreateResult(in oldest, asHealing: false, CombatAssociationKind.None));
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

    private void TrimSamePayloadSelfRecoveryGroups()
    {
        while (_samePayloadSelfRecoveryGroups.Count > MaxSamePayloadSelfRecoveryGroups)
            _samePayloadSelfRecoveryGroups.RemoveAt(0);
    }

    private void TrimSamePayloadSelfPairRecoveryGroups()
    {
        while (_samePayloadSelfPairRecoveryGroups.Count > MaxSamePayloadSelfPairRecoveryGroups)
            _samePayloadSelfPairRecoveryGroups.RemoveAt(0);
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
    CompactDirectValueCanonicalizer.PendingCompactInlineRecoveryGroup[] InlineRecoveryGroups,
    CompactDirectValueCanonicalizer.PendingSamePayloadSelfRecoveryGroup[] SamePayloadSelfRecoveryGroups,
    CompactDirectValueCanonicalizer.PendingSamePayloadSelfPairRecoveryGroup[] SamePayloadSelfPairRecoveryGroups);
