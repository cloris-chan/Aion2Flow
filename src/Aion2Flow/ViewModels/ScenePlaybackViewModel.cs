using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using Cloris.Aion2Flow.Presentation;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Playback;
using Cloris.Aion2Flow.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class ScenePlaybackViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly ScenePlaybackTrack[] TrackOrder =
    [
        ScenePlaybackTrack.Combat,
        ScenePlaybackTrack.Resource,
        ScenePlaybackTrack.Aura,
        ScenePlaybackTrack.State,
        ScenePlaybackTrack.Scene,
        ScenePlaybackTrack.Action,
        ScenePlaybackTrack.Diagnostic,
        ScenePlaybackTrack.Other
    ];

    private const int MaxTimelineMarkers = 1_800;
    private const int MaxEventWindowMarkers = 96;
    private const long EventWindowRadiusMilliseconds = 4_000;
    private const long StepMilliseconds = 1_000;
    private const long DetailRefreshIntervalMilliseconds = 250;
    private const long EventWindowRefreshIntervalMilliseconds = 1_000;

    private static readonly IBrush CombatBrush = Brush.Parse("#18D7F4");
    private static readonly IBrush ResourceBrush = Brush.Parse("#8CE271");
    private static readonly IBrush AuraBrush = Brush.Parse("#C98EFF");
    private static readonly IBrush StateBrush = Brush.Parse("#FFD166");
    private static readonly IBrush SceneBrush = Brush.Parse("#FF8A65");
    private static readonly IBrush ActionBrush = Brush.Parse("#65A7FF");
    private static readonly IBrush DiagnosticBrush = Brush.Parse("#9AA8B4");
    private static readonly IBrush OtherBrush = Brush.Parse("#D4DCE5");
    private static readonly ProgressSegment[] EmptySegments = [];

    private readonly Lock _frameGate = new();
    private readonly ScenePlaybackController _controller;
    private readonly IScenePlaybackSource _source;
    private CancellationTokenSource? _seekCancellation;
    private ScenePlaybackFrame _currentFrame;
    private ScenePlaybackFrameChangedEventArgs? _pendingFrameChanged;
    private IReadOnlyList<PlaybackTimelineLane> _timelineMarkerTracks = [];
    private double _timelineMarkerDuration = -1;
    private long _lastDetailProjectionPosition = long.MinValue;
    private long _lastEventWindowPosition = long.MinValue;
    private bool _isApplyingFrame;
    private bool _isDisposed;
    private bool _frameApplyQueued;
    private bool _forceNextDetailProjection = true;
    private bool _timelineMarkersInitialized;

    public ScenePlaybackViewModel(ArchivedEncounterRecord record, SceneDisplayContext displayContext)
        : this(record, displayContext, Ioc.Default.GetRequiredService<LocalizationService>())
    {
    }

    public ScenePlaybackViewModel(ArchivedEncounterRecord record, SceneDisplayContext displayContext, LocalizationService localization)
    {
        DisplayContext = displayContext;
        Localization = localization;
        _source = new ArchivedScenePlaybackSource(record);
        _controller = new ScenePlaybackController(_source);
        _controller.FrameChanged += OnFrameChanged;
        Localization.LanguageChanged += OnLanguageChanged;
        WindowTitle = string.Format(CultureInfo.CurrentCulture, Localization["Playback_WindowTitleFormat"], displayContext.ResolveMapName(record.Snapshot.MapId));
        ArchivedAtText = record.ArchivedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        MapId = record.Snapshot.MapId;
        _currentFrame = _controller.CurrentFrame;
        ApplyFrame(_currentFrame, _controller.State);
    }

    public SceneDisplayContext DisplayContext { get; }

    public LocalizationService Localization { get; }

    [ObservableProperty]
    public partial string WindowTitle { get; set; } = "Playback";

    [ObservableProperty]
    public partial string ArchivedAtText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial uint MapId { get; set; }

    [ObservableProperty]
    public partial double PositionMilliseconds { get; set; }

    [ObservableProperty]
    public partial double DurationMilliseconds { get; set; }

    [ObservableProperty]
    public partial string PositionText { get; set; } = "00:00.000";

    [ObservableProperty]
    public partial string DurationText { get; set; } = "00:00.000";

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double Speed { get; set; } = 1d;

    [ObservableProperty]
    public partial string SpeedText { get; set; } = "1x";

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsCheckpointing { get; set; }

    [ObservableProperty]
    public partial int CheckpointCount { get; set; }

    [ObservableProperty]
    public partial string CheckpointText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TimelineMarkerLimitText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<PlaybackTimelineLane> TimelineTracks { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<PlaybackCombatantRowViewModel> Combatants { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<PlaybackAuraRowViewModel> ActiveAuras { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<PlaybackEventRowViewModel> EventWindow { get; set; } = [];

    partial void OnPositionMillisecondsChanged(double value)
    {
        PositionText = FormatTime(value);
        if (!_isApplyingFrame)
            RequestSeek(value);
    }

    public void RequestSeek(double positionMilliseconds)
    {
        if (_isDisposed)
            return;

        var duration = DurationMilliseconds;
        var target = duration > 0
            ? Math.Clamp(positionMilliseconds, 0d, duration)
            : Math.Max(0d, positionMilliseconds);
        _forceNextDetailProjection = true;
        _seekCancellation?.Cancel();
        _seekCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _seekCancellation = cancellation;
        _ = SeekCoreAsync((long)Math.Round(target, MidpointRounding.AwayFromZero), cancellation.Token);
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (IsPlaying)
            _controller.Pause();
        else
            _controller.Play();

        ApplyFrame(_controller.CurrentFrame, _controller.State);
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        _forceNextDetailProjection = true;
        var frame = await _controller.StopAsync().ConfigureAwait(true);
        ApplyFrame(frame, _controller.State);
    }

    [RelayCommand]
    private void StepBackward() => PositionMilliseconds = Math.Max(0d, PositionMilliseconds - StepMilliseconds);

    [RelayCommand]
    private void StepForward()
    {
        var duration = DurationMilliseconds;
        PositionMilliseconds = duration > 0
            ? Math.Min(duration, PositionMilliseconds + StepMilliseconds)
            : PositionMilliseconds + StepMilliseconds;
    }

    [RelayCommand]
    private void SetSpeed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
            return;

        _controller.SetSpeed(speed);
        ApplyFrame(_controller.CurrentFrame, _controller.State);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _forceNextDetailProjection = true;
        var frame = await _controller.RefreshAsync().ConfigureAwait(true);
        ApplyFrame(frame, _controller.State);
    }

    [RelayCommand]
    private void RebuildCheckpoints()
    {
        _controller.StartCheckpointRebuild();
        ApplyFrame(_controller.CurrentFrame, _controller.State);
    }

    private async Task SeekCoreAsync(long positionMilliseconds, CancellationToken cancellationToken)
    {
        try
        {
            IsLoading = true;
            await _controller.SeekAsync(positionMilliseconds, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = ex.Message);
        }
    }

    private void OnFrameChanged(object? sender, ScenePlaybackFrameChangedEventArgs e)
    {
        if (_isDisposed)
            return;

        lock (_frameGate)
        {
            _pendingFrameChanged = e;
            if (_frameApplyQueued)
                return;

            _frameApplyQueued = true;
        }

        Dispatcher.UIThread.Post(ApplyPendingFrame, DispatcherPriority.Background);
    }

    private void ApplyPendingFrame()
    {
        ScenePlaybackFrameChangedEventArgs? pending;
        lock (_frameGate)
        {
            pending = _pendingFrameChanged;
            _pendingFrameChanged = null;
        }

        if (!_isDisposed && pending is not null)
            ApplyFrame(pending.Frame, pending.State);

        lock (_frameGate)
        {
            if (_pendingFrameChanged is null)
            {
                _frameApplyQueued = false;
                return;
            }
        }

        Dispatcher.UIThread.Post(ApplyPendingFrame, DispatcherPriority.Background);
    }

    private void ApplyFrame(ScenePlaybackFrame frame, ScenePlaybackControllerState state)
    {
        if (_isDisposed)
            return;

        _currentFrame = frame;
        _isApplyingFrame = true;
        PositionMilliseconds = frame.PositionMilliseconds;
        DurationMilliseconds = frame.TimeRange.DurationMilliseconds;
        _isApplyingFrame = false;

        PositionText = FormatTime(frame.PositionMilliseconds);
        DurationText = FormatTime(frame.TimeRange.DurationMilliseconds);
        Speed = state.Speed;
        SpeedText = FormatSpeed(state.Speed);
        IsPlaying = state.IsPlaying;
        IsLoading = state.IsLoading;
        IsCheckpointing = state.IsCheckpointing;
        CheckpointCount = state.CheckpointCount;
        CheckpointText = state.IsCheckpointing
            ? string.Format(CultureInfo.CurrentCulture, Localization["Playback_CheckpointsBuildingFormat"], state.CheckpointCount.ToString("N0", CultureInfo.CurrentCulture))
            : string.Format(CultureInfo.CurrentCulture, Localization["Playback_CheckpointsReadyFormat"], state.CheckpointCount.ToString("N0", CultureInfo.CurrentCulture));
        StatusText = state.IsLoading
            ? Localization["Playback_Status_Loading"]
            : state.IsPlaying
                ? Localization["Playback_Status_Playing"]
                : Localization["Playback_Status_Paused"];

        RefreshTimelineTracks(frame);

        if (!ShouldRefreshDetails(frame, state))
            return;

        Combatants = CreateCombatants(frame);
        ActiveAuras = CreateAuras(frame);
        if (ShouldRefreshEventWindow(frame, state))
        {
            EventWindow = CreateEventWindow(frame, state);
            _lastEventWindowPosition = frame.PositionMilliseconds;
        }

        _lastDetailProjectionPosition = frame.PositionMilliseconds;
        _forceNextDetailProjection = false;
    }

    private bool ShouldRefreshDetails(ScenePlaybackFrame frame, ScenePlaybackControllerState state)
    {
        if (_forceNextDetailProjection || !state.IsPlaying)
            return true;

        if (_lastDetailProjectionPosition == long.MinValue)
            return true;

        if (frame.TimeRange.DurationMilliseconds > 0 && frame.PositionMilliseconds >= frame.TimeRange.DurationMilliseconds)
            return true;

        return Math.Abs(frame.PositionMilliseconds - _lastDetailProjectionPosition) >= DetailRefreshIntervalMilliseconds;
    }

    private bool ShouldRefreshEventWindow(ScenePlaybackFrame frame, ScenePlaybackControllerState state)
    {
        if (_forceNextDetailProjection || !state.IsPlaying)
            return true;

        if (_lastEventWindowPosition == long.MinValue)
            return true;

        if (frame.TimeRange.DurationMilliseconds > 0 && frame.PositionMilliseconds >= frame.TimeRange.DurationMilliseconds)
            return true;

        return Math.Abs(frame.PositionMilliseconds - _lastEventWindowPosition) >= EventWindowRefreshIntervalMilliseconds;
    }

    private IReadOnlyList<PlaybackCombatantRowViewModel> CreateCombatants(ScenePlaybackFrame frame)
    {
        var combatants = frame.Snapshot.Combatants.AsSpan();
        if (combatants.Length == 0 && frame.Resources.Count == 0)
            return [];

        var resources = new Dictionary<int, ScenePlaybackResourceState>(frame.Resources.Count);
        for (var i = 0; i < frame.Resources.Count; i++)
            resources[frame.Resources[i].EntityId] = frame.Resources[i];

        var added = new HashSet<int>();
        var result = new List<PlaybackCombatantRowViewModel>(Math.Max(combatants.Length, frame.Resources.Count));
        for (var i = 0; i < combatants.Length; i++)
        {
            var entry = combatants[i];
            var metrics = entry.Metrics;
            resources.TryGetValue(entry.Id, out var resource);
            if (metrics.DamageAmount <= 0 && metrics.HealingAmount <= 0 && resource.EntityId == 0)
                continue;

            result.Add(new PlaybackCombatantRowViewModel(
                entry.Id,
                DisplayContext.ResolveEntityName(entry.Id),
                FormatNumber(metrics.DamageAmount),
                FormatNumber(metrics.DamagePerSecond),
                FormatNumber(metrics.HealingAmount),
                FormatNumber(metrics.HealingPerSecond),
                CreateHpText(in resource),
                CreateHpSegments(in resource)));
            added.Add(entry.Id);
        }

        for (var i = 0; i < frame.Resources.Count; i++)
        {
            var resource = frame.Resources[i];
            if (!added.Add(resource.EntityId))
                continue;

            result.Add(new PlaybackCombatantRowViewModel(
                resource.EntityId,
                DisplayContext.ResolveEntityName(resource.EntityId),
                "0",
                "0",
                "0",
                "0",
                CreateHpText(in resource),
                CreateHpSegments(in resource)));
        }

        result.Sort(static (left, right) =>
        {
            var cmp = ParseSortable(right.DamageText).CompareTo(ParseSortable(left.DamageText));
            return cmp != 0 ? cmp : string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
        });
        return result;
    }

    private IReadOnlyList<PlaybackAuraRowViewModel> CreateAuras(ScenePlaybackFrame frame)
    {
        if (frame.ActiveAuras.Count == 0)
            return [];

        var result = new PlaybackAuraRowViewModel[frame.ActiveAuras.Count];
        for (var i = 0; i < frame.ActiveAuras.Count; i++)
        {
            var aura = frame.ActiveAuras[i];
            result[i] = new PlaybackAuraRowViewModel(
                aura.SkillCode,
                DisplayContext.ResolveSkillName(aura.SkillCode),
                DisplayContext.ResolveEntityName(aura.SourceEntityId),
                DisplayContext.ResolveEntityName(aura.TargetEntityId),
                aura.StackCount,
                aura.SequenceId);
        }

        Array.Sort(result, static (left, right) =>
        {
            var cmp = string.Compare(left.TargetName, right.TargetName, StringComparison.CurrentCulture);
            return cmp != 0 ? cmp : string.Compare(left.SkillName, right.SkillName, StringComparison.CurrentCulture);
        });
        return result;
    }

    private void RefreshTimelineTracks(ScenePlaybackFrame frame)
    {
        var duration = frame.TimeRange.DurationMilliseconds;
        if (!_timelineMarkersInitialized || Math.Abs(_timelineMarkerDuration - duration) > double.Epsilon)
        {
            _timelineMarkerTracks = CreateTimelineTracks(frame);
            _timelineMarkerDuration = duration;
            _timelineMarkersInitialized = true;
        }

        TimelineTracks = ApplyTimelinePosition(_timelineMarkerTracks, frame.PositionMilliseconds, duration);
    }

    private static IReadOnlyList<PlaybackTimelineLane> ApplyTimelinePosition(IReadOnlyList<PlaybackTimelineLane> lanes, double positionMilliseconds, double durationMilliseconds)
    {
        if (lanes.Count == 0)
            return [];

        var result = new PlaybackTimelineLane[lanes.Count];
        for (var i = 0; i < lanes.Count; i++)
            result[i] = lanes[i] with { PositionMilliseconds = positionMilliseconds, DurationMilliseconds = durationMilliseconds };
        return result;
    }

    private static string CreateHpText(in ScenePlaybackResourceState resource)
    {
        if (resource.EntityId == 0)
            return string.Empty;

        var current = Math.Max(0, resource.CurrentValue ?? 0);
        var maximum = Math.Max(current, resource.MaximumValue ?? 0);
        return maximum > 0 ? $"{FormatNumber(current)} / {FormatNumber(maximum)}" : FormatNumber(current);
    }

    private static double CreateHpRatio(in ScenePlaybackResourceState resource)
    {
        if (resource.EntityId == 0)
            return 0d;

        var current = Math.Max(0, resource.CurrentValue ?? 0);
        var maximum = Math.Max(current, resource.MaximumValue ?? 0);
        return maximum > 0 ? Math.Clamp(current / (double)maximum, 0d, 1d) : 0d;
    }

    private static IReadOnlyList<ProgressSegment> CreateHpSegments(in ScenePlaybackResourceState resource)
    {
        var ratio = CreateHpRatio(in resource);
        return ratio > 0 ? [new ProgressSegment(ratio, ResourceBrush)] : EmptySegments;
    }

    private IReadOnlyList<PlaybackTimelineLane> CreateTimelineTracks(ScenePlaybackFrame frame)
    {
        var segment = _source.CreateTimelineSegment();
        var duration = frame.TimeRange.DurationMilliseconds;
        if (segment.IsEmpty || duration <= 0)
        {
            TimelineMarkerLimitText = string.Empty;
            return [];
        }

        var read = ScenePlaybackTrackReader.Read(segment, frame.TimeRange, 0, duration, MaxTimelineMarkers);
        var groups = new Dictionary<ScenePlaybackTrack, List<PlaybackTimelineMarker>>();
        for (var i = 0; i < read.Markers.Count; i++)
        {
            var marker = read.Markers[i];
            var track = marker.Track;
            if (!groups.TryGetValue(track, out var list))
            {
                list = [];
                groups.Add(track, list);
            }

            list.Add(new PlaybackTimelineMarker(marker.PositionMilliseconds, ResolveMarkerWeight(marker), ResolveTrackBrush(track), CreateMarkerText(marker)));
        }

        TimelineMarkerLimitText = read.HasMore
            ? string.Format(CultureInfo.CurrentCulture, Localization["Playback_TimelineMarkerLimitFormat"], MaxTimelineMarkers.ToString("N0", CultureInfo.CurrentCulture))
            : string.Empty;

        var lanes = new List<PlaybackTimelineLane>(TrackOrder.Length);
        foreach (var track in TrackOrder)
        {
            groups.TryGetValue(track, out var markers);
            if (markers is null || markers.Count == 0)
                continue;

            lanes.Add(new PlaybackTimelineLane(
                ResolveTrackName(track),
                track,
                ResolveTrackBrush(track),
                markers,
                duration,
                frame.PositionMilliseconds,
                markers.Count));
        }

        return lanes;
    }

    private IReadOnlyList<PlaybackEventRowViewModel> CreateEventWindow(ScenePlaybackFrame frame, ScenePlaybackControllerState state)
    {
        var position = frame.PositionMilliseconds;
        var start = Math.Max(0, position - EventWindowRadiusMilliseconds);
        var end = frame.TimeRange.DurationMilliseconds > 0
            ? Math.Min(frame.TimeRange.DurationMilliseconds, position + EventWindowRadiusMilliseconds)
            : position + EventWindowRadiusMilliseconds;
        if (state.IsPlaying && frame.RecentMarkers.Count > 0)
            return CreateEventWindowRows(FilterRecentMarkers(frame.RecentMarkers, start, end));

        var segment = _source.CreateTimelineSegment();
        var read = ScenePlaybackTrackReader.Read(segment, frame.TimeRange, start, end, MaxEventWindowMarkers);
        if (read.Markers.Count == 0)
            return [];

        return CreateEventWindowRows(read.Markers);
    }

    private static IReadOnlyList<ScenePlaybackTrackMarker> FilterRecentMarkers(IReadOnlyList<ScenePlaybackTrackMarker> markers, long start, long end)
    {
        var result = new List<ScenePlaybackTrackMarker>(Math.Min(markers.Count, MaxEventWindowMarkers));
        for (var i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            if (marker.PositionMilliseconds < start || marker.PositionMilliseconds > end)
                continue;

            result.Add(marker);
            if (result.Count > MaxEventWindowMarkers)
                result.RemoveAt(0);
        }

        return result;
    }

    private IReadOnlyList<PlaybackEventRowViewModel> CreateEventWindowRows(IReadOnlyList<ScenePlaybackTrackMarker> markers)
    {
        if (markers.Count == 0)
            return [];

        var result = new PlaybackEventRowViewModel[markers.Count];
        for (var i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            result[i] = new PlaybackEventRowViewModel(
                FormatTime(marker.PositionMilliseconds),
                ResolveTrackName(marker.Track),
                DisplayContext.ResolveEntityName(marker.SourceEntityId),
                DisplayContext.ResolveEntityName(marker.TargetEntityId),
                marker.SkillCode > 0 ? DisplayContext.ResolveSkillName(marker.SkillCode) : string.Empty,
                CreateAmountText(marker));
        }

        return result;
    }

    private string CreateMarkerText(ScenePlaybackTrackMarker marker)
    {
        var amount = CreateAmountText(marker);
        var skill = marker.SkillCode > 0 ? DisplayContext.ResolveSkillName(marker.SkillCode) : string.Empty;
        if (!string.IsNullOrWhiteSpace(skill) && !string.IsNullOrWhiteSpace(amount))
            return $"{FormatTime(marker.PositionMilliseconds)} {skill} {amount}";
        if (!string.IsNullOrWhiteSpace(skill))
            return $"{FormatTime(marker.PositionMilliseconds)} {skill}";
        if (!string.IsNullOrWhiteSpace(amount))
            return $"{FormatTime(marker.PositionMilliseconds)} {amount}";
        return FormatTime(marker.PositionMilliseconds);
    }

    private static string CreateAmountText(ScenePlaybackTrackMarker marker)
    {
        if (marker.Track == ScenePlaybackTrack.Combat && marker.Amount != 0)
            return FormatSigned(marker.Amount);

        if (marker.Track == ScenePlaybackTrack.Resource)
        {
            var current = marker.CurrentValue.HasValue ? FormatNumber(marker.CurrentValue.Value) : "?";
            var maximum = marker.MaximumValue.HasValue ? FormatNumber(marker.MaximumValue.Value) : "?";
            return $"{current}/{maximum}";
        }

        if (marker.Track == ScenePlaybackTrack.Aura)
            return $"result {marker.ResultCode.ToString(CultureInfo.InvariantCulture)}";

        return string.Empty;
    }

    private static double ResolveMarkerWeight(ScenePlaybackTrackMarker marker)
    {
        var amount = Math.Abs(marker.Amount);
        return marker.Track == ScenePlaybackTrack.Combat && amount > 0
            ? Math.Clamp(Math.Log10(amount + 1) * 2.2d, 3d, 12d)
            : 5d;
    }

    private static IBrush ResolveTrackBrush(ScenePlaybackTrack track) => track switch
    {
        ScenePlaybackTrack.Combat => CombatBrush,
        ScenePlaybackTrack.Resource => ResourceBrush,
        ScenePlaybackTrack.Aura => AuraBrush,
        ScenePlaybackTrack.State => StateBrush,
        ScenePlaybackTrack.Scene => SceneBrush,
        ScenePlaybackTrack.Action => ActionBrush,
        ScenePlaybackTrack.Diagnostic => DiagnosticBrush,
        _ => OtherBrush
    };

    private string ResolveTrackName(ScenePlaybackTrack track) => track switch
    {
        ScenePlaybackTrack.Combat => Localization["Playback_Track_Combat"],
        ScenePlaybackTrack.Resource => Localization["Playback_Track_RemainingHp"],
        ScenePlaybackTrack.Aura => Localization["Playback_Track_Aura"],
        ScenePlaybackTrack.State => Localization["Playback_Track_State"],
        ScenePlaybackTrack.Scene => Localization["Playback_Track_Scene"],
        ScenePlaybackTrack.Action => Localization["Playback_Track_Action"],
        ScenePlaybackTrack.Diagnostic => Localization["Playback_Track_Diagnostic"],
        _ => Localization["Playback_Track_Other"]
    };

    private static string FormatSpeed(double speed)
        => speed.ToString(speed % 1 == 0 ? "0x" : "0.##x", CultureInfo.InvariantCulture);

    private static string FormatNumber(double value)
        => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatNumber(long value)
        => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatSigned(long value)
        => value > 0
            ? "+" + FormatNumber(value)
            : value.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatTime(double milliseconds)
    {
        var value = TimeSpan.FromMilliseconds(Math.Max(0d, milliseconds));
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : value.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static double ParseSortable(string value)
        => double.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) ? parsed : 0d;

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _seekCancellation?.Cancel();
        _seekCancellation?.Dispose();
        Localization.LanguageChanged -= OnLanguageChanged;
        _controller.FrameChanged -= OnFrameChanged;
        await _controller.DisposeAsync().ConfigureAwait(false);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        WindowTitle = string.Format(CultureInfo.CurrentCulture, Localization["Playback_WindowTitleFormat"], DisplayContext.ResolveMapName(MapId));
        _timelineMarkersInitialized = false;
        ApplyFrame(_currentFrame, _controller.State);
    }
}

public sealed record PlaybackCombatantRowViewModel(int EntityId, string Name, string DamageText, string DamagePerSecondText, string HealingText, string HealingPerSecondText, string HpText, IReadOnlyList<ProgressSegment> HpSegments);

public sealed record PlaybackAuraRowViewModel(int SkillCode, string SkillName, string SourceName, string TargetName, int StackCount, int SequenceId);

public sealed record PlaybackEventRowViewModel(string TimeText, string TrackText, string SourceText, string TargetText, string SkillText, string AmountText);
