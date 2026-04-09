namespace Cloris.Aion2Flow.Combat.NpcRuntime;

public sealed class NpcRuntimeObservation
{
    public int InstanceId { get; set; }
    public uint? Value2136 { get; set; }
    public uint? Sequence2136 { get; set; }
    public uint? Value0140 { get; set; }
    public uint? Value0240 { get; set; }
    public byte? State4636Value0 { get; set; }
    public byte? State4636Value1 { get; set; }
    public int? Sequence2C38 { get; set; }
    public int? Result2C38 { get; set; }
    public int? Hp { get; set; }
    public bool? BattleToggledOn { get; set; }
    public NpcRuntimePhaseHint PhaseHint { get; set; }

    public NpcRuntimeObservation DeepClone()
    {
        return new NpcRuntimeObservation
        {
            InstanceId = InstanceId,
            Value2136 = Value2136,
            Sequence2136 = Sequence2136,
            Value0140 = Value0140,
            Value0240 = Value0240,
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
