using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Runtime;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public class PeriodicPoolCanonicalizerTests
{
    [Fact]
    public void ScenePath_NormalizesSelfPeriodicHealingRemainingTotal()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        const int playerId = 2508;
        const int chainId = 4242;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        var seed = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 17091250,
            OriginalSkillCode = 1709125011,
            Damage = 4676,
            Unknown = chainId,
            Timestamp = 1_000
        };
        seed.SetPeriodicEffect(PeriodicEffectRelation.Self, 9);
        sink.AppendCombatPacket(seed);

        var remainingTotals = new[] { 4209, 3742, 3275 };
        for (var i = 0; i < remainingTotals.Length; i++)
        {
            var tick = new ParsedCombatPacket
            {
                SourceId = playerId,
                TargetId = playerId,
                SkillCode = 17091250,
                OriginalSkillCode = 1709125011,
                Damage = remainingTotals[i],
                Unknown = chainId,
                Timestamp = 3_000 + (i * 2_000L),
                PeriodicTailPrefixValue = 467
            };
            tick.SetPeriodicEffect(PeriodicEffectRelation.Self, 11);
            sink.AppendCombatPacket(tick);
        }

        var combat = Apply(journal);

        Assert.True(combat.TryGetCombatant(playerId, out var combatant));
        Assert.Equal(1401, combatant!.OutgoingHealing);
        Assert.Equal(1401, combatant.IncomingHealing);
        Assert.Equal(0, combatant.OutgoingDamage);
    }

    [Fact]
    public void ScenePath_SelfPeriodicHealingTerminalTickUsesTailValue()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        const int playerId = 2508;
        const int chainId = 4242;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        var seed = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 17091250,
            OriginalSkillCode = 1709125011,
            Damage = 4676,
            Unknown = chainId,
            Timestamp = 1_000
        };
        seed.SetPeriodicEffect(PeriodicEffectRelation.Self, 9);
        sink.AppendCombatPacket(seed);

        var terminal = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 17091250,
            OriginalSkillCode = 1709125011,
            Damage = 0,
            Unknown = chainId,
            Timestamp = 3_000,
            PeriodicTailPrefixValue = 4676
        };
        terminal.SetPeriodicEffect(PeriodicEffectRelation.Self, 11);
        sink.AppendCombatPacket(terminal);

        var combat = Apply(journal);

        Assert.True(combat.TryGetCombatant(playerId, out var combatant));
        Assert.Equal(4676, combatant!.OutgoingHealing);
        Assert.Equal(0, combatant.OutgoingHits);
        Assert.Equal(0, combatant.OutgoingAttempts);
    }

    [Fact]
    public void ScenePath_PromotesAmbiguousTargetChainToShieldGrantAndAbsorb()
    {
        const int casterId = 100;
        const int targetId = 200;
        const int chainId = 9001;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        var grant = new ParsedCombatPacket
        {
            SourceId = casterId,
            TargetId = targetId,
            SkillCode = 18730000,
            OriginalSkillCode = 18730000,
            Damage = 1000,
            Unknown = chainId,
            Timestamp = 1_000
        };
        grant.SetPeriodicEffect(PeriodicEffectRelation.Target, 9);
        sink.AppendCombatPacket(grant);

        var continuation = new ParsedCombatPacket
        {
            SourceId = 300,
            TargetId = targetId,
            SkillCode = 18730000,
            OriginalSkillCode = 18730000,
            Damage = 700,
            Unknown = chainId,
            Timestamp = 2_000,
            PeriodicTailPrefixValue = 300
        };
        continuation.SetPeriodicEffect(PeriodicEffectRelation.Target, 11);
        sink.AppendCombatPacket(continuation);

        var combat = Apply(journal);

        Assert.True(combat.TryGetCombatant(casterId, out var caster));
        Assert.True(combat.TryGetCombatant(targetId, out var target));
        Assert.True(combat.TryGetPair(casterId, targetId, out var grantPair));
        Assert.False(combat.TryGetPair(300, targetId, out _));
        Assert.Equal(1000, caster!.OutgoingShield);
        Assert.Equal(300, caster.OutgoingShieldAbsorbed);
        Assert.Equal(1000, target!.IncomingShield);
        Assert.Equal(300, target.IncomingShieldAbsorbed);
        Assert.Equal(0, target.IncomingDamage);
        Assert.Equal(0, target.IncomingHits);
        Assert.Equal(0, target.IncomingAttempts);
        Assert.Equal(1000, grantPair!.TotalShield);
        Assert.Equal(300, grantPair.TotalShieldAbsorbed);
    }

    [Fact]
    public void ScenePath_CasterToAllyContinuationIsPeriodicHealingNotShield()
    {
        const int casterId = 100;
        const int targetId = 200;
        const int chainId = 9002;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        var seed = new ParsedCombatPacket
        {
            SourceId = casterId,
            TargetId = targetId,
            SkillCode = 18120010,
            OriginalSkillCode = 1812001011,
            Damage = 3000,
            Unknown = chainId,
            Timestamp = 1_000
        };
        seed.SetPeriodicEffect(PeriodicEffectRelation.Target, 9);
        sink.AppendCombatPacket(seed);

        var tick = new ParsedCombatPacket
        {
            SourceId = casterId,
            TargetId = targetId,
            SkillCode = 18120010,
            OriginalSkillCode = 1812001011,
            Damage = 2400,
            Unknown = chainId,
            Timestamp = 2_000,
            PeriodicTailPrefixValue = 600
        };
        tick.SetPeriodicEffect(PeriodicEffectRelation.Target, 11);
        sink.AppendCombatPacket(tick);

        var combat = Apply(journal);

        Assert.True(combat.TryGetCombatant(casterId, out var caster));
        Assert.True(combat.TryGetCombatant(targetId, out var target));
        Assert.False(combat.TryGetPair(casterId, targetId, out var pair) && pair!.TotalShield > 0);
        Assert.Equal(600, caster!.OutgoingHealing);
        Assert.Equal(600, target!.IncomingHealing);
        Assert.Equal(0, caster.OutgoingShield);
        Assert.Equal(0, target.IncomingShield);
    }

    [Fact]
    public void ScenePath_MixedContinuationsClassifyEachMode11PacketIndependently()
    {
        const int casterId = 100;
        const int targetId = 200;
        const int attackerId = 300;
        const int chainId = 9003;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        var seed = new ParsedCombatPacket
        {
            SourceId = casterId,
            TargetId = targetId,
            SkillCode = 16190000,
            OriginalSkillCode = 1619000011,
            Damage = 3000,
            Unknown = chainId,
            Timestamp = 1_000
        };
        seed.SetPeriodicEffect(PeriodicEffectRelation.Target, 9);
        sink.AppendCombatPacket(seed);

        var healTick = new ParsedCombatPacket
        {
            SourceId = casterId,
            TargetId = targetId,
            SkillCode = 16190000,
            OriginalSkillCode = 1619000011,
            Damage = 2800,
            Unknown = chainId,
            Timestamp = 2_000,
            PeriodicTailPrefixValue = 200
        };
        healTick.SetPeriodicEffect(PeriodicEffectRelation.Target, 11);
        sink.AppendCombatPacket(healTick);

        var shieldTick = new ParsedCombatPacket
        {
            SourceId = attackerId,
            TargetId = targetId,
            SkillCode = 16190000,
            OriginalSkillCode = 1619000011,
            Damage = 2500,
            Unknown = chainId,
            Timestamp = 3_000,
            PeriodicTailPrefixValue = 300
        };
        shieldTick.SetPeriodicEffect(PeriodicEffectRelation.Target, 11);
        sink.AppendCombatPacket(shieldTick);

        var combat = Apply(journal);

        Assert.True(combat.TryGetCombatant(casterId, out var caster));
        Assert.True(combat.TryGetCombatant(targetId, out var target));
        Assert.True(combat.TryGetPair(casterId, targetId, out var pair));
        Assert.Equal(200, caster!.OutgoingHealing);
        Assert.Equal(3000, caster.OutgoingShield);
        Assert.Equal(300, caster.OutgoingShieldAbsorbed);
        Assert.Equal(200, target!.IncomingHealing);
        Assert.Equal(3000, target.IncomingShield);
        Assert.Equal(300, target.IncomingShieldAbsorbed);
        Assert.Equal(3000, pair!.TotalShield);
        Assert.Equal(300, pair.TotalShieldAbsorbed);
        Assert.False(combat.TryGetPair(attackerId, targetId, out _));
    }

    [Fact]
    public void ScenePath_SelfPeriodicHealingUsesTailPrefixWhenRemainingDoesNotDecrease()
    {
        const int playerId = 8470;
        const int chainId = 77;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        var seed = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 2011101,
            OriginalSkillCode = 201110111,
            Damage = 2117,
            Unknown = chainId,
            Timestamp = 1_000
        };
        seed.SetPeriodicEffect(PeriodicEffectRelation.Self, 9);
        sink.AppendCombatPacket(seed);

        var tick = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 2011101,
            OriginalSkillCode = 201110112,
            Damage = 2258,
            Unknown = chainId,
            Timestamp = 2_000,
            PeriodicTailPrefixValue = 423
        };
        tick.SetPeriodicEffect(PeriodicEffectRelation.Self, 11);
        sink.AppendCombatPacket(tick);

        var combat = Apply(journal);

        Assert.True(combat.TryGetCombatant(playerId, out var player));
        Assert.Equal(423, player!.OutgoingHealing);
        Assert.Equal(423, player.IncomingHealing);
        Assert.Equal(0, player.OutgoingShield);
    }

    [Fact]
    public void ScenePath_Mode9GrantOnlyDoesNotEmitSingletonShield()
    {
        const int playerId = 8470;
        const int chainId = 69;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        var grant = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 12070000,
            OriginalSkillCode = 1207000711,
            Damage = 2975,
            Unknown = chainId,
            Timestamp = 1_000
        };
        grant.SetPeriodicEffect(PeriodicEffectRelation.Self, 9);
        sink.AppendCombatPacket(grant);

        var combat = Apply(journal);

        Assert.False(combat.TryGetCombatant(playerId, out var player));
        Assert.Null(player);
        Assert.False(combat.TryGetPair(playerId, playerId, out _));
    }

    [Fact]
    public void ScenePath_ShieldContinuationEmitsGrantBeforeAbsorb()
    {
        const int playerId = 8470;
        const int attackerId = 136787;
        const int chainId = 70;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        var grant = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 12070000,
            OriginalSkillCode = 1207000711,
            Damage = 2975,
            Unknown = chainId,
            Timestamp = 1_000
        };
        grant.SetPeriodicEffect(PeriodicEffectRelation.Self, 9);
        sink.AppendCombatPacket(grant);

        var continuation = new ParsedCombatPacket
        {
            SourceId = attackerId,
            TargetId = playerId,
            SkillCode = 12070000,
            OriginalSkillCode = 1207000711,
            Damage = 2575,
            Unknown = chainId,
            Timestamp = 2_000,
            PeriodicTailPrefixValue = 400
        };
        continuation.SetPeriodicEffect(PeriodicEffectRelation.Target, 11);
        sink.AppendCombatPacket(continuation);

        var combat = Apply(journal);

        Assert.True(combat.TryGetCombatant(playerId, out var player));
        Assert.Equal(2975, player!.OutgoingShield);
        Assert.Equal(2975, player.IncomingShield);
        Assert.Equal(400, player.OutgoingShieldAbsorbed);
        Assert.Equal(400, player.IncomingShieldAbsorbed);
        Assert.Equal(0, player.OutgoingHealing);
        Assert.Equal(0, player.OutgoingDamage);
    }

    [Fact]
    public void ScenePath_Mode10ClosesShieldStateWithoutSynthesizingAbsorb()
    {
        const int playerId = 8470;
        const int attackerId = 136787;
        const int chainId = 59;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        var grant = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 12130040,
            OriginalSkillCode = 1213004021,
            Damage = 3539,
            Unknown = chainId,
            Timestamp = 1_000
        };
        grant.SetPeriodicEffect(PeriodicEffectRelation.Self, 9);
        sink.AppendCombatPacket(grant);

        var terminal = new ParsedCombatPacket
        {
            SourceId = attackerId,
            TargetId = playerId,
            SkillCode = 12130040,
            OriginalSkillCode = 1213004021,
            Damage = 3539,
            Unknown = chainId,
            Timestamp = 2_000
        };
        terminal.SetPeriodicEffect(PeriodicEffectRelation.Target, 10);
        sink.AppendCombatPacket(terminal);

        var combat = Apply(journal);

        Assert.False(combat.TryGetCombatant(playerId, out var player));
        Assert.Null(player);
        Assert.False(combat.TryGetPair(playerId, playerId, out var grantPair));
        Assert.Null(grantPair);
        Assert.False(combat.TryGetPair(attackerId, playerId, out _));
    }

    [Fact]
    public void JournalingSink_PreservesRawPeriodicPacketObservation()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        const int playerId = 2508;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        var packet = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 17091250,
            OriginalSkillCode = 1709125011,
            Damage = 4676,
            Unknown = 4242,
            Timestamp = 1_000
        };
        packet.SetPeriodicEffect(PeriodicEffectRelation.Self, 9);

        sink.AppendCombatPacket(packet);

        Assert.Equal(4676, journal.Read(0).Combat!.Value.Damage);
        Assert.Equal(4676, packet.Damage);
    }

    [Fact]
    public void ScenePath_Replay_EnhanceSpiritBenedictionPeriodicHealing_MatchesGroundTruth()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260426031332.log"));

        var combat = Apply(replay.SceneJournal);

        Assert.True(combat.TryGetCombatant(10277, out var player));
        Assert.True(combat.TryGetCombatant(37299, out var summon));
        Assert.Equal(3438, player!.OutgoingHealing);
        Assert.Equal(1737, player.IncomingHealing);
        Assert.Equal(1701, summon!.IncomingHealing);
    }

    private static CombatStore Apply(ObservedEventJournal journal)
    {
        var combat = new CombatStore();
        var applier = new DomainEventApplier(new EntityStore(), new SceneBoundaryStore(), combat);
        applier.ApplyJournal(journal);
        return combat;
    }
}
