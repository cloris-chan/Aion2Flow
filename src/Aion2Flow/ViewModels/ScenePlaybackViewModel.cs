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
    private const int MaxEventWindowMarkers = 96;
    private const int MaxSkillTimelineEventKeys = 96;
    private const int MaxSkillTimelineMarkersPerTrack = 96;
    private const long EventWindowRadiusMilliseconds = 4_000;
    private const long StepMilliseconds = 1_000;
    private const long CombatantRefreshIntervalMilliseconds = 250;

    private readonly Lock _frameGate = new();
    private readonly ScenePlaybackController _controller;
    private readonly IScenePlaybackSource _source;
    private readonly ArchivedEncounterRecord _record;
    private readonly UiFrameBatchService _frameBatchService;
    private readonly Dictionary<int, ScenePlaybackResourceState> _resourceScratch = [];
    private readonly HashSet<int> _seenCombatantIds = [];
    private readonly HashSet<long> _eventWindowOrdinals = [];
    private readonly Dictionary<SkillBaseKey, IReadOnlyList<PlaybackTimelineMarker>> _skillTimelineMarkersByBaseKey = [];
    private CancellationTokenSource? _detailCancellation;
    private CancellationTokenSource? _skillTimelineCancellation;
    private CancellationTokenSource? _auraTimelineCancellation;
    private Task? _detailTask;
    private Task? _skillTimelineTask;
    private Task? _auraTimelineTask;
    private ScenePlaybackFrame _currentFrame;
    private ScenePlaybackFrameChangedEventArgs? _pendingFrameChanged;
    private PlaybackTimelineStrip _globalTimeline = PlaybackTimelineStrip.Empty;
    private Dictionary<int, PlaybackTimelineStrip> _combatantTimelines = [];
    private IReadOnlyList<PlaybackAuraTimelineLane> _auraTimelineTracks = [];
    private double _timelineMarkerDuration = -1;
    private long _lastCombatantRefreshTick;
    private long _lastEventWindowEndObservationOrdinal = long.MinValue;
    private long _displayTextRevision;
    private bool _isApplyingFrame;
    private bool _isDisposed;
    private bool _frameApplyQueued;
    private bool _forceNextCombatantRefresh = true;
    private bool _detailRefreshQueued;
    private bool _detailRequestPending;
    private bool _timelineMarkersInitialized;
    private long _detailRequestGeneration;
    private long _skillTimelineRequestGeneration;

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

    public bool IsAuraTimelineVisible => SelectedCombatantId > 0 && AuraTimelineTracks.Count > 0;

    public bool HasOutgoingDamageTimelineRows => OutgoingDamageTimelineRows.Count > 0;

    public bool HasOutgoingHealingTimelineRows => OutgoingHealingTimelineRows.Count > 0;

    public bool HasOutgoingShieldTimelineRows => OutgoingShieldTimelineRows.Count > 0;

    public bool HasIncomingDamageTimelineRows => IncomingDamageTimelineRows.Count > 0;

    public bool HasIncomingHealingTimelineRows => IncomingHealingTimelineRows.Count > 0;

    public bool HasIncomingShieldTimelineRows => IncomingShieldTimelineRows.Count > 0;

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
    public partial PlaybackTimelineStrip GlobalTimeline { get; set; } = PlaybackTimelineStrip.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuraTimelineVisible))]
    public partial IReadOnlyList<PlaybackAuraTimelineLane> AuraTimelineTracks { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutgoingDamageTimelineRows))]
    public partial IReadOnlyList<PlaybackSkillTimelineLane> OutgoingDamageTimelineRows { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutgoingHealingTimelineRows))]
    public partial IReadOnlyList<PlaybackSkillTimelineLane> OutgoingHealingTimelineRows { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutgoingShieldTimelineRows))]
    public partial IReadOnlyList<PlaybackSkillTimelineLane> OutgoingShieldTimelineRows { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIncomingDamageTimelineRows))]
    public partial IReadOnlyList<PlaybackSkillTimelineLane> IncomingDamageTimelineRows { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIncomingHealingTimelineRows))]
    public partial IReadOnlyList<PlaybackSkillTimelineLane> IncomingHealingTimelineRows { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIncomingShieldTimelineRows))]
    public partial IReadOnlyList<PlaybackSkillTimelineLane> IncomingShieldTimelineRows { get; set; } = [];

    [ObservableProperty]
    public partial string SelectedCombatantName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedCombatantDamageText { get; set; } = "0";

    [ObservableProperty]
    public partial string SelectedCombatantDpsText { get; set; } = "0";

    [ObservableProperty]
    public partial string SelectedCombatantHealingText { get; set; } = "0";

    [ObservableProperty]
    public partial string SelectedCombatantHpText { get; set; } = string.Empty;

    public KeyedObservableCollection<int, PlaybackCombatantRowViewModel> Combatants { get; } = new(static row => row.EntityId)
    {
        ResetThreshold = 256
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuraTimelineVisible))]
    public partial int SelectedCombatantId { get; set; }

    [ObservableProperty]
    public partial int ExpandedCombatantId { get; set; }

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
        _skillTimelineCancellation?.Cancel();
        _auraTimelineCancellation?.Cancel();
        _detailRequestGeneration++;
        _skillTimelineRequestGeneration++;
        _skillTimelineMarkersByBaseKey.Clear();
        ClearPlaybackDetailTimelineRows();
        _auraTimelineTracks = [];
        AuraTimelineTracks = [];
        if (value <= 0)
        {
            ApplyCombatantState(value, ExpandedCombatantId);
            ClearSelectedCombatantSummary();
            CombatantDetails.Deactivate();
            return;
        }

        _forceNextCombatantRefresh = true;
        _detailRefreshQueued = true;
        ApplyCombatantState(value, ExpandedCombatantId);
        RefreshSelectedCombatantSummary();
        RequestSkillTimeline(value);
        RequestAuraTimeline(value);
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
            if (ExpandedCombatantId != 0 && ExpandedCombatantId != combatant.EntityId)
                ExpandedCombatantId = 0;
            SelectedCombatantId = combatant.EntityId;
        }
    }

    public void ToggleCombatantExpansion(PlaybackCombatantRowViewModel combatant)
    {
        if (combatant.EntityId <= 0)
            return;

        SelectedCombatantId = combatant.EntityId;
        ExpandedCombatantId = ExpandedCombatantId == combatant.EntityId ? 0 : combatant.EntityId;
    }

    public void RequestSeek(double positionMilliseconds)
    {
        if (_isDisposed)
            return;

        var duration = DurationMilliseconds;
        var target = duration > 0 ? Math.Clamp(positionMilliseconds, 0d, duration) : Math.Max(0d, positionMilliseconds);
        _forceNextCombatantRefresh = true;
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
        _forceNextCombatantRefresh = true;
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
            _forceNextCombatantRefresh = true;
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

        if (!ShouldRefreshCombatants(frame, state))
            return;

        RefreshCombatants(frame);
        RefreshSelectedCombatantSummary();
        RequestCombatantDetail(frame);
        _lastCombatantRefreshTick = Environment.TickCount64;
        _forceNextCombatantRefresh = false;
    }

    private bool ShouldRefreshCombatants(ScenePlaybackFrame frame, ScenePlaybackControllerState state)
    {
        if (_forceNextCombatantRefresh || !state.IsPlaying)
            return true;

        if (_lastCombatantRefreshTick == 0)
            return true;

        if (frame.TimeRange.DurationMilliseconds > 0 && frame.PositionMilliseconds >= frame.TimeRange.DurationMilliseconds)
            return true;

        return Environment.TickCount64 - _lastCombatantRefreshTick >= CombatantRefreshIntervalMilliseconds;
    }

    private bool ShouldRefreshEventWindow(ScenePlaybackFrame frame, ScenePlaybackControllerState state)
    {
        if (_forceNextCombatantRefresh || !state.IsPlaying)
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
        row.UpdateHpSegments(CreateHpRatio(in resource), ScenePlaybackTimelineBuilder.ResourceBrush);
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

    private void RefreshSelectedCombatantSummary()
    {
        var combatantId = SelectedCombatantId;
        if (combatantId <= 0 || !Combatants.TryGetValue(combatantId, out var combatant))
        {
            ClearSelectedCombatantSummary();
            return;
        }

        SelectedCombatantName = combatant.Name;
        SelectedCombatantDamageText = combatant.DamageText;
        SelectedCombatantDpsText = combatant.DamagePerSecondText;
        SelectedCombatantHealingText = combatant.HealingText;
        SelectedCombatantHpText = combatant.HpText;
    }

    private void ClearSelectedCombatantSummary()
    {
        SelectedCombatantName = string.Empty;
        SelectedCombatantDamageText = "0";
        SelectedCombatantDpsText = "0";
        SelectedCombatantHealingText = "0";
        SelectedCombatantHpText = string.Empty;
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
                RefreshPlaybackDetailTimelineRows();
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
            if (SelectedCombatantId > 0)
            {
                RequestSkillTimeline(SelectedCombatantId);
                RequestAuraTimeline(SelectedCombatantId);
            }
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
        => ScenePlaybackTimelineBuilder.BuildTimelineStrips(_controller.CreateTimelineSegment(), frame.TimeRange.DurationMilliseconds, CreateMarkerText);

    private void RequestSkillTimeline(int combatantId)
    {
        _skillTimelineCancellation?.Cancel();
        _skillTimelineCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _skillTimelineCancellation = cancellation;
        var generation = ++_skillTimelineRequestGeneration;
        var segment = _controller.CreateTimelineSegment();
        var duration = (long)Math.Round(DurationMilliseconds, MidpointRounding.AwayFromZero);
        _skillTimelineTask = ProjectSkillTimelineAsync(combatantId, segment, duration, generation, cancellation);
    }

    private async Task ProjectSkillTimelineAsync(int combatantId, SceneJournalSegment segment, long durationMilliseconds, long generation, CancellationTokenSource cancellation)
    {
        try
        {
            var read = await Task.Run(
                () => ScenePlaybackTrackReader.ReadCombatSkillSampled(segment, combatantId, 0, durationMilliseconds, MaxSkillTimelineEventKeys, MaxSkillTimelineMarkersPerTrack),
                cancellation.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_isDisposed ||
                    cancellation.IsCancellationRequested ||
                    generation != _skillTimelineRequestGeneration ||
                    combatantId != SelectedCombatantId)
                {
                    return;
                }

                RebuildSkillTimelineMarkerMap(read);
                RefreshPlaybackDetailTimelineRows();
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
                if (!ReferenceEquals(_skillTimelineCancellation, cancellation))
                    return;

                cancellation.Dispose();
                _skillTimelineCancellation = null;
                _skillTimelineTask = null;
            });
        }
    }

    private void RebuildSkillTimelineMarkerMap(ScenePlaybackCombatSkillSampledReadResult read)
    {
        _skillTimelineMarkersByBaseKey.Clear();
        for (var skillIndex = 0; skillIndex < read.Skills.Count; skillIndex++)
        {
            var skill = read.Skills[skillIndex];
            var key = SkillBaseKey.FromEventKey(skill.EventKey);
            var accent = ScenePlaybackTimelineBuilder.ResolveSkillBrush(key);
            var markers = new PlaybackTimelineMarker[skill.Samples.Count];
            for (var sampleIndex = 0; sampleIndex < skill.Samples.Count; sampleIndex++)
            {
                var sample = skill.Samples[sampleIndex];
                var marker = sample.Marker;
                markers[sampleIndex] = new PlaybackTimelineMarker(
                    marker.PositionMilliseconds,
                    ResolveSkillMarkerWeight(marker.Amount, sample.EventCount),
                    accent,
                    CreateSkillMarkerText(marker));
            }

            if (_skillTimelineMarkersByBaseKey.TryGetValue(key, out var existing))
                _skillTimelineMarkersByBaseKey[key] = MergeTimelineMarkers(existing, markers);
            else
                _skillTimelineMarkersByBaseKey[key] = markers;
        }
    }

    private static PlaybackTimelineMarker[] MergeTimelineMarkers(IReadOnlyList<PlaybackTimelineMarker> existing, IReadOnlyList<PlaybackTimelineMarker> added)
    {
        var merged = new PlaybackTimelineMarker[existing.Count + added.Count];
        var index = 0;
        for (var i = 0; i < existing.Count; i++)
            merged[index++] = existing[i];
        for (var i = 0; i < added.Count; i++)
            merged[index++] = added[i];

        Array.Sort(merged, static (left, right) => left.PositionMilliseconds.CompareTo(right.PositionMilliseconds));
        return merged;
    }

    private void RefreshPlaybackDetailTimelineRows()
    {
        OutgoingDamageTimelineRows = CreatePlaybackDetailTimelineRows(CombatantDetails.OutgoingDamage);
        OutgoingHealingTimelineRows = CreatePlaybackDetailTimelineRows(CombatantDetails.OutgoingHealing);
        OutgoingShieldTimelineRows = CreatePlaybackDetailTimelineRows(CombatantDetails.OutgoingShield);
        IncomingDamageTimelineRows = CreatePlaybackDetailTimelineRows(CombatantDetails.IncomingDamage);
        IncomingHealingTimelineRows = CreatePlaybackDetailTimelineRows(CombatantDetails.IncomingHealing);
        IncomingShieldTimelineRows = CreatePlaybackDetailTimelineRows(CombatantDetails.IncomingShield);
    }

    private void ClearPlaybackDetailTimelineRows()
    {
        OutgoingDamageTimelineRows = [];
        OutgoingHealingTimelineRows = [];
        OutgoingShieldTimelineRows = [];
        IncomingDamageTimelineRows = [];
        IncomingHealingTimelineRows = [];
        IncomingShieldTimelineRows = [];
    }

    private PlaybackSkillTimelineLane[] CreatePlaybackDetailTimelineRows(SkillDetailSectionViewModel section)
    {
        if (section.Rows.Count == 0)
            return [];

        var rows = new PlaybackSkillTimelineLane[section.Rows.Count];
        var durationSeconds = ResolveDetailDurationSeconds(section);
        for (var i = 0; i < section.Rows.Count; i++)
        {
            var row = section.Rows[i];
            var markers = ResolveSkillTimelineMarkers(row.BaseKey, row.SkillCode);
            rows[i] = new PlaybackSkillTimelineLane(
                row.SkillCode,
                row.DisplayName,
                markers,
                row.EventCount,
                FormatNumber(row.TotalAmount),
                FormatNumber(durationSeconds > 0 ? row.TotalAmount / durationSeconds : 0d),
                CreateDirectText(row),
                CreatePeriodicText(row),
                CreateCountRateText(row.Hits, row.Attempts > 0 ? row.Hits / (double)row.Attempts : 0d),
                CreateCountRateText(row.Criticals, row.CriticalRate),
                CreateCountRateText(row.Perfect, row.PerfectRate),
                CreateCountRateText(row.Smite, row.SmiteRate),
                CreateCountRateText(row.MultiHit, row.MultiHitRate),
                CreateDirectionText(row),
                CreateAvoidanceText(row),
                CreateGuardText(row),
                row.SharePercent.ToString("P1", CultureInfo.CurrentCulture));
        }

        return rows;
    }

    private IReadOnlyList<PlaybackTimelineMarker> ResolveSkillTimelineMarkers(SkillBaseKey baseKey, int skillCode)
    {
        if (_skillTimelineMarkersByBaseKey.TryGetValue(baseKey, out var markers))
            return markers;

        if (skillCode > 0)
        {
            var key = SkillBaseKey.FromEventKey(new CombatEventKey(skillCode, default, default));
            if (_skillTimelineMarkersByBaseKey.TryGetValue(key, out markers))
                return markers;
        }

        return [];
    }

    private double ResolveDetailDurationSeconds(SkillDetailSectionViewModel section)
    {
        if (section.DurationSeconds > 0)
            return section.DurationSeconds;

        return PositionMilliseconds > 0 ? PositionMilliseconds / 1_000d : 0d;
    }

    private static double ResolveSkillMarkerWeight(long amount, int eventCount)
    {
        var magnitude = amount >= 0 ? (double)amount : -(double)(amount + 1) + 1d;
        var baseWeight = magnitude > 0
            ? Math.Clamp(Math.Log10(magnitude + 1) * 2.2d, 3d, 12d)
            : 5d;
        return Math.Clamp(baseWeight + Math.Log2(Math.Max(1, eventCount)) * 0.75d, 3d, 12d);
    }

    private static string CreateCountRateText(int count, double rate)
        => count > 0 ? $"{count.ToString("N0", CultureInfo.CurrentCulture)} / {rate.ToString("P1", CultureInfo.CurrentCulture)}" : "0";

    private static string CreatePeriodicText(SkillDetailRowViewModel row)
    {
        var auxiliary = row.PeriodicAmount + row.DrainAmount + row.RegenerationAmount + row.ShieldAbsorbedAmount;
        return auxiliary > 0 ? FormatNumber(auxiliary) : "0";
    }

    private static string CreateDirectText(SkillDetailRowViewModel row)
    {
        if (row.DirectAmount > 0)
            return FormatNumber(row.DirectAmount);

        return row.ShieldAmount > 0 ? FormatNumber(row.ShieldAmount) : "0";
    }

    private static string CreateDirectionText(SkillDetailRowViewModel row)
    {
        if (row.Front <= 0 && row.Back <= 0)
            return "0";

        return $"{row.Front.ToString("N0", CultureInfo.CurrentCulture)} / {row.Back.ToString("N0", CultureInfo.CurrentCulture)}";
    }

    private static string CreateAvoidanceText(SkillDetailRowViewModel row)
    {
        var count = row.Evades + row.Invincible + row.Endurance + row.Regeneration;
        if (count <= 0)
            return "0";

        return $"{count.ToString("N0", CultureInfo.CurrentCulture)} / {((row.EvadeRate + row.InvincibleRate + row.EnduranceRate + row.RegenerationRate) / 4d).ToString("P1", CultureInfo.CurrentCulture)}";
    }

    private static string CreateGuardText(SkillDetailRowViewModel row)
    {
        var count = row.Parry + row.PerfectParry + row.Block + row.PerfectBlock;
        if (count <= 0)
            return "0";

        return count.ToString("N0", CultureInfo.CurrentCulture);
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

                _auraTimelineTracks = ScenePlaybackTimelineBuilder.BuildAuraTimelineTracks(timeline, durationMilliseconds, Localization, DisplayContext);
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

    private static List<ScenePlaybackTrackMarker> FilterRecentMarkers(IReadOnlyList<ScenePlaybackTrackMarker> markers, long start, long end)
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
        row.TimelineMarkers =
        [
            new PlaybackTimelineMarker(
                marker.PositionMilliseconds,
                8d,
                ScenePlaybackTimelineBuilder.ResolveTrackBrush(marker.Track),
                CreateMarkerText(marker))
        ];
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

    private string CreateSkillMarkerText(ScenePlaybackCombatSkillMarker marker)
    {
        var amount = FormatSigned(marker.Amount);
        var skillName = ResolveCombatEventDisplayName(marker.EventKey);
        if (!string.IsNullOrWhiteSpace(skillName))
            return $"{FormatTime(marker.PositionMilliseconds)} {skillName} {amount}";
        return $"{FormatTime(marker.PositionMilliseconds)} {amount}";
    }

    private string ResolveCombatEventDisplayName(CombatEventKey eventKey)
    {
        if (eventKey.SkillCode > 0)
            return DisplayContext.ResolveSkillName(eventKey.SkillCode);

        var bodyName = DisplayContext.ResolveSkillName(eventKey.BodyResourceEffectRef);
        if (!string.IsNullOrWhiteSpace(bodyName))
            return bodyName;

        var detailName = DisplayContext.ResolveSkillName(eventKey.DetailResourceEffectRef);
        if (!string.IsNullOrWhiteSpace(detailName))
            return detailName;

        return eventKey.FormatFallbackLabel(Localization["Skill_UnknownEffect"]);
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
        _skillTimelineCancellation?.Cancel();
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

        if (_skillTimelineTask is not null)
        {
            try
            {
                await _skillTimelineTask.ConfigureAwait(false);
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
        _skillTimelineCancellation?.Dispose();
        _auraTimelineCancellation?.Dispose();
        await _controller.DisposeAsync().ConfigureAwait(false);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _displayTextRevision++;
        SceneName = DisplayContext.ResolveSceneName(_record.ScenePayload.Kind, _record.Snapshot.MapId, _record.ScenePayload.BossNpcCodes);
        WindowTitle = string.Format(CultureInfo.CurrentCulture, Localization["Playback_WindowTitleFormat"], SceneName);
        _timelineMarkersInitialized = false;
        _forceNextCombatantRefresh = true;
        if (SelectedCombatantId > 0)
        {
            _detailRefreshQueued = true;
            if (!_detailRequestPending)
                RequestCombatantDetail(_currentFrame);

            RequestSkillTimeline(SelectedCombatantId);
            RequestAuraTimeline(SelectedCombatantId);
        }

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

    public IReadOnlyList<PlaybackTimelineMarker> TimelineMarkers
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = [];
}
