using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public sealed class ScenePlaybackFrame
{
    public Guid EncounterId { get; init; }
    public long PositionMilliseconds { get; init; }
    public long PositionTimestampMilliseconds { get; init; }
    public ScenePlaybackTimeRange TimeRange { get; init; }
    public SceneJournalSegment AppliedSegment { get; init; }
    public SceneCombatSnapshot Snapshot { get; init; } = SceneCombatSnapshot.Empty;
    public ScenePlaybackCombatTotals CombatTotals { get; init; }
    public IReadOnlyList<ScenePlaybackResourceState> Resources { get; init; } = [];
    public IReadOnlyList<ScenePlaybackAuraState> ActiveAuras { get; init; } = [];
    public IReadOnlyList<ScenePlaybackTrackWindow> Tracks { get; init; } = [];
}

public readonly record struct ScenePlaybackTimeRange(long StartTimestampMilliseconds, long EndTimestampMilliseconds, long DurationMilliseconds, bool HasTimestamps);

public readonly record struct ScenePlaybackCombatTotals(long TotalDamage, long TotalHealing, long TotalShield, long TotalShieldAbsorbed, double DamagePerSecond, double HealingPerSecond, long ElapsedMilliseconds);

public readonly record struct ScenePlaybackResourceState(int EntityId, long? CurrentValue, long? MaximumValue, long? Delta, int ResourceKind, long ObservedAtMilliseconds, long ObservationOrdinal);

public readonly record struct ScenePlaybackAuraState(int SourceEntityId, int TargetEntityId, int SkillCode, int StackCount, int SequenceId, int ChainId, int ResultCode, int Mode, long ObservedAtMilliseconds, long ObservationOrdinal);

public readonly record struct ScenePlaybackTrackWindow(ScenePlaybackTrack Track, long StartObservationOrdinal, long EndObservationOrdinalExclusive, int Count);

public enum ScenePlaybackTrack
{
    Combat,
    Resource,
    Aura,
    Scene,
    State,
    Diagnostic,
    Action,
    Other
}
