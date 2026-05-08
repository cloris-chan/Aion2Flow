using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class CompactOutcomeCanonicalizer
{
    private const int MaxPendingAvoidances = 32;
    private readonly record struct PendingDirectBlockedDamage(int SourceId, int TargetId, TimelineStamp Stamp, long ObservedAtMilliseconds, CombatObservation Observation);
    private readonly record struct PendingCompactAvoidance(int SourceId, int TargetId, int OriginalSkillCode, int Marker, TimelineStamp Stamp, long ObservedAtMilliseconds);
    private readonly record struct AvoidedSignature(int SourceId, int TargetId, int Marker);
    private readonly List<PendingDirectBlockedDamage> _pendingDirect = [];
    private readonly List<PendingCompactAvoidance> _pendingCompact = [];
    private readonly List<PendingCompactAvoidance> _pendingCompactDamage = [];
    private readonly List<PendingCompactAvoidance> _pendingCompactControls0638 = [];
    private readonly List<StampedCombatCanonicalizationResult> _storedDamage = [];
    private readonly HashSet<int> _currentBatchDodgeTargets = [];
    private readonly HashSet<AvoidedSignature> _resolvedAvoidanceSignatures = [];
    private readonly HashSet<(int TargetId, int SkillCode)> _confirmedCompactDamage = [];
    private long _currentBatchOrdinal;

    public IReadOnlyList<StampedCombatCanonicalizationResult> NormalizeCombat(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds = 0)
    {
        var prefix = EnsureBatch(stamp.BatchOrdinal);

        if (TryObserveDirectBlockedDamage(sourceId, targetId, in stamp, in observation, observedAtMilliseconds))
            return prefix;

        var result = new StampedCombatCanonicalizationResult(sourceId, targetId, stamp, observedAtMilliseconds, observation);
        TrackStored(in result);
        return Append(prefix, result);
    }

    public IReadOnlyList<StampedCombatCanonicalizationResult> ObserveCompactValue0438(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds = 0)
    {
        var isCompactSignal = IsCompactSignalShape(in observation) && observation.EventKind == CombatEventKind.Unknown && observation.ValueKind == CombatValueKind.Unknown;
        if (!isCompactSignal)
        {
            var directPrefix = EnsureBatch(stamp.BatchOrdinal);
            var directResult = new StampedCombatCanonicalizationResult(sourceId, targetId, stamp, observedAtMilliseconds, observation);
            TrackStored(in directResult);
            return Append(directPrefix, directResult);
        }

        if (IsCompactType2Sidecar(in observation) && IsCompactDamageConfirmation(sourceId, targetId, in observation))
        {
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

    public IReadOnlyList<StampedCombatCanonicalizationResult> ObserveCompactControl0238(int sourceId, in TimelineStamp stamp, in CombatObservation observation)
    {
        var prefix = EnsureBatch(stamp.BatchOrdinal);
        ObserveDodgeSignal(sourceId, in observation);
        return prefix;
    }

    public IReadOnlyList<StampedCombatCanonicalizationResult> ObserveCompactControl0638(int sourceId, in TimelineStamp stamp, in CombatObservation observation, long observedAtMilliseconds = 0)
    {
        var prefix = EnsureBatch(stamp.BatchOrdinal);
        if (sourceId > 0 && observation.Marker > 0 && observation.SkillCode > 0)
            _pendingCompactControls0638.Add(new PendingCompactAvoidance(sourceId, 0, observation.SkillCode, observation.Marker, stamp, observedAtMilliseconds));

        ObserveDodgeSignal(sourceId, in observation);
        return prefix;
    }

    public IReadOnlyList<StampedCombatCanonicalizationResult> CompleteBatch(long batchOrdinal)
    {
        if (_currentBatchOrdinal == 0)
            return batchOrdinal == long.MaxValue ? FlushOrphanCompactHits() : [];

        if (batchOrdinal > 0 && _currentBatchOrdinal > 0 && batchOrdinal < _currentBatchOrdinal)
            return [];

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
        return results;
    }

    private IReadOnlyList<StampedCombatCanonicalizationResult> FinalizeAll()
    {
        var results = new List<StampedCombatCanonicalizationResult>();
        var finalized = FinalizeBatch();
        TrackStored(finalized);
        results.AddRange(finalized);
        results.AddRange(FlushOrphanCompactHits());
        return results;
    }

    private IReadOnlyList<StampedCombatCanonicalizationResult> FlushOrphanCompactHits()
    {
        var storedKeys = new HashSet<(long Batch, int Source, int Target, int Marker)>();
        var damageMarkersBySource = new HashSet<(int Source, int Marker)>();
        var lastDamageTargetBySourceBaseSkill = new Dictionary<(int Source, int BaseSkill), int>();
        var damageHitsBySourceBaseSkill = new Dictionary<(int Source, int BaseSkill), int>();
        foreach (var pending in _storedDamage)
        {
            if (pending.Observation.EventKind != CombatEventKind.Damage)
                continue;
            if (pending.SourceId <= 0 || pending.Observation.Marker <= 0)
                continue;

            damageMarkersBySource.Add((pending.SourceId, pending.Observation.Marker));
            var observation = pending.Observation;
            var baseSkill = observation.BaseSkillCode > 0 ? observation.BaseSkillCode : ResolveBaseSkillCode(OriginalSkillCode(in observation));
            if (baseSkill > 0 && pending.TargetId > 0)
            {
                lastDamageTargetBySourceBaseSkill[(pending.SourceId, baseSkill)] = pending.TargetId;
                var key = (pending.SourceId, baseSkill);
                damageHitsBySourceBaseSkill.TryGetValue(key, out var prev);
                damageHitsBySourceBaseSkill[key] = prev + 1;
            }

            if (pending.TargetId <= 0 || pending.SourceId == pending.TargetId)
                continue;

            storedKeys.Add((pending.Stamp.BatchOrdinal, pending.SourceId, pending.TargetId, pending.Observation.Marker));
            storedKeys.Add((pending.Stamp.BatchOrdinal - 1, pending.SourceId, pending.TargetId, pending.Observation.Marker));
            storedKeys.Add((pending.Stamp.BatchOrdinal + 1, pending.SourceId, pending.TargetId, pending.Observation.Marker));
        }

        var results = new List<StampedCombatCanonicalizationResult>();
        foreach (var pending in _pendingCompactDamage)
        {
            if (pending.Marker <= 0)
                continue;

            if (storedKeys.Contains((pending.Stamp.BatchOrdinal, pending.SourceId, pending.TargetId, pending.Marker)))
                continue;

            if (!IsPlayerOrphanItemSkillCandidate(pending.OriginalSkillCode))
                continue;

            results.Add(CreateOrphanCompactHit(in pending, pending.TargetId));
        }

        var coveredMarkers = new HashSet<(int Source, int Marker)>(damageMarkersBySource);
        foreach (var pending in _pendingCompactDamage)
        {
            if (pending.SourceId > 0 && pending.Marker > 0)
                coveredMarkers.Add((pending.SourceId, pending.Marker));
        }

        foreach (var pending in _pendingCompact)
        {
            if (pending.SourceId > 0 && pending.Marker > 0)
                coveredMarkers.Add((pending.SourceId, pending.Marker));
        }

        var seen0638Markers = new HashSet<(int Source, int Marker)>();
        var emittedBySourceBaseSkill = new Dictionary<(int Source, int BaseSkill), int>();
        var totalControlsBySourceBaseSkill = new Dictionary<(int Source, int BaseSkill), int>();
        foreach (var pending in _pendingCompactControls0638)
        {
            if (pending.Marker <= 0 || pending.SourceId <= 0)
                continue;

            var baseSkill = ResolveBaseSkillCode(pending.OriginalSkillCode);
            if (baseSkill <= 0)
                continue;

            var key = (pending.SourceId, baseSkill);
            totalControlsBySourceBaseSkill.TryGetValue(key, out var prev);
            totalControlsBySourceBaseSkill[key] = prev + 1;
        }

        foreach (var pending in _pendingCompactControls0638)
        {
            if (pending.Marker <= 0 || pending.SourceId <= 0)
                continue;

            if (coveredMarkers.Contains((pending.SourceId, pending.Marker)))
                continue;

            if (!IsPlayerOrphanItemSkillCandidate(pending.OriginalSkillCode))
                continue;

            if (!seen0638Markers.Add((pending.SourceId, pending.Marker)))
                continue;

            var baseSkill = ResolveBaseSkillCode(pending.OriginalSkillCode);
            if (baseSkill <= 0)
                continue;

            if (!lastDamageTargetBySourceBaseSkill.TryGetValue((pending.SourceId, baseSkill), out var targetId) || targetId <= 0)
                continue;

            var key = (pending.SourceId, baseSkill);
            damageHitsBySourceBaseSkill.TryGetValue(key, out var damageCount);
            emittedBySourceBaseSkill.TryGetValue(key, out var emittedCount);
            totalControlsBySourceBaseSkill.TryGetValue(key, out var totalControls);
            if (damageCount + emittedCount >= totalControls)
                continue;

            results.Add(CreateOrphanCompactHit(in pending, targetId));
            emittedBySourceBaseSkill[key] = emittedCount + 1;
        }

        _pendingCompactDamage.Clear();
        _pendingCompactControls0638.Clear();
        _storedDamage.Clear();
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
        TrackStored(results);
        _currentBatchOrdinal = resolvedBatchOrdinal;
        return results;
    }

    private void TrackStored(IReadOnlyList<StampedCombatCanonicalizationResult> results)
    {
        foreach (var result in results)
            TrackStored(in result);
    }

    private void TrackStored(in StampedCombatCanonicalizationResult result)
    {
        if (result.Observation.EventKind == CombatEventKind.Damage)
            _storedDamage.Add(result);
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
        CombatResourceRegistry.NormalizePacketForStorage(packet);
        return FromPacket(packet, in observation);
    }

    private static CombatObservation NormalizeBaseObservation(int sourceId, int targetId, in CombatObservation observation)
    {
        var packet = ToPacket(sourceId, targetId, in observation);
        CombatResourceRegistry.NormalizePacketForStorage(packet);
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
        CombatResourceRegistry.NormalizePacketForStorage(packet);
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
