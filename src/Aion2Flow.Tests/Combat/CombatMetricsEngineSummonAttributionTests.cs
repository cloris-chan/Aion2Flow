using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Tests.Combat;

public sealed class CombatMetricsEngineSummonAttributionTests
{
    [Fact]
    public void Attributes_Summon_Damage_To_Owner_In_Snapshot()
    {
        CombatMetricsEngine.SkillMap = new SkillCollection();
        var engine = new CombatMetricsEngine();
        const int ownerId = 12115;
        const int summonId = 18345;
        const int targetId = 17640;

        engine.Store.AppendSummon(ownerId, summonId);
        engine.Store.AppendNickname(ownerId, "Owner");

        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = targetId,
            OriginalSkillCode = 17150342,
            SkillCode = 17150342,
            Damage = 4609,
            Type = 3
        });

        Thread.Sleep(5);

        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = targetId,
            OriginalSkillCode = 17150342,
            SkillCode = 17150342,
            Damage = 4384,
            Type = 2
        });

        var snapshot = engine.CreateSnapshot();

        Assert.True(snapshot.BattleTime > 0);
        Assert.True(snapshot.Combatants.ContainsKey(ownerId));
        Assert.False(snapshot.Combatants.ContainsKey(summonId));

        var owner = snapshot.Combatants[ownerId];
        Assert.Equal("Owner", owner.Nickname);
        Assert.Equal(8993, owner.DamageAmount);
        Assert.Single(owner.Skills);

        var skill = owner.Skills.Values.Single();
        Assert.Equal(8993, skill.DamageAmount);
        Assert.Equal(2, skill.Times);
    }
}
