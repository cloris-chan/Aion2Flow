using Cloris.Aion2Flow.SceneRuntime.Canonicalization;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public class CombatPacketFactTests
{
    [Fact]
    public void ScenePath_ClassifiesCompactActionDirectRecoveryAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactActionOpener(sceneId, 0, sourceId: 8972, bodyCodeRaw: 17800001, marker: 193, mode: 0, flag: 0, echoSourceId: 8972));
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
    public void ScenePath_ClassifiesOutOfOrderCompactActionDirectRecoveryAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 8972, targetId: 5578, bodyCodeRaw: 17800001, marker: 193, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 2048));
        journal.Append(CreateCompactActionOpener(sceneId, 1, sourceId: 8972, bodyCodeRaw: 17800001, marker: 193, mode: 0, flag: 0, echoSourceId: 8972));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 5578, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(2048, pair.TotalHealing);
        Assert.True(combat.TryGetCombatant(8972, out var source));
        Assert.Equal(0, source!.OutgoingDamage);
        Assert.Equal(2048, source.OutgoingHealing);
    }

    [Fact]
    public void ScenePath_ClassifiesCompactActionDirectRecoveryAcrossFrameEntryScopes()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 8972, targetId: 5578, bodyCodeRaw: 17800001, marker: 193, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 2048, scopeId: 101));
        journal.Append(CreateCompactActionOpener(sceneId, 1, sourceId: 8972, bodyCodeRaw: 17800001, marker: 193, mode: 0, flag: 0, echoSourceId: 8972, scopeId: 102));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 5578, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(2048, pair.TotalHealing);
    }

    [Fact]
    public void ScenePath_ClassifiesCompactActionDirectRecoveryAcrossAdjacentPacketBatches()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactActionOpener(sceneId, 0, sourceId: 8972, bodyCodeRaw: 17800001, marker: 193, mode: 0, flag: 0, echoSourceId: 8972, batchOrdinal: 100));
        journal.Append(CreateDirectValue(sceneId, 1, sourceId: 8972, targetId: 5578, bodyCodeRaw: 17800001, marker: 193, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 2048, batchOrdinal: 101));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 5578, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(2048, pair.TotalHealing);
    }

    [Fact]
    public void ScenePath_ClassifiesSelfCompactActionDirectRecoveryAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 8972, targetId: 8972, bodyCodeRaw: 17800001, marker: 193, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 2048));
        journal.Append(CreateCompactActionOpener(sceneId, 1, sourceId: 8972, bodyCodeRaw: 17800001, marker: 193, mode: 0, flag: 0, echoSourceId: 8972));

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
    public void ScenePath_ClassifiesCompactActionDirectRecoveryWithVariableValueUnknownAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactActionOpener(sceneId, 0, sourceId: 3013, bodyCodeRaw: 17121351, marker: 41, mode: 0, flag: 0, echoSourceId: 3013));
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
    public void ScenePath_ClassifiesType12CompactActionDirectRecoveryAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactActionOpener(sceneId, 0, sourceId: 8972, bodyCodeRaw: 17100140, marker: 186, mode: 12, flag: 0, echoSourceId: 8972));
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
    public void ScenePath_ClassifiesInlineSidecarCompactActionRecoveryAsHealing()
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
    public void ScenePath_FlushesUnmatchedCompactActionDirectValueAsDamage()
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
    public void ScenePath_DoesNotClassifyOutOfOrderTargetEchoCompactActionDamageAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateDirectValue(sceneId, 0, sourceId: 8972, targetId: 144994, bodyCodeRaw: 17730001, marker: 194, layoutTag: 4, flag: 0, type: 2, chainId: 16702, damage: 2519));
        journal.Append(CreateCompactActionOpener(sceneId, 1, sourceId: 8972, bodyCodeRaw: 17730001, marker: 194, mode: 0, flag: 0, echoSourceId: 144994));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8972, 144994, out var pair));
        Assert.Equal(2519, pair!.TotalDamage);
        Assert.Equal(0, pair.TotalHealing);
    }

    [Fact]
    public void ScenePath_DoesNotClassifyTargetedCompactActionDirectDamageAsHealing()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(CreateCompactActionOpener(sceneId, 0, sourceId: 8972, bodyCodeRaw: 17730001, marker: 194, mode: 0, flag: 0, echoSourceId: 144994));
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
        journal.Append(CreateCompactActionOpener(sceneId, 0, sourceId: 12632, bodyCodeRaw: 13120240, marker: 196, mode: 0, flag: 2, echoSourceId: 12632));
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
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = Guid.NewGuid(),
            Stamp = new TimelineStamp { ObservationOrdinal = 0, FrameOrdinal = 10, BatchOrdinal = 100 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 8171,
            TargetEntityId = 42995,
            Combat = new CombatObservation
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
            }
        });

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8171, 42995, out var pair));
        Assert.Equal(1, pair!.MultiHitCount);
        Assert.True(combat.TryGetCombatant(8171, out var source));
        Assert.Equal(1, source!.OutgoingMultiHits);
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

    private static ObservedEventEnvelope CreateCompactActionOpener(Guid sceneId, long ordinal, int sourceId, int bodyCodeRaw, int marker, int mode, int flag, int echoSourceId, int scopeId = 0, long batchOrdinal = 100) => new()
    {
        SceneSessionId = sceneId,
        Stamp = new TimelineStamp { ObservationOrdinal = ordinal, FrameOrdinal = ordinal + 10, BatchOrdinal = batchOrdinal },
        Domain = ObservedEventDomain.Combat,
        SourceEntityId = sourceId,
        TargetEntityId = 0,
        Raw = new RawPacketReference(0x0238, 0, 0, CreateStructurePath(scopeId)),
        Combat = new CombatObservation
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
        }
    };

    private static ObservedEventEnvelope CreateDirectValue(Guid sceneId, long ordinal, int sourceId, int targetId, int bodyCodeRaw, int marker, int layoutTag, int flag, int type, int chainId, int damage, int scopeId = 0, long batchOrdinal = 100, int loop = 1) => new()
    {
        SceneSessionId = sceneId,
        Stamp = new TimelineStamp { ObservationOrdinal = ordinal, FrameOrdinal = ordinal + 10, BatchOrdinal = batchOrdinal },
        Domain = ObservedEventDomain.Combat,
        SourceEntityId = sourceId,
        TargetEntityId = targetId,
        Raw = new RawPacketReference(0x0438, 0, 0, CreateStructurePath(scopeId)),
        Combat = new CombatObservation
        {
            SkillCode = bodyCodeRaw,
            BodySkillVariantRaw = bodyCodeRaw,
            Damage = damage,
            HitCount = 1,
            AttemptCount = 1,
            Marker = marker,
            LayoutTag = layoutTag,
            Flag = flag,
            Type = type,
            Loop = loop,
            ChainId = chainId
        }
    };

    private static ObservedEventEnvelope CreateInlineSidecar(Guid sceneId, long ordinal, int sourceId, int targetId, int bodyCodeRaw, int marker, int type, int scopeId = 0, long batchOrdinal = 100) => new()
    {
        SceneSessionId = sceneId,
        Stamp = new TimelineStamp { ObservationOrdinal = ordinal, FrameOrdinal = ordinal + 10, BatchOrdinal = batchOrdinal },
        Domain = ObservedEventDomain.Combat,
        SourceEntityId = sourceId,
        TargetEntityId = targetId,
        Raw = new RawPacketReference(0x0438, 0, 0, CreateStructurePath(scopeId)),
        Combat = new CombatObservation
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
        }
    };

    private static PacketStructurePath CreateStructurePath(int scopeId)
    {
        if (scopeId <= 0)
            return default;

        var root = new PacketStructureReference(PacketStructureKind.TransportPacket, 10, 0, 1, 0, 0, 256, 0, 256);
        var leaf = new PacketStructureReference(PacketStructureKind.FrameBatchEntry, scopeId, root.ScopeId, 2, 0, 0, 64, 0, 64);
        return default(PacketStructurePath).Push(root).Push(leaf);
    }
}
