using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class CombatOccurrenceMaterializerTests
{
    public CombatOccurrenceMaterializerTests()
    {
        CombatResourceRegistry.SetGameResources(ResourceCatalog.Load(ResourceLanguage.English));
    }

    [Fact]
    public void Damage_MaterializesMetricAndMechanicIndependently()
    {
        var observation = new CombatWireObservation
        {
            SkillCode = 11_000_020,
            Damage = 1_200,
            HitCount = 2,
            AttemptCount = 3,
            MultiHitCount = 4,
            Modifiers = DamageModifiers.Critical | DamageModifiers.MultiHit
        };

        var result = Resolve(in observation, CombatPacketRule.CompactDirectValue);

        Assert.True(result.Contribution.HasValue);
        Assert.True(result.Mechanic.HasValue);
        Assert.False(result.Resource.HasValue);

        var contribution = result.Contribution.Value;
        Assert.Equal(CombatMetricKind.Damage, contribution.Metric);
        Assert.Equal(CombatDeliveryKind.Direct, contribution.Delivery);
        Assert.Equal(1_200, contribution.Amount);

        var mechanic = result.Mechanic.Value;
        Assert.Equal(DamageModifiers.Critical | DamageModifiers.MultiHit, mechanic.Modifiers);
        Assert.Equal(2, mechanic.HitCount);
        Assert.Equal(3, mechanic.AttemptCount);
        Assert.Equal(1, mechanic.MultiHitCount);
        Assert.Equal(4, mechanic.MultiHitSubCount);
    }

    [Theory]
    [InlineData(DamageModifiers.Evade, CombatPacketRule.CompactAvoidance)]
    [InlineData(DamageModifiers.Invincible, CombatPacketRule.ActiveSkillInvincible)]
    public void PureAvoidance_MaterializesOnlyMechanic(
        DamageModifiers modifier,
        CombatPacketRule packetRule)
    {
        var observation = new CombatWireObservation
        {
            SkillCode = 11_380_050,
            Damage = 0,
            AttemptCount = 1,
            Modifiers = modifier,
            OutcomeKind = modifier == DamageModifiers.Invincible
                ? CombatWireOutcomeKind.ActiveSkillInvincible
                : CombatWireOutcomeKind.None
        };

        var result = Resolve(in observation, packetRule);

        Assert.False(result.Contribution.HasValue);
        Assert.True(result.Mechanic.HasValue);
        Assert.False(result.Resource.HasValue);

        var mechanic = result.Mechanic.Value;
        Assert.Equal(0, mechanic.HitCount);
        Assert.Equal(1, mechanic.AttemptCount);
        Assert.Equal(modifier == DamageModifiers.Evade ? 1 : 0, mechanic.EvadeCount);
        Assert.Equal(modifier == DamageModifiers.Invincible ? 1 : 0, mechanic.InvincibleCount);
        Assert.Equal(packetRule, mechanic.Resolution.PacketRule);
    }

    [Fact]
    public void HealthResource_MaterializesRestoreAndHealingContribution()
    {
        var observation = new CombatWireObservation
        {
            SkillCode = 17_410_040,
            Damage = 1_234,
            ResourceKind = CombatResourceKind.Health
        };

        var result = Resolve(in observation);

        Assert.True(result.Contribution.HasValue);
        Assert.False(result.Mechanic.HasValue);
        Assert.True(result.Resource.HasValue);

        var contribution = result.Contribution.Value;
        Assert.Equal(CombatMetricKind.Healing, contribution.Metric);
        Assert.Equal(CombatDeliveryKind.Direct, contribution.Delivery);
        Assert.Equal(1_234, contribution.Amount);
        Assert.Equal(CombatPacketRule.DirectHealthResource, contribution.Resolution.PacketRule);

        var resource = result.Resource.Value;
        Assert.Equal(CombatResourceKind.Health, resource.Resource);
        Assert.Equal(CombatResourceFlowKind.Restore, resource.Flow);
        Assert.Equal(CombatResourceDeliveryKind.Direct, resource.Delivery);
        Assert.Equal(1_234, resource.Amount);
        Assert.Equal(CombatPacketRule.DirectHealthResource, resource.Resolution.PacketRule);
    }

    [Fact]
    public void ManaResource_MaterializesUnknownFlowWithoutMetricContribution()
    {
        var observation = new CombatWireObservation
        {
            SkillCode = 17_410_040,
            Damage = 567,
            ResourceKind = CombatResourceKind.Mana
        };

        var result = Resolve(in observation);

        Assert.False(result.Contribution.HasValue);
        Assert.False(result.Mechanic.HasValue);
        Assert.True(result.Resource.HasValue);

        var resource = result.Resource.Value;
        Assert.Equal(CombatResourceKind.Mana, resource.Resource);
        Assert.Equal(CombatResourceFlowKind.Unknown, resource.Flow);
        Assert.Equal(CombatResourceDeliveryKind.Direct, resource.Delivery);
        Assert.Equal(567, resource.Amount);
        Assert.Equal(CombatPacketRule.DirectManaResource, resource.Resolution.PacketRule);
    }

    [Theory]
    [InlineData(101_000_011, CombatMetricKind.Healing, CombatSemanticMatchKind.ExactNode)]
    [InlineData(400_840, CombatMetricKind.Damage, CombatSemanticMatchKind.UnambiguousSlot)]
    public void OwnerTargetCandidate_AdmitsOnlyProvedDirectSemanticContribution(
        int detailResourceEffectRef,
        CombatMetricKind expectedMetric,
        CombatSemanticMatchKind expectedMatch)
    {
        var observation = new CombatWireObservation
        {
            SkillCode = detailResourceEffectRef == 400_840 ? 4_008 : 1_010_000,
            Damage = 1_064,
            HitCount = 1,
            AttemptCount = 1,
            DetailResourceEffectRef = ResourceEffectRef.FromRaw((uint)detailResourceEffectRef)
        };

        var result = Resolve(
            in observation,
            suppression: CombatSuppressionReason.OwnerTargetSummonResource);

        Assert.True(result.IsAdmitted);
        var contribution = Assert.IsType<CombatContribution>(result.Contribution);
        Assert.Equal(expectedMetric, contribution.Metric);
        Assert.Equal(CombatPacketRule.DirectValue, contribution.Resolution.PacketRule);
        Assert.Equal(CombatResolutionAuthority.SkillSemantic, contribution.Resolution.Authority);
        Assert.Equal(expectedMatch, contribution.Resolution.SemanticMatch);
    }

    [Fact]
    public void OwnerTargetCandidate_SuppressesPacketDefaultAndSecondaryValues()
    {
        var observation = new CombatWireObservation
        {
            SkillCode = 1_800_030,
            Damage = 185,
            HitCount = 1,
            AttemptCount = 1,
            DrainHealAmount = 90,
            RegenerationAmount = 45
        };

        var result = Resolve(
            in observation,
            suppression: CombatSuppressionReason.OwnerTargetSummonResource);

        Assert.False(result.IsAdmitted);
        Assert.False(result.HasAny);
    }

    [Fact]
    public void SystemPeriodicRecoverySeed_IsSuppressedBeforeMaterialization()
    {
        var observation = new CombatWireObservation
        {
            SkillCode = 17_091_250,
            Damage = 4_747,
            HitCount = 1,
            AttemptCount = 1
        };

        var result = Resolve(
            in observation,
            suppression: CombatSuppressionReason.SystemPeriodicRecoverySeed);

        Assert.False(result.IsAdmitted);
        Assert.False(result.HasAny);
    }

    [Theory]
    [InlineData(PeriodicEffectRelation.Self)]
    [InlineData(PeriodicEffectRelation.Target)]
    public void PeriodicPoolCandidate_AdmitsOnlyProvedSemanticShield(
        PeriodicEffectRelation relation)
    {
        var observation = new CombatWireObservation
        {
            SkillCode = 17_420_010,
            Damage = 3_119,
            ChainId = 79,
            BodyResourceEffectRef = ResourceEffectRef.FromRaw(1_742_001_011),
            PeriodicRelation = relation,
            PeriodicMode = 9,
            PeriodicTailSkillCodeRaw = 17_420_010,
            PeriodicTailLength = 4
        };

        var result = Resolve(
            in observation,
            suppression: CombatSuppressionReason.PeriodicPoolSemanticCandidate,
            materialization: CombatMaterializationKind.PeriodicPoolGrant);

        Assert.True(result.IsAdmitted);
        var contribution = Assert.IsType<CombatContribution>(result.Contribution);
        Assert.Equal(CombatMetricKind.ShieldGranted, contribution.Metric);
        Assert.Equal(CombatDeliveryKind.Pool, contribution.Delivery);
        Assert.Equal(CombatPacketRule.PeriodicValue, contribution.Resolution.PacketRule);
        Assert.Equal(CombatResolutionAuthority.SkillSemantic, contribution.Resolution.Authority);
        Assert.Equal(CombatSemanticMatchKind.UnambiguousSlot, contribution.Resolution.SemanticMatch);
    }

    [Fact]
    public void PeriodicPoolCandidate_SuppressesNonsemanticSingletonSeed()
    {
        var observation = new CombatWireObservation
        {
            SkillCode = 12_070_000,
            Damage = 2_975,
            ChainId = 69,
            PeriodicRelation = PeriodicEffectRelation.Self,
            PeriodicMode = 9
        };

        var result = Resolve(
            in observation,
            suppression: CombatSuppressionReason.PeriodicPoolSemanticCandidate,
            materialization: CombatMaterializationKind.PeriodicPoolGrant);

        Assert.False(result.IsAdmitted);
        Assert.False(result.HasAny);
    }

    [Fact]
    public void EmptyPrimary_RemainsAdmittedSoSecondaryValuesCanMaterialize()
    {
        var observation = new CombatWireObservation
        {
            SkillCode = 2_010_302,
            DrainHealAmount = 90,
            RegenerationAmount = 45
        };

        var result = Resolve(in observation, sourceId: 100, targetId: 100);

        Assert.True(result.IsAdmitted);
        Assert.False(result.HasAny);
    }

    private static CombatOccurrenceMaterialization Resolve(
        in CombatWireObservation observation,
        CombatPacketRule packetRule = CombatPacketRule.None,
        CombatSuppressionReason suppression = CombatSuppressionReason.None,
        int sourceId = 100,
        int targetId = 200,
        CombatMaterializationKind materialization = CombatMaterializationKind.Primary)
    {
        var occurrence = new CombatOccurrenceResolution(
            packetRule,
            materialization,
            CombatAssociationKind.None,
            suppression);
        return CombatOccurrenceMaterializer.Resolve(sourceId, targetId, in observation, in occurrence);
    }
}
