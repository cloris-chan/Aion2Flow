using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public static class CombatEventClassifier
{
    public static CombatSemanticResolution Resolve(ParsedCombatPacket packet)
    {
        var observation = packet.ToObservation();
        return Resolve(packet.SourceId, packet.TargetId, in observation);
    }

    public static CombatEventKind Classify(ParsedCombatPacket packet)
    {
        var observation = packet.ToObservation();
        return Resolve(packet.SourceId, packet.TargetId, in observation).EventKind;
    }

    public static CombatValueKind ClassifyValueKind(ParsedCombatPacket packet)
    {
        var observation = packet.ToObservation();
        return Resolve(packet.SourceId, packet.TargetId, in observation).ValueKind;
    }

    public static (CombatEventKind EventKind, CombatValueKind ValueKind) Classify(int sourceId, int targetId, in CombatObservation observation)
    {
        var resolution = Resolve(sourceId, targetId, in observation);
        return (resolution.EventKind, resolution.ValueKind);
    }

    public static CombatSemanticResolution Resolve(int sourceId, int targetId, in CombatObservation observation)
    {
        if (IsOutcomeOnlyAvoidance(in observation))
            return CreatePacketResolution(CombatEventKind.Damage, CombatValueKind.Damage, CombatSemanticEvidenceKind.PacketAvoidance, in observation);

        if (IsDrainHealSynthesis(sourceId, targetId, in observation))
            return CreatePacketResolution(CombatEventKind.Healing, CombatValueKind.DrainHealing, CombatSemanticEvidenceKind.DrainSecondary, in observation);

        if (observation.PeriodicRelation != PeriodicEffectRelation.None)
            return ClassifyPeriodic(in observation);

        return ClassifyDirect(sourceId, targetId, in observation);
    }

    public static bool CountsTowardsDamage(ParsedCombatPacket packet) => packet.EventKind == CombatEventKind.Damage;

    private static CombatSemanticResolution ClassifyDirect(int sourceId, int targetId, in CombatObservation observation)
    {
        if (observation.ResourceKind == CombatResourceKind.Health)
            return CreatePacketResolution(CombatEventKind.Healing, CombatValueKind.Healing, CombatSemanticEvidenceKind.PacketResourceKind, in observation);

        if (observation.ResourceKind == CombatResourceKind.Mana)
            return CreatePacketResolution(CombatEventKind.Support, CombatValueKind.Support, CombatSemanticEvidenceKind.PacketResourceKind, in observation);

        if (TryClassifyDirectResourceSemantic(in observation, exactEffectOnly: true, out var resourceClassification))
            return resourceClassification;

        if (sourceId > 0 && targetId > 0 && sourceId == targetId)
            return CreatePacketResolution(CombatEventKind.Support, CombatValueKind.Support, CombatSemanticEvidenceKind.PacketRelation, in observation);

        if (TryConfirmDirectDamageWithSlot(in observation, out var slotClassification))
            return slotClassification;

        return CreatePacketResolution(CombatEventKind.Damage, CombatValueKind.Damage, CombatSemanticEvidenceKind.PacketFallback, in observation);
    }

    private static CombatSemanticResolution ClassifyPeriodic(in CombatObservation observation)
    {
        if (observation.PeriodicRelation == PeriodicEffectRelation.Self)
        {
            if (CombatObservationTraits.IsPeriodicSelfMode(in observation, 10))
                return CreatePacketResolution(CombatEventKind.Support, CombatValueKind.Support, CombatSemanticEvidenceKind.PeriodicContext, in observation);

            if (observation.ResourceKind == CombatResourceKind.Mana)
                return CreatePacketResolution(CombatEventKind.Support, CombatValueKind.Support, CombatSemanticEvidenceKind.PacketResourceKind, in observation);

            if (observation.ResourceKind == CombatResourceKind.Health ||
                CombatObservationTraits.IsPeriodicSelfMode(in observation, 11))
                return CreatePacketResolution(CombatEventKind.Healing, CombatValueKind.PeriodicHealing, observation.ResourceKind == CombatResourceKind.Health ? CombatSemanticEvidenceKind.PacketResourceKind : CombatSemanticEvidenceKind.PeriodicContext, in observation);

            if (TryClassifyPeriodicResourceSemantic(in observation, out var selfResourceClassification))
                return selfResourceClassification;

            return CreatePacketResolution(CombatEventKind.Support, CombatValueKind.Support, CombatSemanticEvidenceKind.PeriodicContext, in observation);
        }

        if (observation.PeriodicRelation != PeriodicEffectRelation.Target)
            return CreatePacketResolution(CombatEventKind.Damage, CombatValueKind.Damage, CombatSemanticEvidenceKind.PeriodicContext, in observation);

        if (CombatObservationTraits.IsPeriodicTargetMode(in observation, 8))
            return CreatePacketResolution(CombatEventKind.Support, CombatValueKind.Support, CombatSemanticEvidenceKind.PeriodicContext, in observation);

        if (observation.ResourceKind == CombatResourceKind.Mana)
            return CreatePacketResolution(CombatEventKind.Support, CombatValueKind.Support, CombatSemanticEvidenceKind.PacketResourceKind, in observation);

        if (observation.ResourceKind == CombatResourceKind.Health)
        {
            var valueKind = CombatObservationTraits.IsPeriodicTargetInitialEffect(in observation) ? CombatValueKind.Healing : CombatValueKind.PeriodicHealing;
            return CreatePacketResolution(CombatEventKind.Healing, valueKind, CombatSemanticEvidenceKind.PacketResourceKind, in observation);
        }

        if (CombatObservationTraits.IsTargetPeriodicSupportSeed(in observation))
            return CreatePacketResolution(CombatEventKind.Support, CombatValueKind.Support, CombatSemanticEvidenceKind.PeriodicContext, in observation);

        if (TryClassifyPeriodicResourceSemantic(in observation, out var targetResourceClassification))
            return targetResourceClassification;

        var fallbackValueKind = CombatObservationTraits.IsPeriodicTargetInitialEffect(in observation) ? CombatValueKind.Damage : CombatValueKind.PeriodicDamage;
        return CreatePacketResolution(CombatEventKind.Damage, fallbackValueKind, CombatSemanticEvidenceKind.PeriodicContext, in observation);
    }

    private static bool TryClassifyDirectResourceSemantic(
        in CombatObservation observation,
        bool exactEffectOnly,
        out CombatSemanticResolution classification)
    {
        if (!CombatResourceRegistry.TryResolveDirectCombatResourceSemantics(in observation, out var semantics))
        {
            classification = default;
            return false;
        }

        var isExactEffect = semantics.NodeKind == SkillSemanticResourceNodeKind.SkillEffect && semantics.RawId == unchecked((uint)semantics.NodeId);
        if (isExactEffect != exactEffectOnly)
        {
            classification = default;
            return false;
        }

        var quantifiedFacets = semantics.DirectFacets & (SkillSemanticFacet.Damage | SkillSemanticFacet.Healing | SkillSemanticFacet.Support);
        if (quantifiedFacets == SkillSemanticFacet.Damage)
        {
            classification = CreateResourceResolution(CombatEventKind.Damage, CombatValueKind.Damage, in observation, in semantics);
            return true;
        }

        if (quantifiedFacets == SkillSemanticFacet.Healing)
        {
            classification = CreateResourceResolution(CombatEventKind.Healing, CombatValueKind.Healing, in observation, in semantics);
            return true;
        }

        if (quantifiedFacets == SkillSemanticFacet.Support)
        {
            classification = CreateResourceResolution(CombatEventKind.Support, CombatValueKind.Support, in observation, in semantics);
            return true;
        }

        if ((semantics.Facets & SkillSemanticFacet.Shield) != 0)
        {
            classification = CreateResourceResolution(CombatEventKind.Support, CombatValueKind.Shield, in observation, in semantics);
            return true;
        }

        if ((semantics.Facets & (SkillSemanticFacet.Buff | SkillSemanticFacet.Debuff | SkillSemanticFacet.DamageOverTime | SkillSemanticFacet.HealingOverTime)) != 0)
        {
            classification = CreateResourceResolution(CombatEventKind.Support, CombatValueKind.Support, in observation, in semantics);
            return true;
        }

        classification = default;
        return false;
    }

    private static bool TryConfirmDirectDamageWithSlot(in CombatObservation observation, out CombatSemanticResolution classification)
    {
        if (!CombatResourceRegistry.TryResolveDirectCombatResourceSemantics(in observation, out var semantics) ||
            !semantics.HasUnambiguousSlot ||
            semantics.NodeKind == SkillSemanticResourceNodeKind.SkillEffect && semantics.RawId == unchecked((uint)semantics.NodeId))
        {
            classification = default;
            return false;
        }

        var quantifiedFacets = semantics.DirectFacets & (SkillSemanticFacet.Damage | SkillSemanticFacet.Healing | SkillSemanticFacet.Support);
        if (quantifiedFacets != SkillSemanticFacet.Damage)
        {
            classification = default;
            return false;
        }

        classification = CreateResourceResolution(CombatEventKind.Damage, CombatValueKind.Damage, in observation, in semantics);
        return true;
    }

    private static bool TryClassifyPeriodicResourceSemantic(
        in CombatObservation observation,
        out CombatSemanticResolution classification)
    {
        if (CombatObservationTraits.IsPeriodicTargetInitialEffect(in observation) ||
            !CombatResourceRegistry.TryResolvePeriodicCombatResourceSemantics(in observation, out var semantics))
        {
            classification = default;
            return false;
        }

        var periodicFacets = semantics.Facets & (SkillSemanticFacet.DamageOverTime | SkillSemanticFacet.HealingOverTime);
        if (periodicFacets == SkillSemanticFacet.DamageOverTime)
        {
            classification = CreateResourceResolution(CombatEventKind.Damage, CombatValueKind.PeriodicDamage, in observation, in semantics);
            return true;
        }

        if (periodicFacets == SkillSemanticFacet.HealingOverTime)
        {
            classification = CreateResourceResolution(CombatEventKind.Healing, CombatValueKind.PeriodicHealing, in observation, in semantics);
            return true;
        }

        classification = default;
        return false;
    }

    private static CombatSemanticResolution CreatePacketResolution(
        CombatEventKind eventKind,
        CombatValueKind valueKind,
        CombatSemanticEvidenceKind evidenceKind,
        in CombatObservation observation)
    {
        if (CombatResourceRegistry.TryResolvePeriodicCombatResourceSemantics(in observation, out var semantics))
        {
            return CreateResolution(eventKind, valueKind, evidenceKind, in semantics);
        }

        return new CombatSemanticResolution(eventKind, valueKind, evidenceKind, SkillSemanticFacet.None, SkillSemanticFacet.None, default, default, 0, 0, -1, 0);
    }

    private static CombatSemanticResolution CreateResourceResolution(
        CombatEventKind eventKind,
        CombatValueKind valueKind,
        in CombatObservation observation,
        in SkillSemanticResourceResolution semantics)
    {
        var evidenceKind = semantics.NodeKind == SkillSemanticResourceNodeKind.SkillEffect && semantics.RawId == unchecked((uint)semantics.NodeId)
            ? CombatSemanticEvidenceKind.ExactEffect
            : semantics.HasUnambiguousSlot
                ? CombatSemanticEvidenceKind.SlotMatch
                : CombatSemanticEvidenceKind.ResourceNode;
        return CreateResolution(eventKind, valueKind, evidenceKind, in semantics);
    }

    private static CombatSemanticResolution CreateResolution(
        CombatEventKind eventKind,
        CombatValueKind valueKind,
        CombatSemanticEvidenceKind evidenceKind,
        in SkillSemanticResourceResolution semantics)
        => new(
            eventKind,
            valueKind,
            evidenceKind,
            semantics.DirectFacets,
            semantics.Facets,
            ResourceEffectRef.FromRaw(semantics.RawId),
            semantics.NodeKind,
            semantics.NodeId,
            semantics.Slot?.SkillId ?? 0,
            semantics.Slot?.Slot ?? -1,
            semantics.CandidateSlotCount);

    private static bool IsOutcomeOnlyAvoidance(in CombatObservation observation)
    {
        if (observation.Damage > 0)
            return false;

        if ((observation.Modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) == 0)
            return false;

        return Math.Max(observation.HitCount, observation.AttemptCount) > 0;
    }

    private static bool IsDrainHealSynthesis(int sourceId, int targetId, in CombatObservation observation) =>
        sourceId > 0 && sourceId == targetId && observation.Damage > 0 && observation.DrainHealAmount > 0;

}

