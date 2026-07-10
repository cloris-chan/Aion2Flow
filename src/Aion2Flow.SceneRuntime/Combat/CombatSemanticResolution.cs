using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources.Catalog;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public enum CombatSemanticEvidenceKind : byte
{
    ExactEffect = 1,
    SlotMatch = 2,
    ResourceNode = 3,
    PeriodicContext = 4,
    PacketResourceKind = 5,
    DrainSecondary = 6,
    PacketAvoidance = 7,
    PacketRelation = 8,
    PacketFallback = 9
}

public readonly record struct CombatSemanticResolution(
    CombatEventKind EventKind,
    CombatValueKind ValueKind,
    CombatSemanticEvidenceKind EvidenceKind,
    SkillSemanticFacet DirectFacets,
    SkillSemanticFacet Facets,
    ResourceEffectRef ResourceEffectRef,
    SkillSemanticResourceNodeKind ResourceNodeKind,
    int ResourceNodeId,
    int ResourceSkillId,
    int EffectSlot,
    int ResourceCandidateSlotCount)
{
    public bool HasResourceEvidence => ResourceNodeId > 0;
}
