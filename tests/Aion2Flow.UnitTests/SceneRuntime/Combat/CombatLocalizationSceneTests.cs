using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class CombatLocalizationSceneTests
{
    [Fact]
    public void CombatResourceRegistry_DisplaySkillName_Tracks_Current_Language_Resources()
    {
        try
        {
            var observation = new CombatWireObservation
            {
                SkillCode = 2011101
            };

            CombatResourceRegistry.LoadSkillMap("zh-TW");
            var zhName = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).Skills[2011101].Name;

            Assert.Equal(zhName, CombatResourceRegistry.DisplaySkillNameFor(observation.SkillCode));

            CombatResourceRegistry.LoadSkillMap("en-US");
            var enName = ResourceCatalog.Load(ResourceLanguage.English).Skills[2011101].Name;

            Assert.Equal(enName, CombatResourceRegistry.DisplaySkillNameFor(observation.SkillCode));
            Assert.NotEqual(zhName, enName);
        }
        finally
        {
            CombatResourceRegistry.LoadSkillMap("zh-TW");
        }
    }

    [Theory]
    [InlineData(1227237, "攻擊")]
    [InlineData(1227265, "亡靈迅殺")]
    public void CombatResourceRegistry_DisplaySkillName_Resolves_Client_SkillDat_Entries(int skillCode, string expectedName)
    {
        try
        {
            CombatResourceRegistry.LoadSkillMap("zh-TW");

            Assert.Equal(expectedName, CombatResourceRegistry.DisplaySkillNameFor(skillCode));
        }
        finally
        {
            CombatResourceRegistry.LoadSkillMap("zh-TW");
        }
    }

    [Theory]
    [InlineData(1607415, "攻擊")]
    [InlineData(1607400, "攻擊")]
    public void CombatResourceRegistry_DisplaySkillName_Uses_Exact_Client_SkillDat_Entries(int skillCode, string expectedName)
    {
        try
        {
            CombatResourceRegistry.LoadSkillMap("zh-TW");

            Assert.Equal(expectedName, CombatResourceRegistry.DisplaySkillNameFor(skillCode));
        }
        finally
        {
            CombatResourceRegistry.LoadSkillMap("zh-TW");
        }
    }

    [Fact]
    public void CreateBattleSnapshot_Keeps_Combat_Totals_Stable_When_Language_Changes()
    {
        try
        {
            CombatResourceRegistry.LoadSkillMap("zh-TW");
            using var scene = new SceneTestHarness();
            const int sourceId = 2007;
            const int targetId = 55783;
            const int skillCode = 11800008;

            scene.AppendNickname(sourceId, "Perigee");
            var firstDamage = new CombatWireObservation
            {
                SkillCode = skillCode,
                Damage = 77669,
                HitCount = 1,
                AttemptCount = 1
            };
            scene.AppendCombatWireObservation(sourceId, targetId, in firstDamage);
            Thread.Sleep(5);
            var secondDamage = new CombatWireObservation
            {
                SkillCode = skillCode,
                Damage = 77669,
                HitCount = 1,
                AttemptCount = 1
            };
            scene.AppendCombatWireObservation(sourceId, targetId, in secondDamage);

            var zhSnapshot = scene.CreateSnapshot();
            Assert.True(zhSnapshot.Combatants.TryGetValue(sourceId, out var zhCombatant));
            var zhSkills = scene.CreateSkillBreakdown(zhSnapshot, sourceId).Skills;
            Assert.True(zhSkills.TryGetBySkillCode(skillCode, out var zhSkill));
            var zhSkillName = CombatResourceRegistry.DisplaySkillNameFor(zhSkill.SkillCode);

            CombatResourceRegistry.LoadSkillMap("en-US");
            var enSnapshot = scene.CreateSnapshot();

            Assert.True(enSnapshot.Combatants.TryGetValue(sourceId, out var enCombatant));
            var enSkills = scene.CreateSkillBreakdown(enSnapshot, sourceId).Skills;
            Assert.True(enSkills.TryGetBySkillCode(skillCode, out var enSkill));

            Assert.Equal(zhCombatant.DamageAmount, enCombatant.DamageAmount);
            Assert.Equal(zhCombatant.HealingAmount, enCombatant.HealingAmount);
            Assert.Equal(zhCombatant.DrainDamageAmount, enCombatant.DrainDamageAmount);
            Assert.Equal(zhCombatant.DamageContribution, enCombatant.DamageContribution);

            Assert.Equal(zhSkill, enSkill);

            Assert.Equal("殺氣破裂", zhSkillName);
            var enSkillName = CombatResourceRegistry.DisplaySkillNameFor(enSkill.SkillCode);
            Assert.Equal("Murderous Burst", enSkillName);
            Assert.NotEqual(zhSkillName, enSkillName);
        }
        finally
        {
            CombatResourceRegistry.LoadSkillMap("zh-TW");
        }
    }
}
