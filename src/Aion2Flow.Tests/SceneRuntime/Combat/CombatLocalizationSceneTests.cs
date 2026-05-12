using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class CombatLocalizationSceneTests
{
    [Fact]
    public void CombatEventClassifier_DisplaySkillName_Tracks_Current_Language_Resources()
    {
        try
        {
            var observation = new CombatObservation
            {
                SkillCode = 2011101,
                OriginalSkillCode = 2011101,
                Damage = 100,
                EventKind = CombatEventKind.Healing,
                ValueKind = CombatValueKind.PeriodicHealing
            };

            CombatResourceRegistry.LoadSkillMap("zh-TW");
            var zhName = ResourceDatabase.LoadSkills("zh-TW")[2011101].Name;

            Assert.Equal(zhName, CombatEventClassifier.DisplaySkillNameFor(observation.SkillCode));

            CombatResourceRegistry.LoadSkillMap("en-US");
            var enName = ResourceDatabase.LoadSkills("en-US")[2011101].Name;

            Assert.Equal(enName, CombatEventClassifier.DisplaySkillNameFor(observation.SkillCode));
            Assert.NotEqual(zhName, enName);
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
            scene.AppendCombatPacket(new ParsedCombatPacket
            {
                SourceId = sourceId,
                TargetId = targetId,
                SkillCode = skillCode,
                OriginalSkillCode = skillCode,
                Damage = 77669
            });
            Thread.Sleep(5);
            scene.AppendCombatPacket(new ParsedCombatPacket
            {
                SourceId = sourceId,
                TargetId = targetId,
                SkillCode = skillCode,
                OriginalSkillCode = skillCode,
                Damage = 77669
            });

            var zhSnapshot = scene.CreateSnapshot();
            Assert.True(zhSnapshot.Combatants.TryGetValue(sourceId, out var zhCombatant));
            var zhSkills = scene.CreateSkillBreakdown(zhSnapshot, sourceId).Skills;
            Assert.True(zhSkills.TryGetValue(skillCode, out var zhSkill));
            var zhSkillName = CombatEventClassifier.DisplaySkillNameFor(zhSkill.SkillCode);

            CombatResourceRegistry.LoadSkillMap("en-US");
            var enSnapshot = scene.CreateSnapshot();

            Assert.True(enSnapshot.Combatants.TryGetValue(sourceId, out var enCombatant));
            var enSkills = scene.CreateSkillBreakdown(enSnapshot, sourceId).Skills;
            Assert.True(enSkills.TryGetValue(skillCode, out var enSkill));

            Assert.Equal(zhCombatant.DamageAmount, enCombatant.DamageAmount);
            Assert.Equal(zhCombatant.HealingAmount, enCombatant.HealingAmount);
            Assert.Equal(zhCombatant.DrainDamageAmount, enCombatant.DrainDamageAmount);
            Assert.Equal(zhCombatant.DamageContribution, enCombatant.DamageContribution);

            Assert.Equal(zhSkill.DamageAmount, enSkill.DamageAmount);
            Assert.Equal(zhSkill.Times, enSkill.Times);
            Assert.Equal(zhSkill.SupportTimes, enSkill.SupportTimes);
            Assert.Equal(zhSkill.PrimaryValueKind, enSkill.PrimaryValueKind);
            Assert.Equal(zhSkill.EventKind, enSkill.EventKind);

            Assert.Equal("殺氣破裂", zhSkillName);
            var enSkillName = CombatEventClassifier.DisplaySkillNameFor(enSkill.SkillCode);
            Assert.Equal("Murderous Burst", enSkillName);
            Assert.NotEqual(zhSkillName, enSkillName);
        }
        finally
        {
            CombatResourceRegistry.LoadSkillMap("zh-TW");
        }
    }
}
