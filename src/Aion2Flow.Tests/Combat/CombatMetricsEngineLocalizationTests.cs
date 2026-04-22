using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Tests.Combat;

public sealed class CombatMetricsEngineLocalizationTests
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

            CombatMetricsEngine.LoadSkillMap("zh-TW");
            var zhName = ResourceDatabase.LoadSkills("zh-TW")[2011101].Name;
            var metrics = new SkillMetrics(packet);

            Assert.Equal(zhName, metrics.SkillName);

            CombatMetricsEngine.LoadSkillMap("en-US");
            var enName = ResourceDatabase.LoadSkills("en-US")[2011101].Name;

            Assert.Equal(enName, metrics.SkillName);
            Assert.NotEqual(zhName, enName);
        }
        finally
        {
            CombatMetricsEngine.LoadSkillMap("zh-TW");
        }
    }

    [Fact]
    public void CreateBattleSnapshot_Keeps_Combat_Totals_Stable_When_Language_Changes()
    {
        try
        {
            CombatMetricsEngine.LoadSkillMap("zh-TW");
            var engine = new CombatMetricsEngine();
            const int sourceId = 2007;
            const int targetId = 55783;
            const int skillCode = 11800008;

            engine.Store.AppendNickname(sourceId, "Perigee");
            engine.Store.AppendCombatPacket(new ParsedCombatPacket
            {
                SourceId = sourceId,
                TargetId = targetId,
                SkillCode = skillCode,
                OriginalSkillCode = skillCode,
                Damage = 77669
            });
            Thread.Sleep(5);
            engine.Store.AppendCombatPacket(new ParsedCombatPacket
            {
                SourceId = sourceId,
                TargetId = targetId,
                SkillCode = skillCode,
                OriginalSkillCode = skillCode,
                Damage = 77669
            });

            var zhSnapshot = engine.CreateBattleSnapshot();
            Assert.True(zhSnapshot.Combatants.TryGetValue(sourceId, out var zhCombatant));
            Assert.True(zhCombatant.Skills.TryGetValue(skillCode, out var zhSkill));
            var zhSkillName = zhSkill.SkillName;

            CombatMetricsEngine.LoadSkillMap("en-US");
            var enSnapshot = engine.CreateBattleSnapshot();

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
            CombatMetricsEngine.LoadSkillMap("zh-TW");
        }
    }
}
