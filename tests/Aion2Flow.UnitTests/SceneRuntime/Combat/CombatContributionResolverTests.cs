using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class CombatContributionResolverTests
{
    public CombatContributionResolverTests()
    {
        CombatResourceRegistry.SetGameResources(ResourceCatalog.Load(ResourceLanguage.English));
    }

    [Fact]
    public void Proven_CompactRecovery_Is_Authoritative_Over_Resource_Semantics()
    {
        var observation = DirectValue(1_010_000, 1_064) with
        {
            DetailResourceEffectRef = ResourceEffectRef.FromRaw(101_000_011)
        };

        var contribution = Resolve(18_846, 18_846, in observation, CombatPacketRule.CompactRecovery);

        Assert.Equal(CombatMetricKind.Healing, contribution.Metric);
        Assert.Equal(CombatDeliveryKind.Direct, contribution.Delivery);
        Assert.Equal(CombatPacketRule.CompactRecovery, contribution.Resolution.PacketRule);
        Assert.Equal(CombatResolutionAuthority.Packet, contribution.Resolution.Authority);
        Assert.Equal(CombatSemanticMatchKind.None, contribution.Resolution.SemanticMatch);
    }

    [Fact]
    public void Unknown_Self_Direct_Value_Is_Not_A_Combat_Contribution()
    {
        var observation = DirectValue(2_010_302, 400_000);

        Assert.False(TryResolve(9_024, 9_024, in observation, out _));
    }

    [Fact]
    public void Packet_And_Semantic_Evidence_Are_Independently_Evaluable()
    {
        var observation = DirectValue(18_720_001, 566) with
        {
            BodySkillVariantRaw = 18_720_001,
            DetailResourceEffectRef = ResourceEffectRef.FromRaw(1_872_000_111)
        };
        var occurrence = new CombatOccurrenceResolution(
            CombatPacketRule.CompactDirectValue,
            CombatMaterializationKind.CompactAssociated,
            CombatAssociationKind.None,
            CombatSuppressionReason.None);

        var packet = CombatPacketEvidenceResolver.Evaluate(15_931, 15_931, in observation, in occurrence);
        var semantic = CombatSemanticEvidenceResolver.Evaluate(in observation);

        Assert.Equal(CombatPacketEvidenceStrength.Default, packet.Strength);
        Assert.Equal(CombatMetricKind.Damage, packet.Candidate!.Value.Metric);
        Assert.Equal(CombatSemanticMatchKind.ExactNode, semantic.Match);
        Assert.Equal(CombatMetricKind.Healing, semantic.Candidate!.Value.Metric);

        Assert.False(CombatContributionResolver.TryResolvePacketOnly(
            15_931,
            15_931,
            in observation,
            in occurrence,
            in packet,
            out _));
        Assert.True(CombatContributionResolver.TryResolveSemanticOnly(
            15_931,
            15_931,
            in observation,
            in occurrence,
            in semantic,
            out var semanticOnly));
        Assert.Equal(CombatMetricKind.Healing, semanticOnly.Metric);
        Assert.Equal(CombatResolutionAuthority.SkillSemantic, semanticOnly.Resolution.Authority);

        Assert.True(CombatContributionResolver.TryResolve(15_931, 15_931, in observation, in occurrence, out var contribution));
        Assert.Equal(CombatMetricKind.Healing, contribution.Metric);
        Assert.Equal(CombatResolutionAuthority.SkillSemantic, contribution.Resolution.Authority);
        Assert.Equal(CombatPacketRule.CompactDirectValue, contribution.Resolution.PacketRule);
    }

    [Fact]
    public void Health_Resource_Is_Packet_Authoritative_Direct_Healing()
    {
        var observation = DirectValue(17_410_040, 1_234) with
        {
            ResourceKind = CombatResourceKind.Health
        };

        var contribution = Resolve(12_115, 12_115, in observation);

        Assert.Equal(CombatMetricKind.Healing, contribution.Metric);
        Assert.Equal(CombatDeliveryKind.Direct, contribution.Delivery);
        Assert.Equal(CombatPacketRule.DirectHealthResource, contribution.Resolution.PacketRule);
        Assert.Equal(CombatResolutionAuthority.Packet, contribution.Resolution.Authority);
    }

    [Fact]
    public void NonHealth_Resource_Is_Not_A_Combat_Contribution()
    {
        var observation = DirectValue(17_410_040, 1_234) with
        {
            ResourceKind = CombatResourceKind.Mana
        };

        Assert.False(TryResolve(12_115, 12_115, in observation, out _));
    }

    [Fact]
    public void Quantified_Other_Target_Without_Semantics_Uses_Damage_Default()
    {
        var observation = DirectValue(1_800_030, 185);

        var contribution = Resolve(45_872, 1_734, in observation);

        Assert.Equal(CombatMetricKind.Damage, contribution.Metric);
        Assert.Equal(CombatDeliveryKind.Direct, contribution.Delivery);
        Assert.Equal(CombatPacketRule.DirectValue, contribution.Resolution.PacketRule);
        Assert.Equal(CombatResolutionAuthority.PacketDefault, contribution.Resolution.Authority);
    }

    [Fact]
    public void Exact_SkillEffect_Ref_Resolves_Self_Healing()
    {
        var observation = DirectValue(1_010_000, 1_064) with
        {
            DetailResourceEffectRef = ResourceEffectRef.FromRaw(101_000_011)
        };

        var contribution = Resolve(18_846, 18_846, in observation);

        Assert.Equal(CombatMetricKind.Healing, contribution.Metric);
        Assert.Equal(CombatDeliveryKind.Direct, contribution.Delivery);
        Assert.Equal(CombatPacketRule.DirectValue, contribution.Resolution.PacketRule);
        Assert.Equal(CombatResolutionAuthority.SkillSemantic, contribution.Resolution.Authority);
        Assert.Equal(CombatSemanticMatchKind.ExactNode, contribution.Resolution.SemanticMatch);
        Assert.Equal(SkillSemanticResourceNodeKind.SkillEffect, contribution.Resolution.ResourceNodeKind);
        Assert.Equal(101_000_011, contribution.Resolution.ResourceNodeId);
    }

    [Fact]
    public void Effect_Group_Ref_Uses_An_Unambiguous_Owning_Slot()
    {
        var observation = DirectValue(4_008, 198) with
        {
            DetailResourceEffectRef = ResourceEffectRef.FromRaw(400_840)
        };

        var contribution = Resolve(4_156, 34_135, in observation);

        Assert.Equal(CombatMetricKind.Damage, contribution.Metric);
        Assert.Equal(CombatSemanticMatchKind.UnambiguousSlot, contribution.Resolution.SemanticMatch);
        Assert.Equal(SkillSemanticResourceNodeKind.SkillEffectGroup, contribution.Resolution.ResourceNodeKind);
        Assert.Equal(40_084, contribution.Resolution.ResourceNodeId);
        Assert.Equal(4_008, contribution.Resolution.ResourceSkillId);
        Assert.Equal(1, contribution.Resolution.ResourceCandidateSlotCount);
    }

    [Fact]
    public void Periodic_Initial_Target_Value_Uses_Direct_Damage_Default()
    {
        var observation = PeriodicValue(17_070_240, 15_392, PeriodicEffectRelation.Target, mode: 1);

        var contribution = Resolve(12_115, 17_640, in observation);

        Assert.Equal(CombatMetricKind.Damage, contribution.Metric);
        Assert.Equal(CombatDeliveryKind.Direct, contribution.Delivery);
        Assert.Equal(CombatPacketRule.PeriodicValue, contribution.Resolution.PacketRule);
    }

    [Fact]
    public void Periodic_Target_Tick_Uses_Periodic_Damage_Default()
    {
        var observation = PeriodicValue(17_080_240, 1_117, PeriodicEffectRelation.Target, mode: 2);

        var contribution = Resolve(12_115, 17_640, in observation);

        Assert.Equal(CombatMetricKind.Damage, contribution.Metric);
        Assert.Equal(CombatDeliveryKind.Periodic, contribution.Delivery);
        Assert.Equal(CombatPacketRule.PeriodicValue, contribution.Resolution.PacketRule);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(11)]
    public void Periodic_Target_State_Seed_Modes_Do_Not_Produce_Combat(int mode)
    {
        var observation = PeriodicValue(17_730_000, 2_457, PeriodicEffectRelation.Target, mode);

        Assert.False(TryResolve(4_121, 19_621, in observation, out _));
    }

    [Fact]
    public void Periodic_Self_Mode11_Is_Packet_Authoritative_Healing()
    {
        var observation = PeriodicValue(17_091_250, 4_747, PeriodicEffectRelation.Self, mode: 11);

        var contribution = Resolve(12_115, 12_115, in observation);

        Assert.Equal(CombatMetricKind.Healing, contribution.Metric);
        Assert.Equal(CombatDeliveryKind.Periodic, contribution.Delivery);
        Assert.Equal(CombatPacketRule.PeriodicValue, contribution.Resolution.PacketRule);
        Assert.Equal(CombatResolutionAuthority.Packet, contribution.Resolution.Authority);
    }

    [Fact]
    public void Periodic_Self_Mode10_Does_Not_Produce_Combat()
    {
        var observation = PeriodicValue(17_091_250, 4_747, PeriodicEffectRelation.Self, mode: 10);

        Assert.False(TryResolve(12_115, 12_115, in observation, out _));
    }

    [Fact]
    public void Periodic_AbnormalEffect_Ref_Resolves_Shield_Pool()
    {
        var observation = PeriodicValue(17_420_010, 3_119, PeriodicEffectRelation.Self, mode: 9) with
        {
            BodyResourceEffectRef = ResourceEffectRef.FromRaw(1_742_001_011),
            PeriodicTailSkillCodeRaw = 17_420_010
        };

        var semantic = CombatSemanticEvidenceResolver.Evaluate(in observation);
        var contribution = Resolve(15_104, 15_104, in observation);

        Assert.Equal(CombatSemanticMatchKind.UnambiguousSlot, semantic.Match);
        Assert.Equal(CombatMetricKind.ShieldGranted, semantic.Candidate!.Value.Metric);
        Assert.Equal(CombatDeliveryKind.Pool, semantic.Candidate.Value.Delivery);
        Assert.Equal(CombatMetricKind.ShieldGranted, contribution.Metric);
        Assert.Equal(CombatDeliveryKind.Pool, contribution.Delivery);
        Assert.Equal(CombatPacketRule.PeriodicValue, contribution.Resolution.PacketRule);
        Assert.Equal(CombatSemanticMatchKind.UnambiguousSlot, contribution.Resolution.SemanticMatch);
        Assert.Equal(SkillSemanticResourceNodeKind.SkillAbnormalEffect, contribution.Resolution.ResourceNodeKind);
        Assert.Equal(1_742_001_011, contribution.Resolution.ResourceNodeId);
    }

    [Fact]
    public void Periodic_Pool_Modes_Apply_The_Production_Suppression_Contract()
    {
        var observation = PeriodicValue(17_420_010, 3_119, PeriodicEffectRelation.Self, mode: 9) with
        {
            BodyResourceEffectRef = ResourceEffectRef.FromRaw(1_742_001_011),
            PeriodicTailSkillCodeRaw = 17_420_010
        };
        var occurrence = new CombatOccurrenceResolution(
            CombatPacketRule.PeriodicValue,
            CombatMaterializationKind.PeriodicPoolGrant,
            CombatAssociationKind.None,
            CombatSuppressionReason.PeriodicPoolSemanticCandidate);
        var packet = CombatPacketEvidenceResolver.Evaluate(15_104, 15_104, in observation, in occurrence);
        var semantic = CombatSemanticEvidenceResolver.Evaluate(in observation);

        Assert.False(CombatContributionResolver.TryResolvePacketOnly(
            15_104,
            15_104,
            in observation,
            in occurrence,
            in packet,
            out _));
        Assert.True(CombatContributionResolver.TryResolveSemanticOnly(
            15_104,
            15_104,
            in observation,
            in occurrence,
            in semantic,
            out var semanticOnly));
        Assert.Equal(CombatMetricKind.ShieldGranted, semanticOnly.Metric);
        Assert.Equal(CombatDeliveryKind.Pool, semanticOnly.Delivery);
    }

    [Fact]
    public void Packet_Avoidance_Materializes_Only_An_Attempt_Mechanic()
    {
        var observation = DirectValue(skillCode: 0, amount: 0) with
        {
            BodyResourceEffectRef = ResourceEffectRef.FromRaw(1_138_005_011),
            AttemptCount = 1,
            Modifiers = DamageModifiers.Invincible,
            OutcomeKind = CombatWireOutcomeKind.ActiveSkillInvincible
        };

        var materialization = Materialize(8_912, 8_912, in observation, CombatPacketRule.ActiveSkillInvincible);

        Assert.False(materialization.Contribution.HasValue);
        Assert.False(materialization.Resource.HasValue);
        var mechanic = Assert.IsType<CombatMechanicOccurrence>(materialization.Mechanic);
        Assert.Equal(0, mechanic.HitCount);
        Assert.Equal(1, mechanic.AttemptCount);
        Assert.Equal(1, mechanic.InvincibleCount);
        Assert.Equal(CombatResolutionAuthority.Packet, mechanic.Resolution.Authority);
    }

    [Fact]
    public void Secondary_Drain_Is_An_Explicit_Healing_Materialization()
    {
        var observation = DirectValue(12_240_010, 540);

        var contribution = Resolve(
            12_115,
            12_115,
            in observation,
            CombatPacketRule.DrainSecondary,
            CombatMaterializationKind.DrainSecondary);

        Assert.Equal(CombatMetricKind.Healing, contribution.Metric);
        Assert.Equal(CombatDeliveryKind.Drain, contribution.Delivery);
        Assert.Equal(540, contribution.Amount);
        Assert.Equal(CombatMaterializationKind.DrainSecondary, contribution.Resolution.Materialization);
        Assert.Equal(CombatResolutionAuthority.Packet, contribution.Resolution.Authority);
    }

    [Fact]
    public void Damage_Counts_Are_Normalized_Once_In_The_Mechanic()
    {
        var observation = DirectValue(1_100_020, 1_000) with
        {
            HitCount = 3,
            AttemptCount = 1,
            Modifiers = DamageModifiers.Evade | DamageModifiers.MultiHit
        };

        var materialization = Materialize(100, 200, in observation, CombatPacketRule.CompactDirectValue);

        var contribution = Assert.IsType<CombatContribution>(materialization.Contribution);
        Assert.Equal(1_000, contribution.Amount);
        var mechanic = Assert.IsType<CombatMechanicOccurrence>(materialization.Mechanic);
        Assert.Equal(3, mechanic.HitCount);
        Assert.Equal(3, mechanic.AttemptCount);
        Assert.Equal(3, mechanic.EvadeCount);
        Assert.Equal(1, mechanic.MultiHitCount);
    }

    [Fact]
    public void Resolver_Does_Not_Allocate_Per_Event()
    {
        var observation = DirectValue(1_100_020, 100);
        var occurrence = new CombatOccurrenceResolution(
            CombatPacketRule.CompactDirectValue,
            CombatMaterializationKind.Primary,
            CombatAssociationKind.None,
            CombatSuppressionReason.None);

        for (var i = 0; i < 10_000; i++)
            _ = CombatContributionResolver.TryResolve(100, 200, in observation, in occurrence, out _);

        var checksum = 0L;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            CombatContributionResolver.TryResolve(100, 200, in observation, in occurrence, out var contribution);
            checksum += contribution.Amount;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        GC.KeepAlive(checksum);
        Assert.Equal(0, allocated);
    }

    private static CombatWireObservation DirectValue(int skillCode, long amount) => new()
    {
        SkillCode = skillCode,
        Damage = amount,
        HitCount = amount > 0 ? 1 : 0,
        AttemptCount = amount > 0 ? 1 : 0
    };

    private static CombatWireObservation PeriodicValue(
        int skillCode,
        long amount,
        PeriodicEffectRelation relation,
        int mode) => DirectValue(skillCode, amount) with
        {
            PeriodicRelation = relation,
            PeriodicMode = mode
        };

    private static bool TryResolve(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        out CombatContribution contribution)
    {
        var occurrence = CombatOccurrenceResolution.Primary;
        return CombatContributionResolver.TryResolve(sourceId, targetId, in observation, in occurrence, out contribution);
    }

    private static CombatContribution Resolve(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        CombatPacketRule packetRule = CombatPacketRule.None,
        CombatMaterializationKind materialization = CombatMaterializationKind.Primary)
    {
        var occurrence = new CombatOccurrenceResolution(
            packetRule,
            materialization,
            CombatAssociationKind.None,
            CombatSuppressionReason.None);
        Assert.True(CombatContributionResolver.TryResolve(
            sourceId,
            targetId,
            in observation,
            in occurrence,
            out var contribution));
        return contribution;
    }

    private static CombatOccurrenceMaterialization Materialize(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        CombatPacketRule packetRule)
    {
        var occurrence = new CombatOccurrenceResolution(
            packetRule,
            CombatMaterializationKind.Primary,
            CombatAssociationKind.None,
            CombatSuppressionReason.None);
        return CombatOccurrenceMaterializer.Resolve(sourceId, targetId, in observation, in occurrence);
    }
}
