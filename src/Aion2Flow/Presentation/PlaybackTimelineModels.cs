using Avalonia.Media;
using Cloris.Aion2Flow.SceneRuntime.Playback;

namespace Cloris.Aion2Flow.Presentation;

public sealed record PlaybackTimelineMarker(double PositionMilliseconds, double Weight, IBrush Brush, string Text, bool IsApplication = false);

public sealed record PlaybackTimelineSpan(double StartMilliseconds, double EndMilliseconds, IBrush FillBrush, IBrush BorderBrush);

public sealed record PlaybackTimelineLane(string Name, ScenePlaybackTrack Track, IBrush AccentBrush, IReadOnlyList<PlaybackTimelineMarker> Markers, double DurationMilliseconds, double PositionMilliseconds, int Count);

public sealed record PlaybackAuraTimelineLane(int SkillCode, string FallbackText, IBrush AccentBrush, IReadOnlyList<PlaybackTimelineMarker> Markers, IReadOnlyList<PlaybackTimelineSpan> Spans, double DurationMilliseconds, double PositionMilliseconds, int Count);