public static class CombatObservationTraits
{
    public static bool IsTargetPeriodicSupportSeed(in CombatObservation observation) => IsPeriodicTargetMode(in observation, 9) || IsPeriodicTargetMode(in observation, 11);

    public static bool IsPeriodicSelfMode(in CombatObservation observation, int mode) => observation.PeriodicRelation == PeriodicEffectRelation.Self && observation.PeriodicMode == mode;

    public static bool IsPeriodicTargetMode(in CombatObservation observation, int mode) => observation.PeriodicRelation == PeriodicEffectRelation.Target && observation.PeriodicMode == mode;

    public static bool IsPeriodicTargetInitialEffect(in CombatObservation observation) => observation.PeriodicRelation == PeriodicEffectRelation.Target && observation.PeriodicMode == 1;

    public static string FormatEffectLabel(in CombatObservation observation)
    {
        if (observation.PeriodicRelation != PeriodicEffectRelation.None)
            return FormatPeriodicEffectLabel(observation.PeriodicRelation, observation.PeriodicMode);

        return observation.EffectTag == PacketEffectTag.None ? string.Empty : FormatEffectTagLabel(observation.EffectTag);
    }

    private static string FormatPeriodicEffectLabel(PeriodicEffectRelation relation, int mode)
    {
        if (relation == PeriodicEffectRelation.None)
            return string.Empty;

        if (relation == PeriodicEffectRelation.Self)
        {
            return mode switch
            {
                1 => "periodic-self-initial",
                3 => "periodic-self-tick",
                _ => $"periodic-self-mode-{mode}"
            };
        }

        return mode switch
        {
            1 => "periodic-target-initial",
            2 => "periodic-target-tick",
            3 => "periodic-target-tick",
            _ => $"periodic-target-mode-{mode}"
        };
    }

    private static string FormatEffectTagLabel(PacketEffectTag effectTag) =>
        effectTag switch
        {
            PacketEffectTag.CompactEvade => "compact-evade",
            PacketEffectTag.PeriodicLinkInvincible => "periodic-link-invincible",
            PacketEffectTag.ActiveSkillInvincible => "active-skill-invincible",
            _ => string.Empty
        };
}
