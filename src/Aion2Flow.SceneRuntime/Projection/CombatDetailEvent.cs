using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public readonly record struct CombatDetailEvent(CombatObservation Observation, int SourceId, int TargetId, long ObservedAtMilliseconds, long Revision = 0)
{
    public int SkillCode => Observation.SkillCode;
    public long Amount => Observation.Damage;
    public CombatEventKind EventKind => Observation.EventKind;
    public CombatValueKind ValueKind => Observation.ValueKind;
    public PacketEffectTag EffectTag => Observation.EffectTag;
    public long ObservedAt => ObservedAtMilliseconds;
}
