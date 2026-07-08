using Cloris.Aion2Flow.Protocol.Combat;
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
    public IReadOnlyList<ScenePlaybackResourceState> Resources { get; init; } = [];
    public IReadOnlyList<ScenePlaybackAuraState> ActiveAuras { get; init; } = [];
    public IReadOnlyList<ScenePlaybackTrackWindow> Tracks { get; init; } = [];
    public IReadOnlyList<ScenePlaybackTrackMarker> RecentMarkers { get; init; } = [];
}

public readonly record struct ScenePlaybackTimeRange(long StartOffsetMilliseconds, long EndOffsetMilliseconds, long DurationMilliseconds, bool HasTiming);

public readonly record struct ScenePlaybackCombatTotals(long TotalDamage, long TotalHealing, long TotalShield, long TotalShieldAbsorbed, double DamagePerSecond, double HealingPerSecond, long ElapsedMilliseconds);

public readonly record struct ScenePlaybackResourceState(int EntityId, long? CurrentValue, long? MaximumValue, long? Delta, int ResourceKind, long ObservedAtMilliseconds, long ObservationOrdinal);

public readonly record struct ScenePlaybackAuraState(int EntityId, int OriginEntityId, int InstanceSequenceId, int StackCount, int Mode, int GroupCode, ushort DurationMilliseconds, ResourceEffectRef DisplayResourceEffectRef, long OpenedAtMilliseconds, long RenewedAtMilliseconds, long? ExpiresAtMilliseconds, long OpenObservationOrdinal, long LastObservationOrdinal);

public readonly record struct ScenePlaybackTrackWindow(ScenePlaybackTrack Track, long StartObservationOrdinal, long EndObservationOrdinalExclusive, int Count);

public readonly record struct ScenePlaybackCombatantDetail(long PositionMilliseconds, long EndObservationOrdinalExclusive, SceneCombatSnapshot Snapshot, CombatDetailUpdateResult Update, IReadOnlyList<CombatDetailEvent> Events);

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
