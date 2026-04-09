using Cloris.Aion2Flow.Combat.NpcRuntime;

namespace Cloris.Aion2Flow.Combat;

public sealed class EncounterSummary
{
    public int TrackingTargetId { get; set; }
    public NpcRuntimePhaseHint PhaseHint { get; set; }
    public bool IsActive { get; set; }
    public bool ShouldArchive { get; set; }
    public string Reason { get; set; } = string.Empty;

    public EncounterSummary DeepClone()
    {
        return new EncounterSummary
        {
            TrackingTargetId = TrackingTargetId,
            PhaseHint = PhaseHint,
            IsActive = IsActive,
            ShouldArchive = ShouldArchive,
            Reason = Reason
        };
    }
}
