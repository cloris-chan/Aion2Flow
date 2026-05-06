using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Scene.Model;
using Cloris.Aion2Flow.Scene.Observation;

namespace Cloris.Aion2Flow.Scene.Canonicalization;

public sealed class CompactOutcomeCanonicalizer
{
    private const int MaxPendingAvoidances = 32;
    private readonly record struct PendingDirectBlockedDamage(int SourceId, int TargetId, TimelineStamp Stamp, CombatObservation Observation);
    private readonly record struct PendingCompactAvoidance(int SourceId, int TargetId, int OriginalSkillCode, int Marker, TimelineStamp Stamp);
    private readonly record struct AvoidedSignature(int SourceId, int TargetId, int Marker);
    private readonly List<PendingDirectBlockedDamage> _pendingDirect = [];
    private readonly List<PendingCompactAvoidance> _pendingCompact = [];
    private readonly HashSet<int> _currentBatchDodgeTargets = [];
    private readonly HashSet<AvoidedSignature> _resolvedAvoidanceSignatures = [];
    private readonly HashSet<(int TargetId, int SkillCode)> _confirmedCompactDamage = [];
    private long _currentBatchOrdinal;

    public IReadOnlyList<StampedCombatCanonicalizationResult> NormalizeCombat(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation)
    {
        var isCompactType2Sidecar = IsCompactType2Sidecar(in observation);
        if (isCompactType2Sidecar && IsCompactDamageConfirmation(sourceId, targetId, in observation))
        {
            _confirmedCompactDamage.Add((targetId, observation.SkillCode));
            CancelPendingCompactEvade(targetId, observation.SkillCode);
        }

        var prefix = EnsureBatch(stamp.BatchOrdinal);

        if (isCompactType2Sidecar)
            return prefix;

        if (TryObserveCompactAvoidance(sourceId, targetId, in stamp, in observation))
            return prefix;

        if (TryObserveDirectBlockedDamage(sourceId, targetId, in stamp, in observation))
            return prefix;

        return Append(prefix, new StampedCombatCanonicalizationResult(sourceId, targetId, stamp, observation));
    }

    public IReadOnlyList<StampedCombatCanonicalizationResult> ObserveCompactControl0238(int sourceId, in TimelineStamp stamp, in CombatObservation observation)
    {
        var prefix = EnsureBatch(stamp.BatchOrdinal);
        ObserveDodgeSignal(sourceId, in observation);
        return prefix;
    }

    public IReadOnlyList<StampedCombatCanonicalizationResult> ObserveCompactControl0638(int sourceId, in TimelineStamp stamp, in CombatObservation observation)
    {
        var prefix = EnsureBatch(stamp.BatchOrdinal);
        ObserveDodgeSignal(sourceId, in observation);
        return prefix;
    }

    public IReadOnlyList<StampedCombatCanonicalizationResult> CompleteBatch(long batchOrdinal)
    {
        if (_currentBatchOrdinal == 0)
            return [];

        if (batchOrdinal > 0 && _currentBatchOrdinal > 0 && batchOrdinal < _currentBatchOrdinal)
            return [];

        return FinalizeBatch();
    }

    private bool TryObserveCompactAvoidance(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation)
    {
        if (!IsCompactEvadeSignal(sourceId, targetId, in observation) || observation.Marker <= 0)
            return false;

        var trackedSkillCode = ResolveTrackedSkillCode(observation.SkillCode);
        if (trackedSkillCode <= 0 || sourceId <= 0 || targetId <= 0 || sourceId == targetId)
            return false;

        var signature = new AvoidedSignature(sourceId, targetId, observation.Marker);
        if (_resolvedAvoidanceSignatures.Contains(signature))
            return true;

        _pendingCompact.Add(new PendingCompactAvoidance(sourceId, targetId, observation.SkillCode, observation.Marker, stamp));
        TrimPending();
        return true;
    }

    private bool TryObserveDirectBlockedDamage(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation)
    {
        if (!IsDirectBlockedDamageCandidate(sourceId, targetId, in observation))
            return false;

        var signature = new AvoidedSignature(sourceId, targetId, observation.Marker);
        if (_resolvedAvoidanceSignatures.Contains(signature))
            return true;

        _pendingDirect.Add(new PendingDirectBlockedDamage(sourceId, targetId, stamp, observation));
        TrimPending();
        return true;
    }

