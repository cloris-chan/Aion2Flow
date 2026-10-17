using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class SystemPeriodicRecoveryCanonicalizer
{
    private readonly record struct Key(int SourceId, int TargetId, int ChainId, int TailSkillCodeRaw);
    private readonly record struct State(long Damage);
    private readonly Dictionary<Key, State> _seeds = [];

    public CombatCanonicalizationResult Normalize(int sourceId, int targetId, in CombatWireObservation observation)
    {
        if (!TryGetKey(sourceId, targetId, in observation, out var key, out var isSeed))
            return new CombatCanonicalizationResult(sourceId, targetId, observation);

        if (isSeed)
        {
            _seeds[key] = new State(observation.Damage);
            return new CombatCanonicalizationResult(
                sourceId,
                targetId,
                observation,
                CombatPacketRule.None,
                suppression: CombatSuppressionReason.SystemPeriodicRecoverySeed);
        }

        if (!_seeds.TryGetValue(key, out var state))
            return new CombatCanonicalizationResult(sourceId, targetId, observation);

        _seeds.Remove(key);
        if (observation.Damage != state.Damage)
            return new CombatCanonicalizationResult(sourceId, targetId, observation);

        return new CombatCanonicalizationResult(
            sourceId,
            targetId,
            observation,
            CombatPacketRule.PeriodicRecovery,
            CombatMaterializationKind.PeriodicRecovery);
    }

    private static bool TryGetKey(int sourceId, int targetId, in CombatWireObservation observation, out Key key, out bool isSeed)
    {
        key = default;
        isSeed = false;

        if (sourceId <= 0 ||
            targetId <= 0 ||
            sourceId != targetId ||
            observation.Damage <= 0 ||
            observation.PeriodicRelation != PeriodicEffectRelation.Self ||
            observation.PeriodicMode is not (1 or 2) ||
            observation.ChainId == 0)
            return false;

        key = new Key(sourceId, targetId, observation.ChainId, observation.PeriodicTailSkillCodeRaw);
        isSeed = observation.PeriodicMode == 1;
        return true;
    }
}
