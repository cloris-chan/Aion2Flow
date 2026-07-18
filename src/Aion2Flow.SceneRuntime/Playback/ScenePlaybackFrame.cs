using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public sealed class ScenePlaybackFrame
{
    public Guid EncounterId { get; init; }
    public long PositionMilliseconds { get; init; }
    public ScenePlaybackTimeRange TimeRange { get; init; }
    public SceneJournalSegment AppliedSegment { get; init; }
    public SceneCombatSnapshot Snapshot { get; init; } = SceneCombatSnapshot.Empty;
    public ScenePlaybackCombatTotals CombatTotals { get; init; }
    public IReadOnlyList<EntityVitalState> EntityVitals { get; init; } = [];
    public IReadOnlyList<AuraInstanceState> ActiveAuras { get; init; } = [];
    public IReadOnlyList<ScenePlaybackTrackWindow> Tracks { get; init; } = [];
}

public readonly record struct ScenePlaybackTimeRange(long StartOffsetMilliseconds, long EndOffsetMilliseconds, long DurationMilliseconds, bool HasTiming);

public readonly record struct ScenePlaybackCombatTotals(long TotalDamage, long TotalHealing, long TotalShield, long TotalShieldAbsorbed, double DamagePerSecond, double HealingPerSecond, long ElapsedMilliseconds);

public readonly record struct ScenePlaybackTrackWindow(ScenePlaybackTrack Track, long StartObservationOrdinal, long EndObservationOrdinalExclusive, int Count);

public readonly record struct ScenePlaybackCombatantDetail(
    long PositionMilliseconds,
    long EndObservationOrdinalExclusive,
    SceneCombatSnapshot Snapshot,
    CombatDetailUpdateResult Update,
    CombatDetailEventSet Events);

public enum ScenePlaybackTrack
{
    Combat,
    Mechanic,
    Resource,
    EntityVital,
    Aura,
    Scene,
    State,
    Diagnostic,
    Action,
    Other
}