    private IReadOnlyList<StampedCombatCanonicalizationResult> FinalizeBatch()
    {
        var results = new List<StampedCombatCanonicalizationResult>(_pendingDirect.Count + _pendingCompact.Count);

        foreach (var pending in _pendingDirect)
        {
            var signature = new AvoidedSignature(pending.SourceId, pending.TargetId, pending.Observation.Marker);
            if (_resolvedAvoidanceSignatures.Contains(signature))
                continue;

            if (_currentBatchDodgeTargets.Contains(pending.TargetId))
            {
                _resolvedAvoidanceSignatures.Add(signature);
                var observation = pending.Observation;
                results.Add(new StampedCombatCanonicalizationResult(pending.SourceId, pending.TargetId, pending.Stamp, ApplyAvoidedModifier(pending.SourceId, pending.TargetId, in observation, DamageModifiers.Evade, PacketEffectTag.ActiveDodgeEvade)));
            }
            else
            {
                var observation = pending.Observation;
                results.Add(new StampedCombatCanonicalizationResult(pending.SourceId, pending.TargetId, pending.Stamp, NormalizeBaseObservation(pending.SourceId, pending.TargetId, in observation)));
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
            results.Add(new StampedCombatCanonicalizationResult(pending.SourceId, pending.TargetId, pending.Stamp, CreateCompactEvade(in pending)));
        }

        _pendingDirect.Clear();
        _pendingCompact.Clear();
        _currentBatchDodgeTargets.Clear();
        _resolvedAvoidanceSignatures.Clear();
        _currentBatchOrdinal = 0;
        return results;
    }

    private IReadOnlyList<StampedCombatCanonicalizationResult> EnsureBatch(long batchOrdinal)
    {
        var resolvedBatchOrdinal = batchOrdinal > 0 ? batchOrdinal : 0;
        if (_currentBatchOrdinal == 0)
        {
            _currentBatchOrdinal = resolvedBatchOrdinal;
            return [];
        }

        if (resolvedBatchOrdinal == 0 || resolvedBatchOrdinal == _currentBatchOrdinal)
            return [];

        var results = FinalizeBatch();
        _currentBatchOrdinal = resolvedBatchOrdinal;
        return results;
    }

    private static IReadOnlyList<StampedCombatCanonicalizationResult> Append(IReadOnlyList<StampedCombatCanonicalizationResult> prefix, in StampedCombatCanonicalizationResult result)
    {
        if (prefix.Count == 0)
            return [result];

        var results = new List<StampedCombatCanonicalizationResult>(prefix.Count + 1);
        results.AddRange(prefix);
        results.Add(result);
        return results;
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

    private static bool IsCompactDamageConfirmation(int sourceId, int targetId, in CombatObservation observation) =>
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
        var packet = ToPacket(sourceId, targetId, in observation);
        packet.Damage = 0;
        packet.HitContribution = 0;
        packet.AttemptContribution = Math.Max(packet.AttemptContribution, 1);
        packet.Modifiers &= ~(DamageModifiers.Evade | DamageModifiers.Invincible | DamageModifiers.Critical);
        packet.Modifiers |= modifier;
        packet.SetEffectTag(effectTag);
        packet.IsNormalized = false;
        CombatMetricsEngine.NormalizePacketForStorage(packet);
        return FromPacket(packet, in observation);
    }

    private static CombatObservation NormalizeBaseObservation(int sourceId, int targetId, in CombatObservation observation)
    {
        var packet = ToPacket(sourceId, targetId, in observation);
        CombatMetricsEngine.NormalizePacketForStorage(packet);
        return FromPacket(packet, in observation);
    }

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
        var packet = ToPacket(pending.SourceId, pending.TargetId, in observation);
        CombatMetricsEngine.NormalizePacketForStorage(packet);
        return FromPacket(packet, in observation);
    }

    private static ParsedCombatPacket ToPacket(int sourceId, int targetId, in CombatObservation observation)
    {
        var packet = new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = observation.SkillCode,
            OriginalSkillCode = observation.OriginalSkillCode,
            BaseSkillCode = observation.BaseSkillCode,
            Damage = checked((int)observation.Damage),
            HitContribution = observation.HitCount,
            AttemptContribution = observation.AttemptCount,
            DetailRaw = observation.DetailRaw,
            Marker = observation.Marker,
            Type = observation.Type,
            Flag = observation.Flag,
            LayoutTag = observation.LayoutTag,
            Loop = observation.Loop,
            MultiHitCount = observation.MultiHitCount,
            DrainHealAmount = observation.DrainHealAmount,
            RegenerationAmount = observation.RegenerationAmount,
            Modifiers = observation.Modifiers,
            ResourceKind = observation.ResourceKind,
            EventKind = observation.EventKind,
            ValueKind = observation.ValueKind
        };

        if (observation.PeriodicRelation != PeriodicEffectRelation.None)
            packet.SetPeriodicEffect(observation.PeriodicRelation, observation.PeriodicMode);

        if (observation.EffectTag != PacketEffectTag.None)
            packet.SetEffectTag(observation.EffectTag);

        return packet;
    }

    private static CombatObservation FromPacket(ParsedCombatPacket packet, in CombatObservation original) => original with
    {
        SkillCode = packet.SkillCode,
        OriginalSkillCode = packet.OriginalSkillCode,
        BaseSkillCode = packet.BaseSkillCode,
        Damage = packet.Damage,
        HitCount = packet.HitContribution,
        AttemptCount = packet.AttemptContribution,
        DetailRaw = packet.DetailRaw,
        Marker = packet.Marker,
        Type = packet.Type,
        Flag = packet.Flag,
        LayoutTag = packet.LayoutTag,
        Loop = packet.Loop,
        MultiHitCount = packet.MultiHitCount,
        DrainHealAmount = packet.DrainHealAmount,
        RegenerationAmount = packet.RegenerationAmount,
        Modifiers = packet.Modifiers,
        ResourceKind = packet.ResourceKind,
        EventKind = packet.EventKind,
        ValueKind = packet.ValueKind,
        EffectTag = packet.EffectTag,
        PeriodicRelation = packet.PeriodicRelation,
        PeriodicMode = packet.PeriodicMode
    };

    private static int ResolveTrackedSkillCode(int skillCode)
    {
        if (skillCode <= 0)
            return 0;

        var variant = CombatMetricsEngine.ParseSkillVariant(skillCode);
        return CombatMetricsEngine.InferOriginalSkillCode(skillCode) ?? variant.NormalizedSkillCode;
    }

    private static bool IsDodgeSkill(int trackedSkillCode)
    {
        var suffix = trackedSkillCode % 1000000;
        if (suffix != 100)
            return false;
        var classPrefix = trackedSkillCode / 1000000;
        return classPrefix is >= 11 and <= 18;
    }
}

public readonly record struct StampedCombatCanonicalizationResult(int SourceId, int TargetId, TimelineStamp Stamp, CombatObservation Observation);
