namespace Cloris.Aion2Flow.Resources.Catalog;

[Flags]
public enum SkillQuantifiedFacet : ushort
{
    None = 0,
    DirectDamage = 1 << 0,
    DirectHealing = 1 << 1,
    PeriodicDamage = 1 << 2,
    PeriodicHealing = 1 << 3,
    Shield = 1 << 4
}

[Flags]
public enum SkillAuraFacet : byte
{
    None = 0,
    Buff = 1 << 0,
    Debuff = 1 << 1
}

[Flags]
public enum SkillSemanticKnowledge : byte
{
    None = 0,
    Classified = 1 << 0,
    KnownNonQuantified = 1 << 1,
    Unclassified = 1 << 2
}

public readonly record struct SkillSemanticValue(
    SkillQuantifiedFacet QuantifiedFacets,
    SkillAuraFacet AuraFacets,
    SkillSemanticKnowledge Knowledge)
{
    public static SkillSemanticValue Empty => default;

    public bool IsEmpty =>
        QuantifiedFacets == SkillQuantifiedFacet.None &&
        AuraFacets == SkillAuraFacet.None &&
        Knowledge == SkillSemanticKnowledge.None;

    public bool HasUnclassifiedSemantics =>
        (Knowledge & SkillSemanticKnowledge.Unclassified) != 0;

    public static SkillSemanticValue Classified(
        SkillQuantifiedFacet quantifiedFacets = SkillQuantifiedFacet.None,
        SkillAuraFacet auraFacets = SkillAuraFacet.None)
    {
        if (quantifiedFacets == SkillQuantifiedFacet.None && auraFacets == SkillAuraFacet.None)
            throw new ArgumentException("Classified skill semantics must contain a quantified or aura facet.");

        return new SkillSemanticValue(quantifiedFacets, auraFacets, SkillSemanticKnowledge.Classified);
    }

    public static SkillSemanticValue KnownNonQuantified =>
        new(SkillQuantifiedFacet.None, SkillAuraFacet.None, SkillSemanticKnowledge.KnownNonQuantified);

    public static SkillSemanticValue Unclassified =>
        new(SkillQuantifiedFacet.None, SkillAuraFacet.None, SkillSemanticKnowledge.Unclassified);

    public static SkillSemanticValue operator |(SkillSemanticValue left, SkillSemanticValue right) =>
        new(
            left.QuantifiedFacets | right.QuantifiedFacets,
            left.AuraFacets | right.AuraFacets,
            left.Knowledge | right.Knowledge);
}
