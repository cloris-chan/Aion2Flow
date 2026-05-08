using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Scene.Runtime;
using Cloris.Aion2Flow.Scene.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Scene;

public class PeriodicChainCanonicalizerTests
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
                Timestamp = 3_000 + (i * 2_000L)
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
    public void ScenePath_SelfPeriodicHealingTerminalTickConsumesRemainingTotal()
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
            Timestamp = 3_000
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
            Timestamp = 2_000
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
        var applier = new DomainEventApplier(new EntityStore(), new MetadataStore(), combat);
        applier.ApplyJournal(journal);
        return combat;
    }
}
