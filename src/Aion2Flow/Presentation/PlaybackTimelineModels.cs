using Avalonia.Media;
using Cloris.Aion2Flow.SceneRuntime.Playback;

namespace Cloris.Aion2Flow.Presentation;

public sealed record PlaybackTimelineMarker(double PositionMilliseconds, double Weight, IBrush Brush, string Text);

public sealed record PlaybackTimelineLane(string Name, ScenePlaybackTrack Track, IBrush AccentBrush, IReadOnlyList<PlaybackTimelineMarker> Markers, double DurationMilliseconds, double PositionMilliseconds, int Count);
