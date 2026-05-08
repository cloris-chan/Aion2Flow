using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Scene.Stores;

namespace Cloris.Aion2Flow.Scene.Canonicalization;

public sealed class OwnerTargetSummonRestoreCanonicalizer(EntityStore entities)
{
    private const int WindSpiritOwnerRestoreSkillCode = 16990003;

    public CombatCanonicalizationResult Normalize(int sourceId, int targetId, in CombatObservation observation)
    {
        if (!IsOwnerTargetSummonRestore(sourceId, targetId, in observation))
            return new CombatCanonicalizationResult(sourceId, targetId, observation);

        return new CombatCanonicalizationResult(sourceId, targetId, observation with
        {
            EventKind = CombatEventKind.Healing,
            ValueKind = CombatValueKind.Healing
        });
    }

    private bool IsOwnerTargetSummonRestore(int sourceId, int targetId, in CombatObservation observation)
    {
        if (sourceId <= 0 || targetId <= 0 || observation.Damage <= 0 || !entities.TryGet(sourceId, out var entity) || entity.OwnerEntityId != targetId)
            return false;

        var originalSkillCode = observation.OriginalSkillCode != 0 ? observation.OriginalSkillCode : observation.SkillCode;
        return observation.SkillCode == WindSpiritOwnerRestoreSkillCode || originalSkillCode == WindSpiritOwnerRestoreSkillCode;
    }
}
