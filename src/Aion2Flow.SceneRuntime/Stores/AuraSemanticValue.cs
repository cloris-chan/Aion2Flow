using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public enum AuraDisposition : byte
{
    Unknown = 0,
    Buff = 1,
    Debuff = 2
}

public enum AuraSemanticMatchKind : byte
{
    None = 0,
    ExactNode = 1,
    UnambiguousSlot = 2
}

public readonly record struct AuraSemanticTrace(
    AuraSemanticMatchKind Match,
    SkillSemanticValue DirectSemantics,
    SkillSemanticValue Semantics,
    ResourceEffectRef ResourceEffectRef,
    SkillSemanticResourceNodeKind ResourceNodeKind,
    int ResourceNodeId,
    int ResourceSkillId,
    int EffectSlot,
    int ResourceCandidateSlotCount)
{
    public bool HasResourceEvidence => ResourceNodeId > 0;
}

public readonly record struct AuraSemanticValue(
    AuraDisposition Disposition,
    AuraSemanticTrace Trace)
{
    public bool IsAnnotated => Disposition != AuraDisposition.Unknown;
}

internal static class AuraSemanticResolver
{
    public static AuraSemanticValue Resolve(ResourceEffectRef resourceEffectRef)
    {
        if (!CombatResourceRegistry.TryResolveAuraResourceSemantics(resourceEffectRef, out var resolution))
        {
            return new AuraSemanticValue(
                AuraDisposition.Unknown,
                new AuraSemanticTrace(
                    AuraSemanticMatchKind.None,
                    default,
                    default,
                    resourceEffectRef,
                    default,
                    0,
                    0,
                    -1,
                    0));
        }

        return Resolve(in resolution);
    }

    internal static AuraSemanticValue Resolve(in SkillSemanticResourceResolution resolution)
    {
        var match = GetMatch(in resolution);
        var trace = new AuraSemanticTrace(
            match,
            resolution.DirectSemantics,
            resolution.Semantics,
            resolution.RawId == 0 ? default : ResourceEffectRef.FromRaw(resolution.RawId),
            resolution.NodeKind,
            resolution.NodeId,
            resolution.Slot?.SkillId ?? 0,
            resolution.Slot?.Slot ?? -1,
            resolution.CandidateSlotCount);
        if (match == AuraSemanticMatchKind.None)
            return new AuraSemanticValue(AuraDisposition.Unknown, trace);

        var disposition = resolution.Semantics.AuraFacets switch
        {
            SkillAuraFacet.Buff => AuraDisposition.Buff,
            SkillAuraFacet.Debuff => AuraDisposition.Debuff,
            _ => AuraDisposition.Unknown
        };
        return new AuraSemanticValue(disposition, trace);
    }

    private static AuraSemanticMatchKind GetMatch(in SkillSemanticResourceResolution resolution)
    {
        if (resolution.NodeId <= 0)
            return AuraSemanticMatchKind.None;

        if (resolution.RawId == unchecked((uint)resolution.NodeId))
            return AuraSemanticMatchKind.ExactNode;

        return resolution.HasUnambiguousSlot
            ? AuraSemanticMatchKind.UnambiguousSlot
            : AuraSemanticMatchKind.None;
    }
}
