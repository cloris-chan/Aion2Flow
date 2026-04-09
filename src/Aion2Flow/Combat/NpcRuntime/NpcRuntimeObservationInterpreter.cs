namespace Cloris.Aion2Flow.Combat.NpcRuntime;

internal static class NpcRuntimeObservationInterpreter
{
    public static NpcRuntimePhaseHint InferPhaseHint(NpcRuntimeObservation observation)
    {
        if (observation.Value2136 == 200003 || observation.Value0140 == 200003 || observation.Value0240 == 200003)
        {
            if (observation.State4636Value1 == 79)
            {
                return NpcRuntimePhaseHint.SceneActivation;
            }
        }

        if (observation.Value2136 == 1010 || observation.Value0140 == 1010 || observation.Value0240 == 1010)
        {
            var hasMatchingTeardown2C38 = observation.Result2C38 == 7 &&
                (!observation.Sequence2136.HasValue ||
                 (observation.Sequence2C38.HasValue && observation.Sequence2136.Value == observation.Sequence2C38.Value));

            if (hasMatchingTeardown2C38 || observation.State4636Value1 == 0)
            {
                return NpcRuntimePhaseHint.Teardown;
            }
        }

        if (observation.BattleToggledOn == true || observation.Hp.HasValue)
        {
            return NpcRuntimePhaseHint.ActiveCombat;
        }

        return NpcRuntimePhaseHint.Unknown;
    }
}
