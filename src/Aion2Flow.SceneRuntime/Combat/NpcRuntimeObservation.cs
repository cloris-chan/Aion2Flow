namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public sealed class NpcRuntimeObservation
{
    public int InstanceId { get; set; }
    public byte? State4636Value0 { get; set; }
    public byte? State4636Value1 { get; set; }
    public int? Sequence2C38 { get; set; }
    public int? Result2C38 { get; set; }
    public long? Hp { get; set; }
    public bool? BattleToggledOn { get; set; }
    public NpcRuntimePhaseHint PhaseHint { get; set; }

    public NpcRuntimeObservation DeepClone()
    {
        return new NpcRuntimeObservation
        {
            InstanceId = InstanceId,
            State4636Value0 = State4636Value0,
            State4636Value1 = State4636Value1,
            Sequence2C38 = Sequence2C38,
            Result2C38 = Result2C38,
            Hp = Hp,
            BattleToggledOn = BattleToggledOn,
            PhaseHint = PhaseHint
        };
    }
}
