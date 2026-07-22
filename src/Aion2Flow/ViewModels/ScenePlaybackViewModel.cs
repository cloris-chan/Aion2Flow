using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using Cloris.Aion2Flow.Collections;
using Cloris.Aion2Flow.Presentation;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Playback;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class ScenePlaybackViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxEventWindowMarkers = 96;
    private const long EventWindowRadiusMilliseconds = 4_000;
    private const long EventWindowRefreshIntervalMilliseconds = 100;
    private const long StepMilliseconds = 1_000;
    private const long CombatantRefreshIntervalMilliseconds = 250;
    private const long LiveRefreshIntervalMilliseconds = 250;
    private const long MinimumLiveIndexGrowth = 1;
    private const double MinimumTimelineViewportMilliseconds = 1_000d;
    private const double TimelineZoomStep = 2d;

    private readonly Lock _frameGate = new();
    private readonly ScenePlaybackController _controller;
    private readonly PlaybackSeekCoordinator _seekCoordinator;
    private readonly IScenePlaybackSource _source;
    private SceneCombatSnapshot _sceneDescriptorSnapshot;
    private readonly UiFrameBatchService _frameBatchService;
    private readonly Dictionary<int, EntityVitalState> _vitalScratch = [];
    private readonly HashSet<int> _seenCombatantIds = [];
    private readonly HashSet<ScenePlaybackEventId> _eventWindowIds = [];
    private readonly ScenePlaybackEventMarker[] _materializedEventBuffer = new ScenePlaybackEventMarker[MaxEventWindowMarkers];
    private readonly ScenePlaybackEventMarker[] _nonCombatEventBuffer = new ScenePlaybackEventMarker[MaxEventWindowMarkers];
    private readonly ScenePlaybackEventMarker[] _eventMarkerBuffer = new ScenePlaybackEventMarker[MaxEventWindowMarkers];
    private string _eventScopeSkillText = string.Empty;
    private CancellationTokenSource? _detailCancellation;
    private CancellationTokenSource? _eventIndexCancellation;
    private CancellationTokenSource? _eventWindowCancellation;
    private CancellationTokenSource? _timelineProjectionCancellation;
    private CancellationTokenSource? _auraTimelineCancellation;
    private CancellationTokenSource? _liveRefreshCancellation;
    private Task? _detailTask;
    private Task? _eventIndexTask;
    private Task? _eventWindowTask;
    private Task? _timelineProjectionTask;
    private Task? _auraTimelineTask;
    private Task? _liveRefreshTask;
    private ScenePlaybackFrame _currentFrame;
    private ScenePlaybackTrackIndex? _eventIndex;
    private ScenePlaybackFrameChangedEventArgs? _pendingFrameChanged;
    private PlaybackTimelineStrip _globalTimeline = PlaybackTimelineStrip.Empty;
    private Dictionary<int, PlaybackTimelineStrip> _combatantTimelines = [];
    private IReadOnlyList<PlaybackAuraTimelineLane> _auraTimelineTracks = [];
    private double _timelineMarkerDuration = -1;
    private long _lastCombatantRefreshTick;
    private long _lastEventWindowRefreshTick;
    private long _lastLiveRefreshTimestampTicks = long.MinValue;
    private long _lastLivePresentationRefreshTick;
    private long _displayTextRevision;
    private bool _isApplyingFrame;
    private bool _isDisposed;
    private bool _frameApplyQueued;
    private bool _forceNextCombatantRefresh = true;
    private bool _forceNextEventWindowRefresh = true;
    private bool _detailRefreshQueued;
    private bool _detailRequestPending;
    private bool _eventWindowRefreshQueued;
    private bool _hasEventWindowRows;
    private bool _timelineMarkersInitialized;
    private bool _timelineProjectionPending;
    private bool _scrubRefreshPending;
    private bool _eventIndexRefreshQueued;
    private bool _liveSourceFinalized;
    private bool _liveFinalizationApplied;
    private bool _lastAuraTimelineWasGrowing;
    private long _detailRequestGeneration;
    private long _timelineProjectionGeneration;
    private long _lastAuraTimelineEndObservationOrdinalExclusive;

    public ScenePlaybackViewModel(IScenePlaybackSource source, SceneDisplayContext displayContext, LocalizationService localization)
        : this(source, displayContext, localization, Ioc.Default.GetRequiredService<IScenePlaybackTickSourceFactory>())
    {
    }

    internal ScenePlaybackViewModel(IScenePlaybackSource source, SceneDisplayContext displayContext, LocalizationService localization, IScenePlaybackTickSourceFactory tickSourceFactory)
        : this(source, displayContext, localization, tickSourceFactory, Ioc.Default.GetRequiredService<UiFrameBatchService>())
    {
    }

    internal ScenePlaybackViewModel(IScenePlaybackSource source, SceneDisplayContext displayContext, LocalizationService localization, IScenePlaybackTickSourceFactory tickSourceFactory, UiFrameBatchService frameBatchService)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(displayContext);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(tickSourceFactory);
        ArgumentNullException.ThrowIfNull(frameBatchService);
        _source = source;
        _sceneDescriptorSnapshot = source.CreateSnapshot();
        _frameBatchService = frameBatchService;
        DisplayContext = displayContext;
        Localization = localization;
        CombatantDetails = new CombatantDetailsFlyoutViewModel(localization, frameBatchService)
        {
            DisplayContext = displayContext
        };
        _controller = new ScenePlaybackController(_source, tickSourceFactory, ScenePlaybackControllerOptions.Default);
        _seekCoordinator = new PlaybackSeekCoordinator(SeekCoreAsync, ReportSeekError, ReportSeekIdle);
        _controller.FrameChanged += OnFrameChanged;
        Localization.LanguageChanged += OnLanguageChanged;
        SceneName = displayContext.ResolveSceneName(_sceneDescriptorSnapshot.Kind, _sceneDescriptorSnapshot.MapId, _sceneDescriptorSnapshot.BossNpcCodes);
        WindowTitle = string.Format(CultureInfo.CurrentCulture, Localization["Playback_WindowTitleFormat"], SceneName);
        SceneStartedText = source.SceneStarted.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        _currentFrame = _controller.CurrentFrame;
        RequestEventIndex();
        ApplyFrame(_currentFrame, _controller.State);
    }

    public SceneDisplayContext DisplayContext { get; private set; }

    public LocalizationService Localization { get; }

    public CombatantDetailsFlyoutViewModel CombatantDetails { get; }

    public int SelectedCombatantId => SelectedCombatant?.EntityId ?? 0;

    public bool HasSelectedCombatant => SelectedCombatant is not null;

    public bool IsOutgoingDetailSelected => SelectedDetailMode == PlaybackDetailMode.Outgoing;

    public bool IsIncomingDetailSelected => SelectedDetailMode == PlaybackDetailMode.Incoming;

    public bool IsAurasDetailSelected => SelectedDetailMode == PlaybackDetailMode.Auras;

    public bool IsOutgoingDetailVisible => HasSelectedCombatant && IsOutgoingDetailSelected;

    public bool IsIncomingDetailVisible => HasSelectedCombatant && IsIncomingDetailSelected;

    public bool IsAurasDetailVisible => HasSelectedCombatant && IsAurasDetailSelected;

    public bool IsHalfSpeed => Math.Abs(Speed - 0.5d) <= double.Epsilon;

    public bool IsNormalSpeed => Math.Abs(Speed - 1d) <= double.Epsilon;

    public bool IsDoubleSpeed => Math.Abs(Speed - 2d) <= double.Epsilon;

    public bool IsQuadrupleSpeed => Math.Abs(Speed - 4d) <= double.Epsilon;

    public bool HasAuraTimelineRows => AuraTimelineTracks.Count > 0;

    public bool IsAurasDetailEmpty => IsAurasDetailVisible && !HasAuraTimelineRows;

    public bool IsEventsWorkspaceEmpty => !HasEventWindowRows;

    public bool IsEventScopeAll => !EventSelection.HasCombatant;

    public bool HasEventScopeCombatant => EventSelection.HasCombatant;

    public bool HasEventScopeRelation => EventSelection.HasRelation;

    public bool HasEventScopeCategory => EventSelection.HasCategory;

    public bool HasEventScopeSkill => EventSelection.HasSkill;

    public string EventScopeCombatantText => EventSelection.HasCombatant
        ? DisplayContext.ResolveEntityName(EventSelection.CombatantId)
        : string.Empty;

    public string EventScopeRelationText => EventSelection.Relation switch
    {
        ScenePlaybackEventRelation.Outgoing => Localization["Direction_Outgoing"],
        ScenePlaybackEventRelation.Incoming => Localization["Direction_Incoming"],
        ScenePlaybackEventRelation.Aura => Localization["Playback_AuraCoverage"],
        _ => string.Empty
    };

    public string EventScopeCategoryText => EventSelection.Category switch
    {
        CombatContributionCategory.Damage => Localization["Category_Damage"],
        CombatContributionCategory.Healing => Localization["Category_Healing"],
        CombatContributionCategory.Shield => Localization["Category_Shield"],
        _ => string.Empty
    };

    public string EventScopeSkillText => _eventScopeSkillText;

    public bool HasEventWindowRows
    {
        get => _hasEventWindowRows;
        private set
        {
            if (SetProperty(ref _hasEventWindowRows, value))
                OnPropertyChanged(nameof(IsEventsWorkspaceEmpty));
        }
    }

    [ObservableProperty]
    public partial string WindowTitle { get; set; } = "Playback";

    [ObservableProperty]
    public partial string SceneStartedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SceneName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double PositionMilliseconds { get; set; }

    [ObservableProperty]
    public partial double DurationMilliseconds { get; set; }

    [ObservableProperty]
    public partial PlaybackTimelineViewport TimelineViewport { get; set; } = PlaybackTimelineViewport.Empty;

    [ObservableProperty]
    public partial string TimelineZoomText { get; set; } = "1x";

    [ObservableProperty]
    public partial string PositionText { get; set; } = "00:00.000";

    [ObservableProperty]
    public partial string DurationText { get; set; } = "00:00.000";

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHalfSpeed))]
    [NotifyPropertyChangedFor(nameof(IsNormalSpeed))]
    [NotifyPropertyChangedFor(nameof(IsDoubleSpeed))]
    [NotifyPropertyChangedFor(nameof(IsQuadrupleSpeed))]
    public partial double Speed { get; set; } = 1d;

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial PlaybackTimelineStrip GlobalTimeline { get; set; } = PlaybackTimelineStrip.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuraTimelineRows))]
    [NotifyPropertyChangedFor(nameof(IsAurasDetailEmpty))]
    public partial IReadOnlyList<PlaybackAuraTimelineLane> AuraTimelineTracks { get; set; } = [];

    [ObservableProperty]
    public partial PlaybackAuraTimelineLane? SelectedAura { get; set; }

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
    [NotifyPropertyChangedFor(nameof(SelectedCombatantId))]
    [NotifyPropertyChangedFor(nameof(HasSelectedCombatant))]
    [NotifyPropertyChangedFor(nameof(IsOutgoingDetailVisible))]
    [NotifyPropertyChangedFor(nameof(IsIncomingDetailVisible))]
    [NotifyPropertyChangedFor(nameof(IsAurasDetailVisible))]
    [NotifyPropertyChangedFor(nameof(IsAurasDetailEmpty))]
    public partial PlaybackCombatantRowViewModel? SelectedCombatant { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOutgoingDetailSelected))]
    [NotifyPropertyChangedFor(nameof(IsIncomingDetailSelected))]
    [NotifyPropertyChangedFor(nameof(IsAurasDetailSelected))]
    [NotifyPropertyChangedFor(nameof(IsOutgoingDetailVisible))]
    [NotifyPropertyChangedFor(nameof(IsIncomingDetailVisible))]
    [NotifyPropertyChangedFor(nameof(IsAurasDetailVisible))]
    [NotifyPropertyChangedFor(nameof(IsAurasDetailEmpty))]
    public partial PlaybackDetailMode SelectedDetailMode { get; set; } = PlaybackDetailMode.Outgoing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEventScopeAll))]
    [NotifyPropertyChangedFor(nameof(HasEventScopeCombatant))]
    [NotifyPropertyChangedFor(nameof(HasEventScopeRelation))]
    [NotifyPropertyChangedFor(nameof(HasEventScopeCategory))]
    [NotifyPropertyChangedFor(nameof(HasEventScopeSkill))]
    [NotifyPropertyChangedFor(nameof(EventScopeCombatantText))]
    [NotifyPropertyChangedFor(nameof(EventScopeRelationText))]
    [NotifyPropertyChangedFor(nameof(EventScopeCategoryText))]
    public partial ScenePlaybackEventScope EventSelection { get; set; } = ScenePlaybackEventScope.All;

    public KeyedObservableCollection<ScenePlaybackEventId, PlaybackEventRowViewModel> EventWindow { get; } = new(static row => row.EventId)
    {
        ResetThreshold = 256
    };

    partial void OnPositionMillisecondsChanged(double value)
    {
        PositionText = FormatTime(value);
        if (!_isApplyingFrame)
            RequestSeek(value);
    }

    partial void OnSelectedCombatantChanged(PlaybackCombatantRowViewModel? value)
    {
        var combatantId = value?.EntityId ?? 0;
        _detailCancellation?.Cancel();
        _auraTimelineCancellation?.Cancel();
        _detailRequestGeneration++;
        _auraTimelineTracks = [];
        _lastAuraTimelineEndObservationOrdinalExclusive = 0;
        _lastAuraTimelineWasGrowing = false;
        AuraTimelineTracks = [];
        SelectedAura = null;
        if (combatantId <= 0)
        {
            ClearSelectedCombatantSummary();
            CombatantDetails.Deactivate();
            SetEventSelection(ScenePlaybackEventScope.All);
            return;
        }

        _forceNextCombatantRefresh = true;
        _detailRefreshQueued = true;
        RefreshSelectedCombatantSummary();
        SetEventSelection(ScenePlaybackEventScope.ForCombatant(combatantId));
        RequestSelectedDetailData(combatantId);
    }

    partial void OnSelectedDetailModeChanged(PlaybackDetailMode value)
    {
        if (SelectedCombatantId > 0)
            RequestSelectedDetailData(SelectedCombatantId);
    }

    partial void OnSelectedAuraChanged(PlaybackAuraTimelineLane? value)
    {
        var combatantId = SelectedCombatantId;
        if (combatantId <= 0 || SelectedDetailMode != PlaybackDetailMode.Auras)
            return;

        if (value is null)
        {
            SetEventSelection(ScenePlaybackEventScope.ForRelation(combatantId, ScenePlaybackEventRelation.Aura));
            return;
        }

        SetEventSelection(
            ScenePlaybackEventScope.ForAura(combatantId, value.AuraIdentity),
            ResolveAuraDisplayName(value));
    }

    partial void OnEventSelectionChanged(ScenePlaybackEventScope value)
    {
        _forceNextEventWindowRefresh = true;
        RefreshVisibleEventWindow();
    }

    [RelayCommand]
    private void ShowOutgoingDetail()
    {
        SelectedDetailMode = PlaybackDetailMode.Outgoing;
        SelectEventRelation(ScenePlaybackEventRelation.Outgoing);
    }

    [RelayCommand]
    private void ShowIncomingDetail()
    {
        SelectedDetailMode = PlaybackDetailMode.Incoming;
        SelectEventRelation(ScenePlaybackEventRelation.Incoming);
    }

    [RelayCommand]
    private void ShowAurasDetail()
    {
        SelectedDetailMode = PlaybackDetailMode.Auras;
        SelectedAura = null;
        SelectEventRelation(ScenePlaybackEventRelation.Aura);
    }

    [RelayCommand]
    private void ShowAllEvents() => SetEventSelection(ScenePlaybackEventScope.All);

    [RelayCommand]
    private void ShowCombatantEvents()
    {
        if (EventSelection.CombatantId > 0)
            SetEventSelection(ScenePlaybackEventScope.ForCombatant(EventSelection.CombatantId));
    }

    [RelayCommand]
    private void ShowRelationEvents()
    {
        if (EventSelection.CombatantId > 0 && EventSelection.HasRelation)
            SetEventSelection(ScenePlaybackEventScope.ForRelation(EventSelection.CombatantId, EventSelection.Relation));
    }

    [RelayCommand]
    private void ShowCategoryEvents()
    {
        if (EventSelection.CombatantId > 0 && EventSelection.Category is { } category)
            SetEventSelection(ScenePlaybackEventScope.ForCategory(EventSelection.CombatantId, EventSelection.Relation, category));
    }

    public void SelectCombatDetail(CombatContributionCategory category, SkillBaseKey? skillBaseKey, string? skillDisplayName)
    {
        var combatantId = SelectedCombatantId;
        var relation = SelectedDetailMode switch
        {
            PlaybackDetailMode.Outgoing => ScenePlaybackEventRelation.Outgoing,
            PlaybackDetailMode.Incoming => ScenePlaybackEventRelation.Incoming,
            _ => ScenePlaybackEventRelation.All
        };
        if (combatantId <= 0 || relation == ScenePlaybackEventRelation.All)
            return;

        if (skillBaseKey is { } key)
        {
            SetEventSelection(
                ScenePlaybackEventScope.ForSkill(combatantId, relation, category, key),
                skillDisplayName);
            return;
        }

        SetEventSelection(ScenePlaybackEventScope.ForCategory(combatantId, relation, category));
    }

    private void SelectEventRelation(ScenePlaybackEventRelation relation)
    {
        var combatantId = SelectedCombatantId;
        if (combatantId > 0)
            SetEventSelection(ScenePlaybackEventScope.ForRelation(combatantId, relation));
    }

    private void SetEventSelection(ScenePlaybackEventScope selection, string? skillDisplayName = null)
    {
        if (selection.SkillBaseKey is { } baseKey &&
            selection.Category is { } category &&
            selection.Relation is ScenePlaybackEventRelation.Outgoing or ScenePlaybackEventRelation.Incoming)
        {
            CombatantDetails.SynchronizeSkillSelection(
                selection.Relation == ScenePlaybackEventRelation.Outgoing,
                category,
                baseKey);
        }
        else
        {
            CombatantDetails.ClearSkillSelection();
        }

        var displayName = skillDisplayName ?? string.Empty;
        if (!string.Equals(_eventScopeSkillText, displayName, StringComparison.Ordinal))
        {
            _eventScopeSkillText = displayName;
            OnPropertyChanged(nameof(EventScopeSkillText));
        }

        EventSelection = selection;
    }

    public void ProcessUiFrame(TimeSpan timestamp)
    {
        if (_isDisposed ||
            _source.SourceKind != ScenePlaybackSourceKind.Live ||
            _liveSourceFinalized ||
            IsPlaying ||
            IsLoading)
        {
            return;
        }

        var timestampTicks = timestamp.Ticks;
        if (_lastLiveRefreshTimestampTicks != long.MinValue &&
            timestampTicks >= _lastLiveRefreshTimestampTicks &&
            timestampTicks - _lastLiveRefreshTimestampTicks < LiveRefreshIntervalMilliseconds * TimeSpan.TicksPerMillisecond)
        {
            return;
        }

        _lastLiveRefreshTimestampTicks = timestampTicks;
        if (_liveRefreshTask is null)
        {
            var cancellation = new CancellationTokenSource();
            _liveRefreshCancellation = cancellation;
            _liveRefreshTask = RefreshLiveSourceAsync(cancellation);
        }
    }

    private async Task RefreshLiveSourceAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await _controller.RefreshAsync(cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException) when (_isDisposed)
        {
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_liveRefreshCancellation, cancellation))
            {
                _liveRefreshCancellation = null;
                _liveRefreshTask = null;
            }

            cancellation.Dispose();
        }
    }

    public void RequestSeek(double positionMilliseconds)
    {
        if (_isDisposed)
            return;

        var duration = DurationMilliseconds;
        var target = duration > 0 ? Math.Clamp(positionMilliseconds, 0d, duration) : Math.Max(0d, positionMilliseconds);
        _scrubRefreshPending = true;
        CancelScrubCompetingWork();
        if (EnsureTimelinePositionVisible(target, requestProjection: false))
            _timelineProjectionPending = true;
        _forceNextCombatantRefresh = true;
        _forceNextEventWindowRefresh = true;
        IsLoading = true;
        _seekCoordinator.Request((long)Math.Round(target, MidpointRounding.AwayFromZero));
    }

    public void ZoomTimelineAt(double factor, double anchorMilliseconds)
    {
        if (!double.IsFinite(factor) || factor <= 0d || DurationMilliseconds <= 0d)
            return;

        ResizeTimelineViewport(factor, Math.Clamp(anchorMilliseconds, 0d, DurationMilliseconds));
    }

    [RelayCommand]
    private void ZoomTimelineIn() => ResizeTimelineViewport(1d / TimelineZoomStep, PositionMilliseconds);

    [RelayCommand]
    private void ZoomTimelineOut() => ResizeTimelineViewport(TimelineZoomStep, PositionMilliseconds);

    [RelayCommand]
    private void ResetTimelineZoom() => SetTimelineViewport(new PlaybackTimelineViewport(0d, DurationMilliseconds));

    private void ResizeTimelineViewport(double factor, double anchorMilliseconds)
    {
        var totalDuration = DurationMilliseconds;
        if (totalDuration <= 0d)
            return;

        var current = TimelineViewport.IsEmpty
            ? new PlaybackTimelineViewport(0d, totalDuration)
            : TimelineViewport;
        var minimumDuration = Math.Min(MinimumTimelineViewportMilliseconds, totalDuration);
        var targetDuration = Math.Clamp(current.DurationMilliseconds * factor, minimumDuration, totalDuration);
        var anchorRatio = current.DurationMilliseconds > 0d
            ? Math.Clamp((anchorMilliseconds - current.StartMilliseconds) / current.DurationMilliseconds, 0d, 1d)
            : 0.5d;
        var start = Math.Clamp(anchorMilliseconds - targetDuration * anchorRatio, 0d, totalDuration - targetDuration);
        SetTimelineViewport(new PlaybackTimelineViewport(start, start + targetDuration));
    }

    private bool EnsureTimelinePositionVisible(double positionMilliseconds, bool requestProjection = true)
    {
        var viewport = TimelineViewport;
        var totalDuration = DurationMilliseconds;
        if (viewport.IsEmpty || totalDuration <= 0d || viewport.Contains(positionMilliseconds))
            return false;

        var viewportDuration = Math.Min(viewport.DurationMilliseconds, totalDuration);
        var start = Math.Clamp(positionMilliseconds - viewportDuration * 0.5d, 0d, totalDuration - viewportDuration);
        return SetTimelineViewport(new PlaybackTimelineViewport(start, start + viewportDuration), requestProjection);
    }

    private bool SetTimelineViewport(PlaybackTimelineViewport viewport, bool requestProjection = true)
    {
        var changed = TimelineViewport != viewport;
        TimelineViewport = viewport;
        var totalDuration = DurationMilliseconds;
        var zoom = totalDuration > 0d && viewport.DurationMilliseconds > 0d
            ? totalDuration / viewport.DurationMilliseconds
            : 1d;
        TimelineZoomText = $"{zoom:0.#}x";
        if (!changed)
            return false;

        if (requestProjection)
            RequestTimelineProjection();
        return true;
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
        _scrubRefreshPending = false;
        CancelScrubCompetingWork();
        _seekCoordinator.CancelPending();
        _forceNextCombatantRefresh = true;
        _forceNextEventWindowRefresh = true;
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

    private async ValueTask SeekCoreAsync(long positionMilliseconds, CancellationToken cancellationToken)
        => await _controller.SeekCoalescedAsync(positionMilliseconds, cancellationToken).ConfigureAwait(false);

    private void ReportSeekError(Exception exception)
        => Dispatcher.UIThread.Post(() =>
        {
            if (!_isDisposed)
                StatusText = exception.Message;
        });

    private void ReportSeekIdle()
        => Dispatcher.UIThread.Post(() =>
        {
            if (_isDisposed)
                return;

            if (_seekCoordinator.IsBusy || _controller.IsLoading)
            {
                IsLoading = true;
                return;
            }

            var refreshAfterScrub = _scrubRefreshPending;
            _scrubRefreshPending = false;
            IsLoading = false;
            if (!refreshAfterScrub)
                return;

            _forceNextCombatantRefresh = true;
            _forceNextEventWindowRefresh = true;
            ApplyFrame(_controller.CurrentFrame, _controller.State);
        });

    private async Task StepEventAsync(int direction)
    {
        try
        {
            _scrubRefreshPending = false;
            CancelScrubCompetingWork();
            _seekCoordinator.CancelPending();
            _forceNextCombatantRefresh = true;
            _forceNextEventWindowRefresh = true;
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

        var scrubActive = _scrubRefreshPending || _seekCoordinator.IsBusy || _controller.IsLoading || state.IsLoading;
        _currentFrame = frame;
        var duration = frame.TimeRange.DurationMilliseconds;
        var previousDuration = DurationMilliseconds;
        var previousViewport = TimelineViewport;
        var timelineViewportChanged = scrubActive ? false : _timelineProjectionPending;
        if (!scrubActive)
            _timelineProjectionPending = false;
        _isApplyingFrame = true;
        PositionMilliseconds = frame.PositionMilliseconds;
        if (Math.Abs(previousDuration - duration) > double.Epsilon)
        {
            DurationMilliseconds = duration;
            DurationText = FormatTime(duration);
            timelineViewportChanged = SetTimelineViewport(
                ResolveViewportAfterDurationChange(previousViewport, previousDuration, duration),
                requestProjection: false);
        }
        timelineViewportChanged |= EnsureTimelinePositionVisible(frame.PositionMilliseconds, requestProjection: false);
        _isApplyingFrame = false;

        if (Math.Abs(Speed - state.Speed) > double.Epsilon)
            Speed = state.Speed;
        IsPlaying = state.IsPlaying;
        IsLoading = state.IsLoading || scrubActive;

        var statusText = scrubActive || state.IsLoading
            ? Localization["Playback_Status_Loading"]
            : state.IsPlaying
                ? Localization["Playback_Status_Playing"]
                : Localization["Playback_Status_Paused"];
        if (!string.Equals(StatusText, statusText, StringComparison.Ordinal))
            StatusText = statusText;

        if (scrubActive)
        {
            GlobalTimeline = _globalTimeline;
            return;
        }

        var liveSegment = RefreshLivePresentationState();
        RefreshLiveTimelineData(liveSegment);
        RefreshTimelineTracks(frame, timelineViewportChanged);
        ApplyLiveFinalization(liveSegment);
        if (ShouldRefreshEventWindow(frame, state))
        {
            RequestEventWindowRefresh();
            _lastEventWindowRefreshTick = Environment.TickCount64;
            _forceNextEventWindowRefresh = false;
        }

        if (!ShouldRefreshCombatants(frame, state))
            return;

        RefreshCombatants(frame);
        RefreshSelectedCombatantSummary();
        if (SelectedDetailMode is PlaybackDetailMode.Outgoing or PlaybackDetailMode.Incoming)
            RequestCombatantDetail(frame);
        _lastCombatantRefreshTick = Environment.TickCount64;
        _forceNextCombatantRefresh = false;
    }

    private void CancelScrubCompetingWork()
    {
        _eventWindowRefreshQueued = false;
        _eventWindowCancellation?.Cancel();
        _detailRefreshQueued = false;
        _detailCancellation?.Cancel();
    }

    internal static PlaybackTimelineViewport ResolveViewportAfterDurationChange(
        PlaybackTimelineViewport viewport,
        double previousDurationMilliseconds,
        double durationMilliseconds)
    {
        if (durationMilliseconds <= 0d)
            return PlaybackTimelineViewport.Empty;

        if (viewport.IsEmpty ||
            previousDurationMilliseconds <= 0d ||
            viewport.StartMilliseconds <= 0d && viewport.EndMilliseconds >= previousDurationMilliseconds)
        {
            return new PlaybackTimelineViewport(0d, durationMilliseconds);
        }

        var viewportDuration = Math.Min(viewport.DurationMilliseconds, durationMilliseconds);
        var start = Math.Clamp(viewport.StartMilliseconds, 0d, durationMilliseconds - viewportDuration);
        return new PlaybackTimelineViewport(start, start + viewportDuration);
    }

    private SceneJournalSegment RefreshLivePresentationState()
    {
        if (_source.SourceKind != ScenePlaybackSourceKind.Live)
            return default;

        var segment = _controller.CreateTimelineSegment().CreateBoundedSnapshot();
        var now = Environment.TickCount64;
        if (segment.IsLiveGrowing &&
            _lastLivePresentationRefreshTick != 0 &&
            now - _lastLivePresentationRefreshTick < LiveRefreshIntervalMilliseconds)
        {
            return segment;
        }

        _lastLivePresentationRefreshTick = now;
        var snapshot = _source.CreateSnapshot();
        var frozenIdentityScope = SceneIdentityScope.Empty;
        var isFrozen = false;
        if (_source is LiveScenePlaybackSource liveSource && liveSource.TryGetFrozenArchive(out var frozenArchive))
        {
            isFrozen = true;
            snapshot = frozenArchive.Snapshot;
            frozenIdentityScope = frozenArchive.Payload.IdentityScope;
        }

        if (!ReferenceEquals(_sceneDescriptorSnapshot, snapshot) || isFrozen && DisplayContext.MetadataRegistry is not null)
        {
            _sceneDescriptorSnapshot = snapshot;
            var context = isFrozen
                ? new SceneDisplayContext(
                    frozenIdentityScope,
                    null,
                    snapshot,
                    DisplayContext.Resources,
                    DisplayContext.UnknownSceneName)
                : new SceneDisplayContext(
                    SceneIdentityScope.Empty,
                    DisplayContext.MetadataRegistry,
                    snapshot,
                    DisplayContext.Resources,
                    DisplayContext.UnknownSceneName);
            ReplaceDisplayContext(context);
        }

        var sceneName = DisplayContext.ResolveSceneName(snapshot.Kind, snapshot.MapId, snapshot.BossNpcCodes);
        if (!string.Equals(SceneName, sceneName, StringComparison.Ordinal))
        {
            SceneName = sceneName;
            WindowTitle = string.Format(CultureInfo.CurrentCulture, Localization["Playback_WindowTitleFormat"], sceneName);
        }

        return segment;
    }

    private void ReplaceDisplayContext(SceneDisplayContext displayContext)
    {
        DisplayContext = displayContext;
        CombatantDetails.DisplayContext = displayContext;
        _displayTextRevision++;
        _forceNextCombatantRefresh = true;
        _forceNextEventWindowRefresh = true;
        OnPropertyChanged(nameof(DisplayContext));
        OnPropertyChanged(nameof(EventScopeCombatantText));
    }

    private void RefreshLiveTimelineData(SceneJournalSegment segment)
    {
        if (_source.SourceKind != ScenePlaybackSourceKind.Live)
            return;

        var endObservationOrdinalExclusive = segment.CurrentEndObservationOrdinalExclusive;
        if (ShouldRefreshLiveIndex(_eventIndex, segment))
            RequestEventIndex(segment);

        if (SelectedDetailMode == PlaybackDetailMode.Auras &&
            SelectedCombatantId > 0 &&
            _auraTimelineTask is null &&
            (HasReachedLiveGrowthThreshold(
                 segment.StartObservationOrdinal,
                 _lastAuraTimelineEndObservationOrdinalExclusive,
                 endObservationOrdinalExclusive) ||
             !segment.IsLiveGrowing &&
             (_lastAuraTimelineWasGrowing || _lastAuraTimelineEndObservationOrdinalExclusive < endObservationOrdinalExclusive)))
        {
            RequestAuraTimeline(SelectedCombatantId, segment);
        }
    }

    internal static bool ShouldRefreshLiveIndex(ScenePlaybackTrackIndex? index, SceneJournalSegment segment)
    {
        if (index is null)
            return true;

        var endObservationOrdinalExclusive = segment.CurrentEndObservationOrdinalExclusive;
        if (!segment.IsLiveGrowing &&
            (index.IsSourceGrowing || index.EndObservationOrdinalExclusive < endObservationOrdinalExclusive))
        {
            return true;
        }

        return segment.IsLiveGrowing && HasReachedLiveGrowthThreshold(
            segment.StartObservationOrdinal,
            index.EndObservationOrdinalExclusive,
            endObservationOrdinalExclusive);
    }

    private static bool HasReachedLiveGrowthThreshold(
        long startObservationOrdinal,
        long indexedEndObservationOrdinalExclusive,
        long currentEndObservationOrdinalExclusive)
    {
        if (currentEndObservationOrdinalExclusive <= indexedEndObservationOrdinalExclusive)
            return false;

        var indexedCount = Math.Max(0, indexedEndObservationOrdinalExclusive - startObservationOrdinal);
        var growthThreshold = Math.Max(MinimumLiveIndexGrowth, indexedCount / 4);
        return currentEndObservationOrdinalExclusive - indexedEndObservationOrdinalExclusive >= growthThreshold;
    }

    private void ApplyLiveFinalization(SceneJournalSegment segment)
    {
        if (_source.SourceKind != ScenePlaybackSourceKind.Live || segment.IsEmpty || segment.IsLiveGrowing)
            return;

        if (!_liveFinalizationApplied)
        {
            _liveFinalizationApplied = true;
            _controller.StartCheckpointRebuild();
        }

        var endObservationOrdinalExclusive = segment.CurrentEndObservationOrdinalExclusive;
        var eventIndexFinalized = _eventIndex is
        {
            IsSourceGrowing: false
        } && _eventIndex.EndObservationOrdinalExclusive >= endObservationOrdinalExclusive;
        var auraTimelineFinalized = SelectedDetailMode != PlaybackDetailMode.Auras ||
            SelectedCombatantId <= 0 ||
            !_lastAuraTimelineWasGrowing &&
            _lastAuraTimelineEndObservationOrdinalExclusive >= endObservationOrdinalExclusive;
        _liveSourceFinalized = eventIndexFinalized && auraTimelineFinalized;
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
        if (_eventIndex is null)
            return false;

        if (_forceNextEventWindowRefresh || !state.IsPlaying)
            return true;

        if (frame.TimeRange.DurationMilliseconds > 0 && frame.PositionMilliseconds >= frame.TimeRange.DurationMilliseconds)
            return true;

        return Environment.TickCount64 - _lastEventWindowRefreshTick >= EventWindowRefreshIntervalMilliseconds;
    }

    private void RefreshCombatants(ScenePlaybackFrame frame)
    {
        var combatants = frame.Snapshot.Combatants.AsSpan();
        if (combatants.Length == 0 && frame.EntityVitals.Count == 0)
        {
            if (Combatants.Count > 0)
                Combatants.Clear();
            if (SelectedCombatant is not null)
                SelectedCombatant = null;
            return;
        }

        _vitalScratch.Clear();
        for (var i = 0; i < frame.EntityVitals.Count; i++)
            _vitalScratch[frame.EntityVitals[i].EntityId] = frame.EntityVitals[i];

        var timelines = _combatantTimelines;
        _seenCombatantIds.Clear();
        using (var deferral = Combatants.SuspendNotifications())
        {
            foreach (var row in deferral.Snapshot)
            {
                var id = row.EntityId;
                var hasMetrics = frame.Snapshot.Combatants.TryGetValue(id, out var metrics);
                var hasVital = _vitalScratch.TryGetValue(id, out var vital);
                if (!hasMetrics && !hasVital)
                {
                    Combatants.Remove(id);
                    continue;
                }

                ApplyCombatantRow(row, id, in metrics, hasMetrics, in vital, ResolveCombatantTimeline(timelines, id));
                _seenCombatantIds.Add(id);
            }

            for (var i = 0; i < combatants.Length; i++)
            {
                var entry = combatants[i];
                if (!_seenCombatantIds.Add(entry.Id))
                    continue;

                var metrics = entry.Metrics;
                _vitalScratch.TryGetValue(entry.Id, out var vital);
                var row = new PlaybackCombatantRowViewModel(_frameBatchService, entry.Id);
                ApplyCombatantRow(row, entry.Id, in metrics, hasMetrics: true, in vital, ResolveCombatantTimeline(timelines, entry.Id));
                Combatants.Add(row);
            }

            for (var i = 0; i < frame.EntityVitals.Count; i++)
            {
                var vital = frame.EntityVitals[i];
                if (!_seenCombatantIds.Add(vital.EntityId))
                    continue;

                var metrics = default(SceneCombatantMetrics);
                var row = new PlaybackCombatantRowViewModel(_frameBatchService, vital.EntityId);
                ApplyCombatantRow(row, vital.EntityId, in metrics, hasMetrics: false, in vital, ResolveCombatantTimeline(timelines, vital.EntityId));
                Combatants.Add(row);
            }

            Combatants.Sort(CompareCombatantRows);
        }

        if (Combatants.Count == 0)
        {
            if (SelectedCombatant is not null)
                SelectedCombatant = null;
        }
        else if (SelectedCombatant is not null)
        {
            if (!Combatants.TryGetValue(SelectedCombatant.EntityId, out var currentSelection))
                SelectedCombatant = null;
            else if (!ReferenceEquals(SelectedCombatant, currentSelection))
                SelectedCombatant = currentSelection;
            else
                OnPropertyChanged(nameof(SelectedCombatant));
        }
    }

    private void ApplyCombatantRow(PlaybackCombatantRowViewModel row, int entityId, in SceneCombatantMetrics metrics, bool hasMetrics, in EntityVitalState vital, PlaybackTimelineStrip timeline)
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
        row.HpText = CreateHpText(in vital);
        row.UpdateHpSegments(CreateHpRatio(in vital), ScenePlaybackTimelineBuilder.EntityVitalBrush);
        row.Timeline = timeline;
    }

    private static int CompareCombatantRows(PlaybackCombatantRowViewModel left, PlaybackCombatantRowViewModel right)
    {
        var cmp = right.DamageAmount.CompareTo(left.DamageAmount);
        if (cmp != 0)
            return cmp;

        cmp = string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
        return cmp != 0 ? cmp : left.EntityId.CompareTo(right.EntityId);
    }

    private void RequestSelectedDetailData(int combatantId)
    {
        switch (SelectedDetailMode)
        {
            case PlaybackDetailMode.Outgoing:
            case PlaybackDetailMode.Incoming:
                _auraTimelineCancellation?.Cancel();
                _detailRefreshQueued = true;
                if (!_detailRequestPending)
                    RequestCombatantDetail(_currentFrame);
                break;
            case PlaybackDetailMode.Auras:
                _detailRefreshQueued = false;
                _detailCancellation?.Cancel();
                RequestAuraTimeline(combatantId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(SelectedDetailMode), SelectedDetailMode, null);
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

        SelectedCombatantDamageText = combatant.DamageText;
        SelectedCombatantDpsText = combatant.DamagePerSecondText;
        SelectedCombatantHealingText = combatant.HealingText;
        SelectedCombatantHpText = combatant.HpText;
    }

    private void ClearSelectedCombatantSummary()
    {
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

        if (_scrubRefreshPending || _seekCoordinator.IsBusy || _controller.IsLoading)
        {
            _detailRefreshQueued = true;
            return;
        }

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
        _detailTask = Task.Run(
            () => ProjectCombatantDetailAsync(
                frame.EncounterId,
                combatantId,
                frame.AppliedSegment.EndObservationOrdinalExclusive,
                generation,
                cancellation),
            CancellationToken.None);
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
                if (_detailRefreshQueued &&
                    !_isDisposed &&
                    SelectedCombatantId > 0 &&
                    SelectedDetailMode is PlaybackDetailMode.Outgoing or PlaybackDetailMode.Incoming)
                {
                    RequestCombatantDetail(_currentFrame);
                }
            });
        }
    }

    private void RefreshTimelineTracks(ScenePlaybackFrame frame, bool viewportChanged = false)
    {
        var duration = frame.TimeRange.DurationMilliseconds;
        var requiresProjection = viewportChanged;
        if (!_timelineMarkersInitialized || Math.Abs(_timelineMarkerDuration - duration) > double.Epsilon)
        {
            _timelineMarkerDuration = duration;
            _timelineMarkersInitialized = true;
            requiresProjection = true;
        }

        if (requiresProjection)
            RequestTimelineProjection();

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

    private static string CreateHpText(in EntityVitalState vital)
    {
        if (vital.EntityId == 0)
            return string.Empty;

        var current = Math.Max(0, vital.CurrentHp);
        var maximum = Math.Max(current, vital.MaxHp ?? 0);
        return maximum > 0 ? $"{FormatNumber(current)} / {FormatNumber(maximum)}" : FormatNumber(current);
    }

    private static double CreateHpRatio(in EntityVitalState vital)
    {
        if (vital.EntityId == 0)
            return 0d;

        var current = Math.Max(0, vital.CurrentHp);
        var maximum = Math.Max(current, vital.MaxHp ?? 0);
        return maximum > 0 ? Math.Clamp(current / (double)maximum, 0d, 1d) : 0d;
    }

    private void RequestTimelineProjection()
    {
        var index = _eventIndex;
        var viewport = TimelineViewport;
        if (_isDisposed || index is null || viewport.IsEmpty)
            return;

        _timelineProjectionCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _timelineProjectionCancellation = cancellation;
        var generation = ++_timelineProjectionGeneration;
        var startMilliseconds = (long)Math.Floor(viewport.StartMilliseconds);
        var endMilliseconds = (long)Math.Ceiling(viewport.EndMilliseconds);
        var window = index.ReadWindow(
            startMilliseconds,
            endMilliseconds,
            index.EndObservationOrdinalExclusive,
            int.MaxValue);
        _timelineProjectionTask = ProjectTimelineAsync(window, viewport, generation, cancellation);
    }

    private async Task ProjectTimelineAsync(
        ScenePlaybackTrackMarkerWindow window,
        PlaybackTimelineViewport viewport,
        long generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            var timelines = await Task.Run(
                () => ScenePlaybackTimelineBuilder.TryBuildTimelineStrips(window, viewport, CreateMarkerText, cancellation.Token),
                CancellationToken.None).ConfigureAwait(false);
            if (timelines is not { } completedTimelines)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_isDisposed ||
                    cancellation.IsCancellationRequested ||
                    generation != _timelineProjectionGeneration ||
                    viewport != TimelineViewport)
                {
                    return;
                }

                _globalTimeline = completedTimelines.Global;
                _combatantTimelines = completedTimelines.Combatants;
                GlobalTimeline = _globalTimeline;
                ApplyCombatantTimelines(_combatantTimelines);
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
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
                var isCurrent = ReferenceEquals(_timelineProjectionCancellation, cancellation);
                cancellation.Dispose();
                if (!isCurrent)
                    return;

                _timelineProjectionCancellation = null;
                _timelineProjectionTask = null;
            });
        }
    }

    private void RequestAuraTimeline(int targetEntityId)
        => RequestAuraTimeline(targetEntityId, _controller.CreateTimelineSegment().CreateBoundedSnapshot());

    private void RequestAuraTimeline(int targetEntityId, SceneJournalSegment segment)
    {
        _auraTimelineCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _auraTimelineCancellation = cancellation;
        var endObservationOrdinalExclusive = segment.CurrentEndObservationOrdinalExclusive;
        var duration = (long)Math.Round(DurationMilliseconds, MidpointRounding.AwayFromZero);
        _auraTimelineTask = ProjectAuraTimelineAsync(
            targetEntityId,
            segment,
            endObservationOrdinalExclusive,
            segment.IsLiveGrowing,
            duration,
            cancellation);
    }

    private async Task ProjectAuraTimelineAsync(
        int targetEntityId,
        SceneJournalSegment segment,
        long endObservationOrdinalExclusive,
        bool isSourceGrowing,
        long durationMilliseconds,
        CancellationTokenSource cancellation)
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

                var selectedIdentity = SelectedAura?.AuraIdentity ?? default;
                _auraTimelineTracks = ScenePlaybackTimelineBuilder.BuildAuraTimelineTracks(timeline, durationMilliseconds, Localization, DisplayContext);
                AuraTimelineTracks = _auraTimelineTracks;
                _lastAuraTimelineEndObservationOrdinalExclusive = endObservationOrdinalExclusive;
                _lastAuraTimelineWasGrowing = isSourceGrowing;
                SelectedAura = selectedIdentity.IsEmpty
                    ? null
                    : FindAuraTimelineLane(_auraTimelineTracks, selectedIdentity);
                ApplyLiveFinalization(_controller.CreateTimelineSegment().CreateBoundedSnapshot());
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
                var isCurrent = ReferenceEquals(_auraTimelineCancellation, cancellation);
                cancellation.Dispose();
                if (!isCurrent)
                    return;

                _auraTimelineCancellation = null;
                _auraTimelineTask = null;
            });
        }
    }

    private static PlaybackAuraTimelineLane? FindAuraTimelineLane(
        IReadOnlyList<PlaybackAuraTimelineLane> lanes,
        ScenePlaybackAuraIdentity identity)
    {
        for (var i = 0; i < lanes.Count; i++)
        {
            if (lanes[i].AuraIdentity == identity)
                return lanes[i];
        }

        return null;
    }

    private string ResolveAuraDisplayName(PlaybackAuraTimelineLane lane)
        => lane.SkillCode > 0 ? DisplayContext.ResolveSkillName(lane.SkillCode) : lane.FallbackText;

    private void RequestEventIndex()
        => RequestEventIndex(_controller.CreateTimelineSegment().CreateBoundedSnapshot());

    private void RequestEventIndex(SceneJournalSegment segment)
    {
        if (_isDisposed)
            return;

        var requestedEnd = segment.CurrentEndObservationOrdinalExclusive;
        if (_eventIndex is not null &&
            _eventIndex.EndObservationOrdinalExclusive >= requestedEnd &&
            _eventIndex.IsSourceGrowing == segment.IsLiveGrowing &&
            _eventIndexTask is null)
        {
            return;
        }

        if (_eventIndexTask is not null)
        {
            _eventIndexRefreshQueued = true;
            return;
        }

        var cancellation = new CancellationTokenSource();
        _eventIndexCancellation = cancellation;
        _eventIndexTask = BuildEventIndexAsync(segment, cancellation);
    }

    private async Task BuildEventIndexAsync(SceneJournalSegment segment, CancellationTokenSource cancellation)
    {
        var succeeded = false;
        try
        {
            var index = await Task.Run(
                () => ScenePlaybackTrackIndex.Build(segment, cancellation.Token),
                cancellation.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_isDisposed || cancellation.IsCancellationRequested)
                    return;

                _eventIndex = index;
                succeeded = true;
                RequestTimelineProjection();
                ApplyLiveFinalization(_controller.CreateTimelineSegment().CreateBoundedSnapshot());
                _forceNextEventWindowRefresh = true;
                if (ShouldRefreshEventWindow(_currentFrame, _controller.State))
                {
                    RequestEventWindowRefresh();
                    _lastEventWindowRefreshTick = Environment.TickCount64;
                    _forceNextEventWindowRefresh = false;
                }
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
                if (!ReferenceEquals(_eventIndexCancellation, cancellation))
                    return;

                cancellation.Dispose();
                _eventIndexCancellation = null;
                _eventIndexTask = null;
                var shouldRefresh = succeeded && _eventIndexRefreshQueued;
                _eventIndexRefreshQueued = false;
                if (shouldRefresh && !_isDisposed)
                {
                    var currentSegment = _controller.CreateTimelineSegment().CreateBoundedSnapshot();
                    if (ShouldRefreshLiveIndex(_eventIndex, currentSegment))
                        RequestEventIndex(currentSegment);
                }
            });
        }
    }

    private void RequestEventWindowRefresh()
    {
        var index = _eventIndex;
        if (_isDisposed || index is null)
            return;

        if (_scrubRefreshPending || _seekCoordinator.IsBusy || _controller.IsLoading)
        {
            _eventWindowRefreshQueued = true;
            return;
        }

        if (_eventWindowTask is not null)
        {
            _eventWindowRefreshQueued = true;
            return;
        }

        var frame = _currentFrame;
        var position = frame.PositionMilliseconds;
        var start = Math.Max(0, position - EventWindowRadiusMilliseconds);
        var end = frame.TimeRange.DurationMilliseconds > 0
            ? Math.Min(frame.TimeRange.DurationMilliseconds, position + EventWindowRadiusMilliseconds)
            : position + EventWindowRadiusMilliseconds;
        var cancellation = new CancellationTokenSource();
        _eventWindowRefreshQueued = false;
        _eventWindowCancellation = cancellation;
        _eventWindowTask = Task.Run(
            () => RefreshEventWindowAsync(
                frame.PositionMilliseconds,
                frame.AppliedSegment.EndObservationOrdinalExclusive,
                EventSelection,
                (long)start,
                (long)end,
                index,
                cancellation),
            CancellationToken.None);
    }

    private async Task RefreshEventWindowAsync(
        long positionMilliseconds,
        long expectedEndObservationOrdinalExclusive,
        ScenePlaybackEventScope scope,
        long startPositionMilliseconds,
        long endPositionMilliseconds,
        ScenePlaybackTrackIndex index,
        CancellationTokenSource cancellation)
    {
        try
        {
            var materializedRead = await _controller.CopyLatestMaterializedEventsAsync(
                scope,
                startPositionMilliseconds,
                endPositionMilliseconds,
                _materializedEventBuffer,
                cancellation.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_isDisposed || cancellation.IsCancellationRequested)
                    return;

                var frame = _currentFrame;
                if (materializedRead.EndObservationOrdinalExclusive != expectedEndObservationOrdinalExclusive ||
                    frame.AppliedSegment.EndObservationOrdinalExclusive != expectedEndObservationOrdinalExclusive ||
                    frame.PositionMilliseconds != positionMilliseconds ||
                    EventSelection != scope)
                {
                    _eventWindowRefreshQueued = true;
                    return;
                }

                var nonCombatRead = index.CopyLatestNonCombatEvents(
                    scope,
                    startPositionMilliseconds,
                    endPositionMilliseconds,
                    expectedEndObservationOrdinalExclusive,
                    _nonCombatEventBuffer);
                var markerCount = MergeLatestEventMarkers(
                    _materializedEventBuffer.AsSpan(0, materializedRead.Count),
                    _nonCombatEventBuffer.AsSpan(0, nonCombatRead.Count),
                    _eventMarkerBuffer);
                ApplyEventWindowMarkers(_eventMarkerBuffer.AsSpan(0, markerCount));
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
                var isCurrent = ReferenceEquals(_eventWindowCancellation, cancellation);
                cancellation.Dispose();
                if (!isCurrent)
                    return;

                _eventWindowCancellation = null;
                _eventWindowTask = null;
                if (_eventWindowRefreshQueued && !_isDisposed)
                    RequestEventWindowRefresh();
            });
        }
    }

    private void ApplyEventWindowMarkers(ReadOnlySpan<ScenePlaybackEventMarker> markers)
    {
        if (markers.Length == 0)
        {
            if (EventWindow.Count > 0)
                EventWindow.Clear();
            HasEventWindowRows = false;
            return;
        }

        _eventWindowIds.Clear();
        for (var i = 0; i < markers.Length; i++)
            _eventWindowIds.Add(markers[i].Id);

        using var deferral = EventWindow.SuspendNotifications();
        foreach (var row in deferral.Snapshot)
        {
            if (!_eventWindowIds.Contains(row.EventId))
                EventWindow.Remove(row.EventId);
        }

        for (var i = 0; i < markers.Length; i++)
        {
            ref readonly var marker = ref markers[i];
            if (!EventWindow.TryGetValue(marker.Id, out var row))
            {
                row = new PlaybackEventRowViewModel(_frameBatchService, marker.Id);
                EventWindow.Add(row);
            }

            if (!row.HasMarker ||
                row.Marker != marker ||
                row.DisplayTextRevision != _displayTextRevision)
            {
                ApplyEventRow(row, in marker);
            }
        }

        EventWindow.Sort(CompareEventRows);
        HasEventWindowRows = true;
    }

    private static int MergeLatestEventMarkers(
        ReadOnlySpan<ScenePlaybackEventMarker> materialized,
        ReadOnlySpan<ScenePlaybackEventMarker> nonCombat,
        Span<ScenePlaybackEventMarker> destination)
    {
        var count = Math.Min(destination.Length, materialized.Length + nonCombat.Length);
        var materializedIndex = materialized.Length - 1;
        var nonCombatIndex = nonCombat.Length - 1;
        for (var destinationIndex = count - 1; destinationIndex >= 0; destinationIndex--)
        {
            if (materializedIndex < 0)
            {
                destination[destinationIndex] = nonCombat[nonCombatIndex--];
                continue;
            }

            if (nonCombatIndex < 0)
            {
                destination[destinationIndex] = materialized[materializedIndex--];
                continue;
            }

            if (CompareEventMarkers(materialized[materializedIndex], nonCombat[nonCombatIndex]) > 0)
                destination[destinationIndex] = materialized[materializedIndex--];
            else
                destination[destinationIndex] = nonCombat[nonCombatIndex--];
        }

        return count;
    }

    private void RefreshVisibleEventWindow()
    {
        if (_eventIndex is null)
            return;

        RequestEventWindowRefresh();
        _lastEventWindowRefreshTick = Environment.TickCount64;
        _forceNextEventWindowRefresh = false;
    }

    private void ApplyEventRow(PlaybackEventRowViewModel row, in ScenePlaybackEventMarker marker)
    {
        var trackMarker = marker.Marker;
        row.Marker = marker;
        row.HasMarker = true;
        row.PositionMilliseconds = marker.PositionMilliseconds;
        row.TimeText = FormatTime(marker.PositionMilliseconds);
        row.TrackText = ResolveTrackName(marker.Track);
        row.SourceText = DisplayContext.ResolveEntityName(marker.SourceEntityId);
        row.TargetText = DisplayContext.ResolveEntityName(marker.TargetEntityId);
        row.SkillText = ResolveEventMarkerDisplayName(in trackMarker);
        row.AmountText = CreateEventValueText(in marker);
        row.DisplayTextRevision = _displayTextRevision;
    }

    private static int CompareEventRows(PlaybackEventRowViewModel left, PlaybackEventRowViewModel right)
    {
        var cmp = left.PositionMilliseconds.CompareTo(right.PositionMilliseconds);
        return cmp != 0 ? cmp : left.EventId.CompareTo(right.EventId);
    }

    private static int CompareEventMarkers(in ScenePlaybackEventMarker left, in ScenePlaybackEventMarker right)
    {
        var comparison = left.PositionMilliseconds.CompareTo(right.PositionMilliseconds);
        return comparison != 0 ? comparison : left.Id.CompareTo(right.Id);
    }

    private string CreateMarkerText(ScenePlaybackTrackMarker marker)
    {
        var amount = CreateAmountText(marker);
        var skillName = ResolveEventMarkerDisplayName(in marker);
        if (!string.IsNullOrWhiteSpace(skillName) && !string.IsNullOrWhiteSpace(amount))
            return $"{FormatTime(marker.PositionMilliseconds)} {skillName} {amount}";
        if (!string.IsNullOrWhiteSpace(skillName))
            return $"{FormatTime(marker.PositionMilliseconds)} {skillName}";
        if (!string.IsNullOrWhiteSpace(amount))
            return $"{FormatTime(marker.PositionMilliseconds)} {amount}";
        return FormatTime(marker.PositionMilliseconds);
    }

    private string ResolveEventMarkerDisplayName(in ScenePlaybackTrackMarker marker)
    {
        if (marker.Track is ScenePlaybackTrack.Combat or ScenePlaybackTrack.Mechanic or ScenePlaybackTrack.Resource)
            return ResolveCombatEventDisplayName(marker.EventKey);

        return marker.DisplayResourceEffectRef.IsEmpty
            ? string.Empty
            : DisplayContext.ResolveSkillName(marker.DisplayResourceEffectRef);
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

    private string CreateEventValueText(in ScenePlaybackEventMarker marker)
    {
        return marker.Id.Kind switch
        {
            ScenePlaybackEventFactKind.Metric when marker.Contribution is { } contribution => FormatSigned(contribution.Amount),
            ScenePlaybackEventFactKind.Mechanic when marker.Mechanic is { } mechanic => CreateMechanicValueText(in mechanic),
            ScenePlaybackEventFactKind.Resource when marker.Resource is { } resource => CreateResourceValueText(in resource),
            _ => CreateAmountText(marker.Marker)
        };
    }

    private string CreateMechanicValueText(in CombatMechanicOccurrence mechanic)
    {
        var parts = new List<string>(4);
        if (mechanic.HitCount > 0 || mechanic.AttemptCount > 0)
        {
            parts.Add(string.Format(
                CultureInfo.CurrentCulture,
                Localization["Playback_Mechanic_HitAttemptFormat"],
                mechanic.HitCount,
                mechanic.AttemptCount));
        }
        if (mechanic.EvadeCount > 0)
            parts.Add($"{Localization["Stat_Evade"]} {mechanic.EvadeCount:N0}");
        if (mechanic.InvincibleCount > 0)
            parts.Add($"{Localization["Stat_Invincible"]} {mechanic.InvincibleCount:N0}");
        if (mechanic.MultiHitCount > 0)
            parts.Add($"{Localization["Stat_MultiHit"]} {mechanic.MultiHitCount:N0}");
        return string.Join(" | ", parts);
    }

    private static string CreateResourceValueText(in CombatResourceOccurrence resource)
    {
        var name = resource.Resource switch
        {
            CombatResourceKind.Health => "HP",
            CombatResourceKind.Mana => "MP",
            _ => string.Empty
        };
        var amount = resource.Flow == CombatResourceFlowKind.Spend ? -resource.Amount : resource.Amount;
        var amountText = FormatSigned(amount);
        return name.Length == 0 ? amountText : $"{name} {amountText}";
    }

    private string CreateAmountText(ScenePlaybackTrackMarker marker)
    {
        if (marker.Track == ScenePlaybackTrack.Combat && marker.Amount != 0)
            return FormatSigned(marker.Amount);

        if (marker.Track == ScenePlaybackTrack.EntityVital)
        {
            var current = marker.CurrentHp.HasValue ? FormatNumber(marker.CurrentHp.Value) : "?";
            var maximum = marker.MaxHp.HasValue ? FormatNumber(marker.MaxHp.Value) : "?";
            return $"{current}/{maximum}";
        }

        if (marker.Track == ScenePlaybackTrack.Aura)
        {
            return marker.LifecycleEventKind switch
            {
                AuraLifecycleEventKind.Open when marker.DurationMilliseconds == ushort.MaxValue => Localization["Playback_Lifecycle_OpenIndefinite"],
                AuraLifecycleEventKind.Open => string.Format(CultureInfo.CurrentCulture, Localization["Playback_Lifecycle_OpenFormat"], marker.DurationMilliseconds),
                AuraLifecycleEventKind.Renew => Localization["Playback_Lifecycle_Renew"],
                AuraLifecycleEventKind.Result => string.Format(CultureInfo.CurrentCulture, Localization["Playback_Lifecycle_ResultFormat"], marker.ResultCode),
                _ => string.Empty
            };
        }

        return string.Empty;
    }

    private string ResolveTrackName(ScenePlaybackTrack track) => track switch
    {
        ScenePlaybackTrack.Combat => Localization["Playback_Track_Combat"],
        ScenePlaybackTrack.Mechanic => Localization["Playback_Track_Mechanic"],
        ScenePlaybackTrack.Resource => Localization["Playback_Track_Resource"],
        ScenePlaybackTrack.EntityVital => Localization["Playback_Track_RemainingHp"],
        ScenePlaybackTrack.Aura => Localization["Playback_Track_Aura"],
        ScenePlaybackTrack.State => Localization["Playback_Track_State"],
        ScenePlaybackTrack.Scene => Localization["Playback_Track_Scene"],
        ScenePlaybackTrack.Action => Localization["Playback_Track_Action"],
        ScenePlaybackTrack.Diagnostic => Localization["Playback_Track_Diagnostic"],
        _ => Localization["Playback_Track_Other"]
    };

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
        _eventIndexCancellation?.Cancel();
        _eventWindowCancellation?.Cancel();
        _timelineProjectionCancellation?.Cancel();
        _auraTimelineCancellation?.Cancel();
        _liveRefreshCancellation?.Cancel();
        Localization.LanguageChanged -= OnLanguageChanged;
        _controller.FrameChanged -= OnFrameChanged;
        await _seekCoordinator.DisposeAsync().ConfigureAwait(false);
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

        if (_eventIndexTask is not null)
        {
            try
            {
                await _eventIndexTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_eventWindowTask is not null)
        {
            try
            {
                await _eventWindowTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_timelineProjectionTask is not null)
        {
            try
            {
                await _timelineProjectionTask.ConfigureAwait(false);
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

        if (_liveRefreshTask is not null)
        {
            try
            {
                await _liveRefreshTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _detailCancellation?.Dispose();
        _eventIndexCancellation?.Dispose();
        _eventWindowCancellation?.Dispose();
        _timelineProjectionCancellation?.Dispose();
        _auraTimelineCancellation?.Dispose();
        _liveRefreshCancellation?.Dispose();
        CombatantDetails.Dispose();
        await _controller.DisposeAsync().ConfigureAwait(false);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _displayTextRevision++;
        SceneName = DisplayContext.ResolveSceneName(_sceneDescriptorSnapshot.Kind, _sceneDescriptorSnapshot.MapId, _sceneDescriptorSnapshot.BossNpcCodes);
        WindowTitle = string.Format(CultureInfo.CurrentCulture, Localization["Playback_WindowTitleFormat"], SceneName);
        _timelineMarkersInitialized = false;
        _forceNextCombatantRefresh = true;
        _forceNextEventWindowRefresh = true;
        if (SelectedCombatantId > 0)
        {
            _detailRefreshQueued = true;
            RequestSelectedDetailData(SelectedCombatantId);
        }

        OnPropertyChanged(nameof(EventScopeCombatantText));
        OnPropertyChanged(nameof(EventScopeRelationText));
        OnPropertyChanged(nameof(EventScopeCategoryText));
        OnPropertyChanged(nameof(EventScopeSkillText));
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

public sealed class PlaybackEventRowViewModel(UiFrameBatchService frameBatchService, ScenePlaybackEventId eventId) : FrameBatchedObservableObject(frameBatchService)
{
    public ScenePlaybackEventId EventId { get; } = eventId;

    public bool HasMarker { get; set; }

    public ScenePlaybackEventMarker Marker { get; set; }

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
