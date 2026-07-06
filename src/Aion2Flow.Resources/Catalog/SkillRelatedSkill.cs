namespace Cloris.Aion2Flow.Resources.Catalog;

public readonly record struct SkillRelatedSkill(int OwnerSkillId, int RelatedSkillCode, int RelatedSourceSkillId, SkillRelationKind Kind, string RelatedSourceKey, string ParentKey);

public enum SkillRelationKind : byte
{
    PacketAlias = 1,
    NestedBlock = 2,
    RowBase = 3,
    ChainReference = 4,
    CancelException = 5
}
