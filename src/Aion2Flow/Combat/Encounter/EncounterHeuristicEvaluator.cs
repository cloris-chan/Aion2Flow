using Cloris.Aion2Flow.Combat.NpcRuntime;

namespace Cloris.Aion2Flow.Combat;

internal static class EncounterHeuristicEvaluator
{
    public static EncounterSummary Evaluate(int trackingTargetId, long battleTime, NpcRuntimeObservation? observation)
    {
        if (trackingTargetId <= 0)
        {
            return new EncounterSummary
            {
                TrackingTargetId = 0,
                PhaseHint = NpcRuntimePhaseHint.Unknown,
                IsActive = false,
                ShouldArchive = false,
                Reason = "no-target"
            };
        }

        if (observation?.PhaseHint == NpcRuntimePhaseHint.Teardown)
        {
            return new EncounterSummary
            {
                TrackingTargetId = trackingTargetId,
                PhaseHint = observation.PhaseHint,
                IsActive = false,
                ShouldArchive = battleTime > 0 || observation.Hp.HasValue,
                Reason = "teardown-hint"
            };
        }

        if (observation?.PhaseHint == NpcRuntimePhaseHint.SceneActivation)
        {
            return new EncounterSummary
            {
                TrackingTargetId = trackingTargetId,
                PhaseHint = observation.PhaseHint,
                IsActive = true,
                ShouldArchive = false,
                Reason = "scene-activation-hint"
            };
        }

        if (battleTime > 0)
        {
            return new EncounterSummary
            {
                TrackingTargetId = trackingTargetId,
                PhaseHint = observation?.PhaseHint ?? NpcRuntimePhaseHint.Unknown,
                IsActive = true,
                ShouldArchive = false,
                Reason = "battle-time"
            };
        }

        if (observation?.BattleToggledOn == true || observation?.Hp.HasValue == true)
        {
            return new EncounterSummary
            {
                TrackingTargetId = trackingTargetId,
                PhaseHint = observation?.PhaseHint ?? NpcRuntimePhaseHint.Unknown,
                IsActive = true,
                ShouldArchive = false,
                Reason = observation?.BattleToggledOn == true ? "battle-toggle" : "hp-observed"
            };
        }

        return new EncounterSummary
        {
            TrackingTargetId = trackingTargetId,
            PhaseHint = observation?.PhaseHint ?? NpcRuntimePhaseHint.Unknown,
            IsActive = false,
            ShouldArchive = false,
            Reason = "insufficient-signal"
        };
    }
}
