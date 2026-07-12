using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Canonicalization;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public class CombatPacketFactTests
{
    public CombatPacketFactTests()
        => CombatResourceRegistry.SetGameResources([], new Dictionary<int, NpcDisplayEntry>());

    [Fact]
    public void CombatContributionCanonicalization_UsesSemanticBitOrder()
    {
        Assert.Equal(0, (int)CombatContributionCanonicalization.None);
        Assert.Equal(1 << 0, (int)CombatContributionCanonicalization.CompactDirectValue);
        Assert.Equal(1 << 1, (int)CombatContributionCanonicalization.CompactRecoveryByOpener);
        Assert.Equal(1 << 2, (int)CombatContributionCanonicalization.CompactRecoveryByInlineGroup);
        Assert.Equal(1 << 3, (int)CombatContributionCanonicalization.CompactRecoveryBySelfValueGroup);
        Assert.Equal(1 << 4, (int)CombatContributionCanonicalization.CompactAvoidance);
        Assert.Equal(1 << 5, (int)CombatContributionCanonicalization.OwnerTargetSummonResource);
        Assert.Equal(1 << 6, (int)CombatContributionCanonicalization.SystemPeriodicRecoverySeed);
        Assert.Equal(1 << 7, (int)CombatContributionCanonicalization.SystemPeriodicRecoveryHealing);
        Assert.Equal(1 << 8, (int)CombatContributionCanonicalization.PeriodicStandaloneDamage);
        Assert.Equal(1 << 9, (int)CombatContributionCanonicalization.PeriodicStandaloneContinuation);
        Assert.Equal(1 << 10, (int)CombatContributionCanonicalization.PeriodicContinuationHealing);
        Assert.Equal(1 << 11, (int)CombatContributionCanonicalization.PeriodicShieldGrant);
        Assert.Equal(1 << 12, (int)CombatContributionCanonicalization.PeriodicShieldAbsorbed);
    }

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
    }

    [Fact]
    public void ScenePath_CompactControlCloseStillMatchesLaterRecoveryValue()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactControlOpener(sceneId, 0, sourceId: 8972, bodyCodeRaw: 17800001, marker: 193, mode: 0, flag: 0, echoSourceId: 8972));
        journal.Append(CreateCompactControlCloser(sceneId, 1, sourceId: 8972, bodyCodeRaw: 17800001, marker: 193, flag: 0));
        journal.Append(CreateDirectValue(sceneId, 2, sourceId: 8972, targetId: 5578, bodyCodeRaw: 17800001, marker: 193, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 2048, eventKind: CombatEventKind.Support, valueKind: CombatValueKind.Support));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 5578, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(2048, pair.TotalHealing);
        Assert.True(combat.TryGetCombatant(8972, out var source));
        Assert.Equal(0, source!.OutgoingDamage);
        Assert.Equal(2048, source.OutgoingHealing);
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
        Assert.Equal(2, combat.Events.Count(static e => (e.Canonicalization & CombatContributionCanonicalization.CompactRecoveryBySelfValueGroup) != 0));
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
        Assert.Equal(2, combat.Events.Count(static e => (e.Canonicalization & CombatContributionCanonicalization.CompactRecoveryBySelfValueGroup) != 0));
    }

    [Fact]
    public void ScenePath_DoesNotClassifySelfValueRecoveryAcrossCompressedPayloads()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 4587, targetId: 3039, bodyCodeRaw: 17800001, marker: 111, layoutTag: 4, flag: 0, type: 2, chainId: 21686, damage: 2586, loop: 1, detailRef: 1780000111, structurePath: CreateCompressedPayloadStructurePath(compressedScopeId: 600, leafScopeId: 601, siblingIndex: 18)));
        journal.Append(CreateDirectValue(sceneId, 1, sourceId: 4587, targetId: 4587, bodyCodeRaw: 17800001, marker: 111, layoutTag: 4, flag: 0, type: 2, chainId: 21686, damage: 2104, loop: 1, detailRef: 1780000111, structurePath: CreateCompressedPayloadStructurePath(compressedScopeId: 700, leafScopeId: 701, siblingIndex: 36)));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(4587, 3039, out var target));
        Assert.Equal(2586, target!.TotalDamage);
        Assert.Equal(0, target.TotalHealing);
        Assert.True(combat.TryGetPair(4587, 4587, out var self));
        Assert.Equal(0, self!.TotalHealing);
        Assert.DoesNotContain(combat.Events, static e => (e.Canonicalization & CombatContributionCanonicalization.CompactRecoveryBySelfValueGroup) != 0);
    }

    [Fact]
    public void ScenePath_DoesNotClassifySamePayloadLoop1SelfPairAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 4587, targetId: 4587, bodyCodeRaw: 17800001, marker: 111, layoutTag: 4, flag: 0, type: 2, chainId: 21686, damage: 2586, loop: 1, detailRef: 1780000111, structurePath: CreateCompressedPayloadStructurePath(compressedScopeId: 800, leafScopeId: 801, siblingIndex: 18)));
        journal.Append(CreateDirectValue(sceneId, 1, sourceId: 4587, targetId: 4587, bodyCodeRaw: 17800001, marker: 111, layoutTag: 4, flag: 0, type: 2, chainId: 21686, damage: 2104, loop: 1, detailRef: 1780000112, structurePath: CreateCompressedPayloadStructurePath(compressedScopeId: 800, leafScopeId: 802, siblingIndex: 19)));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(4587, 4587, out var self));
        Assert.Equal(0, self!.TotalHealing);
        Assert.DoesNotContain(combat.Events, static e => (e.Canonicalization & CombatContributionCanonicalization.CompactRecoveryBySelfValueGroup) != 0);
    }

    [Fact]
    public void ScenePath_DoesNotClassifySamePayloadSelfPairWithSameDetailRefAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 7023, targetId: 7023, bodyCodeRaw: 17101450, marker: 78, layoutTag: 4, flag: 0, type: 2, chainId: 16694, damage: 3432, loop: 2, detailRef: 1710004011, structurePath: CreateCompressedPayloadStructurePath(compressedScopeId: 810, leafScopeId: 811, siblingIndex: 142)));
        journal.Append(CreateDirectValue(sceneId, 1, sourceId: 7023, targetId: 7023, bodyCodeRaw: 17101450, marker: 78, layoutTag: 4, flag: 0, type: 2, chainId: 16694, damage: 4087, loop: 2, detailRef: 1710004011, structurePath: CreateCompressedPayloadStructurePath(compressedScopeId: 810, leafScopeId: 812, siblingIndex: 143)));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(7023, 7023, out var self));
        Assert.Equal(0, self!.TotalHealing);
        Assert.DoesNotContain(combat.Events, static e => (e.Canonicalization & CombatContributionCanonicalization.CompactRecoveryBySelfValueGroup) != 0);
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
    }

    [Fact]
    public void ScenePath_PreservesParserAuthoritativeMultiHitFact()
    {
        var journal = new ObservedEventJournal();
        journal.Append(new ObservedEventTestEntry<CombatObservation>(
            new ObservedEventHeader(
                Guid.NewGuid(),
                new TimelineStamp { ObservationOrdinal = 0, FlushId = 100 },
                8171,
                42995,
                default),
            new CombatObservation
            {
                SkillCode = 17010230,
                Damage = 2400,
                HitCount = 1,
                AttemptCount = 1,
                Marker = 5,
                MultiHitCount = 2,
                Modifiers = DamageModifiers.MultiHit,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8171, 42995, out var pair));
        Assert.Equal(1, pair!.MultiHitCount);
        Assert.True(combat.TryGetCombatant(8171, out var source));
        Assert.Equal(1, source!.OutgoingMultiHits);
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

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(31338, 2141, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(3, pair.AttemptCount);
        Assert.Equal(3, pair.EvadeCount);
    }

    [Fact]
    public void CompactAvoidance_DoesNotPromoteOutOfDomainBodyToken()
    {
        var canonicalizer = new CompactAvoidanceCanonicalizer();
        var stamp = new TimelineStamp { ObservationOrdinal = 1, FlushId = 1 };
        var observation = new CombatObservation
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
        var observation = new CombatObservation
        {
            SkillCode = 17010230,
            Damage = 2400,
            HitCount = 1,
            AttemptCount = 1,
            Marker = 5,
            MultiHitCount = 2,
            Modifiers = DamageModifiers.MultiHit,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };

        var results = canonicalizer.Normalize(8171, 42995, in observation);
        Assert.Equal(1, results.Count);
        var result = results[0];

        Assert.Equal(2, result.Observation.MultiHitCount);
        Assert.Equal(DamageModifiers.MultiHit, result.Observation.Modifiers & DamageModifiers.MultiHit);
    }

    private static CombatStore Apply(ObservedEventJournal journal)
    {
        var combat = new CombatStore();
        var applier = new DomainEventApplier(new EntityStore(), new SceneBoundaryStore(), combat);
        applier.ApplyJournal(journal);
        return combat;
    }

    private static ObservedEventTestEntry<CombatObservation> CreateCompactControlOpener(Guid sceneId, long ordinal, int sourceId, int bodyCodeRaw, int marker, int mode, int flag, int echoSourceId, int scopeId = 100, long flushId = 100) =>
        new(
            new ObservedEventHeader(
                sceneId,
                new TimelineStamp { ObservationOrdinal = ordinal, FlushId = flushId },
                sourceId,
                0,
                new RawPacketReference(0x0238, 0, 0, CreateStructurePath(scopeId))),
            new CombatObservation
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

    private static ObservedEventTestEntry<CombatObservation> CreateCompactControlCloser(Guid sceneId, long ordinal, int sourceId, int bodyCodeRaw, int marker, int flag, int scopeId = 100, long flushId = 100) =>
        new(
            new ObservedEventHeader(
                sceneId,
                new TimelineStamp { ObservationOrdinal = ordinal, FlushId = flushId },
                sourceId,
                0,
                new RawPacketReference(0x0638, 0, 0, CreateStructurePath(scopeId))),
            new CombatObservation
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

    private static ObservedEventTestEntry<CombatObservation> CreateCompactAvoidanceSignal(Guid sceneId, long ordinal, int sourceId, int targetId, int bodyCodeRaw, int marker, int layoutTag, int scopeId = 100, long flushId = 100) =>
        new(
            new ObservedEventHeader(
                sceneId,
                new TimelineStamp { ObservationOrdinal = ordinal, FlushId = flushId },
                sourceId,
                targetId,
                new RawPacketReference(0x0438, 0, 0, CreateStructurePath(scopeId))),
            new CombatObservation
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

    private static ObservedEventTestEntry<CombatObservation> CreateDirectValue(Guid sceneId, long ordinal, int sourceId, int targetId, int bodyCodeRaw, int marker, int layoutTag, int flag, int type, int chainId, int damage, int scopeId = 100, long flushId = 100, int loop = 1, CombatEventKind eventKind = CombatEventKind.Unknown, CombatValueKind valueKind = CombatValueKind.Unknown, uint detailRef = 0, PacketStructurePath structurePath = default) =>
        new(
            new ObservedEventHeader(
                sceneId,
                new TimelineStamp { ObservationOrdinal = ordinal, FlushId = flushId },
                sourceId,
                targetId,
                new RawPacketReference(0x0438, 0, 0, structurePath.IsEmpty ? CreateStructurePath(scopeId) : structurePath)),
            new CombatObservation
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
            ChainId = chainId,
            EventKind = eventKind,
            ValueKind = valueKind
        });

    private static ObservedEventTestEntry<CombatObservation> CreateInlineSidecar(Guid sceneId, long ordinal, int sourceId, int targetId, int bodyCodeRaw, int marker, int type, int scopeId = 100, long flushId = 100) =>
        new(
            new ObservedEventHeader(
                sceneId,
                new TimelineStamp { ObservationOrdinal = ordinal, FlushId = flushId },
                sourceId,
                targetId,
                new RawPacketReference(0x0438, 0, 0, CreateStructurePath(scopeId))),
            new CombatObservation
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
}
