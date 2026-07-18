using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public enum ScenePlaybackEventRelation : byte
{
    All,
    Related,
    Outgoing,
    Incoming,
    Aura
}

public readonly record struct ScenePlaybackEventScope
{
    private ScenePlaybackEventScope(
        int combatantId,
        ScenePlaybackEventRelation relation,
        CombatContributionCategory? category,
        SkillBaseKey? skillBaseKey,
        ScenePlaybackAuraIdentity auraIdentity)
    {
        CombatantId = combatantId;
        Relation = relation;
        Category = category;
        SkillBaseKey = skillBaseKey;
        AuraIdentity = auraIdentity;
    }

    public static ScenePlaybackEventScope All { get; } = default;

    public int CombatantId { get; }

    public ScenePlaybackEventRelation Relation { get; }

    public CombatContributionCategory? Category { get; }

    public SkillBaseKey? SkillBaseKey { get; }

    public ScenePlaybackAuraIdentity AuraIdentity { get; }

    public bool HasCombatant => CombatantId > 0;

    public bool HasRelation => Relation is ScenePlaybackEventRelation.Outgoing or ScenePlaybackEventRelation.Incoming or ScenePlaybackEventRelation.Aura;

    public bool HasCategory => Category.HasValue;

    public bool HasSkill => SkillBaseKey.HasValue || !AuraIdentity.IsEmpty;

    public bool IncludesMaterializedEvents => Relation != ScenePlaybackEventRelation.Aura;

    public static ScenePlaybackEventScope ForCombatant(int combatantId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(combatantId);
        return new ScenePlaybackEventScope(combatantId, ScenePlaybackEventRelation.Related, null, null, default);
    }

    public static ScenePlaybackEventScope ForRelation(int combatantId, ScenePlaybackEventRelation relation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(combatantId);
        if (relation is not (ScenePlaybackEventRelation.Outgoing or ScenePlaybackEventRelation.Incoming or ScenePlaybackEventRelation.Aura))
            throw new ArgumentOutOfRangeException(nameof(relation), relation, "A combatant relation must be outgoing, incoming, or aura.");

        return new ScenePlaybackEventScope(combatantId, relation, null, null, default);
    }

    public static ScenePlaybackEventScope ForCategory(int combatantId, ScenePlaybackEventRelation relation, CombatContributionCategory category)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(combatantId);
        if (relation is not (ScenePlaybackEventRelation.Outgoing or ScenePlaybackEventRelation.Incoming))
            throw new ArgumentOutOfRangeException(nameof(relation), relation, "Combat categories require an incoming or outgoing relation.");

        return new ScenePlaybackEventScope(combatantId, relation, category, null, default);
    }

    public static ScenePlaybackEventScope ForSkill(
        int combatantId,
        ScenePlaybackEventRelation relation,
        CombatContributionCategory category,
        SkillBaseKey skillBaseKey)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(combatantId);
        if (relation is not (ScenePlaybackEventRelation.Outgoing or ScenePlaybackEventRelation.Incoming))
            throw new ArgumentOutOfRangeException(nameof(relation), relation, "Combat skills require an incoming or outgoing relation.");

        return new ScenePlaybackEventScope(combatantId, relation, category, skillBaseKey, default);
    }

    public static ScenePlaybackEventScope ForAura(int combatantId, ScenePlaybackAuraIdentity auraIdentity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(combatantId);
        if (auraIdentity.IsEmpty)
            throw new ArgumentException("Aura event selection requires an effect or instance identity.", nameof(auraIdentity));

        return new ScenePlaybackEventScope(combatantId, ScenePlaybackEventRelation.Aura, null, null, auraIdentity);
    }
}

public enum ScenePlaybackEventFactKind : byte
{
    Metric,
    Mechanic,
    Resource,
    Observation
}

public readonly record struct ScenePlaybackEventId(ScenePlaybackEventFactKind Kind, long Ordinal) : IComparable<ScenePlaybackEventId>
{
    public int CompareTo(ScenePlaybackEventId other)
    {
        var comparison = Kind.CompareTo(other.Kind);
        return comparison != 0 ? comparison : Ordinal.CompareTo(other.Ordinal);
    }
}

public readonly record struct ScenePlaybackEventMarker(
    ScenePlaybackEventId Id,
    ScenePlaybackTrackMarker Marker,
    SkillBaseKey SkillBaseKey,
    CombatContribution? Contribution,
    CombatMechanicOccurrence? Mechanic,
    CombatResourceOccurrence? Resource)
{
    public ScenePlaybackTrack Track => Marker.Track;
    public long PositionMilliseconds => Marker.PositionMilliseconds;
    public long ObservationOrdinal => Marker.ObservationOrdinal;
    public int SourceEntityId => Marker.SourceEntityId;
    public int TargetEntityId => Marker.TargetEntityId;
    public CombatEventKey EventKey => Marker.EventKey;
    public ScenePlaybackCombatEventFlags CombatEventFlags => Marker.CombatEventFlags;
    public long Amount => Marker.Amount;
    public long? CurrentHp => Marker.CurrentHp;
    public long? MaxHp => Marker.MaxHp;
    public AuraLifecycleEventKind LifecycleEventKind => Marker.LifecycleEventKind;
    public int ResultCode => Marker.ResultCode;
    public int DurationMilliseconds => Marker.DurationMilliseconds;
    public ResourceEffectRef DisplayResourceEffectRef => Marker.DisplayResourceEffectRef;
    public AuraSemanticValue AuraSemantics => Marker.AuraSemantics;
    public AuraDisposition AuraDisposition => Marker.AuraDisposition;
    public AuraSemanticTrace AuraSemanticTrace => Marker.AuraSemanticTrace;
    public ScenePlaybackAuraIdentity AuraIdentity => Marker.AuraIdentity;
}

public readonly record struct ScenePlaybackEventReadResult(int Count, long EndObservationOrdinalExclusive);
