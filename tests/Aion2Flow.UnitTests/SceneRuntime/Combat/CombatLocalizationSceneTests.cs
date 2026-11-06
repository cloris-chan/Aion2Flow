using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class CombatLocalizationSceneTests
{
    [Fact]
    public void ResourceRegistriesKeepIndependentLanguages()
    {
        var zh = CreateContext(ResourceLanguage.TraditionalChinese);
        var en = CreateContext(ResourceLanguage.English);

        Assert.Equal(zh.Resources.Skills[2011101].Name, zh.CombatResources.DisplaySkillNameFor(2011101));
        Assert.Equal(en.Resources.Skills[2011101].Name, en.CombatResources.DisplaySkillNameFor(2011101));
        Assert.NotEqual(zh.CombatResources.DisplaySkillNameFor(2011101), en.CombatResources.DisplaySkillNameFor(2011101));
        Assert.Equal(zh.Resources.Skills[2011101].Name, zh.CombatResources.DisplaySkillNameFor(2011101));
    }

    [Theory]
    [InlineData(1227237, "攻擊")]
    [InlineData(1227265, "亡靈迅殺")]
    [InlineData(1607415, "攻擊")]
    [InlineData(1607400, "攻擊")]
    public void ResourceRegistryResolvesExactClientSkillEntries(int skillCode, string expectedName)
    {
        var context = CreateContext(ResourceLanguage.TraditionalChinese);

        Assert.Equal(expectedName, context.CombatResources.DisplaySkillNameFor(skillCode));
    }

    [Fact]
    public void SessionLanguagesProduceIdenticalCombatTotalsAndKeepOriginContext()
    {
        var zh = CreateContext(ResourceLanguage.TraditionalChinese);
        var en = CreateContext(ResourceLanguage.English);
        using var zhScene = new SceneTestHarness(zh);
        using var enScene = new SceneTestHarness(en);
        const int sourceId = 2007;
        const int targetId = 55783;
        const int skillCode = 11800008;
        var damage = new CombatWireObservation
        {
            SkillCode = skillCode,
            Damage = 77669,
            HitCount = 1,
            AttemptCount = 1
        };

        foreach (var scene in new[] { zhScene, enScene })
        {
            scene.AppendNickname(sourceId, "Perigee");
            scene.AppendCombatWireObservation(sourceId, targetId, in damage, 1_000);
            scene.AppendCombatWireObservation(sourceId, targetId, in damage, 2_000);
        }

        var zhSnapshot = zhScene.CreateSnapshot();
        var enSnapshot = enScene.CreateSnapshot();
        var zhSkill = Assert.Single(zhScene.CreateSkillBreakdown(zhSnapshot, sourceId).Skills.Values);
        var enSkill = Assert.Single(enScene.CreateSkillBreakdown(enSnapshot, sourceId).Skills.Values);

        Assert.Same(zh, zhSnapshot.ClientContext);
        Assert.Same(en, enSnapshot.ClientContext);
        Assert.Equal(zhSnapshot.Combatants[sourceId].DamageAmount, enSnapshot.Combatants[sourceId].DamageAmount);
        Assert.Equal(155338, zhSnapshot.Combatants[sourceId].DamageAmount);
        Assert.Equal(zhSkill, enSkill);
        Assert.Equal("殺氣破裂", zh.CombatResources.DisplaySkillNameFor(zhSkill.SkillCode));
        Assert.Equal("Murderous Burst", en.CombatResources.DisplaySkillNameFor(enSkill.SkillCode));

        var archive = zhScene.Owner.CreateArchivePayload();
        Assert.Same(zh, archive.ClientContext);
        Assert.Same(zh, archive.TimelineSegment.ClientContext);
    }

    private static SceneClientContext CreateContext(string language) =>
        new(ClientProfiles.Purple, ResourceCatalog.Load(ClientProfiles.Purple, language));
}
