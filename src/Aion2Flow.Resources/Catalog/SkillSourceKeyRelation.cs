namespace Cloris.Aion2Flow.Resources.Catalog;

public enum SkillSourceKeyRelation : byte
{
    None = 0,
    ExactRecord = 1,
    SameFamilyRecord = 2,
    FighterStancePairRecord = 3,
    PacketAlias = 4,
    OtherMismatch = 5,
    GenericSourceKey = 6
}
