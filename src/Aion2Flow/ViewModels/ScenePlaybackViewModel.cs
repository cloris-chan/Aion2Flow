using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using Cloris.Aion2Flow.Collections;
using Cloris.Aion2Flow.Presentation;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;
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

    private const int MaxTimelineMarkersPerTrack = 256;
    private const int MaxCombatantTimelineMarkersPerTrack = 96;
    private const int MaxEventWindowMarkers = 96;
    private const long EventWindowRadiusMilliseconds = 4_000;
    private const long StepMilliseconds = 1_000;
    private const long DetailRefreshIntervalMilliseconds = 250;

    private static readonly IBrush CombatBrush = Brush.Parse("#18D7F4");
    private static readonly IBrush ResourceBrush = Brush.Parse("#8CE271");
    private static readonly IBrush AuraBrush = Brush.Parse("#C98EFF");
    private static readonly IBrush StateBrush = Brush.Parse("#FFD166");
    private static readonly IBrush SceneBrush = Brush.Parse("#FF8A65");
    private static readonly IBrush ActionBrush = Brush.Parse("#65A7FF");
    private static readonly IBrush DiagnosticBrush = Brush.Parse("#9AA8B4");
    private static readonly IBrush OtherBrush = Brush.Parse("#D4DCE5");
    private static readonly IBrush[] AuraAccentBrushes =
    [
        Brush.Parse("#22D3EE"),
        Brush.Parse("#89D66B"),
        Brush.Parse("#FFD166"),
        Brush.Parse("#C98EFF"),
        Brush.Parse("#FF8A65"),
        Brush.Parse("#65A7FF")
    ];
    private static readonly IBrush[] AuraFillBrushes =
    [
        Brush.Parse("#4022D3EE"),
        Brush.Parse("#4089D66B"),
        Brush.Parse("#40FFD166"),
        Brush.Parse("#40C98EFF"),
        Brush.Parse("#40FF8A65"),
        Brush.Parse("#4065A7FF")
    ];
    private readonly Lock _frameGate = new();
    private readonly ScenePlaybackController _controller;
    private readonly IScenePlaybackSource _source;
    private readonly ArchivedEncounterRecord _record;
    private readonly UiFrameBatchService _frameBatchService;
    private readonly Dictionary<int, ScenePlaybackResourceState> _resourceScratch = [];
    private readonly HashSet<int> _seenCombatantIds = [];
    private readonly HashSet<long> _eventWindowOrdinals = [];
    private CancellationTokenSource? _detailCancellation;
    private CancellationTokenSource? _auraTimelineCancellation;
    private Task? _detailTask;
    private Task? _auraTimelineTask;
    private ScenePlaybackFrame _currentFrame;
    private ScenePlaybackFrameChangedEventArgs? _pendingFrameChanged;
    private PlaybackTimelineStrip _globalTimeline = PlaybackTimelineStrip.Empty;
    private Dictionary<int, PlaybackTimelineStrip> _combatantTimelines = [];
    private IReadOnlyList<PlaybackAuraTimelineLane> _auraTimelineTracks = [];
    private double _timelineMarkerDuration = -1;
    private long _lastDetailProjectionTick;
    private long _lastEventWindowEndObservationOrdinal = long.MinValue;
    private long _displayTextRevision;
    private bool _isApplyingFrame;
    private bool _isDisposed;
    private bool _frameApplyQueued;
    private bool _forceNextDetailProjection = true;
    private bool _detailRefreshQueued;
    private bool _detailRequestPending;
    private bool _timelineMarkersInitialized;
    private long _detailRequestGeneration;

    public ScenePlaybackViewModel(ArchivedEncounterRecord record, SceneDisplayContext displayContext)
        : this(record, displayContext, Ioc.Default.GetRequiredService<LocalizationService>())
    {
    }

    public ScenePlaybackViewModel(ArchivedEncounterRecord record, SceneDisplayContext displayContext, LocalizationService localization)
        : this(record, displayContext, localization, Ioc.Default.GetRequiredService<IScenePlaybackTickSourceFactory>())
    {
    }

    internal ScenePlaybackViewModel(ArchivedEncounterRecord record, SceneDisplayContext displayContext, LocalizationService localization, IScenePlaybackTickSourceFactory tickSourceFactory)
        : this(record, displayContext, localization, tickSourceFactory, Ioc.Default.GetRequiredService<UiFrameBatchService>())
    {
    }

    internal ScenePlaybackViewModel(ArchivedEncounterRecord record, SceneDisplayContext displayContext, LocalizationService localization, IScenePlaybackTickSourceFactory tickSourceFactory, UiFrameBatchService frameBatchService)
    {
        _record = record;
        _frameBatchService = frameBatchService;
        DisplayContext = displayContext;
        Localization = localization;
        CombatantDetails = new CombatantDetailsFlyoutViewModel(localization, frameBatchService)
        {
            DisplayContext = displayContext
        };
        _source = new ArchivedScenePlaybackSource(record);
        _controller = new ScenePlaybackController(_source, tickSourceFactory, ScenePlaybackControllerOptions.Default);
        _controller.FrameChanged += OnFrameChanged;
        Localization.LanguageChanged += OnLanguageChanged;
        SceneName = displayContext.ResolveSceneName(record.ScenePayload.Kind, record.Snapshot.MapId, record.ScenePayload.BossNpcCodes);
        WindowTitle = string.Format(CultureInfo.CurrentCulture, Localization["Playback_WindowTitleFormat"], SceneName);
        ArchivedAtText = record.ArchivedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        _currentFrame = _controller.CurrentFrame;
        ApplyFrame(_currentFrame, _controller.State);
    }

    public SceneDisplayContext DisplayContext { get; }

    public LocalizationService Localization { get; }

    public CombatantDetailsFlyoutViewModel CombatantDetails { get; }

    public bool IsCombatantDetailsVisible => IsDetailsInspectorOpen && SelectedCombatantId > 0;

    public bool IsAuraTimelineVisible => SelectedCombatantId > 0 && AuraTimelineTracks.Count > 0;

    [ObservableProperty]
    public partial string WindowTitle { get; set; } = "Playback";

    [ObservableProperty]
    public partial string ArchivedAtText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SceneName { get; set; } = string.Empty;

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
    public partial PlaybackTimelineStrip GlobalTimeline { get; set; } = PlaybackTimelineStrip.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuraTimelineVisible))]
    public partial IReadOnlyList<PlaybackAuraTimelineLane> AuraTimelineTracks { get; set; } = [];

    public KeyedObservableCollection<int, PlaybackCombatantRowViewModel> Combatants { get; } = new(static row => row.EntityId)
    {
        ResetThreshold = 256
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCombatantDetailsVisible))]
    [NotifyPropertyChangedFor(nameof(IsAuraTimelineVisible))]
    public partial int SelectedCombatantId { get; set; }

    [ObservableProperty]
    public partial int ExpandedCombatantId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCombatantDetailsVisible))]
    public partial bool IsDetailsInspectorOpen { get; set; }

    public KeyedObservableCollection<long, PlaybackEventRowViewModel> EventWindow { get; } = new(static row => row.ObservationOrdinal)
    {
        ResetThreshold = 256
    };

    partial void OnPositionMillisecondsChanged(double value)
    {
        PositionText = FormatTime(value);
        if (!_isApplyingFrame)
            RequestSeek(value);
    }

    partial void OnSelectedCombatantIdChanged(int value)
    {
        _detailCancellation?.Cancel();
        _auraTimelineCancellation?.Cancel();
        _detailRequestGeneration++;
        _auraTimelineTracks = [];
        AuraTimelineTracks = [];
        if (value <= 0)
        {
            IsDetailsInspectorOpen = false;
            ApplyCombatantState(value, ExpandedCombatantId);
            _detailRefreshQueued = false;
            CombatantDetails.Deactivate();
            return;
        }

        _forceNextDetailProjection = true;
        _detailRefreshQueued = true;
        ApplyCombatantState(value, ExpandedCombatantId);
        RequestAuraTimeline(value);
        IsDetailsInspectorOpen = true;
        if (!_detailRequestPending)
            RequestCombatantDetail(_currentFrame);
    }

    partial void OnIsDetailsInspectorOpenChanged(bool value)
    {
        if (!value)
        {
            _detailCancellation?.Cancel();
            _detailRefreshQueued = false;
            CombatantDetails.Deactivate();
            return;
        }

        if (SelectedCombatantId <= 0)
            return;

        _forceNextDetailProjection = true;
        _detailRefreshQueued = true;
        if (!_detailRequestPending)
            RequestCombatantDetail(_currentFrame);
    }

    partial void OnExpandedCombatantIdChanged(int value)
    {
        ApplyCombatantState(SelectedCombatantId, value);
    }

    public void SelectCombatant(PlaybackCombatantRowViewModel combatant)
    {
        if (combatant.EntityId > 0)
        {
            IsDetailsInspectorOpen = true;
            if (ExpandedCombatantId != 0 && ExpandedCombatantId != combatant.EntityId)
                ExpandedCombatantId = 0;
            SelectedCombatantId = combatant.EntityId;
        }
    }

    public void ToggleCombatantExpansion(PlaybackCombatantRowViewModel combatant)
    {
        if (combatant.EntityId <= 0)
            return;

        IsDetailsInspectorOpen = true;
        SelectedCombatantId = combatant.EntityId;
        ExpandedCombatantId = ExpandedCombatantId == combatant.EntityId ? 0 : combatant.EntityId;
    }

    [RelayCommand]
    private void CloseDetailsInspector() => IsDetailsInspectorOpen = false;

    public void RequestSeek(double positionMilliseconds)
    {
        if (_isDisposed)
            return;

        var duration = DurationMilliseconds;
        var target = duration > 0 ? Math.Clamp(positionMilliseconds, 0d, duration) : Math.Max(0d, positionMilliseconds);
        _forceNextDetailProjection = true;
        _ = SeekCoreAsync((long)Math.Round(target, MidpointRounding.AwayFromZero));
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
        PositionMilliseconds = duration > 0 ? Math.Min(duration, PositionMilliseconds + StepMilliseconds) : PositionMilliseconds + StepMilliseconds;
    }

    [RelayCommand]
    private Task StepEventBackwardAsync() => StepEventAsync(-1);

    [RelayCommand]
    private Task StepEventForwardAsync() => StepEventAsync(1);

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

    private async Task SeekCoreAsync(long positionMilliseconds)
    {
        try
        {
            IsLoading = true;
            await _controller.SeekAsync(positionMilliseconds).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = ex.Message);
        }
    }

    private async Task StepEventAsync(int direction)
    {
        try
        {
            _forceNextDetailProjection = true;
            IsLoading = true;
            await _controller.StepEventAsync(direction).ConfigureAwait(false);
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
        var duration = frame.TimeRange.DurationMilliseconds;
        _isApplyingFrame = true;
        PositionMilliseconds = frame.PositionMilliseconds;
        if (Math.Abs(DurationMilliseconds - duration) > double.Epsilon)
        {
            DurationMilliseconds = duration;
            DurationText = FormatTime(duration);
        }
        _isApplyingFrame = false;

        if (Math.Abs(Speed - state.Speed) > double.Epsilon)
        {
            Speed = state.Speed;
            SpeedText = FormatSpeed(state.Speed);
        }
        IsPlaying = state.IsPlaying;
        IsLoading = state.IsLoading;
        var checkpointTextNeedsUpdate = IsCheckpointing != state.IsCheckpointing || CheckpointCount != state.CheckpointCount || CheckpointText.Length == 0;
        if (IsCheckpointing != state.IsCheckpointing)
            IsCheckpointing = state.IsCheckpointing;
        if (CheckpointCount != state.CheckpointCount)
            CheckpointCount = state.CheckpointCount;
        if (checkpointTextNeedsUpdate)
        {
            CheckpointText = state.IsCheckpointing
                ? string.Format(CultureInfo.CurrentCulture, Localization["Playback_CheckpointsBuildingFormat"], state.CheckpointCount.ToString("N0", CultureInfo.CurrentCulture))
                : string.Format(CultureInfo.CurrentCulture, Localization["Playback_CheckpointsReadyFormat"], state.CheckpointCount.ToString("N0", CultureInfo.CurrentCulture));
        }

        var statusText = state.IsLoading
            ? Localization["Playback_Status_Loading"]
            : state.IsPlaying
                ? Localization["Playback_Status_Playing"]
                : Localization["Playback_Status_Paused"];
        if (!string.Equals(StatusText, statusText, StringComparison.Ordinal))
            StatusText = statusText;

        RefreshTimelineTracks(frame);
        if (ShouldRefreshEventWindow(frame, state))
        {
            RefreshEventWindow(frame);
            _lastEventWindowEndObservationOrdinal = frame.AppliedSegment.EndObservationOrdinalExclusive;
        }

        if (!ShouldRefreshDetails(frame, state))
            return;

        RefreshCombatants(frame);
        RequestCombatantDetail(frame);
        _lastDetailProjectionTick = Environment.TickCount64;
        _forceNextDetailProjection = false;
    }

    private bool ShouldRefreshDetails(ScenePlaybackFrame frame, ScenePlaybackControllerState state)
    {
        if (_forceNextDetailProjection || !state.IsPlaying)
            return true;

        if (_lastDetailProjectionTick == 0)
            return true;

        if (frame.TimeRange.DurationMilliseconds > 0 && frame.PositionMilliseconds >= frame.TimeRange.DurationMilliseconds)
            return true;

        return Environment.TickCount64 - _lastDetailProjectionTick >= DetailRefreshIntervalMilliseconds;
    }

    private bool ShouldRefreshEventWindow(ScenePlaybackFrame frame, ScenePlaybackControllerState state)
    {
        if (_forceNextDetailProjection || !state.IsPlaying)
            return true;

        return frame.AppliedSegment.EndObservationOrdinalExclusive != _lastEventWindowEndObservationOrdinal;
    }

    private void RefreshCombatants(ScenePlaybackFrame frame)
    {
        var combatants = frame.Snapshot.Combatants.AsSpan();
        if (combatants.Length == 0 && frame.Resources.Count == 0)
        {
            if (Combatants.Count > 0)
                Combatants.Clear();
            return;
        }

        _resourceScratch.Clear();
        for (var i = 0; i < frame.Resources.Count; i++)
            _resourceScratch[frame.Resources[i].EntityId] = frame.Resources[i];

        var timelines = _combatantTimelines;
        _seenCombatantIds.Clear();
        using var deferral = Combatants.SuspendNotifications();
        foreach (var row in deferral.Snapshot)
        {
            var id = row.EntityId;
            var hasMetrics = frame.Snapshot.Combatants.TryGetValue(id, out var metrics);
            var hasResource = _resourceScratch.TryGetValue(id, out var resource);
            if (!hasMetrics && !hasResource)
            {
                Combatants.Remove(id);
                continue;
            }

            ApplyCombatantRow(row, id, in metrics, hasMetrics, in resource, ResolveCombatantTimeline(timelines, id));
            _seenCombatantIds.Add(id);
        }

        for (var i = 0; i < combatants.Length; i++)
        {
            var entry = combatants[i];
            if (!_seenCombatantIds.Add(entry.Id))
                continue;

            var metrics = entry.Metrics;
            _resourceScratch.TryGetValue(entry.Id, out var resource);
            var row = new PlaybackCombatantRowViewModel(_frameBatchService, entry.Id);
            ApplyCombatantRow(row, entry.Id, in metrics, hasMetrics: true, in resource, ResolveCombatantTimeline(timelines, entry.Id));
            Combatants.Add(row);
        }

        for (var i = 0; i < frame.Resources.Count; i++)
        {
            var resource = frame.Resources[i];
            if (!_seenCombatantIds.Add(resource.EntityId))
                continue;

            var metrics = default(SceneCombatantMetrics);
            var row = new PlaybackCombatantRowViewModel(_frameBatchService, resource.EntityId);
            ApplyCombatantRow(row, resource.EntityId, in metrics, hasMetrics: false, in resource, ResolveCombatantTimeline(timelines, resource.EntityId));
            Combatants.Add(row);
        }

        Combatants.Sort(CompareCombatantRows);
    }

    private void ApplyCombatantRow(PlaybackCombatantRowViewModel row, int entityId, in SceneCombatantMetrics metrics, bool hasMetrics, in ScenePlaybackResourceState resource, PlaybackTimelineStrip timeline)
    {
        var damage = hasMetrics ? metrics.DamageAmount : 0;
        var damagePerSecond = hasMetrics ? metrics.DamagePerSecond : 0;
        var healing = hasMetrics ? metrics.HealingAmount : 0;
        var healingPerSecond = hasMetrics ? metrics.HealingPerSecond : 0;
        row.Name = DisplayContext.ResolveEntityName(entityId);
        row.DamageAmount = damage;
        row.DamageText = FormatNumber(damage);
        row.DamagePerSecondText = FormatNumber(damagePerSecond);
        row.HealingText = FormatNumber(healing);
        row.HealingPerSecondText = FormatNumber(healingPerSecond);
        row.HpText = CreateHpText(in resource);
        row.UpdateHpSegments(CreateHpRatio(in resource), ResourceBrush);
        row.Timeline = timeline;
        row.IsSelected = entityId == SelectedCombatantId;
        row.IsExpanded = entityId == ExpandedCombatantId;
    }

    private static int CompareCombatantRows(PlaybackCombatantRowViewModel left, PlaybackCombatantRowViewModel right)
    {
        var cmp = right.DamageAmount.CompareTo(left.DamageAmount);
        if (cmp != 0)
            return cmp;

        cmp = string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
        return cmp != 0 ? cmp : left.EntityId.CompareTo(right.EntityId);
    }

    private void ApplyCombatantState(int selectedCombatantId, int expandedCombatantId)
    {
        for (var i = 0; i < Combatants.Count; i++)
        {
            var combatant = Combatants[i];
            combatant.IsSelected = combatant.EntityId == selectedCombatantId;
            combatant.IsExpanded = combatant.EntityId == expandedCombatantId;
        }
    }

    private void RequestCombatantDetail(ScenePlaybackFrame frame)
    {
        var combatantId = SelectedCombatantId;
        if (_isDisposed || combatantId <= 0)
            return;

        if (_detailRequestPending)
        {
            _detailRefreshQueued = true;
            _detailRequestGeneration++;
            _detailCancellation?.Cancel();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _detailCancellation = cancellation;
        _detailRequestPending = true;
        _detailRefreshQueued = false;
        var generation = ++_detailRequestGeneration;
        _detailTask = ProjectCombatantDetailAsync(frame.EncounterId, combatantId, frame.AppliedSegment.EndObservationOrdinalExclusive, generation, cancellation);
    }

    private async Task ProjectCombatantDetailAsync(Guid encounterId, int combatantId, long expectedEndObservationOrdinalExclusive, long generation, CancellationTokenSource cancellation)
    {
        try
        {
            var projection = await _controller.CreateCombatantDetailAsync(combatantId, cancellation.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_isDisposed ||
                    cancellation.IsCancellationRequested ||
                    generation != _detailRequestGeneration ||
                    combatantId != SelectedCombatantId ||
                    projection.EndObservationOrdinalExclusive != expectedEndObservationOrdinalExclusive)
                {
                    return;
                }

                CombatantDetails.SelectPlaybackSceneEncounterCombatant(encounterId, combatantId, projection.Snapshot, projection.Update, projection.Events);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = ex.Message);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ReferenceEquals(_detailCancellation, cancellation))
                    return;

                cancellation.Dispose();
                _detailCancellation = null;
                _detailTask = null;
                _detailRequestPending = false;
                if (_detailRefreshQueued && !_isDisposed && SelectedCombatantId > 0)
                    RequestCombatantDetail(_currentFrame);
            });
        }
    }

    private void RefreshTimelineTracks(ScenePlaybackFrame frame)
    {
        var duration = frame.TimeRange.DurationMilliseconds;
        if (!_timelineMarkersInitialized || Math.Abs(_timelineMarkerDuration - duration) > double.Epsilon)
        {
            var timelines = CreateTimelineStrips(frame);
            _globalTimeline = timelines.Global;
            _combatantTimelines = timelines.Combatants;
            _timelineMarkerDuration = duration;
            _timelineMarkersInitialized = true;
            ApplyCombatantTimelines(_combatantTimelines);
        }

        GlobalTimeline = _globalTimeline;
    }

    private static PlaybackTimelineStrip ResolveCombatantTimeline(Dictionary<int, PlaybackTimelineStrip> timelines, int combatantId)
        => combatantId > 0 && timelines.TryGetValue(combatantId, out var timeline) ? timeline : PlaybackTimelineStrip.Empty;

    private void ApplyCombatantTimelines(Dictionary<int, PlaybackTimelineStrip> timelines)
    {
        for (var i = 0; i < Combatants.Count; i++)
        {
            var combatant = Combatants[i];
            var timeline = ResolveCombatantTimeline(timelines, combatant.EntityId);
            combatant.Timeline = timeline;
        }
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

    private PlaybackTimelineBuildResult CreateTimelineStrips(ScenePlaybackFrame frame)
    {
        var segment = _controller.CreateTimelineSegment();
        var duration = frame.TimeRange.DurationMilliseconds;
        if (segment.IsEmpty || duration <= 0)
            return new PlaybackTimelineBuildResult(PlaybackTimelineStrip.Empty, []);

        var read = ScenePlaybackTrackReader.ReadSampled(segment, 0, duration, MaxTimelineMarkersPerTrack);
        var combatantRead = ScenePlaybackTrackReader.ReadCombatantSampled(segment, 0, duration, MaxCombatantTimelineMarkersPerTrack);
        var global = CreateTimelineStrip(CreateTimelineGroups(read.Samples), read.TrackCounts);
        var combatants = new Dictionary<int, PlaybackTimelineStrip>(combatantRead.Combatants.Count);
        for (var i = 0; i < combatantRead.Combatants.Count; i++)
        {
            var samples = combatantRead.Combatants[i];
            var strip = CreateTimelineStrip(CreateTimelineGroups(samples.Samples), samples.TrackCounts);
            if (strip.Bands.Count > 0)
                combatants[samples.CombatantId] = strip;
        }

        return new PlaybackTimelineBuildResult(global, combatants);
    }

    private Dictionary<ScenePlaybackTrack, List<PlaybackTimelineMarker>> CreateTimelineGroups(IReadOnlyList<ScenePlaybackTrackSample> samples)
    {
        var groups = new Dictionary<ScenePlaybackTrack, List<PlaybackTimelineMarker>>();
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            var marker = sample.Marker;
            var track = marker.Track;
            if (!groups.TryGetValue(track, out var markers))
            {
                markers = [];
                groups.Add(track, markers);
            }

            markers.Add(new PlaybackTimelineMarker(marker.PositionMilliseconds, ResolveMarkerWeight(marker, sample.EventCount), ResolveTrackBrush(track), CreateMarkerText(marker)));
        }

        return groups;
    }

    private static PlaybackTimelineStrip CreateTimelineStrip(Dictionary<ScenePlaybackTrack, List<PlaybackTimelineMarker>> groups, IReadOnlyList<ScenePlaybackTrackCount> trackCounts)
    {
        var bands = new List<PlaybackTimelineBand>(TrackOrder.Length);
        foreach (var track in TrackOrder)
        {
            groups.TryGetValue(track, out var markers);
            if (markers is null || markers.Count == 0)
                continue;

            var trackCount = trackCounts.Count == 0 ? markers.Count : ResolveTrackCount(trackCounts, track);
            bands.Add(new PlaybackTimelineBand(track, ResolveTrackBrush(track), markers, trackCount));
        }

        var totalCount = 0;
        for (var i = 0; i < bands.Count; i++)
            totalCount += bands[i].Count;

        return bands.Count == 0 ? PlaybackTimelineStrip.Empty : new PlaybackTimelineStrip(bands, totalCount);
    }

    private void RequestAuraTimeline(int targetEntityId)
    {
        _auraTimelineCancellation?.Cancel();
        _auraTimelineCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _auraTimelineCancellation = cancellation;
        var segment = _controller.CreateTimelineSegment();
        var duration = (long)Math.Round(DurationMilliseconds, MidpointRounding.AwayFromZero);
        _auraTimelineTask = ProjectAuraTimelineAsync(targetEntityId, segment, duration, cancellation);
    }

    private async Task ProjectAuraTimelineAsync(int targetEntityId, SceneJournalSegment segment, long durationMilliseconds, CancellationTokenSource cancellation)
    {
        try
        {
            var timeline = await Task.Run(
                () => ScenePlaybackAuraTimelineReader.Read(segment, targetEntityId, durationMilliseconds, cancellation.Token),
                cancellation.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_isDisposed || cancellation.IsCancellationRequested || targetEntityId != SelectedCombatantId)
                    return;

                _auraTimelineTracks = CreateAuraTimelineTracks(timeline);
                AuraTimelineTracks = _auraTimelineTracks;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = ex.Message);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ReferenceEquals(_auraTimelineCancellation, cancellation))
                    return;

                cancellation.Dispose();
                _auraTimelineCancellation = null;
                _auraTimelineTask = null;
            });
        }
    }

    private PlaybackAuraTimelineLane[] CreateAuraTimelineTracks(ScenePlaybackAuraTimeline timeline)
    {
        if (timeline.Coverages.Count == 0 && timeline.Applications.Count == 0)
            return [];

        var groups = new Dictionary<AuraTimelineDisplayKey, AuraTimelineLaneBuilder>();
        for (var i = 0; i < timeline.Coverages.Count; i++)
        {
            var coverage = timeline.Coverages[i];
            var key = AuraTimelineDisplayKey.Create(coverage.DisplayResourceEffectRef.RawId, coverage.InstanceSequenceId);
            GetAuraTimelineBuilder(groups, key, coverage.DisplayResourceEffectRef.RawId, coverage.InstanceSequenceId)
                .Coverages.Add((coverage.StartMilliseconds, coverage.EndMilliseconds));
        }

        for (var i = 0; i < timeline.Applications.Count; i++)
        {
            var application = timeline.Applications[i];
            var key = AuraTimelineDisplayKey.Create(application.DisplayResourceEffectRef.RawId, application.InstanceSequenceId);
            GetAuraTimelineBuilder(groups, key, application.DisplayResourceEffectRef.RawId, application.InstanceSequenceId)
                .Applications.Add((application.PositionMilliseconds, application.Kind));
        }

        var result = new List<PlaybackAuraTimelineLane>(groups.Count);
        foreach (var builder in groups.Values)
        {
            var paletteIndex = ResolveAuraPaletteIndex(builder.DisplayResourceEffectRefRaw, builder.InstanceSequenceId);
            var accent = AuraAccentBrushes[paletteIndex];
            var fill = AuraFillBrushes[paletteIndex];
            var skillCode = builder.DisplayResourceEffectRefRaw is > 0 and <= int.MaxValue
                ? (int)builder.DisplayResourceEffectRefRaw
                : 0;
            var fallback = builder.DisplayResourceEffectRefRaw > 0
                ? builder.DisplayResourceEffectRefRaw.ToString(CultureInfo.InvariantCulture)
                : string.Format(CultureInfo.CurrentCulture, Localization["Playback_AuraUnknownFormat"], builder.InstanceSequenceId);
            var markers = new PlaybackTimelineMarker[builder.Applications.Count];
            builder.Applications.Sort(static (left, right) => left.PositionMilliseconds.CompareTo(right.PositionMilliseconds));
            for (var i = 0; i < builder.Applications.Count; i++)
            {
                var application = builder.Applications[i];
                var text = application.Kind == ScenePlaybackLifecycleEventKind.Renew
                    ? Localization["Playback_Lifecycle_Renew"]
                    : Localization["Playback_Lifecycle_OpenIndefinite"];
                markers[i] = new PlaybackTimelineMarker(application.PositionMilliseconds, 16d, accent, text, IsApplication: true);
            }

            var spans = MergeAuraCoverages(builder.Coverages, fill, accent);
            result.Add(new PlaybackAuraTimelineLane(skillCode, fallback, accent, markers, spans, builder.Applications.Count));
        }

        result.Sort((left, right) =>
        {
            var leftName = left.SkillCode > 0 ? DisplayContext.ResolveSkillName(left.SkillCode) : left.FallbackText;
            var rightName = right.SkillCode > 0 ? DisplayContext.ResolveSkillName(right.SkillCode) : right.FallbackText;
            return string.Compare(leftName, rightName, StringComparison.CurrentCulture);
        });
        return result.ToArray();
    }

    private static AuraTimelineLaneBuilder GetAuraTimelineBuilder(
        Dictionary<AuraTimelineDisplayKey, AuraTimelineLaneBuilder> groups,
        AuraTimelineDisplayKey key,
        uint displayResourceEffectRefRaw,
        int instanceSequenceId)
    {
        if (groups.TryGetValue(key, out var builder))
            return builder;

        builder = new AuraTimelineLaneBuilder(displayResourceEffectRefRaw, instanceSequenceId);
        groups.Add(key, builder);
        return builder;
    }

    private static PlaybackTimelineSpan[] MergeAuraCoverages(List<(long StartMilliseconds, long EndMilliseconds)> coverages, IBrush fillBrush, IBrush borderBrush)
    {
        if (coverages.Count == 0)
            return [];

        coverages.Sort(static (left, right) =>
        {
            var comparison = left.StartMilliseconds.CompareTo(right.StartMilliseconds);
            return comparison != 0 ? comparison : left.EndMilliseconds.CompareTo(right.EndMilliseconds);
        });
        var result = new List<PlaybackTimelineSpan>(coverages.Count);
        var start = coverages[0].StartMilliseconds;
        var end = coverages[0].EndMilliseconds;
        for (var i = 1; i < coverages.Count; i++)
        {
            var coverage = coverages[i];
            if (coverage.StartMilliseconds <= end)
            {
                end = Math.Max(end, coverage.EndMilliseconds);
                continue;
            }

            result.Add(new PlaybackTimelineSpan(start, end, fillBrush, borderBrush));
            start = coverage.StartMilliseconds;
            end = coverage.EndMilliseconds;
        }

        result.Add(new PlaybackTimelineSpan(start, end, fillBrush, borderBrush));
        return result.ToArray();
    }

    private static int ResolveAuraPaletteIndex(uint displayResourceEffectRefRaw, int instanceSequenceId)
    {
        var value = displayResourceEffectRefRaw != 0 ? displayResourceEffectRefRaw : unchecked((uint)instanceSequenceId);
        value ^= value >> 16;
        return (int)(value % AuraAccentBrushes.Length);
    }

    private void RefreshEventWindow(ScenePlaybackFrame frame)
    {
        var markers = CreateEventWindowMarkers(frame);
        if (markers.Count == 0)
        {
            if (EventWindow.Count > 0)
                EventWindow.Clear();
            return;
        }

        _eventWindowOrdinals.Clear();
        for (var i = 0; i < markers.Count; i++)
            _eventWindowOrdinals.Add(markers[i].ObservationOrdinal);

        using var deferral = EventWindow.SuspendNotifications();
        foreach (var row in deferral.Snapshot)
        {
            if (!_eventWindowOrdinals.Contains(row.ObservationOrdinal))
                EventWindow.Remove(row.ObservationOrdinal);
        }

        for (var i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            if (!EventWindow.TryGetValue(marker.ObservationOrdinal, out var row))
            {
                row = new PlaybackEventRowViewModel(_frameBatchService, marker.ObservationOrdinal);
                EventWindow.Add(row);
            }

            if (row.DisplayTextRevision != _displayTextRevision)
                ApplyEventRow(row, marker);
        }

        EventWindow.Sort(CompareEventRows);
    }

    private IReadOnlyList<ScenePlaybackTrackMarker> CreateEventWindowMarkers(ScenePlaybackFrame frame)
    {
        var position = frame.PositionMilliseconds;
        var start = Math.Max(0, position - EventWindowRadiusMilliseconds);
        var end = frame.TimeRange.DurationMilliseconds > 0
            ? Math.Min(frame.TimeRange.DurationMilliseconds, position + EventWindowRadiusMilliseconds)
            : position + EventWindowRadiusMilliseconds;
        if (frame.RecentMarkers.Count > 0)
            return FilterRecentMarkers(frame.RecentMarkers, start, end);

        var segment = _controller.CreateTimelineSegment(start, end);
        var read = ScenePlaybackTrackReader.Read(segment, start, end, MaxEventWindowMarkers);
        return read.Markers;
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

    private void ApplyEventRow(PlaybackEventRowViewModel row, ScenePlaybackTrackMarker marker)
    {
        row.PositionMilliseconds = marker.PositionMilliseconds;
        row.TimeText = FormatTime(marker.PositionMilliseconds);
        row.TrackText = ResolveTrackName(marker.Track);
        row.SourceText = DisplayContext.ResolveEntityName(marker.SourceEntityId);
        row.TargetText = DisplayContext.ResolveEntityName(marker.TargetEntityId);
        row.SkillText = marker.SkillCode > 0 ? DisplayContext.ResolveSkillName(marker.SkillCode) : string.Empty;
        row.AmountText = CreateAmountText(marker);
        row.DisplayTextRevision = _displayTextRevision;
    }

    private static int CompareEventRows(PlaybackEventRowViewModel left, PlaybackEventRowViewModel right)
    {
        var cmp = left.PositionMilliseconds.CompareTo(right.PositionMilliseconds);
        return cmp != 0 ? cmp : left.ObservationOrdinal.CompareTo(right.ObservationOrdinal);
    }

    private string CreateMarkerText(ScenePlaybackTrackMarker marker)
    {
        var amount = CreateAmountText(marker);
        var skillName = marker.SkillCode > 0 ? DisplayContext.ResolveSkillName(marker.SkillCode) : string.Empty;
        if (!string.IsNullOrWhiteSpace(skillName) && !string.IsNullOrWhiteSpace(amount))
            return $"{FormatTime(marker.PositionMilliseconds)} {skillName} {amount}";
        if (!string.IsNullOrWhiteSpace(skillName))
            return $"{FormatTime(marker.PositionMilliseconds)} {skillName}";
        if (!string.IsNullOrWhiteSpace(amount))
            return $"{FormatTime(marker.PositionMilliseconds)} {amount}";
        return FormatTime(marker.PositionMilliseconds);
    }

    private string CreateAmountText(ScenePlaybackTrackMarker marker)
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
        {
            return marker.LifecycleEventKind switch
            {
                ScenePlaybackLifecycleEventKind.Open when marker.DurationMilliseconds == ushort.MaxValue => Localization["Playback_Lifecycle_OpenIndefinite"],
                ScenePlaybackLifecycleEventKind.Open => string.Format(CultureInfo.CurrentCulture, Localization["Playback_Lifecycle_OpenFormat"], marker.DurationMilliseconds),
                ScenePlaybackLifecycleEventKind.Renew => Localization["Playback_Lifecycle_Renew"],
                ScenePlaybackLifecycleEventKind.Result => string.Format(CultureInfo.CurrentCulture, Localization["Playback_Lifecycle_ResultFormat"], marker.ResultCode),
                _ => string.Empty
            };
        }

        return string.Empty;
    }

    private static double ResolveMarkerWeight(ScenePlaybackTrackMarker marker, int eventCount)
    {
        var amount = marker.Amount >= 0 ? (double)marker.Amount : -(double)marker.Amount;
        var baseWeight = marker.Track == ScenePlaybackTrack.Combat && amount > 0
            ? Math.Clamp(Math.Log10(amount + 1) * 2.2d, 3d, 12d)
            : 5d;
        return Math.Clamp(baseWeight + Math.Log2(Math.Max(1, eventCount)) * 0.75d, 3d, 12d);
    }

    private static int ResolveTrackCount(IReadOnlyList<ScenePlaybackTrackCount> counts, ScenePlaybackTrack track)
    {
        for (var i = 0; i < counts.Count; i++)
        {
            if (counts[i].Track == track)
                return counts[i].Count;
        }

        return 0;
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

    private static string FormatSpeed(double speed) => speed.ToString(speed % 1 == 0 ? "0x" : "0.##x", CultureInfo.InvariantCulture);

    private static string FormatNumber(double value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatNumber(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatSigned(long value) => value > 0 ? "+" + FormatNumber(value) : value.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatTime(double milliseconds)
    {
        var value = TimeSpan.FromMilliseconds(Math.Max(0d, milliseconds));
        return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture) : value.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _detailCancellation?.Cancel();
        _auraTimelineCancellation?.Cancel();
        Localization.LanguageChanged -= OnLanguageChanged;
        _controller.FrameChanged -= OnFrameChanged;
        if (_detailTask is not null)
        {
            try
            {
                await _detailTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_auraTimelineTask is not null)
        {
            try
            {
                await _auraTimelineTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _detailCancellation?.Dispose();
        _auraTimelineCancellation?.Dispose();
        await _controller.DisposeAsync().ConfigureAwait(false);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _displayTextRevision++;
        SceneName = DisplayContext.ResolveSceneName(_record.ScenePayload.Kind, _record.Snapshot.MapId, _record.ScenePayload.BossNpcCodes);
        WindowTitle = string.Format(CultureInfo.CurrentCulture, Localization["Playback_WindowTitleFormat"], SceneName);
        _timelineMarkersInitialized = false;
        _forceNextDetailProjection = true;
        ApplyFrame(_currentFrame, _controller.State);
    }
}

public sealed class PlaybackCombatantRowViewModel(UiFrameBatchService frameBatchService, int entityId) : FrameBatchedObservableObject(frameBatchService)
{
    public int EntityId { get; } = entityId;

    public string Name
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = string.Empty;

    public long DamageAmount
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public string DamageText
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = "0";

    public string DamagePerSecondText
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = "0";

    public string HealingText
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = "0";

    public string HealingPerSecondText
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = "0";

    public string HpText
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = string.Empty;

    public IReadOnlyList<ProgressSegment> HpSegments
    {
        get;
        private set => SetFrameProperty(ref field, value);
    } = EmptySegments;

    public PlaybackTimelineStrip Timeline
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = PlaybackTimelineStrip.Empty;

    public bool IsSelected
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public bool IsExpanded
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    private static readonly ProgressSegment[] EmptySegments = [];

    public void UpdateHpSegments(double ratio, IBrush brush)
    {
        var resolvedRatio = Math.Clamp(ratio, 0d, 1d);
        if (resolvedRatio <= 0)
        {
            if (HpSegments.Count != 0)
                HpSegments = EmptySegments;
            return;
        }

        var segments = HpSegments;
        if (segments.Count == 1 && Math.Abs(segments[0].Ratio - resolvedRatio) <= 0.000_001 && ReferenceEquals(segments[0].Brush, brush))
            return;

        HpSegments = [new ProgressSegment(resolvedRatio, brush)];
    }
}

public sealed class PlaybackEventRowViewModel(UiFrameBatchService frameBatchService, long observationOrdinal) : FrameBatchedObservableObject(frameBatchService)
{
    public long ObservationOrdinal { get; } = observationOrdinal;

    public long DisplayTextRevision { get; set; } = -1;

    public long PositionMilliseconds
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public string TimeText
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = string.Empty;

    public string TrackText
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = string.Empty;

    public string SourceText
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = string.Empty;

    public string TargetText
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = string.Empty;

    public string SkillText
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = string.Empty;

    public string AmountText
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = string.Empty;
}

internal readonly record struct AuraTimelineDisplayKey(uint DisplayResourceEffectRefRaw, int InstanceSequenceId)
{
    public static AuraTimelineDisplayKey Create(uint displayResourceEffectRefRaw, int instanceSequenceId)
        => displayResourceEffectRefRaw != 0
            ? new AuraTimelineDisplayKey(displayResourceEffectRefRaw, 0)
            : new AuraTimelineDisplayKey(0, instanceSequenceId);
}

internal sealed class AuraTimelineLaneBuilder(uint displayResourceEffectRefRaw, int instanceSequenceId)
{
    public uint DisplayResourceEffectRefRaw { get; } = displayResourceEffectRefRaw;

    public int InstanceSequenceId { get; } = instanceSequenceId;

    public List<(long StartMilliseconds, long EndMilliseconds)> Coverages { get; } = [];

    public List<(long PositionMilliseconds, ScenePlaybackLifecycleEventKind Kind)> Applications { get; } = [];
}

internal readonly record struct PlaybackTimelineBuildResult(PlaybackTimelineStrip Global, Dictionary<int, PlaybackTimelineStrip> Combatants);
