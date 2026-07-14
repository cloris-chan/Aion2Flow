namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public readonly record struct SkillBaseKey(CombatEventKey EventKey) : IComparable<SkillBaseKey>
{
    public int SkillCode => EventKey.SkillCode;

    public static SkillBaseKey FromEventKey(CombatEventKey eventKey)
    {
        if (!CombatResourceRegistry.TryResolveBaseSkillIdForEventKey(eventKey, out var baseSkillId))
            return new SkillBaseKey(eventKey);

        if (baseSkillId <= 0 ||
            baseSkillId == eventKey.SkillCode &&
            eventKey.BodyResourceEffectRef.IsEmpty &&
            eventKey.DetailResourceEffectRef.IsEmpty)
        {
            return new SkillBaseKey(eventKey);
        }

        return new SkillBaseKey(new CombatEventKey(baseSkillId, default, default));
    }

    public int CompareTo(SkillBaseKey other) => EventKey.CompareTo(other.EventKey);
}
