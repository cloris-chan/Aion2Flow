using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Metrics;

namespace Cloris.Aion2Flow.Tests.Combat;

public sealed class CombatMetricsEngineCharacterClassInferenceTests
{
    [Fact]
    public void Does_Not_Infer_CharacterClass_From_Periodic_Self_Support_Proc()
    {
        CombatMetricsEngine.LoadSkillMap("en-US");
        var engine = new CombatMetricsEngine();
        const int playerId = 101;
        const int targetId = 9001;

        engine.Store.AppendNickname(playerId, "Player");
        var periodicPacket = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 18160030,
            OriginalSkillCode = 18160030,
            Damage = 120
        };
        periodicPacket.SetPeriodicEffect(PeriodicEffectRelation.Self, 0);
        engine.Store.AppendCombatPacket(periodicPacket);

        Thread.Sleep(5);

        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 99999999,
            OriginalSkillCode = 99999999,
            Damage = 1350,
        });

        var snapshot = engine.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Null(combatant.CharacterClass);
    }

    [Fact]
    public void Prefers_Offensive_Class_Evidence_Over_Sprint_Mantra_Proc()
    {
        CombatMetricsEngine.LoadSkillMap("en-US");
        var engine = new CombatMetricsEngine();
        const int playerId = 102;
        const int targetId = 9002;

        engine.Store.AppendNickname(playerId, "Ranger");
        var periodicPacket = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 18160030,
            OriginalSkillCode = 18160030,
            Damage = 90
        };
        periodicPacket.SetPeriodicEffect(PeriodicEffectRelation.Self, 0);
        engine.Store.AppendCombatPacket(periodicPacket);

        Thread.Sleep(5);

        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 14342350,
            OriginalSkillCode = 14342350,
            Damage = 2450,
        });

        var snapshot = engine.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(CharacterClass.Ranger, combatant.CharacterClass);
    }
}
