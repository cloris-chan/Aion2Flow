using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources.Catalog;
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
    public void ScenePath_PeriodicPoolKeyUsesTailEffectToAvoidSameChainCollision()
    {
        CombatResourceRegistry.SetGameResources([], new Dictionary<int, NpcDisplayEntry>());

        const int casterA = 100;
        const int casterB = 101;
        const int targetId = 200;
        const int attackerId = 300;
        const int chainId = 77;
        const int tailA = 500011;
        const int tailB = 500022;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        AppendPeriodicPoolPacket(sink, casterA, targetId, 500001, tailA, 1000, chainId, 9, 1_000, 1, 1);
        AppendPeriodicPoolPacket(sink, casterB, targetId, 500002, tailB, 2000, chainId, 9, 1_100, 1, 1);
        AppendPeriodicPoolPacket(sink, attackerId, targetId, 500001, tailA, 700, chainId, 11, 2_000, 2, 2, tailPrefixValue: 300);

        var combat = Apply(journal);

        Assert.True(combat.TryGetCombatant(casterA, out var sourceA));
        Assert.Equal(1000, sourceA!.OutgoingShield);
        Assert.Equal(300, sourceA.OutgoingShieldAbsorbed);
        Assert.False(combat.TryGetCombatant(casterB, out var sourceB) && sourceB!.OutgoingShield > 0);
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
    public void ScenePath_StandaloneMode10TargetPacketBecomesPeriodicDamage()
    {
        const int sourceId = 100;
        const int targetId = 200;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());
        var packet = new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 16140000,
            Damage = 1395,
            Unknown = 6,
            Timestamp = 1_000,
            FrameOrdinal = 1,
            BatchOrdinal = 1,
            BodyResourceEffectRef = ResourceEffectRef.FromRaw(16140000),
            PeriodicTailSkillCodeRaw = 16140030,
            PeriodicTailLength = 4
        };
        packet.SetPeriodicEffect(PeriodicEffectRelation.Target, 10);
        sink.AppendCombatPacket(packet);

        var combat = Apply(journal);

        var combatEvent = Assert.Single(combat.Events);
        Assert.Equal(sourceId, combatEvent.SourceId);
        Assert.Equal(targetId, combatEvent.TargetId);
        Assert.Equal(16140030, combatEvent.Observation.SkillCode);
        Assert.Equal(1395, combatEvent.Observation.Damage);
        Assert.Equal(CombatEventKind.Damage, combatEvent.Observation.EventKind);
        Assert.Equal(CombatValueKind.PeriodicDamage, combatEvent.Observation.ValueKind);
        Assert.Equal(0, combatEvent.HitCount);
        Assert.Equal(0, combatEvent.AttemptCount);
        Assert.True(combat.TryGetCombatant(sourceId, out var source));
        Assert.Equal(1395, source!.OutgoingDamage);
        Assert.True(combat.TryGetPair(sourceId, targetId, out var pair));
        Assert.Equal(1395, pair!.TotalDamage);
    }

    [Fact]
    public void ScenePath_StandaloneMode10SelfPacketDoesNotBecomeDamage()
    {
        const int sourceId = 100;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());
        var packet = new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = sourceId,
            SkillCode = 17091250,
            Damage = 4676,
            Unknown = 4242,
            Timestamp = 1_000,
            FrameOrdinal = 1,
            BatchOrdinal = 1,
            BodyResourceEffectRef = ResourceEffectRef.FromRaw(17091250),
            PeriodicTailSkillCodeRaw = 17091250,
            PeriodicTailLength = 4
        };
        packet.SetPeriodicEffect(PeriodicEffectRelation.Self, 10);
        sink.AppendCombatPacket(packet);

        var combat = Apply(journal);

        Assert.Empty(combat.Events);
        Assert.False(combat.TryGetCombatant(sourceId, out _));
    }

    [Fact]
    public void ScenePath_StandaloneMode10WithoutTailIdentityDoesNotBecomeDamage()
    {
        const int sourceId = 100;
        const int targetId = 200;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());
        var packet = new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 16140000,
            Damage = 1395,
            Unknown = 6,
            Timestamp = 1_000,
            FrameOrdinal = 1,
            BatchOrdinal = 1,
            BodyResourceEffectRef = ResourceEffectRef.FromRaw(16140000),
            PeriodicTailLength = 0
        };
        packet.SetPeriodicEffect(PeriodicEffectRelation.Target, 10);
        sink.AppendCombatPacket(packet);

        var combat = Apply(journal);

        Assert.Empty(combat.Events);
        Assert.False(combat.TryGetCombatant(sourceId, out _));
    }

    [Fact]
    public void ScenePath_RepeatedStandaloneMode10TargetPacketsRemainIndependent()
    {
        const int sourceId = 100;
        const int targetId = 200;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        AppendPeriodicPoolPacket(sink, sourceId, targetId, 16140000, 16140030, 1395, 6, 10, 1_000, 1, 1);
        AppendPeriodicPoolPacket(sink, sourceId, targetId, 16140000, 16140030, 1405, 6, 10, 2_000, 2, 2);

        var combat = Apply(journal);

        Assert.Equal(2, combat.Events.Count);
        Assert.True(combat.TryGetCombatant(sourceId, out var source));
        Assert.Equal(2800, source!.OutgoingDamage);
        Assert.True(combat.TryGetPair(sourceId, targetId, out var pair));
        Assert.Equal(2800, pair!.TotalDamage);
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
        CombatResourceRegistry.SetGameResources(ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).Skills, new Dictionary<int, NpcDisplayEntry>());

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

    private static CombatObservation CreatePeriodicPoolObservation(int skillCode, int damage, int chainId, int mode, int tailSkillCode = 0, int tailPrefixValue = 0)
    {
        return new CombatObservation
        {
            SkillCode = skillCode,
            Damage = damage,
            ChainId = chainId,
            PeriodicRelation = PeriodicEffectRelation.Target,
            PeriodicMode = mode,
            PeriodicTailSkillCodeRaw = tailSkillCode,
            PeriodicTailPrefixValue = tailPrefixValue,
            PeriodicTailLength = tailPrefixValue > 0 ? 5 : tailSkillCode > 0 ? 4 : 0
        };
    }

    private static void AppendPeriodicPoolPacket(
        IRuntimeObservationSink sink,
        int sourceId,
        int targetId,
        int skillCode,
        int tailSkillCode,
        int damage,
        int chainId,
        int mode,
        long timestamp,
        long frameOrdinal,
        long batchOrdinal,
        int tailPrefixValue = 0)
    {
        var packet = new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = skillCode,
            Damage = damage,
            Unknown = chainId,
            Timestamp = timestamp,
            FrameOrdinal = frameOrdinal,
            BatchOrdinal = batchOrdinal,
            BodyResourceEffectRef = ResourceEffectRef.FromRaw(skillCode),
            PeriodicTailSkillCodeRaw = tailSkillCode,
            PeriodicTailPrefixValue = tailPrefixValue,
            PeriodicTailLength = tailPrefixValue > 0 ? 5 : 4
        };
        packet.SetPeriodicEffect(PeriodicEffectRelation.Target, mode);
        sink.AppendCombatPacket(packet);
    }

}
