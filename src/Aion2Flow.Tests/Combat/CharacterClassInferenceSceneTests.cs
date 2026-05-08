using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Combat.Metrics;

namespace Cloris.Aion2Flow.Tests.Combat;

public sealed class CharacterClassInferenceSceneTests
{
    [Fact]
    public void Does_Not_Infer_CharacterClass_From_Periodic_Self_Support_Proc()
    {
        CombatResourceRegistry.LoadSkillMap("en-US");
        using var scene = new SceneTestHarness();
        const int playerId = 101;
        const int targetId = 9001;

        scene.AppendNickname(playerId, "Player");
        var periodicPacket = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 18160030,
            OriginalSkillCode = 18160030,
            Damage = 120
        };
        periodicPacket.SetPeriodicEffect(PeriodicEffectRelation.Self, 0);
        scene.AppendCombatPacket(periodicPacket);

        Thread.Sleep(5);

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 99999999,
            OriginalSkillCode = 99999999,
            Damage = 1350,
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Null(combatant.CharacterClass);
    }

    [Fact]
    public void Prefers_Offensive_Class_Evidence_Over_Sprint_Mantra_Proc()
    {
        CombatResourceRegistry.LoadSkillMap("en-US");
        using var scene = new SceneTestHarness();
        const int playerId = 102;
        const int targetId = 9002;

        scene.AppendNickname(playerId, "Ranger");
        var periodicPacket = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 18160030,
            OriginalSkillCode = 18160030,
            Damage = 90
        };
        periodicPacket.SetPeriodicEffect(PeriodicEffectRelation.Self, 0);
        scene.AppendCombatPacket(periodicPacket);

        Thread.Sleep(5);

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 14342350,
            OriginalSkillCode = 14342350,
            Damage = 2450,
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(CharacterClass.Ranger, combatant.CharacterClass);
    }

    [Fact]
    public void DamageContribution_Includes_PreInference_Damage_When_Class_Is_Inferred_Later()
    {
        CombatResourceRegistry.LoadSkillMap("en-US");
        using var scene = new SceneTestHarness();
        const int playerId = 103;
        const int targetId = 9003;

        scene.AppendNickname(playerId, "Late Ranger");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 99999999,
            OriginalSkillCode = 99999999,
            Damage = 1000,
        });

        Thread.Sleep(5);

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 14342350,
            OriginalSkillCode = 14342350,
            Damage = 500,
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(CharacterClass.Ranger, combatant.CharacterClass);
        Assert.Equal(1500, combatant.DamageAmount);
        Assert.Equal(1d, combatant.DamageContribution, 10);
    }
}
