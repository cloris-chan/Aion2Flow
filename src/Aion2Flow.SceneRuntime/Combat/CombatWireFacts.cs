using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public enum PeriodicEffectRelation : byte
{
    None,
    Self,
    Target
}

public enum CombatWireOutcomeKind : byte
{
    None,
    ActiveSkillInvincible,
    PeriodicLinkInvincible
}

public static class CombatWireTraits
{
    public static bool IsPeriodicSelfMode(in CombatWireObservation observation, int mode) =>
        observation.PeriodicRelation == PeriodicEffectRelation.Self && observation.PeriodicMode == mode;

    public static bool IsPeriodicTargetMode(in CombatWireObservation observation, int mode) =>
        observation.PeriodicRelation == PeriodicEffectRelation.Target && observation.PeriodicMode == mode;

    public static bool IsPeriodicTargetInitialEffect(in CombatWireObservation observation) =>
        IsPeriodicTargetMode(in observation, 1);

    public static bool IsPeriodicTargetStateSeed(in CombatWireObservation observation)
    {
        if (observation.PeriodicRelation != PeriodicEffectRelation.Target)
            return false;

        return observation.PeriodicMode is 7 or 8 or 9 or 11;
    }
}
