using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class OwnerTargetSummonResourceCanonicalizer(EntityStore entities)
{
    public CombatCanonicalizationResult Normalize(int sourceId, int targetId, in CombatWireObservation observation)
    {
        if (!IsOwnerTargetSummonResourceValue(sourceId, targetId, in observation))
            return new CombatCanonicalizationResult(sourceId, targetId, observation);

        return new CombatCanonicalizationResult(
            sourceId,
            targetId,
            observation,
            CombatPacketRule.None,
            suppression: CombatSuppressionReason.OwnerTargetSummonResource);
    }

    private bool IsOwnerTargetSummonResourceValue(int sourceId, int targetId, in CombatWireObservation observation)
    {
        if (sourceId <= 0 ||
            targetId <= 0 ||
            observation.Damage <= 0 ||
            observation.PeriodicRelation != PeriodicEffectRelation.None ||
            observation.LayoutTag != 4 ||
            observation.Flag != 0 ||
            observation.Type != 2 ||
            observation.Loop != 1 ||
            (observation.HitCount <= 0 && observation.AttemptCount <= 0))
            return false;

        return entities.TryGet(sourceId, out var source) && source.OwnerKind == EntityOwnerKind.Summon && source.OwnerEntityId == targetId ||
               entities.TryGet(targetId, out var target) && target.OwnerKind == EntityOwnerKind.Summon && target.OwnerEntityId == sourceId;
    }
}
