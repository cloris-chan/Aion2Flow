using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Canonicalization;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public class CombatPacketFactTests
{
    public CombatPacketFactTests()
        => CombatResourceTestFixture.SetResources([], new Dictionary<int, NpcDisplayEntry>());

    [Fact]
    public void ScenePath_ClassifiesCompactControlDirectRecoveryAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactControlOpener(sceneId, 0, sourceId: 8972, bodyCodeRaw: 17800001, marker: 193, mode: 0, flag: 0, echoSourceId: 8972));
        journal.Append(CreateDirectValue(sceneId, 1, sourceId: 8972, targetId: 5578, bodyCodeRaw: 17800001, marker: 193, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 2048));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 5578, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(2048, pair.TotalHealing);
        Assert.True(combat.TryGetCombatant(8972, out var source));
        Assert.Equal(0, source!.OutgoingDamage);
        Assert.Equal(2048, source.OutgoingHealing);
        AssertCompactContribution(combat, 8972, 5578, 2048, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactOpener);
    }

    [Fact]
    public void ScenePath_PreservesCrossguardRecoveryOpenerAcrossCompletedFlushes()
    {
        const int playerId = 15931;
        const int skillCode = 18720001;
        const int marker = 77;
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        var owner = new SceneReadModelOwner(journal);

        journal.Append(CreateCompactControlOpener(
            sceneId,
            ordinal: 0,
            sourceId: playerId,
            bodyCodeRaw: skillCode,
            marker,
            mode: 0,
            flag: 0,
            echoSourceId: playerId,
            scopeId: 101,
            flushId: 100));
        journal.CompleteFlush(100);
        owner.Refresh();

        Assert.Empty(owner.Combat.Events);

        journal.Append(CreateDirectValue(
            sceneId,
            ordinal: 1,
            sourceId: playerId,
            targetId: playerId,
            bodyCodeRaw: skillCode,
            marker,
            layoutTag: 4,
            flag: 0,
            type: 2,
            chainId: 16702,
            damage: 566,
            scopeId: 102,
            flushId: 101,
            detailRef: 1872000111));
        journal.CompleteFlush(101);
        owner.Refresh();

        Assert.True(owner.Combat.TryGetPair(playerId, playerId, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(566, pair.TotalHealing);
        AssertCompactContribution(
            owner.Combat,
            playerId,
            playerId,
            packetValue: 566,
            CombatMetricKind.Healing,
            CombatPacketRule.CompactRecovery,
            CombatAssociationKind.CompactOpener);
    }

    [Fact]
    public void ScenePath_TransportBoundaryDiscardsPendingRecoveryOpener()
    {
        const int playerId = 15931;
        const int skillCode = 18720001;
        const int marker = 77;
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        var owner = new SceneReadModelOwner(journal);

        journal.Append(CreateCompactControlOpener(sceneId, 0, playerId, skillCode, marker, mode: 0, flag: 0, echoSourceId: playerId, flushId: 100));
        journal.CompleteFlush(100);
        owner.Refresh();

        var boundary = new SceneObservation { Kind = SceneObservationKind.TransportBoundary };
        journal.AppendScene(sceneId, new TimelineStamp { ObservationOrdinal = 1, FlushId = 101 }, 0, 0, in boundary);
        journal.CompleteFlush(101);
        owner.Refresh();

        journal.Append(CreateDirectValue(sceneId, 2, playerId, playerId, skillCode, marker, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 566, flushId: 102));
        journal.CompleteFlush(102);
        owner.Refresh();

        Assert.Empty(owner.Combat.Events);
    }

    [Fact]
    public void ScenePath_ExposesProductionCanonicalOccurrenceToResearchObserver()
    {
        const int playerId = 15931;
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactControlOpener(sceneId, 0, playerId, 18720001, marker: 77, mode: 0, flag: 0, echoSourceId: playerId));
        journal.Append(CreateDirectValue(sceneId, 1, playerId, playerId, 18720001, marker: 77, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 566, detailRef: 1872000111));
        var observer = new RecordingCombatOccurrenceObserver();

        _ = Apply(journal, observer);

        var context = Assert.Single(observer.Contexts);
        Assert.Equal(playerId, context.SourceId);
        Assert.Equal(playerId, context.TargetId);
        Assert.Equal(CombatPacketRule.CompactRecovery, context.Resolution.PacketRule);
        Assert.Equal(CombatMaterializationKind.CompactAssociated, context.Resolution.Materialization);
        Assert.Equal(CombatAssociationKind.CompactOpener, context.Resolution.Association);

        var wire = context.Wire;
        var resolution = context.Resolution;
        var packet = CombatPacketEvidenceResolver.Evaluate(
            context.SourceId,
            context.TargetId,
            in wire,
            in resolution);
        Assert.Equal(CombatPacketEvidenceStrength.Proven, packet.Strength);
        Assert.Equal(CombatMetricKind.Healing, packet.Candidate!.Value.Metric);
    }

    [Fact]
    public void ScenePath_ClassifiesOutOfOrderCompactControlDirectRecoveryAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 8972, targetId: 5578, bodyCodeRaw: 17800001, marker: 193, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 2048));
        journal.Append(CreateCompactControlOpener(sceneId, 1, sourceId: 8972, bodyCodeRaw: 17800001, marker: 193, mode: 0, flag: 0, echoSourceId: 8972));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 5578, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(2048, pair.TotalHealing);
        Assert.True(combat.TryGetCombatant(8972, out var source));
        Assert.Equal(0, source!.OutgoingDamage);
        Assert.Equal(2048, source.OutgoingHealing);
        AssertCompactContribution(combat, 8972, 5578, 2048, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactOpener);
    }

    [Fact]
    public void ScenePath_ClassifiesCompactControlDirectRecoveryAcrossFrameEntryScopes()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 8972, targetId: 5578, bodyCodeRaw: 17800001, marker: 193, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 2048, scopeId: 101));
        journal.Append(CreateCompactControlOpener(sceneId, 1, sourceId: 8972, bodyCodeRaw: 17800001, marker: 193, mode: 0, flag: 0, echoSourceId: 8972, scopeId: 102));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 5578, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(2048, pair.TotalHealing);
        AssertCompactContribution(combat, 8972, 5578, 2048, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactOpener);
    }

    [Fact]
    public void ScenePath_ClassifiesSelfCompactControlDirectRecoveryAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 8972, targetId: 8972, bodyCodeRaw: 17800001, marker: 193, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 2048));
        journal.Append(CreateCompactControlOpener(sceneId, 1, sourceId: 8972, bodyCodeRaw: 17800001, marker: 193, mode: 0, flag: 0, echoSourceId: 8972));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 8972, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(2048, pair.TotalHealing);
        Assert.True(combat.TryGetCombatant(8972, out var source));
        Assert.Equal(0, source!.OutgoingDamage);
        Assert.Equal(2048, source.OutgoingHealing);
        Assert.Equal(2048, source.IncomingHealing);
        AssertCompactContribution(combat, 8972, 8972, 2048, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactOpener);
    }

    [Fact]
    public void ScenePath_ClassifiesCompactControlDirectRecoveryWithVariableValueUnknownAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactControlOpener(sceneId, 0, sourceId: 3013, bodyCodeRaw: 17121351, marker: 41, mode: 0, flag: 0, echoSourceId: 3013));
        journal.Append(CreateDirectValue(sceneId, 1, sourceId: 3013, targetId: 3013, bodyCodeRaw: 17121351, marker: 41, layoutTag: 4, flag: 0, type: 2, chainId: 17503, damage: 8627));
        journal.Append(CreateDirectValue(sceneId, 2, sourceId: 3013, targetId: 3013, bodyCodeRaw: 17121351, marker: 41, layoutTag: 4, flag: 0, type: 2, chainId: 16503, damage: 6448));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(3013, 3013, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(15075, pair.TotalHealing);
        Assert.True(combat.TryGetCombatant(3013, out var source));
        Assert.Equal(0, source!.OutgoingDamage);
        Assert.Equal(15075, source.OutgoingHealing);
        Assert.Equal(15075, source.IncomingHealing);
        AssertCompactContribution(combat, 3013, 3013, 8627, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactOpener);
        AssertCompactContribution(combat, 3013, 3013, 6448, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactOpener);
    }

    [Fact]
    public void ScenePath_ClassifiesType12CompactControlDirectRecoveryAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactControlOpener(sceneId, 0, sourceId: 8972, bodyCodeRaw: 17100140, marker: 186, mode: 12, flag: 0, echoSourceId: 8972));
        journal.Append(CreateDirectValue(sceneId, 1, sourceId: 8972, targetId: 8972, bodyCodeRaw: 17100140, marker: 186, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 3324, loop: 2));
        journal.Append(CreateDirectValue(sceneId, 2, sourceId: 8972, targetId: 5578, bodyCodeRaw: 17100140, marker: 186, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 4698, loop: 1));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 8972, out var self));
        Assert.Equal(0, self!.TotalDamage);
        Assert.Equal(3324, self.TotalHealing);
        Assert.True(combat.TryGetPair(8972, 5578, out var target));
        Assert.Equal(0, target!.TotalDamage);
        Assert.Equal(4698, target.TotalHealing);
        AssertCompactContribution(combat, 8972, 8972, 3324, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactOpener);
        AssertCompactContribution(combat, 8972, 5578, 4698, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactOpener);
    }

    [Fact]
    public void ScenePath_ClassifiesMode8CompactControlDirectRecoveryAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactControlOpener(sceneId, 0, sourceId: 8972, bodyCodeRaw: 17100140, marker: 186, mode: 8, flag: 0, echoSourceId: 8972));
        journal.Append(CreateDirectValue(sceneId, 1, sourceId: 8972, targetId: 8972, bodyCodeRaw: 17100140, marker: 186, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 3324, loop: 2));
        journal.Append(CreateDirectValue(sceneId, 2, sourceId: 8972, targetId: 5578, bodyCodeRaw: 17100140, marker: 186, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 4698, loop: 1));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 8972, out var self));
        Assert.Equal(0, self!.TotalDamage);
        Assert.Equal(3324, self.TotalHealing);
        Assert.True(combat.TryGetPair(8972, 5578, out var target));
        Assert.Equal(0, target!.TotalDamage);
        Assert.Equal(4698, target.TotalHealing);
        AssertCompactContribution(combat, 8972, 8972, 3324, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactOpener);
        AssertCompactContribution(combat, 8972, 5578, 4698, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactOpener);
    }

    [Fact]
    public void ScenePath_DoesNotClassifyClosedOpenerTargetValueAsRecoveryWithoutPacketEvidence()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactControlOpener(sceneId, 0, sourceId: 8972, bodyCodeRaw: 17800001, marker: 193, mode: 0, flag: 0, echoSourceId: 8972));
        journal.Append(CreateCompactControlCloser(sceneId, 1, sourceId: 8972, bodyCodeRaw: 17800001, marker: 193, flag: 0));
        journal.Append(CreateDirectValue(sceneId, 2, sourceId: 8972, targetId: 5578, bodyCodeRaw: 17800001, marker: 193, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 2048));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 5578, out var pair));
        Assert.Equal(2048, pair!.TotalDamage);
        Assert.Equal(0, pair.TotalHealing);
        Assert.True(combat.TryGetCombatant(8972, out var source));
        Assert.Equal(2048, source!.OutgoingDamage);
        Assert.Equal(0, source.OutgoingHealing);
        AssertCompactContribution(combat, 8972, 5578, 2048, CombatMetricKind.Damage, CombatPacketRule.CompactDirectValue, CombatAssociationKind.None);
    }

    [Fact]
    public void ScenePath_CloserRetiresMatchedRecoveryOpener()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactControlOpener(sceneId, 0, sourceId: 8972, bodyCodeRaw: 17800001, marker: 193, mode: 0, flag: 0, echoSourceId: 8972));
        journal.Append(CreateDirectValue(sceneId, 1, sourceId: 8972, targetId: 8972, bodyCodeRaw: 17800001, marker: 193, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 1024));
        journal.Append(CreateCompactControlCloser(sceneId, 2, sourceId: 8972, bodyCodeRaw: 17800001, marker: 193, flag: 0));
        journal.Append(CreateDirectValue(sceneId, 3, sourceId: 8972, targetId: 5578, bodyCodeRaw: 17800001, marker: 193, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 2048));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 8972, out var self));
        Assert.Equal(1024, self!.TotalHealing);
        Assert.True(combat.TryGetPair(8972, 5578, out var target));
        Assert.Equal(2048, target!.TotalDamage);
        Assert.Equal(0, target.TotalHealing);
        AssertCompactContribution(combat, 8972, 5578, 2048, CombatMetricKind.Damage, CombatPacketRule.CompactDirectValue, CombatAssociationKind.None);
    }

    [Fact]
    public void ScenePath_CompactControlCloseWithDifferentBodyCodeIsIgnored()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactControlOpener(sceneId, 0, sourceId: 8972, bodyCodeRaw: 17800001, marker: 193, mode: 0, flag: 0, echoSourceId: 8972));
        journal.Append(CreateCompactControlCloser(sceneId, 1, sourceId: 8972, bodyCodeRaw: 17800002, marker: 193, flag: 0));
        journal.Append(CreateDirectValue(sceneId, 2, sourceId: 8972, targetId: 5578, bodyCodeRaw: 17800001, marker: 193, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 2048));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 5578, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(2048, pair.TotalHealing);
        AssertCompactContribution(combat, 8972, 5578, 2048, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactOpener);
    }

    [Fact]
    public void ScenePath_ClassifiesInlineSidecarCompactControlRecoveryAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 8972, targetId: 5578, bodyCodeRaw: 17121351, marker: 43, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 7761));
        journal.Append(CreateDirectValue(sceneId, 1, sourceId: 8972, targetId: 12632, bodyCodeRaw: 17121351, marker: 43, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 9003));
        journal.Append(CreateDirectValue(sceneId, 2, sourceId: 8972, targetId: 8972, bodyCodeRaw: 17121351, marker: 43, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 5425));
        journal.Append(CreateInlineSidecar(sceneId, 3, sourceId: 8972, targetId: 8972, bodyCodeRaw: 17121351, marker: 43, type: 0));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 5578, out var firstTarget));
        Assert.Equal(0, firstTarget!.TotalDamage);
        Assert.Equal(7761, firstTarget.TotalHealing);
        Assert.True(combat.TryGetPair(8972, 12632, out var secondTarget));
        Assert.Equal(0, secondTarget!.TotalDamage);
        Assert.Equal(9003, secondTarget.TotalHealing);
        Assert.True(combat.TryGetPair(8972, 8972, out var self));
        Assert.Equal(0, self!.TotalDamage);
        Assert.Equal(5425, self.TotalHealing);
        AssertCompactContribution(combat, 8972, 5578, 7761, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactInlineRecoveryGroup);
        AssertCompactContribution(combat, 8972, 12632, 9003, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactInlineRecoveryGroup);
        AssertCompactContribution(combat, 8972, 8972, 5425, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactInlineRecoveryGroup);
    }

    [Fact]
    public void ScenePath_ClassifiesInlineSidecarRecoveryWhenSidecarPrecedesTargetValue()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 8972, targetId: 8972, bodyCodeRaw: 17121351, marker: 43, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 5425));
        journal.Append(CreateInlineSidecar(sceneId, 1, sourceId: 8972, targetId: 8972, bodyCodeRaw: 17121351, marker: 43, type: 2));
        journal.Append(CreateDirectValue(sceneId, 2, sourceId: 8972, targetId: 5578, bodyCodeRaw: 17121351, marker: 43, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 7761));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 5578, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(7761, pair.TotalHealing);
        AssertCompactContribution(combat, 8972, 5578, 7761, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactInlineRecoveryGroup);
    }

    [Fact]
    public void ScenePath_InlineSidecarAssociationDoesNotCrossCompletedFlush()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        var owner = new SceneReadModelOwner(journal);
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 8972, targetId: 8972, bodyCodeRaw: 17121351, marker: 43, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 5425, flushId: 100));
        journal.Append(CreateInlineSidecar(sceneId, 1, sourceId: 8972, targetId: 8972, bodyCodeRaw: 17121351, marker: 43, type: 2, flushId: 100));
        journal.CompleteFlush(100);
        owner.Refresh();

        journal.Append(CreateDirectValue(sceneId, 2, sourceId: 8972, targetId: 5578, bodyCodeRaw: 17121351, marker: 43, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 7761, flushId: 101));
        journal.CompleteFlush(101);
        owner.Refresh();

        Assert.True(owner.Combat.TryGetPair(8972, 8972, out var self));
        Assert.Equal(5425, self!.TotalHealing);
        Assert.True(owner.Combat.TryGetPair(8972, 5578, out var target));
        Assert.Equal(7761, target!.TotalDamage);
        Assert.Equal(0, target.TotalHealing);
        AssertCompactContribution(owner.Combat, 8972, 5578, 7761, CombatMetricKind.Damage, CombatPacketRule.CompactDirectValue, CombatAssociationKind.None);
    }

    [Fact]
    public void ScenePath_ClassifiesSamePayloadSelfValueRecoveryAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 12613, targetId: 8062, bodyCodeRaw: 17100140, marker: 1, layoutTag: 4, flag: 0, type: 2, chainId: 19006, damage: 2694, loop: 1, detailRef: 1710004011, structurePath: CreateCompressedPayloadStructurePath(compressedScopeId: 500, leafScopeId: 501, siblingIndex: 12)));
        journal.Append(CreateDirectValue(sceneId, 1, sourceId: 12613, targetId: 12613, bodyCodeRaw: 17100140, marker: 1, layoutTag: 4, flag: 0, type: 2, chainId: 19006, damage: 2305, loop: 2, detailRef: 1710004011, structurePath: CreateCompressedPayloadStructurePath(compressedScopeId: 500, leafScopeId: 502, siblingIndex: 13)));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(12613, 8062, out var target));
        Assert.Equal(0, target!.TotalDamage);
        Assert.Equal(2694, target.TotalHealing);
        Assert.True(combat.TryGetPair(12613, 12613, out var self));
        Assert.Equal(0, self!.TotalDamage);
        Assert.Equal(2305, self.TotalHealing);
        Assert.Equal(2, combat.Events.Count(static e => e.Contribution.Resolution.Association == CombatAssociationKind.CompactSelfValueGroup));
        AssertCompactContribution(combat, 12613, 8062, 2694, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactSelfValueGroup);
        AssertCompactContribution(combat, 12613, 12613, 2305, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactSelfValueGroup);
    }

    [Fact]
    public void ScenePath_ClassifiesSamePayloadLoop2SelfPairRecoveryAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 7023, targetId: 7023, bodyCodeRaw: 17101450, marker: 78, layoutTag: 4, flag: 0, type: 2, chainId: 16694, damage: 3432, loop: 2, detailRef: 1710004011, structurePath: CreateCompressedPayloadStructurePath(compressedScopeId: 510, leafScopeId: 511, siblingIndex: 142)));
        journal.Append(CreateDirectValue(sceneId, 1, sourceId: 7023, targetId: 7023, bodyCodeRaw: 17101450, marker: 78, layoutTag: 4, flag: 0, type: 2, chainId: 16694, damage: 4087, loop: 2, detailRef: 1710004012, structurePath: CreateCompressedPayloadStructurePath(compressedScopeId: 510, leafScopeId: 512, siblingIndex: 143)));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(7023, 7023, out var self));
        Assert.Equal(0, self!.TotalDamage);
        Assert.Equal(7519, self.TotalHealing);
        Assert.Equal(2, combat.Events.Count(static e => e.Contribution.Resolution.Association == CombatAssociationKind.CompactSelfValueGroup));
        AssertCompactContribution(combat, 7023, 7023, 3432, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactSelfValueGroup);
        AssertCompactContribution(combat, 7023, 7023, 4087, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactSelfValueGroup);
    }

    [Fact]
    public void ScenePath_ClassifiesInlineSelfRecoveryAfterNonRecoveryControlOpenerAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactControlOpener(sceneId, 0, sourceId: 2141, bodyCodeRaw: 12350150, marker: 29, mode: 0, flag: 2, echoSourceId: 30058));
        journal.Append(CreateDirectValue(sceneId, 1, sourceId: 2141, targetId: 2141, bodyCodeRaw: 12350150, marker: 29, layoutTag: 4, flag: 0, type: 2, chainId: 16959, damage: 2673, loop: 2));
        journal.Append(CreateInlineSidecar(sceneId, 2, sourceId: 2141, targetId: 2141, bodyCodeRaw: 12350150, marker: 29, type: 2));
        journal.Append(CreateInlineSidecar(sceneId, 3, sourceId: 2141, targetId: 2141, bodyCodeRaw: 12350150, marker: 29, type: 2));
        journal.Append(CreateDirectValue(sceneId, 4, sourceId: 2141, targetId: 30058, bodyCodeRaw: 12350150, marker: 29, layoutTag: 22, flag: 0, type: 3, chainId: 16959, damage: 10_000, loop: 10_000));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(2141, 2141, out var self));
        Assert.Equal(0, self!.TotalDamage);
        Assert.Equal(2673, self.TotalHealing);
        Assert.True(combat.TryGetPair(2141, 30058, out var target));
        Assert.Equal(10_000, target!.TotalDamage);
        Assert.Equal(0, target.TotalHealing);
        Assert.True(combat.TryGetCombatant(2141, out var source));
        Assert.Equal(10_000, source!.OutgoingDamage);
        Assert.Equal(2673, source.OutgoingHealing);
        AssertCompactContribution(combat, 2141, 2141, 2673, CombatMetricKind.Healing, CombatPacketRule.CompactRecovery, CombatAssociationKind.CompactInlineRecoveryGroup);
        AssertContribution(
            combat,
            2141,
            30058,
            10_000,
            CombatMetricKind.Damage,
            CombatDeliveryKind.Direct,
            CombatPacketRule.DirectValue,
            CombatResolutionAuthority.PacketDefault,
            CombatMaterializationKind.Primary,
            CombatAssociationKind.None);
    }

    [Fact]
    public void ScenePath_DoesNotClassifyInlineSidecarDirectDamageWithoutSelfEvidenceAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 8972, targetId: 144994, bodyCodeRaw: 17730001, marker: 194, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 2519));
        journal.Append(CreateInlineSidecar(sceneId, 1, sourceId: 8972, targetId: 144994, bodyCodeRaw: 17730001, marker: 194, type: 2));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 144994, out var pair));
        Assert.Equal(2519, pair!.TotalDamage);
        Assert.Equal(0, pair.TotalHealing);
        AssertCompactContribution(combat, 8972, 144994, 2519, CombatMetricKind.Damage, CombatPacketRule.CompactDirectValue, CombatAssociationKind.None);
    }

    [Fact]
    public void ScenePath_DoesNotClassifyUnmatchedLoop2DirectDamageAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 8972, targetId: 144994, bodyCodeRaw: 17010230, marker: 189, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 5629, loop: 2));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 144994, out var pair));
        Assert.Equal(5629, pair!.TotalDamage);
        Assert.Equal(0, pair.TotalHealing);
        AssertCompactContribution(combat, 8972, 144994, 5629, CombatMetricKind.Damage, CombatPacketRule.CompactDirectValue, CombatAssociationKind.None);
    }

    [Fact]
    public void ScenePath_FlushesUnmatchedCompactDirectValueAsDamage()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 8972, targetId: 5578, bodyCodeRaw: 17800001, marker: 193, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 2048));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 5578, out var pair));
        Assert.Equal(2048, pair!.TotalDamage);
        Assert.Equal(0, pair.TotalHealing);
        AssertCompactContribution(combat, 8972, 5578, 2048, CombatMetricKind.Damage, CombatPacketRule.CompactDirectValue, CombatAssociationKind.None);
    }

    [Fact]
    public void ScenePath_DoesNotClassifyOutOfOrderTargetEchoCompactControlDamageAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 8972, targetId: 144994, bodyCodeRaw: 17730001, marker: 194, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 2519));
        journal.Append(CreateCompactControlOpener(sceneId, 1, sourceId: 8972, bodyCodeRaw: 17730001, marker: 194, mode: 0, flag: 0, echoSourceId: 144994));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 144994, out var pair));
        Assert.Equal(2519, pair!.TotalDamage);
        Assert.Equal(0, pair.TotalHealing);
        AssertCompactContribution(combat, 8972, 144994, 2519, CombatMetricKind.Damage, CombatPacketRule.CompactDirectValue, CombatAssociationKind.CompactOpener);
    }

    [Fact]
    public void ScenePath_DoesNotClassifyTargetedCompactControlDirectDamageAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactControlOpener(sceneId, 0, sourceId: 8972, bodyCodeRaw: 17730001, marker: 194, mode: 0, flag: 0, echoSourceId: 144994));
        journal.Append(CreateDirectValue(sceneId, 1, sourceId: 8972, targetId: 144994, bodyCodeRaw: 17730001, marker: 194, layoutTag: 6, flag: 0, type: 2, chainId: 16702, damage: 2519));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 144994, out var pair));
        Assert.Equal(2519, pair!.TotalDamage);
        Assert.Equal(0, pair.TotalHealing);
        AssertContribution(
            combat,
            8972,
            144994,
            2519,
            CombatMetricKind.Damage,
            CombatDeliveryKind.Direct,
            CombatPacketRule.DirectValue,
            CombatResolutionAuthority.PacketDefault,
            CombatMaterializationKind.Primary,
            CombatAssociationKind.None);
    }

    [Fact]
    public void ScenePath_DoesNotClassifyNonRecoveryShapeWithSourceEchoAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactControlOpener(sceneId, 0, sourceId: 12632, bodyCodeRaw: 13120240, marker: 196, mode: 0, flag: 2, echoSourceId: 12632));
        journal.Append(CreateDirectValue(sceneId, 1, sourceId: 12632, targetId: 161904, bodyCodeRaw: 13120240, marker: 196, layoutTag: 20, flag: 0, type: 3, chainId: 21678, damage: 52763));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(12632, 161904, out var pair));
        Assert.Equal(52763, pair!.TotalDamage);
        Assert.Equal(0, pair.TotalHealing);
        AssertContribution(
            combat,
            12632,
            161904,
            52763,
            CombatMetricKind.Damage,
            CombatDeliveryKind.Direct,
            CombatPacketRule.DirectValue,
            CombatResolutionAuthority.PacketDefault,
            CombatMaterializationKind.Primary,
            CombatAssociationKind.None);
    }

    [Fact]
    public void ScenePath_PreservesParserAuthoritativeMultiHitFact()
    {
        var journal = new ObservedEventJournal();
        journal.Append(new ObservedEventTestEntry<CombatWireObservation>(
            new ObservedEventHeader(
                Guid.NewGuid(),
                new TimelineStamp { ObservationOrdinal = 0, FlushId = 100 },
                8171,
                42995,
                default),
            new CombatWireObservation
            {
                SkillCode = 17010230,
                Damage = 2400,
                HitCount = 1,
                AttemptCount = 1,
                Marker = 5,
                MultiHitCount = 2,
                Modifiers = DamageModifiers.MultiHit
            }));

        var combat = Apply(journal, out var mechanics, out var resources);

        var pair = CombatPairProjection.GetPair(combat, mechanics, resources, 8171, 42995);
        Assert.True(pair.HasValue);
        Assert.Equal(1, pair.Value.MultiHitCount);
        var source = CombatPairProjection.GetCombatant(combat, mechanics, resources, 8171);
        Assert.True(source.HasValue);
        Assert.Equal(1, source.Value.OutgoingMultiHits);
        var eventRecord = AssertContribution(
            combat,
            8171,
            42995,
            2400,
            CombatMetricKind.Damage,
            CombatDeliveryKind.Direct,
            CombatPacketRule.DirectValue,
            CombatResolutionAuthority.PacketDefault,
            CombatMaterializationKind.Primary,
            CombatAssociationKind.None);
        Assert.Equal(2, eventRecord.Observation.MultiHitCount);
        var mechanicEvent = Assert.Single(mechanics.Events);
        Assert.Equal(2, mechanicEvent.Observation.MultiHitCount);
        Assert.Equal(1, mechanicEvent.Mechanic.MultiHitCount);
        Assert.Equal(2, mechanicEvent.Mechanic.MultiHitSubCount);
    }

    [Fact]
    public void ScenePath_PreservesAdjacentRepeatedCompactAvoidanceEventsWithSameKey()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactAvoidanceSignal(sceneId, 0, sourceId: 31338, targetId: 2141, bodyCodeRaw: 1603150, marker: 23, layoutTag: 2, scopeId: 101, flushId: 100));
        journal.Append(CreateCompactAvoidanceSignal(sceneId, 1, sourceId: 31338, targetId: 2141, bodyCodeRaw: 1603150, marker: 23, layoutTag: 0, scopeId: 102, flushId: 101));
        journal.Append(CreateCompactAvoidanceSignal(sceneId, 2, sourceId: 31338, targetId: 2141, bodyCodeRaw: 1603150, marker: 23, layoutTag: 2, scopeId: 103, flushId: 102));
        journal.Append(CreateCompactAvoidanceSignal(sceneId, 3, sourceId: 31338, targetId: 2141, bodyCodeRaw: 1603150, marker: 23, layoutTag: 0, scopeId: 104, flushId: 103));
        journal.Append(CreateCompactAvoidanceSignal(sceneId, 4, sourceId: 31338, targetId: 2141, bodyCodeRaw: 1603150, marker: 23, layoutTag: 0, scopeId: 105, flushId: 104));

        var combat = Apply(journal, out var mechanics);

        Assert.False(combat.TryGetPair(31338, 2141, out _));
        Assert.True(mechanics.TryGetPair(31338, 2141, out var pair));
        Assert.Equal(3, pair!.AttemptCount);
        Assert.Equal(3, pair.EvadeCount);
        var avoidanceEvents = mechanics.Events.Where(static e => e.Mechanic.Resolution.PacketRule == CombatPacketRule.CompactAvoidance).ToArray();
        Assert.Equal(3, avoidanceEvents.Length);
        Assert.All(avoidanceEvents, static eventRecord =>
        {
            Assert.Equal(0, eventRecord.Observation.Damage);
            Assert.Equal(0, eventRecord.Mechanic.HitCount);
            Assert.Equal(1, eventRecord.Mechanic.AttemptCount);
            Assert.Equal(1, eventRecord.Mechanic.EvadeCount);
            Assert.Equal(CombatResolutionAuthority.Packet, eventRecord.Mechanic.Resolution.Authority);
            Assert.Equal(CombatMaterializationKind.Primary, eventRecord.Mechanic.Resolution.Materialization);
            Assert.Equal(CombatAssociationKind.None, eventRecord.Mechanic.Resolution.Association);
            Assert.Equal(CombatSemanticMatchKind.None, eventRecord.Mechanic.Resolution.SemanticMatch);
        });
    }

    [Fact]
    public void CompactAvoidance_DoesNotPromoteOutOfDomainBodyToken()
    {
        var canonicalizer = new CompactAvoidanceCanonicalizer();
        var stamp = new TimelineStamp { ObservationOrdinal = 1, FlushId = 1 };
        var observation = new CombatWireObservation
        {
            SkillCode = 40_567_740,
            BodySkillVariantRaw = 40_567_740,
            HitCount = 0,
            AttemptCount = 0,
            Marker = 240,
            LayoutTag = 0,
            Type = 1
        };

        var observed = canonicalizer.ObserveCompactValue0438(33, 1_131_441, in stamp, in observation, 0, default);

        Assert.Equal(0, observed.Count);
        Assert.Equal(0, canonicalizer.FlushPending().Count);
    }

    [Fact]
    public void PeriodicNormalizer_PreservesParserAuthoritativeMultiHitCount()
    {
        var canonicalizer = new PeriodicPoolCanonicalizer();
        var observation = new CombatWireObservation
        {
            SkillCode = 17010230,
            Damage = 2400,
            HitCount = 1,
            AttemptCount = 1,
            Marker = 5,
            MultiHitCount = 2,
            Modifiers = DamageModifiers.MultiHit
        };

        var results = canonicalizer.Normalize(8171, 42995, in observation);
        Assert.Equal(1, results.Count);
        var result = results[0];

        Assert.Equal(2400, result.Observation.Damage);
        Assert.Equal(1, result.Observation.HitCount);
        Assert.Equal(1, result.Observation.AttemptCount);
        Assert.Equal(2, result.Observation.MultiHitCount);
        Assert.Equal(DamageModifiers.MultiHit, result.Observation.Modifiers & DamageModifiers.MultiHit);
    }

    private static CombatEventRecord AssertCompactContribution(
        CombatStore combat,
        int sourceId,
        int targetId,
        long packetValue,
        CombatMetricKind metric,
        CombatPacketRule packetRule,
        CombatAssociationKind association) =>
        AssertContribution(
            combat,
            sourceId,
            targetId,
            packetValue,
            metric,
            CombatDeliveryKind.Direct,
            packetRule,
            packetRule == CombatPacketRule.CompactDirectValue
                ? CombatResolutionAuthority.PacketDefault
                : CombatResolutionAuthority.Packet,
            CombatMaterializationKind.CompactAssociated,
            association);

    private static CombatEventRecord AssertContribution(
        CombatStore combat,
        int sourceId,
        int targetId,
        long packetValue,
        CombatMetricKind metric,
        CombatDeliveryKind delivery,
        CombatPacketRule packetRule,
        CombatResolutionAuthority authority,
        CombatMaterializationKind materialization,
        CombatAssociationKind association)
    {
        var eventRecord = Assert.Single(combat.Events, e =>
            e.SourceId == sourceId &&
            e.TargetId == targetId &&
            e.Observation.Damage == packetValue);

        Assert.Equal(packetValue, eventRecord.Observation.Damage);
        Assert.Equal(CombatResourceKind.Unknown, eventRecord.Observation.ResourceKind);
        Assert.Equal(PeriodicEffectRelation.None, eventRecord.Observation.PeriodicRelation);
        Assert.Equal(CombatWireOutcomeKind.None, eventRecord.Observation.OutcomeKind);

        var contribution = eventRecord.Contribution;
        Assert.Equal(metric, contribution.Metric);
        Assert.Equal(delivery, contribution.Delivery);
        Assert.Equal(packetValue, contribution.Amount);
        Assert.Equal(packetRule, contribution.Resolution.PacketRule);
        Assert.Equal(authority, contribution.Resolution.Authority);
        Assert.Equal(materialization, contribution.Resolution.Materialization);
        Assert.Equal(association, contribution.Resolution.Association);
        Assert.Equal(CombatSemanticMatchKind.None, contribution.Resolution.SemanticMatch);
        Assert.False(contribution.Resolution.HasResourceEvidence);
        return eventRecord;
    }

    private static CombatStore Apply(ObservedEventJournal journal)
        => Apply(journal, out _);

    private static CombatStore Apply(ObservedEventJournal journal, ICombatOccurrenceObserver observer)
    {
        var combat = new CombatStore();
        var applier = new DomainEventApplier(new EntityStore(), new SceneBoundaryStore(), combat, observer);
        applier.ApplyJournal(journal);
        return combat;
    }

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

    private static ObservedEventTestEntry<CombatWireObservation> CreateCompactControlOpener(Guid sceneId, long ordinal, int sourceId, int bodyCodeRaw, int marker, int mode, int flag, int echoSourceId, int scopeId = 100, long flushId = 100) =>
        new(
            new ObservedEventHeader(
                sceneId,
                new TimelineStamp { ObservationOrdinal = ordinal, FlushId = flushId },
                sourceId,
                0,
                new RawPacketReference(0x0238, 0, 0, CreateStructurePath(scopeId))),
            new CombatWireObservation
        {
            BodyCodeRaw = unchecked((uint)bodyCodeRaw),
            Damage = 0,
            HitCount = 0,
            AttemptCount = 0,
            DetailRaw = marker,
            Marker = marker,
            Type = mode,
            Flag = flag,
            ChainId = echoSourceId,
            LayoutTag = 0
        });

    private static ObservedEventTestEntry<CombatWireObservation> CreateCompactControlCloser(Guid sceneId, long ordinal, int sourceId, int bodyCodeRaw, int marker, int flag, int scopeId = 100, long flushId = 100) =>
        new(
            new ObservedEventHeader(
                sceneId,
                new TimelineStamp { ObservationOrdinal = ordinal, FlushId = flushId },
                sourceId,
                0,
                new RawPacketReference(0x0638, 0, 0, CreateStructurePath(scopeId))),
            new CombatWireObservation
        {
            BodyResourceEffectRef = ResourceEffectRef.FromRaw(bodyCodeRaw),
            Damage = 0,
            HitCount = 0,
            AttemptCount = 0,
            DetailRaw = marker,
            Marker = marker,
            Flag = flag,
            LayoutTag = 0
        });

    private static ObservedEventTestEntry<CombatWireObservation> CreateCompactAvoidanceSignal(Guid sceneId, long ordinal, int sourceId, int targetId, int bodyCodeRaw, int marker, int layoutTag, int scopeId = 100, long flushId = 100) =>
        new(
            new ObservedEventHeader(
                sceneId,
                new TimelineStamp { ObservationOrdinal = ordinal, FlushId = flushId },
                sourceId,
                targetId,
                new RawPacketReference(0x0438, 0, 0, CreateStructurePath(scopeId))),
            new CombatWireObservation
        {
            SkillCode = bodyCodeRaw,
            BodySkillVariantRaw = bodyCodeRaw,
            Damage = 0,
            HitCount = 0,
            AttemptCount = 0,
            Marker = marker,
            LayoutTag = layoutTag,
            Flag = 0,
            Type = 1,
            Loop = 0,
            ChainId = 0
        });

    private static ObservedEventTestEntry<CombatWireObservation> CreateDirectValue(Guid sceneId, long ordinal, int sourceId, int targetId, int bodyCodeRaw, int marker, int layoutTag, int flag, int type, int chainId, int damage, int scopeId = 100, long flushId = 100, int loop = 1, uint detailRef = 0, PacketStructurePath structurePath = default) =>
        new(
            new ObservedEventHeader(
                sceneId,
                new TimelineStamp { ObservationOrdinal = ordinal, FlushId = flushId },
                sourceId,
                targetId,
                new RawPacketReference(0x0438, 0, 0, structurePath.IsEmpty ? CreateStructurePath(scopeId) : structurePath)),
            new CombatWireObservation
        {
            SkillCode = bodyCodeRaw,
            BodySkillVariantRaw = bodyCodeRaw,
            DetailResourceEffectRef = ResourceEffectRef.FromRaw(detailRef),
            Damage = damage,
            HitCount = 1,
            AttemptCount = 1,
            Marker = marker,
            LayoutTag = layoutTag,
            Flag = flag,
            Type = type,
            Loop = loop,
            ChainId = chainId
        });

    private static ObservedEventTestEntry<CombatWireObservation> CreateInlineSidecar(Guid sceneId, long ordinal, int sourceId, int targetId, int bodyCodeRaw, int marker, int type, int scopeId = 100, long flushId = 100) =>
        new(
            new ObservedEventHeader(
                sceneId,
                new TimelineStamp { ObservationOrdinal = ordinal, FlushId = flushId },
                sourceId,
                targetId,
                new RawPacketReference(0x0438, 0, 0, CreateStructurePath(scopeId))),
            new CombatWireObservation
        {
            SkillCode = bodyCodeRaw,
            BodySkillVariantRaw = bodyCodeRaw,
            Damage = 0,
            HitCount = 0,
            AttemptCount = 0,
            Marker = marker,
            LayoutTag = 0,
            Flag = 0,
            Type = type,
            Loop = 0,
            ChainId = 0
        });

    private static PacketStructurePath CreateStructurePath(int scopeId)
    {
        if (scopeId <= 0)
            return default;

        var root = new PacketStructureReference(PacketStructureKind.TransportPacket, 10, 0, 1, 0, 0, 256, 0, 256);
        var leaf = new PacketStructureReference(PacketStructureKind.FrameBatchEntry, scopeId, root.ScopeId, 2, 0, 0, 64, 0, 64);
        return default(PacketStructurePath).Push(root).Push(leaf);
    }

    private static PacketStructurePath CreateCompressedPayloadStructurePath(int compressedScopeId, int leafScopeId, int siblingIndex)
    {
        var root = new PacketStructureReference(PacketStructureKind.TransportPacket, 10, 0, 1, 0, 0, 4096, 0, 4096);
        var compressed = new PacketStructureReference(PacketStructureKind.CompressedPayload, compressedScopeId, root.ScopeId, 2, 0, 8, 4088, 8, 4088);
        var leaf = new PacketStructureReference(PacketStructureKind.FrameBatchEntry, leafScopeId, compressed.ScopeId, 3, siblingIndex, 0, 30, 0, 30);
        return default(PacketStructurePath).Push(root).Push(compressed).Push(leaf);
    }

    private sealed class RecordingCombatOccurrenceObserver : ICombatOccurrenceObserver
    {
        public List<CombatOccurrenceContext> Contexts { get; } = [];

        public void Observe(in CombatOccurrenceContext context) => Contexts.Add(context);
    }
}
