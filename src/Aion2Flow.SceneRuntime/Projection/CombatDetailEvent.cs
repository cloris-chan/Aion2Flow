using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public readonly record struct CombatDetailEvent
{
    public CombatDetailEvent(
        CombatObservation observation,
        int sourceId,
        int targetId,
        long observedAtMilliseconds,
        long revision,
        CombatEventKey eventKey,
        RawPacketReference raw,
        CombatContribution contribution,
        CombatContributionCanonicalization canonicalization)
    {
        Observation = observation;
        SourceId = sourceId;
        TargetId = targetId;
        ObservedAtMilliseconds = observedAtMilliseconds;
        Revision = revision;
        EventKey = eventKey;
        Raw = raw;
        PacketContext = CombatPacketContext.FromRaw(in raw);
        Contribution = contribution;
        Canonicalization = canonicalization;
    }

    public CombatObservation Observation { get; init; }
    public int SourceId { get; init; }
    public int TargetId { get; init; }
    public long ObservedAtMilliseconds { get; init; }
    public long Revision { get; init; }
    public CombatEventKey EventKey { get; init; }
    public RawPacketReference Raw { get; init; }
    public CombatPacketContext PacketContext { get; init; }
    public CombatContribution Contribution { get; init; }
    public CombatContributionCanonicalization Canonicalization { get; init; }
    public int SkillCode => Observation.SkillCode;
    public long Amount => Observation.Damage;
    public CombatEventKind EventKind => Observation.EventKind;
    public CombatValueKind ValueKind => Observation.ValueKind;
    public PacketEffectTag EffectTag => Observation.EffectTag;
    public long ObservedAt => ObservedAtMilliseconds;
}

public readonly record struct CombatPacketContext(
    RawPacketReference Raw,
    CombatPacketEvidenceKind EvidenceKind,
    PacketStructureKind StructureKind,
    int ScopeId,
    int AssociationScopeId,
    int SiblingIndex,
    bool HasPacketContext,
    bool HasPacketAssociation)
{
    public static CombatPacketContext FromRaw(in RawPacketReference raw)
    {
        var evidenceKind = CombatPacketContextReader.ClassifyPacketEvidence(in raw);
        var hasPacketContext = CombatPacketContextReader.HasPacketContext(in raw);
        var structureKind = raw.StructurePath.Leaf.Kind;
        var scopeId = raw.StructurePath.Leaf.ScopeId;
        if (PacketStructureSiblingPositionResolver.TryResolve(raw.StructurePath, out var position))
        {
            return new CombatPacketContext(
                raw,
                evidenceKind,
                structureKind,
                scopeId,
                position.AssociationScopeId,
                position.SiblingIndex,
                hasPacketContext,
                true);
        }

        return new CombatPacketContext(
            raw,
            evidenceKind,
            structureKind,
            scopeId,
            0,
            -1,
            hasPacketContext,
            false);
    }
}
