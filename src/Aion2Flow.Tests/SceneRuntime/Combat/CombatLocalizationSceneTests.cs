using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class CombatLocalizationSceneTests
{
    [Fact]
    public void SkillMetrics_SkillName_Tracks_Current_Language_Resources()
    {
        try
        {
            var packet = new ParsedCombatPacket
            {
                SkillCode = 2011101,
                OriginalSkillCode = 2011101,
                Damage = 100,
                EventKind = CombatEventKind.Healing,
                ValueKind = CombatValueKind.PeriodicHealing
            };

            CombatResourceRegistry.LoadSkillMap("zh-TW");
            var zhName = ResourceDatabase.LoadSkills("zh-TW")[2011101].Name;
            var metrics = new SkillMetrics(packet);

            Assert.Equal(zhName, metrics.SkillName);

            CombatResourceRegistry.LoadSkillMap("en-US");
            var enName = ResourceDatabase.LoadSkills("en-US")[2011101].Name;

            Assert.Equal(enName, metrics.SkillName);
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
            Assert.True(zhCombatant.Skills.TryGetValue(skillCode, out var zhSkill));
            var zhSkillName = zhSkill.SkillName;

            CombatResourceRegistry.LoadSkillMap("en-US");
            var enSnapshot = scene.CreateSnapshot();

            Assert.True(enSnapshot.Combatants.TryGetValue(sourceId, out var enCombatant));
            Assert.True(enCombatant.Skills.TryGetValue(skillCode, out var enSkill));

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
            Assert.Equal("Murderous Burst", enSkill.SkillName);
            Assert.NotEqual(zhSkillName, enSkill.SkillName);
        }
        finally
        {
            CombatResourceRegistry.LoadSkillMap("zh-TW");
        }
    }
}
