using Avalonia.Media;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Playback;

namespace Cloris.Aion2Flow.Presentation;

public readonly record struct PlaybackTimelineViewport
{
    public PlaybackTimelineViewport(double startMilliseconds, double endMilliseconds)
    {
        if (!double.IsFinite(startMilliseconds) || startMilliseconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(startMilliseconds), "Viewport start must be a finite non-negative value.");
        if (!double.IsFinite(endMilliseconds) || endMilliseconds < startMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(endMilliseconds), "Viewport end must be finite and greater than or equal to its start.");

        StartMilliseconds = startMilliseconds;
        EndMilliseconds = endMilliseconds;
    }

    public static PlaybackTimelineViewport Empty { get; } = default;

    public double StartMilliseconds { get; }

    public double EndMilliseconds { get; }

    public double DurationMilliseconds => EndMilliseconds - StartMilliseconds;

    public bool IsEmpty => EndMilliseconds <= StartMilliseconds;

    public bool Contains(double positionMilliseconds) =>
        !IsEmpty && positionMilliseconds >= StartMilliseconds && positionMilliseconds <= EndMilliseconds;
}

public sealed record PlaybackTimelineMarker(double PositionMilliseconds, double Weight, IBrush Brush, string Text, bool IsApplication = false);

public sealed record PlaybackTimelineSpan(double StartMilliseconds, double EndMilliseconds, IBrush FillBrush, IBrush BorderBrush);

public sealed record PlaybackTimelineBand(ScenePlaybackTrack Track, IBrush Brush, IReadOnlyList<PlaybackTimelineMarker> Markers, int Count);

public sealed record PlaybackTimelineStrip(IReadOnlyList<PlaybackTimelineBand> Bands, int Count)
{
    public static PlaybackTimelineStrip Empty { get; } = new([], 0);
}

public sealed record PlaybackAuraTimelineLane(
    ScenePlaybackAuraIdentity AuraIdentity,
    string FallbackText,
    IReadOnlyList<PlaybackTimelineMarker> Markers,
    IReadOnlyList<PlaybackTimelineSpan> Spans,
    int Count,
    string CoverageText,
    string ActiveTimeText)
{
    public ResourceEffectRef DisplayResourceEffectRef => AuraIdentity.DisplayResourceEffectRef;

    public int InstanceSequenceId => AuraIdentity.InstanceSequenceId;

    public int SkillCode => DisplayResourceEffectRef.RawId is > 0 and <= int.MaxValue
        ? unchecked((int)DisplayResourceEffectRef.RawId)
        : 0;
}
