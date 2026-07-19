using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Runtime;
using Cloris.Aion2Flow.SceneRuntime.Stores;

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

        var seed = CreatePeriodicObservation(17091250, 4676, chainId, PeriodicEffectRelation.Self, 9);
        AppendCombatWireObservation(sink, playerId, playerId, in seed, 1_000);

        var remainingTotals = new[] { 4209, 3742, 3275 };
        for (var i = 0; i < remainingTotals.Length; i++)
        {
            var tick = CreatePeriodicObservation(17091250, remainingTotals[i], chainId, PeriodicEffectRelation.Self, 11, tailPrefixValue: 467);
            AppendCombatWireObservation(sink, playerId, playerId, in tick, 3_000 + (i * 2_000L));
        }

        var combat = Apply(journal, out var mechanics);

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

        var seed = CreatePeriodicObservation(17091250, 4676, chainId, PeriodicEffectRelation.Self, 9);
        AppendCombatWireObservation(sink, playerId, playerId, in seed, 1_000);

        var terminal = CreatePeriodicObservation(17091250, 0, chainId, PeriodicEffectRelation.Self, 11, tailPrefixValue: 4676);
        AppendCombatWireObservation(sink, playerId, playerId, in terminal, 3_000);

        var combat = Apply(journal, out var mechanics, out var resources);

        Assert.True(combat.TryGetCombatant(playerId, out var combatant));
        Assert.Equal(4676, combatant!.OutgoingHealing);
        var summary = CombatPairProjection.GetCombatant(combat, mechanics, resources, playerId);
        Assert.True(summary.HasValue);
        Assert.Equal(0, summary.Value.OutgoingHits);
        Assert.Equal(0, summary.Value.OutgoingAttempts);
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

        var grant = CreatePeriodicObservation(18730000, 1000, chainId, PeriodicEffectRelation.Target, 9);
        AppendCombatWireObservation(sink, casterId, targetId, in grant, 1_000);

        var continuation = CreatePeriodicObservation(18730000, 700, chainId, PeriodicEffectRelation.Target, 11, tailPrefixValue: 300);
        AppendCombatWireObservation(sink, 300, targetId, in continuation, 2_000);

        var combat = Apply(journal, out var mechanics, out var resources);

        Assert.True(combat.TryGetCombatant(casterId, out var caster));
        Assert.True(combat.TryGetCombatant(targetId, out var target));
        Assert.True(combat.TryGetPair(casterId, targetId, out var grantPair));
        Assert.False(combat.TryGetPair(300, targetId, out _));
        Assert.Equal(1000, caster!.OutgoingShield);
        Assert.Equal(300, caster.OutgoingShieldAbsorbed);
        Assert.Equal(1000, target!.IncomingShield);
        Assert.Equal(300, target.IncomingShieldAbsorbed);
        Assert.Equal(0, target.IncomingDamage);
        var targetSummary = CombatPairProjection.GetCombatant(combat, mechanics, resources, targetId);
        Assert.True(targetSummary.HasValue);
        Assert.Equal(0, targetSummary.Value.IncomingHits);
        Assert.Equal(0, targetSummary.Value.IncomingAttempts);
        Assert.Equal(1000, grantPair!.TotalShield);
        Assert.Equal(300, grantPair.TotalShieldAbsorbed);
        var grantEvent = Assert.Single(combat.Events, static e => e.Contribution.Resolution.Materialization == CombatMaterializationKind.PeriodicPoolGrant);
        var absorbedEvent = Assert.Single(combat.Events, static e => e.Contribution.Resolution.Materialization == CombatMaterializationKind.PeriodicPoolAbsorb);
        Assert.Equal(casterId, grantEvent.SourceId);
        Assert.Equal(targetId, grantEvent.TargetId);
        Assert.Equal(casterId, absorbedEvent.SourceId);
        Assert.Equal(targetId, absorbedEvent.TargetId);
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

        var seed = CreatePeriodicObservation(18120010, 3000, chainId, PeriodicEffectRelation.Target, 9);
        AppendCombatWireObservation(sink, casterId, targetId, in seed, 1_000);

        var tick = CreatePeriodicObservation(18120010, 2400, chainId, PeriodicEffectRelation.Target, 11, tailPrefixValue: 600);
        AppendCombatWireObservation(sink, casterId, targetId, in tick, 2_000);

        var combat = Apply(journal, out var mechanics);

        Assert.True(combat.TryGetCombatant(casterId, out var caster));
        Assert.True(combat.TryGetCombatant(targetId, out var target));
        Assert.False(combat.TryGetPair(casterId, targetId, out var pair) && pair!.TotalShield > 0);
        Assert.Equal(600, caster!.OutgoingHealing);
        Assert.Equal(600, target!.IncomingHealing);
        Assert.Equal(0, caster.OutgoingShield);
        Assert.Equal(0, target.IncomingShield);
        var contributionEvent = Assert.Single(combat.Events, static e =>
            e.Contribution.Metric == CombatMetricKind.Healing &&
            e.Contribution.Delivery == CombatDeliveryKind.Periodic &&
            e.Contribution.Resolution.PacketRule == CombatPacketRule.PeriodicRecovery);
        Assert.Equal(casterId, contributionEvent.SourceId);
        Assert.Equal(targetId, contributionEvent.TargetId);
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

        var seed = CreatePeriodicObservation(16190000, 3000, chainId, PeriodicEffectRelation.Target, 9);
        AppendCombatWireObservation(sink, casterId, targetId, in seed, 1_000);

        var healTick = CreatePeriodicObservation(16190000, 2800, chainId, PeriodicEffectRelation.Target, 11, tailPrefixValue: 200);
        AppendCombatWireObservation(sink, casterId, targetId, in healTick, 2_000);

        var shieldTick = CreatePeriodicObservation(16190000, 2500, chainId, PeriodicEffectRelation.Target, 11, tailPrefixValue: 300);
        AppendCombatWireObservation(sink, attackerId, targetId, in shieldTick, 3_000);

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

        var seed = CreatePeriodicObservation(2011101, 2117, chainId, PeriodicEffectRelation.Self, 9);
        AppendCombatWireObservation(sink, playerId, playerId, in seed, 1_000);

        var tick = CreatePeriodicObservation(2011101, 2258, chainId, PeriodicEffectRelation.Self, 11, tailPrefixValue: 423);
        AppendCombatWireObservation(sink, playerId, playerId, in tick, 2_000);

        var combat = Apply(journal);

        Assert.True(combat.TryGetCombatant(playerId, out var player));
        Assert.Equal(423, player!.OutgoingHealing);
        Assert.Equal(423, player.IncomingHealing);
        Assert.Equal(0, player.OutgoingShield);
    }

    [Fact]
    public void ScenePath_PeriodicPoolKeyUsesTailEffectToAvoidSameChainCollision()
    {
        CombatResourceTestFixture.SetResources([], new Dictionary<int, NpcDisplayEntry>());

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

        AppendPeriodicPoolPacket(sink, casterA, targetId, 500001, tailA, 1000, chainId, 9, 1_000, 1);
        AppendPeriodicPoolPacket(sink, casterB, targetId, 500002, tailB, 2000, chainId, 9, 1_100, 1);
        AppendPeriodicPoolPacket(sink, attackerId, targetId, 500001, tailA, 700, chainId, 11, 2_000, 2, tailPrefixValue: 300);

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

        var grant = CreatePeriodicObservation(12070000, 2975, chainId, PeriodicEffectRelation.Self, 9);
        AppendCombatWireObservation(sink, playerId, playerId, in grant, 1_000);

        var combat = Apply(journal);

        Assert.False(combat.TryGetCombatant(playerId, out var player));
        Assert.Null(player);
        Assert.False(combat.TryGetPair(playerId, playerId, out _));
    }

    [Fact]
    public void ScenePath_SemanticMode9ShieldGrantEmitsWithoutAbsorb()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        const int playerId = 15104;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());
        var grant = CreatePeriodicObservation(
            17420010,
            3119,
            79,
            PeriodicEffectRelation.Self,
            9,
            bodyResourceEffectRef: ResourceEffectRef.FromRaw(1742001011),
            tailSkillCode: 17420010,
            tailLength: 4);
        AppendCombatWireObservation(sink, playerId, playerId, in grant, 1_000);

        var combat = Apply(journal);

        Assert.True(combat.TryGetCombatant(playerId, out var player));
        Assert.Equal(3119, player!.OutgoingShield);
        Assert.Equal(3119, player.IncomingShield);
        var shieldEvent = Assert.Single(combat.Events);
        Assert.Equal(CombatMetricKind.ShieldGranted, shieldEvent.Contribution.Metric);
        Assert.Equal(CombatDeliveryKind.Pool, shieldEvent.Contribution.Delivery);
        Assert.Equal(CombatPacketRule.PeriodicValue, shieldEvent.Contribution.Resolution.PacketRule);
        Assert.Equal(CombatResolutionAuthority.SkillSemantic, shieldEvent.Contribution.Resolution.Authority);
        Assert.Equal(CombatSemanticMatchKind.UnambiguousSlot, shieldEvent.Contribution.Resolution.SemanticMatch);
        Assert.Equal(CombatMaterializationKind.PeriodicPoolGrant, shieldEvent.Contribution.Resolution.Materialization);
        Assert.Equal(PeriodicEffectRelation.Self, shieldEvent.Observation.PeriodicRelation);
        Assert.Equal(9, shieldEvent.Observation.PeriodicMode);
        Assert.Equal(ResourceEffectRef.FromRaw(1742001011), shieldEvent.Observation.BodyResourceEffectRef);
    }

    [Fact]
    public void ScenePath_SemanticMode9ShieldGrantIsNotDuplicatedByLaterAbsorb()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        const int playerId = 15104;
        const int attackerId = 136787;
        const int chainId = 79;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());
        var grant = CreatePeriodicObservation(
            17420010,
            3119,
            chainId,
            PeriodicEffectRelation.Self,
            9,
            bodyResourceEffectRef: ResourceEffectRef.FromRaw(1742001011),
            tailSkillCode: 17420010,
            tailLength: 4);
        AppendCombatWireObservation(sink, playerId, playerId, in grant, 1_000);

        var continuation = CreatePeriodicObservation(
            17420010,
            2719,
            chainId,
            PeriodicEffectRelation.Target,
            11,
            bodyResourceEffectRef: ResourceEffectRef.FromRaw(1742001011),
            tailSkillCode: 17420010,
            tailPrefixValue: 400,
            tailLength: 5);
        AppendCombatWireObservation(sink, attackerId, playerId, in continuation, 2_000);

        var combat = Apply(journal);

        Assert.Equal(2, combat.Events.Count);
        var grantEvent = Assert.Single(combat.Events, static e => e.Contribution.Metric == CombatMetricKind.ShieldGranted);
        var absorbEvent = Assert.Single(combat.Events, static e => e.Contribution.Metric == CombatMetricKind.ShieldAbsorbed);
        Assert.Equal(3119, grantEvent.Contribution.Amount);
        Assert.Equal(CombatResolutionAuthority.SkillSemantic, grantEvent.Contribution.Resolution.Authority);
        Assert.Equal(400, absorbEvent.Contribution.Amount);
        Assert.Equal(CombatResolutionAuthority.Packet, absorbEvent.Contribution.Resolution.Authority);
    }

    [Fact]
    public void ScenePath_PacketAndSemanticPathsRemainIndependentAcrossSemanticGrantAndAbsorb()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        const int playerId = 15104;
        const int attackerId = 136787;
        const int chainId = 79;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());
        var observer = new RecordingCombatOccurrenceObserver();
        var grant = CreatePeriodicObservation(
            17420010,
            3119,
            chainId,
            PeriodicEffectRelation.Self,
            9,
            bodyResourceEffectRef: ResourceEffectRef.FromRaw(1742001011),
            tailSkillCode: 17420010,
            tailLength: 4);
        AppendCombatWireObservation(sink, playerId, playerId, in grant, 1_000);
        var continuation = CreatePeriodicObservation(
            17420010,
            2719,
            chainId,
            PeriodicEffectRelation.Target,
            11,
            bodyResourceEffectRef: ResourceEffectRef.FromRaw(1742001011),
            tailSkillCode: 17420010,
            tailPrefixValue: 400,
            tailLength: 5);
        AppendCombatWireObservation(sink, attackerId, playerId, in continuation, 2_000);
        var combat = new CombatStore();
        var applier = new DomainEventApplier(new EntityStore(), new SceneBoundaryStore(), combat, observer);

        applier.ApplyJournal(journal);

        Assert.Equal(3, observer.Contexts.Count);
        var semanticOpen = observer.Contexts[0];
        var packetGrant = observer.Contexts[1];
        var packetAbsorb = observer.Contexts[2];
        Assert.Equal(CombatSuppressionReason.PeriodicPoolSemanticCandidate, semanticOpen.Resolution.Suppression);
        Assert.Equal(CombatPacketRule.PeriodicShieldGrant, packetGrant.Resolution.PacketRule);
        Assert.False(packetGrant.ProductionMaterialization.IsAdmitted);
        Assert.Equal(CombatPacketRule.PeriodicShieldAbsorbed, packetAbsorb.Resolution.PacketRule);

        var packetPath = new CombatContributionPathResolver(CombatContributionPath.PacketOnly);
        var semanticPath = new CombatContributionPathResolver(CombatContributionPath.SemanticOnly);
        Assert.False(TryResolve(packetPath, in semanticOpen, out _));
        Assert.True(TryResolve(semanticPath, in semanticOpen, out var semanticGrant));
        Assert.Equal(CombatResolutionAuthority.SkillSemantic, semanticGrant.Resolution.Authority);
        Assert.True(TryResolve(packetPath, in packetGrant, out var provenGrant));
        Assert.Equal(CombatResolutionAuthority.Packet, provenGrant.Resolution.Authority);
        Assert.False(TryResolve(semanticPath, in packetGrant, out _));
        Assert.True(TryResolve(packetPath, in packetAbsorb, out var absorbed));
        Assert.Equal(CombatMetricKind.ShieldAbsorbed, absorbed.Metric);

        Assert.Equal(2, combat.Events.Count);
        Assert.Single(combat.Events, static e => e.Contribution.Metric == CombatMetricKind.ShieldGranted);
        Assert.Single(combat.Events, static e => e.Contribution.Metric == CombatMetricKind.ShieldAbsorbed);
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

        var grant = CreatePeriodicObservation(12070000, 2975, chainId, PeriodicEffectRelation.Self, 9);
        AppendCombatWireObservation(sink, playerId, playerId, in grant, 1_000);

        var continuation = CreatePeriodicObservation(12070000, 2575, chainId, PeriodicEffectRelation.Target, 11, tailPrefixValue: 400);
        AppendCombatWireObservation(sink, attackerId, playerId, in continuation, 2_000);

        var combat = Apply(journal);

        Assert.True(combat.TryGetCombatant(playerId, out var player));
        Assert.Equal(2975, player!.OutgoingShield);
        Assert.Equal(2975, player.IncomingShield);
        Assert.Equal(400, player.OutgoingShieldAbsorbed);
        Assert.Equal(400, player.IncomingShieldAbsorbed);
        Assert.Equal(0, player.OutgoingHealing);
        Assert.Equal(0, player.OutgoingDamage);
        Assert.Equal(2, combat.Events.Count);
        var grantEvent = Assert.Single(combat.Events, static e => e.Contribution.Metric == CombatMetricKind.ShieldGranted);
        var absorbEvent = Assert.Single(combat.Events, static e => e.Contribution.Metric == CombatMetricKind.ShieldAbsorbed);
        Assert.Equal(CombatPacketRule.PeriodicShieldGrant, grantEvent.Contribution.Resolution.PacketRule);
        Assert.Equal(CombatResolutionAuthority.Packet, grantEvent.Contribution.Resolution.Authority);
        Assert.Equal(CombatPacketRule.PeriodicShieldAbsorbed, absorbEvent.Contribution.Resolution.PacketRule);
        Assert.Equal(CombatResolutionAuthority.Packet, absorbEvent.Contribution.Resolution.Authority);
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

        var grant = CreatePeriodicObservation(12130040, 3539, chainId, PeriodicEffectRelation.Self, 9);
        AppendCombatWireObservation(sink, playerId, playerId, in grant, 1_000);

        var terminal = CreatePeriodicObservation(12130040, 3539, chainId, PeriodicEffectRelation.Target, 10);
        AppendCombatWireObservation(sink, attackerId, playerId, in terminal, 2_000);

        var combat = new CombatStore();
        var observer = new RecordingCombatOccurrenceObserver();
        var applier = new DomainEventApplier(new EntityStore(), new SceneBoundaryStore(), combat, observer);
        applier.ApplyJournal(journal);

        Assert.False(combat.TryGetCombatant(playerId, out var player));
        Assert.Null(player);
        Assert.False(combat.TryGetPair(playerId, playerId, out var grantPair));
        Assert.Null(grantPair);
        Assert.False(combat.TryGetPair(attackerId, playerId, out _));
        var closed = Assert.Single(observer.Contexts, static context =>
            context.Resolution.PacketRule == CombatPacketRule.PeriodicPoolClosed);
        Assert.Equal(CombatMaterializationKind.PeriodicPoolClose, closed.Resolution.Materialization);
        Assert.Equal(CombatSuppressionReason.PeriodicPoolClosed, closed.Resolution.Suppression);
        Assert.False(closed.ProductionMaterialization.IsAdmitted);
        Assert.False(closed.ProductionMaterialization.HasAny);
    }

    [Fact]
    public void PathResolver_Mode10TerminalReleasesSemanticPoolGrantState()
    {
        const int targetId = 8470;
        const int chainId = 59;
        const int tailSkillCode = 12130040;
        var resolver = new CombatContributionPathResolver(CombatContributionPath.SemanticOnly);
        var grant = CreatePeriodicObservation(
            tailSkillCode,
            3539,
            chainId,
            PeriodicEffectRelation.Self,
            9,
            tailSkillCode: tailSkillCode);
        var grantOccurrence = new CombatOccurrenceResolution(
            CombatPacketRule.PeriodicValue,
            CombatMaterializationKind.PeriodicPoolGrant,
            CombatAssociationKind.None,
            CombatSuppressionReason.PeriodicPoolSemanticCandidate);
        var semantic = new CombatSemanticEvidence(
            CombatSemanticMatchKind.ExactNode,
            default,
            new CombatContributionCandidate(CombatMetricKind.ShieldGranted, CombatDeliveryKind.Pool, grant.Damage));

        Assert.True(resolver.TryResolve(targetId, targetId, in grant, in grantOccurrence, default, in semantic, out _));
        Assert.Single(resolver.CreateSnapshot().MaterializedSemanticPoolGrants);

        var terminal = CreatePeriodicObservation(
            tailSkillCode,
            3539,
            chainId,
            PeriodicEffectRelation.Target,
            10,
            tailSkillCode: tailSkillCode);
        var terminalOccurrence = new CombatOccurrenceResolution(
            CombatPacketRule.PeriodicPoolClosed,
            CombatMaterializationKind.PeriodicPoolClose,
            CombatAssociationKind.None,
            CombatSuppressionReason.PeriodicPoolClosed);
        var terminalPacket = new CombatPacketEvidence(
            CombatPacketEvidenceStrength.Proven,
            CombatPacketRule.PeriodicPoolClosed,
            null);

        Assert.False(resolver.TryResolve(136787, targetId, in terminal, in terminalOccurrence, in terminalPacket, default, out _));
        Assert.Empty(resolver.CreateSnapshot().MaterializedSemanticPoolGrants);
    }

    [Fact]
    public void ScenePath_StandaloneMode10TargetPacketBecomesPeriodicDamage()
    {
        const int sourceId = 100;
        const int targetId = 200;
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());
        var packet = CreatePeriodicObservation(
            16140000,
            1395,
            6,
            PeriodicEffectRelation.Target,
            10,
            bodyResourceEffectRef: ResourceEffectRef.FromRaw(16140000),
            tailSkillCode: 16140030,
            tailLength: 4);
        AppendCombatWireObservation(sink, sourceId, targetId, in packet, 1_000, flushId: 1);

        var combat = Apply(journal, out var mechanics);

        var combatEvent = Assert.Single(combat.Events);
        Assert.Equal(sourceId, combatEvent.SourceId);
        Assert.Equal(targetId, combatEvent.TargetId);
        Assert.Equal(16140030, combatEvent.Observation.SkillCode);
        Assert.Equal(1395, combatEvent.Observation.Damage);
        Assert.Equal(CombatMetricKind.Damage, combatEvent.Contribution.Metric);
        Assert.Equal(CombatDeliveryKind.Periodic, combatEvent.Contribution.Delivery);
        Assert.Equal(CombatPacketRule.PeriodicValue, combatEvent.Contribution.Resolution.PacketRule);
        Assert.Empty(mechanics.Events);
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
        var packet = CreatePeriodicObservation(
            17091250,
            4676,
            4242,
            PeriodicEffectRelation.Self,
            10,
            bodyResourceEffectRef: ResourceEffectRef.FromRaw(17091250),
            tailSkillCode: 17091250,
            tailLength: 4);
        AppendCombatWireObservation(sink, sourceId, sourceId, in packet, 1_000, flushId: 1);

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
        var packet = CreatePeriodicObservation(
            16140000,
            1395,
            6,
            PeriodicEffectRelation.Target,
            10,
            bodyResourceEffectRef: ResourceEffectRef.FromRaw(16140000));
        AppendCombatWireObservation(sink, sourceId, targetId, in packet, 1_000, flushId: 1);

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

        AppendPeriodicPoolPacket(sink, sourceId, targetId, 16140000, 16140030, 1395, 6, 10, 1_000, 1);
        AppendPeriodicPoolPacket(sink, sourceId, targetId, 16140000, 16140030, 1405, 6, 10, 2_000, 2);

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

        var packet = CreatePeriodicObservation(17091250, 4676, 4242, PeriodicEffectRelation.Self, 9);
        AppendCombatWireObservation(sink, playerId, playerId, in packet, 1_000);

        Assert.Equal(packet, journal.ReadSnapshot(0).Combat!.Value);
    }

    private static CombatStore Apply(ObservedEventJournal journal)
        => Apply(journal, out _);

    private static CombatStore Apply(ObservedEventJournal journal, out MechanicStore mechanics)
        => Apply(journal, out mechanics, out _);

    private static CombatStore Apply(ObservedEventJournal journal, out MechanicStore mechanics, out ResourceStore resources)
    {
        var combat = new CombatStore();
        var applier = new DomainEventApplier(new EntityStore(), new SceneBoundaryStore(), combat);
        applier.ApplyJournal(journal);
        mechanics = applier.Mechanics;
        resources = applier.Resources;
        return combat;
    }

    private static CombatWireObservation CreatePeriodicObservation(
        int skillCode,
        long damage,
        int chainId,
        PeriodicEffectRelation relation,
        int mode,
        ResourceEffectRef bodyResourceEffectRef = default,
        int tailSkillCode = 0,
        int tailPrefixValue = 0,
        int tailLength = 0)
    {
        return new CombatWireObservation
        {
            SkillCode = skillCode,
            Damage = damage,
            ChainId = chainId,
            BodyResourceEffectRef = bodyResourceEffectRef,
            PeriodicRelation = relation,
            PeriodicMode = mode,
            PeriodicTailSkillCodeRaw = tailSkillCode,
            PeriodicTailPrefixValue = tailPrefixValue,
            PeriodicTailLength = tailLength
        };
    }

    private static void AppendCombatWireObservation(
        IRuntimeObservationSink sink,
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        long timestamp,
        long flushId = 0)
    {
        var source = new PacketObservationSource(timestamp, flushId, 0, 0, 0, default);
        sink.AppendCombatWireObservation(in source, sourceId, targetId, in observation);
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
        long flushId,
        int tailPrefixValue = 0)
    {
        var observation = CreatePeriodicObservation(
            skillCode,
            damage,
            chainId,
            PeriodicEffectRelation.Target,
            mode,
            ResourceEffectRef.FromRaw(skillCode),
            tailSkillCode,
            tailPrefixValue,
            tailPrefixValue > 0 ? 5 : 4);
        AppendCombatWireObservation(sink, sourceId, targetId, in observation, timestamp, flushId);
    }

    private static bool TryResolve(
        CombatContributionPathResolver resolver,
        in CombatOccurrenceContext context,
        out CombatContribution contribution)
    {
        var observation = context.Wire;
        var occurrence = context.Resolution;
        return resolver.TryResolve(context.SourceId, context.TargetId, in observation, in occurrence, out contribution);
    }

    private sealed class RecordingCombatOccurrenceObserver : ICombatOccurrenceObserver
    {
        public List<CombatOccurrenceContext> Contexts { get; } = [];

        public void Observe(in CombatOccurrenceContext context) => Contexts.Add(context);
    }

}
