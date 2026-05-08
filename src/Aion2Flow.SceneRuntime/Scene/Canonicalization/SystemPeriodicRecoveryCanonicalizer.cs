using Cloris.Aion2Flow.Combat;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Scene.Model;
using Cloris.Aion2Flow.Scene.Observation;

namespace Cloris.Aion2Flow.Scene.Canonicalization;

public sealed class SystemPeriodicRecoveryCanonicalizer
{
    private const int PeriodicSelfRecoveryBaseSkillCode = 190000000;
    private readonly record struct Key(int SourceId, int TargetId, int OriginalSkillCode);
    private readonly record struct State(long Damage, long FrameOrdinal, long BatchOrdinal);
    private readonly Dictionary<Key, State> _seeds = [];

    public CombatCanonicalizationResult Normalize(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation)
    {
        if (!TryGetKey(sourceId, targetId, in observation, out var key, out var isSeed))
            return new CombatCanonicalizationResult(sourceId, targetId, observation);

        if (isSeed)
        {
            _seeds[key] = new State(observation.Damage, stamp.FrameOrdinal, stamp.BatchOrdinal);
            return new CombatCanonicalizationResult(sourceId, targetId, observation with
            {
                EventKind = CombatEventKind.Support,
                ValueKind = CombatValueKind.Support
            });
        }

        if (!_seeds.TryGetValue(key, out var state))
            return new CombatCanonicalizationResult(sourceId, targetId, observation);

        _seeds.Remove(key);
        if (!IsContinuationAfterSeed(in stamp, state) || observation.Damage != state.Damage)
            return new CombatCanonicalizationResult(sourceId, targetId, observation);

        return new CombatCanonicalizationResult(sourceId, targetId, observation with
        {
            EventKind = CombatEventKind.Healing,
            ValueKind = CombatValueKind.PeriodicHealing
        });
    }

    private static bool TryGetKey(int sourceId, int targetId, in CombatObservation observation, out Key key, out bool isSeed)
    {
        key = default;
        isSeed = false;

        if (sourceId <= 0 || targetId <= 0 || sourceId != targetId || observation.Damage <= 0 || observation.PeriodicRelation != PeriodicEffectRelation.Self || observation.PeriodicMode is not (1 or 2))
            return false;

        var originalSkillCode = ResolveOriginalSkillCode(in observation);
        if (originalSkillCode <= 0)
            return false;

        var baseSkillCode = observation.BaseSkillCode > 0 ? observation.BaseSkillCode : CombatResourceRegistry.ParseSkillVariant(originalSkillCode).BaseSkillCode;
        if (baseSkillCode != PeriodicSelfRecoveryBaseSkillCode)
            return false;

        key = new Key(sourceId, targetId, originalSkillCode);
        isSeed = observation.PeriodicMode == 1;
        return true;
    }

    private static bool IsContinuationAfterSeed(in TimelineStamp stamp, State state)
    {
        if (stamp.BatchOrdinal > 0 && state.BatchOrdinal > 0)
            return stamp.BatchOrdinal >= state.BatchOrdinal;

        if (stamp.FrameOrdinal > 0 && state.FrameOrdinal > 0)
            return stamp.FrameOrdinal >= state.FrameOrdinal;

        return true;
    }

    private static int ResolveOriginalSkillCode(in CombatObservation observation) =>
        observation.OriginalSkillCode != 0 ? observation.OriginalSkillCode : observation.SkillCode;
}
