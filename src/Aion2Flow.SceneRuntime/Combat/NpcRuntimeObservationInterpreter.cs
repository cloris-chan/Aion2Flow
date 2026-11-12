namespace Cloris.Aion2Flow.SceneRuntime.Combat;

internal static class NpcRuntimeObservationInterpreter
{
    public static NpcRuntimePhaseHint InferPhaseHint(NpcRuntimeObservation observation)
    {
        if (observation.BattleToggledOn == true || observation.Hp.HasValue)
        {
            return NpcRuntimePhaseHint.ActiveCombat;
        }

        return NpcRuntimePhaseHint.Unknown;
    }
}
