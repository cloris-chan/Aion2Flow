using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public readonly record struct CombatDetailFact
{
    public CombatDetailFact(
        CombatWireObservation observation,
        int sourceId,
        int targetId,
        long observedAtMilliseconds,
        long sourceObservationOrdinal,
        long revision,
        CombatEventKey eventKey,
        RawPacketReference raw,
        CombatPacketContext packetContext)
    {
        Observation = observation;
        SourceId = sourceId;
        TargetId = targetId;
        ObservedAtMilliseconds = observedAtMilliseconds;
        SourceObservationOrdinal = sourceObservationOrdinal;
        Revision = revision;
        EventKey = eventKey;
        Raw = raw;
        PacketContext = packetContext;
    }

    public CombatWireObservation Observation { get; init; }
    public int SourceId { get; init; }
    public int TargetId { get; init; }
    public long ObservedAtMilliseconds { get; init; }
    public long SourceObservationOrdinal { get; init; }
    public long Revision { get; init; }
    public CombatEventKey EventKey { get; init; }
    public RawPacketReference Raw { get; init; }
    public CombatPacketContext PacketContext { get; init; }
    public int SkillCode => Observation.SkillCode;
    public long ObservedAt => ObservedAtMilliseconds;

    public static CombatDetailFact Create(
        CombatWireObservation observation,
        int sourceId,
        int targetId,
        long observedAtMilliseconds,
        long sourceObservationOrdinal,
        long revision,
        CombatEventKey eventKey,
        RawPacketReference raw)
        => new(
            observation,
            sourceId,
            targetId,
            observedAtMilliseconds,
            sourceObservationOrdinal,
            revision,
            eventKey,
            raw,
            CombatPacketContext.FromRaw(in raw));
}

public readonly record struct CombatMetricDetailEvent(CombatDetailFact Fact, CombatContribution Contribution)
{
    public CombatWireObservation Observation => Fact.Observation;
    public int SourceId => Fact.SourceId;
    public int TargetId => Fact.TargetId;
    public long ObservedAtMilliseconds => Fact.ObservedAtMilliseconds;
    public long SourceObservationOrdinal => Fact.SourceObservationOrdinal;
    public long Revision => Fact.Revision;
    public CombatEventKey EventKey => Fact.EventKey;
    public RawPacketReference Raw => Fact.Raw;
    public CombatPacketContext PacketContext => Fact.PacketContext;
    public int SkillCode => Fact.SkillCode;
    public long Amount => Contribution.Amount;
    public CombatMetricKind Metric => Contribution.Metric;
    public CombatDeliveryKind Delivery => Contribution.Delivery;
    public CombatResolutionTrace Resolution => Contribution.Resolution;
    public long ObservedAt => Fact.ObservedAt;
}

public readonly record struct CombatMechanicDetailEvent(CombatDetailFact Fact, CombatMechanicOccurrence Mechanic)
{
    public CombatWireObservation Observation => Fact.Observation;
    public int SourceId => Fact.SourceId;
    public int TargetId => Fact.TargetId;
    public long ObservedAtMilliseconds => Fact.ObservedAtMilliseconds;
    public long SourceObservationOrdinal => Fact.SourceObservationOrdinal;
    public long Revision => Fact.Revision;
    public CombatEventKey EventKey => Fact.EventKey;
    public RawPacketReference Raw => Fact.Raw;
    public CombatPacketContext PacketContext => Fact.PacketContext;
    public int SkillCode => Fact.SkillCode;
    public CombatResolutionTrace Resolution => Mechanic.Resolution;
    public long ObservedAt => Fact.ObservedAt;
}

public readonly record struct CombatResourceDetailEvent(CombatDetailFact Fact, CombatResourceOccurrence Resource)
{
    public CombatWireObservation Observation => Fact.Observation;
    public int SourceId => Fact.SourceId;
    public int TargetId => Fact.TargetId;
    public long ObservedAtMilliseconds => Fact.ObservedAtMilliseconds;
    public long SourceObservationOrdinal => Fact.SourceObservationOrdinal;
    public long Revision => Fact.Revision;
    public CombatEventKey EventKey => Fact.EventKey;
    public RawPacketReference Raw => Fact.Raw;
    public CombatPacketContext PacketContext => Fact.PacketContext;
    public int SkillCode => Fact.SkillCode;
    public long Amount => Resource.Amount;
    public CombatResolutionTrace Resolution => Resource.Resolution;
    public long ObservedAt => Fact.ObservedAt;
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

internal readonly record struct CombatDetailProjectionVersion(long IdentityRevision);
