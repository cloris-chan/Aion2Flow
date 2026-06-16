using Avalonia.Media;
using Cloris.Aion2Flow.SceneRuntime.Playback;

namespace Cloris.Aion2Flow.Presentation;

public sealed record PlaybackTimelineMarker(double PositionMilliseconds, double Weight, IBrush Brush, string Text, bool IsApplication = false);

public sealed record PlaybackTimelineSpan(double StartMilliseconds, double EndMilliseconds, IBrush FillBrush, IBrush BorderBrush);

public sealed record PlaybackTimelineBand(ScenePlaybackTrack Track, IBrush Brush, IReadOnlyList<PlaybackTimelineMarker> Markers, int Count);

public sealed record PlaybackTimelineStrip(IReadOnlyList<PlaybackTimelineBand> Bands, int Count)
{
    public static PlaybackTimelineStrip Empty { get; } = new([], 0);
}

public sealed record PlaybackAuraTimelineLane(int SkillCode, string FallbackText, IBrush AccentBrush, IReadOnlyList<PlaybackTimelineMarker> Markers, IReadOnlyList<PlaybackTimelineSpan> Spans, int Count);
