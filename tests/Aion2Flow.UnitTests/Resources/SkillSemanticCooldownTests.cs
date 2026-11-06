using Cloris.Aion2Flow.Resources.Catalog;

namespace Cloris.Aion2Flow.Tests.Resources;

public sealed class SkillSemanticCooldownTests
{
    [Theory]
    [InlineData("DecreaseCooltimeBySelfBlock")]
    [InlineData("DecreaseCooltimeBySelfEvade")]
    public void ConditionalCooldownModifiersHaveNoQuantifiedOrAuraFacets(string effectType)
    {
        var value = Resolve(effectType);

        Assert.Equal(SkillSemanticValue.KnownNonQuantified, value);
        Assert.Equal(SkillQuantifiedFacet.None, value.QuantifiedFacets);
        Assert.Equal(SkillAuraFacet.None, value.AuraFacets);
    }

    [Fact]
    public void UnknownCooldownLikeTypeRemainsUnclassified()
        => Assert.Equal(SkillSemanticValue.Unclassified, Resolve("DecreaseCooltimeByUnknownCondition"));

    private static SkillSemanticValue Resolve(string effectType)
    {
        var definition = new SkillAbnormalEffectDefinition(1, 1, string.Empty, new(effectType), [], string.Empty, false, default);
        var catalog = new SkillSemanticCatalog(new Dictionary<int, SkillEffectDefinition>(), new Dictionary<int, SkillEffectFilterDefinition>(),
            new Dictionary<int, SkillEffectFilterLocationDefinition>(), new Dictionary<int, SkillEffectLevelDefinition>(),
            new Dictionary<int, SkillProjectileDefinition>(), new Dictionary<int, SkillAbnormalDefinition>(),
            new Dictionary<int, SkillAbnormalEffectDefinition> { [definition.Id] = definition },
            new Dictionary<int, SkillAbnormalEffectLevelDefinition>(), new Dictionary<string, SkillAbnormalEffectTypeDefinition>(),
            [], new Dictionary<string, SkillAbnormalPropertyDefinition>(), new Dictionary<string, SkillAbnormalStringDefinition>());

        return SkillSemanticValueIndex.Build(catalog, new Dictionary<int, SkillEffectReference[]>()).AbnormalEffectSemantics[definition.Id];
    }
}
