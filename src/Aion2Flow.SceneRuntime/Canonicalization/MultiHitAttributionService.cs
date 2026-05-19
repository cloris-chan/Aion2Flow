using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class MultiHitAttributionService
{
    private const int MaxCandidates = 64;
    private readonly record struct Candidate(int SourceId, int TargetId, int SkillCode, long FrameOrdinal);
    private readonly List<Candidate> _candidates = [];

    internal StateSnapshot CreateStateSnapshot()
    {
        var candidates = new CandidateStateSnapshot[_candidates.Count];
        for (var i = 0; i < candidates.Length; i++)
        {
            var candidate = _candidates[i];
            candidates[i] = new CandidateStateSnapshot(candidate.SourceId, candidate.TargetId, candidate.SkillCode, candidate.FrameOrdinal);
        }
        return new StateSnapshot(candidates);
    }

    internal void RestoreState(StateSnapshot snapshot)
    {
        _candidates.Clear();
        _candidates.EnsureCapacity(snapshot.Candidates.Length);
        foreach (ref readonly var candidate in snapshot.Candidates.AsSpan())
            _candidates.Add(new Candidate(candidate.SourceId, candidate.TargetId, candidate.SkillCode, candidate.FrameOrdinal));
    }

    public void ObserveCombat(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation)
    {
        if (!IsDirectDamageCandidate(sourceId, targetId, in observation) || observation.HitCount == 0)
            return;

        var trackedSkillCode = ResolveTrackedSkillCode(in observation);
        if (trackedSkillCode <= 0 || observation.Marker <= 0)
            return;

        TrimCandidates();
        _candidates.Add(new Candidate(sourceId, targetId, trackedSkillCode, stamp.FrameOrdinal));
        TrimCandidates();
    }

    public CombatCanonicalizationResult? TrySynthesize2C38Invincible(in ObservedEventEnvelope entry, in AuraObservation aura)
    {
        if (aura.Mode != 1 || aura.ResultCode != 11 || entry.TargetEntityId <= 0 || entry.SourceEntityId != entry.TargetEntityId || aura.SkillCode <= 0)
            return null;

        if (!TryResolveDamageTarget(entry.TargetEntityId, aura.SkillCode, entry.Stamp.FrameOrdinal, out var targetId))
            return null;

        var observation = new CombatObservation
        {
            SkillCode = aura.SkillCode,
            OriginalSkillCode = aura.SkillCode,
            Damage = 0,
            HitCount = 0,
            AttemptCount = 0,
            Marker = aura.SequenceId,
            Modifiers = DamageModifiers.Invincible,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage,
            EffectTag = PacketEffectTag.Aux2C38Invincible
        };
        return new CombatCanonicalizationResult(entry.TargetEntityId, targetId, observation);
    }

    private bool TryResolveDamageTarget(int sourceId, int skillCodeRaw, long frameOrdinal, out int targetId)
    {
        targetId = 0;

        var trackedSkillCode = ResolveTrackedSkillCode(skillCodeRaw);
        if (trackedSkillCode <= 0)
            return false;

        for (var i = _candidates.Count - 1; i >= 0; i--)
        {
            var candidate = _candidates[i];
            if (candidate.SourceId != sourceId || candidate.SkillCode != trackedSkillCode || candidate.TargetId <= 0)
                continue;

            if (frameOrdinal > 0 && candidate.FrameOrdinal > 0 && frameOrdinal < candidate.FrameOrdinal)
                continue;

            targetId = candidate.TargetId;
            _candidates.RemoveAt(i);
            return true;
        }

        return false;
    }

    private void TrimCandidates()
    {
        while (_candidates.Count > MaxCandidates)
            _candidates.RemoveAt(0);
    }

    private static bool IsDirectDamageCandidate(int sourceId, int targetId, in CombatObservation observation)
    {
        if (observation.Damage <= 0 || sourceId <= 0 || targetId <= 0 || sourceId == targetId)
            return false;

        return observation.ValueKind is CombatValueKind.Damage or CombatValueKind.DrainDamage or CombatValueKind.Unknown || observation.EventKind == CombatEventKind.Damage;
    }

    private static int ResolveTrackedSkillCode(in CombatObservation observation)
    {
        var originalSkillCode = observation.OriginalSkillCode != 0 ? observation.OriginalSkillCode : observation.SkillCode;
        return ResolveTrackedSkillCode(originalSkillCode);
    }

    private static int ResolveTrackedSkillCode(int skillCode)
    {
        if (skillCode <= 0)
            return 0;

        var variant = CombatResourceRegistry.ParseSkillVariant(skillCode);
        return CombatResourceRegistry.InferOriginalSkillCode(skillCode) ?? variant.NormalizedSkillCode;
    }

    internal sealed class StateSnapshot(CandidateStateSnapshot[] candidates)
    {
        public CandidateStateSnapshot[] Candidates { get; } = candidates;

        public StateSnapshot DeepClone()
        {
            var candidates = new CandidateStateSnapshot[Candidates.Length];
            Array.Copy(Candidates, candidates, candidates.Length);
            return new StateSnapshot(candidates);
        }
    }

    internal readonly record struct CandidateStateSnapshot(int SourceId, int TargetId, int SkillCode, long FrameOrdinal);
}
