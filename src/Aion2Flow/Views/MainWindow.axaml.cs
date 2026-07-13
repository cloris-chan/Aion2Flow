using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Cloris.Aion2Flow.Assets.Icons;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Hotkeys;
using Cloris.Aion2Flow.Services.Overlay;
using Cloris.Aion2Flow.Services.Settings;
using Cloris.Aion2Flow.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Cloris.Aion2Flow.Views;

public partial class MainWindow : Window
{
    private const uint WmEnterSizeMove = 0x0231;
    private const uint WmExitSizeMove = 0x0232;
    private const int MaxPinNativeRetries = 3;
    private static readonly TimeSpan CursorProbeInterval = TimeSpan.FromMilliseconds(33);

    private readonly GlobalHotkeyService _globalHotkeyService;
    private readonly OverlayInteractionController _overlayInteractionController;
    private readonly SettingsService _settingsService;
    private readonly AvaloniaFrameClockService _frameClock;
    private readonly UiScaleService _uiScale;
    private readonly LocalizationService _localization;
    private readonly Control? _overlayRoot;
    private OverlayPinWindow? _pinWindow;
    private bool _hotkeyAttached;
    private bool _frameClockAttached;
    private bool _isNativeSizeMoveActive;
    private bool _autoHeightRefreshQueued;
    private bool _overlayGeometryRefreshQueued;
    private bool _nativeStyleRefreshQueued;
    private bool _hasAppliedOverlayInteractionMode;
    private bool _hasCursorProbeTimestamp;
    private bool _hasOverlayScreenBounds;
    private int _pinNativeRetryCount;
    private TimeSpan _lastCursorProbeTimestamp;
    private OverlayInteractionMode _appliedOverlayInteractionMode;
    private OverlayWindowInputState _windowInputState;
    private int _overlayScreenLeft;
    private int _overlayScreenTop;
    private int _overlayScreenRight;
    private int _overlayScreenBottom;

    public new MainViewModel DataContext { get => (MainViewModel)base.DataContext!; set => base.DataContext = value; }

    public MainWindow()
    {
        DataContext = Ioc.Default.GetRequiredService<MainViewModel>();
        _globalHotkeyService = Ioc.Default.GetRequiredService<GlobalHotkeyService>();
        _overlayInteractionController = Ioc.Default.GetRequiredService<OverlayInteractionController>();
        _settingsService = Ioc.Default.GetRequiredService<SettingsService>();
        _frameClock = Ioc.Default.GetRequiredService<AvaloniaFrameClockService>();
        _uiScale = Ioc.Default.GetRequiredService<UiScaleService>();
        _localization = Ioc.Default.GetRequiredService<LocalizationService>();
        DataContext.InitializeAsync().ConfigureAwait(false);
        InitializeComponent();
        _overlayRoot = Content as Control;
        DataContext.EncounterHistory.CollectionChanged += OnEncounterHistoryCollectionChanged;
        RebuildEncounterHistoryMenuItems();
        _globalHotkeyService.Triggered += OnGlobalHotkeyTriggered;
        _overlayInteractionController.ModeChanged += OnOverlayInteractionModeChanged;
        PositionChanged += OnWindowPositionChanged;
        ScalingChanged += OnWindowScalingChanged;
        if (_settingsService.Current.MainWindowPosition.HasValue)
        {
            Position = new(_settingsService.Current.MainWindowPosition.Value.X, _settingsService.Current.MainWindowPosition.Value.Y);
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _uiScale.RegisterWindow(this);
        RebuildEncounterHistoryMenuItems();
        AttachGlobalHotkeyHook();
        AttachFrameClock();
        ShowPinWindow();
        ApplyOverlayInteractionMode(_overlayInteractionController.Mode);
    }

    private void AttachFrameClock()
    {
        if (_frameClockAttached)
        {
            return;
        }

        _frameClockAttached = true;
        _frameClock.Frame += OnAnimationFrame;
        _frameClock.Attach(this);
    }

    private void OnAnimationFrame(object? sender, AvaloniaFrameEventArgs e)
    {
        DataContext.ProcessUiFrame(e.Timestamp);
        UpdateClickThroughCursorState(e.Timestamp);
    }

    private void AttachGlobalHotkeyHook()
    {
        if (_hotkeyAttached)
        {
            return;
        }

        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        Win32Properties.AddWndProcHookCallback(this, WndProcHook);
        _globalHotkeyService.AttachWindow(hwnd);
        DataContext.SettingsFlyout.RefreshHotkeyRegistrationState(_globalHotkeyService.IsAttachedTo(hwnd));
        _hotkeyAttached = true;
    }

    private nint WndProcHook(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == GlobalHotkeyService.WmHotkey)
        {
            _globalHotkeyService.HandleWindowMessage(msg, wParam);
        }
        else if (msg == WmEnterSizeMove)
        {
            _isNativeSizeMoveActive = true;
        }
        else if (msg == WmExitSizeMove)
        {
            _isNativeSizeMoveActive = false;
            ScheduleOverlayAutoHeightRefresh();
        }

        return default;
    }

    private void OnGlobalHotkeyTriggered(GlobalHotkeyAction action)
    {
        if (DataContext.SettingsFlyout.CapturingHotkeyAction is not null)
        {
            return;
        }

        if (action == GlobalHotkeyAction.CycleOverlayInteraction)
        {
            _overlayInteractionController.Cycle();
            return;
        }

        if (action == GlobalHotkeyAction.BattleReset && DataContext.IsCapturing && DataContext.ResetCommand.CanExecute(null))
        {
            DataContext.ResetCommand.Execute(null);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        DataContext.EncounterHistory.CollectionChanged -= OnEncounterHistoryCollectionChanged;
        _globalHotkeyService.Triggered -= OnGlobalHotkeyTriggered;
        _overlayInteractionController.ModeChanged -= OnOverlayInteractionModeChanged;
        PositionChanged -= OnWindowPositionChanged;
        ScalingChanged -= OnWindowScalingChanged;
        _globalHotkeyService.DetachWindow();
        _settingsService.Update(settings => settings.MainWindowPosition = new(Position.X, Position.Y));
        ClosePinWindow();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_frameClockAttached)
        {
            _frameClock.Frame -= OnAnimationFrame;
            _frameClock.Detach(this);
            _frameClockAttached = false;
        }

        if (_hotkeyAttached)
        {
            Win32Properties.RemoveWndProcHookCallback(this, WndProcHook);
            _hotkeyAttached = false;
        }

        base.OnClosed(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty)
        {
            InvalidateOverlayScreenGeometry();
        }
        else if (change.Property == TopmostProperty)
        {
            RefreshPinWindowTopmost();
        }
        else if (change.Property == WindowStateProperty)
        {
            InvalidateOverlayScreenGeometry();
            if (WindowState != WindowState.Minimized)
            {
                ScheduleNativeStyleRefresh();
            }
        }
    }

    private void ShowPinWindow()
    {
        if (_pinWindow is not null)
        {
            return;
        }

        var pinWindow = new OverlayPinWindow(_overlayInteractionController, _localization, _uiScale);
        pinWindow.PlacementInvalidated += OnPinWindowPlacementInvalidated;
        _pinWindow = pinWindow;
        RefreshPinWindowTopmost();
        pinWindow.Show(this);
        ScheduleOverlayGeometryRefresh();
    }

    private void ClosePinWindow()
    {
        var pinWindow = _pinWindow;
        if (pinWindow is null)
        {
            return;
        }

        _pinWindow = null;
        pinWindow.PlacementInvalidated -= OnPinWindowPlacementInvalidated;
        pinWindow.Close();
    }

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e) => InvalidateOverlayScreenGeometry();

    private void OnWindowScalingChanged(object? sender, EventArgs e) => InvalidateOverlayScreenGeometry();

    private void OnPinWindowPlacementInvalidated(object? sender, EventArgs e)
    {
        _pinNativeRetryCount = 0;
        ScheduleOverlayGeometryRefresh();
    }

    private void InvalidateOverlayScreenGeometry()
    {
        _hasOverlayScreenBounds = false;
        _pinNativeRetryCount = 0;
        ScheduleOverlayGeometryRefresh();
    }

    private void ScheduleOverlayGeometryRefresh()
    {
        if (_overlayGeometryRefreshQueued)
        {
            return;
        }

        _overlayGeometryRefreshQueued = true;
        Dispatcher.UIThread.Post(RefreshOverlayScreenGeometry, DispatcherPriority.Loaded);
    }

    private void RefreshOverlayScreenGeometry()
    {
        _overlayGeometryRefreshQueued = false;
        if (PlatformImpl is null || WindowState == WindowState.Minimized)
        {
            return;
        }

        var overlayTopLeft = MainHudShell.PointToScreen(default);
        var overlayBottomRight = MainHudShell.PointToScreen(new Point(MainHudShell.Bounds.Width, MainHudShell.Bounds.Height));
        _overlayScreenLeft = overlayTopLeft.X;
        _overlayScreenTop = overlayTopLeft.Y;
        _overlayScreenRight = overlayBottomRight.X;
        _overlayScreenBottom = overlayBottomRight.Y;
        _hasOverlayScreenBounds = MainHudShell.Bounds.Width > 0 && MainHudShell.Bounds.Height > 0;
        if (_windowInputState == OverlayWindowInputState.ClickThroughArmed && IsCursorCurrentlyInsideOverlay())
        {
            TryApplyWindowInputState(OverlayWindowInputState.ClickThroughActive);
        }

        var pinWindow = _pinWindow;
        if (pinWindow is null || pinWindow.PlatformImpl is null)
        {
            return;
        }

        var pinTopLeft = OverlayPinSlot.PointToScreen(default);
        var pinBottomRight = OverlayPinSlot.PointToScreen(new Point(OverlayPinSlot.Bounds.Width, OverlayPinSlot.Bounds.Height));
        var styleApplied = pinWindow.ApplyNativeWindowStyle();
        var boundsApplied = pinWindow.SetScreenBounds(new PixelRect(
            pinTopLeft.X,
            pinTopLeft.Y,
            Math.Max(1, pinBottomRight.X - pinTopLeft.X),
            Math.Max(1, pinBottomRight.Y - pinTopLeft.Y)));
        if (styleApplied && boundsApplied)
        {
            _pinNativeRetryCount = 0;
        }
        else
        {
            SchedulePinNativeRetry();
        }
    }

    private void ScheduleNativeStyleRefresh()
    {
        if (_nativeStyleRefreshQueued)
        {
            return;
        }

        _nativeStyleRefreshQueued = true;
        Dispatcher.UIThread.Post(RefreshNativeWindowStyles, DispatcherPriority.Loaded);
    }

    private void RefreshNativeWindowStyles()
    {
        _nativeStyleRefreshQueued = false;
        if (PlatformImpl is null || WindowState == WindowState.Minimized)
        {
            return;
        }

        var mode = _overlayInteractionController.Mode;
        if (!NativeOverlayWindowStyles.SetInputTransparent(this, _windowInputState.RequiresInputTransparency()))
        {
            if (mode != OverlayInteractionMode.Interactive)
            {
                _overlayInteractionController.SetMode(OverlayInteractionMode.Interactive);
            }
            return;
        }

        if (_pinWindow is { } pinWindow && !pinWindow.ApplyNativeWindowStyle())
        {
            SchedulePinNativeRetry();
        }
    }

    private void SchedulePinNativeRetry()
    {
        if (_pinNativeRetryCount >= MaxPinNativeRetries)
        {
            return;
        }

        _pinNativeRetryCount++;
        ScheduleOverlayGeometryRefresh();
    }

    private void OnOverlayInteractionModeChanged(OverlayInteractionMode mode) => ApplyOverlayInteractionMode(mode);

    private void ApplyOverlayInteractionMode(OverlayInteractionMode mode)
    {
        var isPointerInside = mode == OverlayInteractionMode.ClickThrough && IsCursorCurrentlyInsideOverlay();
        var nextInputState = OverlayWindowInputStateLogic.EnterMode(mode, isPointerInside);
        if (!NativeOverlayWindowStyles.SetInputTransparent(this, nextInputState.RequiresInputTransparency()))
        {
            if (_hasAppliedOverlayInteractionMode && _overlayInteractionController.Mode != _appliedOverlayInteractionMode)
            {
                _overlayInteractionController.SetMode(_appliedOverlayInteractionMode);
            }
            return;
        }

        if (_hasAppliedOverlayInteractionMode)
        {
            CloseTransientSurfaces();
        }
        MainHudShell.Classes.Set("click-through", mode == OverlayInteractionMode.ClickThrough);
        MainHudShell.Opacity = mode == OverlayInteractionMode.Hidden ? 0d : 1d;
        CommitWindowInputState(nextInputState);
        _appliedOverlayInteractionMode = mode;
        _hasAppliedOverlayInteractionMode = true;
        RefreshPinWindowTopmost();
    }

    private void RefreshPinWindowTopmost()
    {
        if (_pinWindow is { } pinWindow)
        {
            pinWindow.Topmost = Topmost || _overlayInteractionController.Mode != OverlayInteractionMode.Interactive;
        }
    }

    private void UpdateClickThroughCursorState(TimeSpan timestamp)
    {
        if (!_windowInputState.ShouldPollCursor())
        {
            return;
        }

        if (_hasCursorProbeTimestamp && timestamp - _lastCursorProbeTimestamp < CursorProbeInterval)
        {
            return;
        }

        _hasCursorProbeTimestamp = true;
        _lastCursorProbeTimestamp = timestamp;
        if (!_hasOverlayScreenBounds || !NativeOverlayWindowStyles.TryGetCursorPosition(out var cursor))
        {
            return;
        }

        if (!IsInsideOverlayScreenBounds(cursor))
        {
            TryApplyWindowInputState(OverlayWindowInputState.ClickThroughArmed);
        }
    }

    private void MainHudShellPointerEntered(object? sender, PointerEventArgs e)
    {
        if (_windowInputState == OverlayWindowInputState.ClickThroughArmed)
        {
            TryApplyWindowInputState(OverlayWindowInputState.ClickThroughActive);
        }
    }

    private bool TryApplyWindowInputState(OverlayWindowInputState state)
    {
        if (_windowInputState == state)
        {
            return true;
        }

        if (!NativeOverlayWindowStyles.SetInputTransparent(this, state.RequiresInputTransparency()))
        {
            return false;
        }

        CommitWindowInputState(state);
        return true;
    }

    private void CommitWindowInputState(OverlayWindowInputState state)
    {
        _windowInputState = state;
        _hasCursorProbeTimestamp = false;
        MainHudShell.Classes.Set("cursor-over", state == OverlayWindowInputState.ClickThroughActive);
    }

    private bool IsCursorCurrentlyInsideOverlay()
    {
        if (!_hasOverlayScreenBounds)
        {
            RefreshOverlayScreenGeometry();
        }

        return _hasOverlayScreenBounds &&
               NativeOverlayWindowStyles.TryGetCursorPosition(out var cursor) &&
               IsInsideOverlayScreenBounds(cursor);
    }

    private bool IsInsideOverlayScreenBounds(PixelPoint cursor) =>
        cursor.X >= _overlayScreenLeft && cursor.X < _overlayScreenRight &&
        cursor.Y >= _overlayScreenTop && cursor.Y < _overlayScreenBottom;

    private void CloseTransientSurfaces()
    {
        SettingsButton.Flyout?.Hide();
        EncounterHistoryButton.Flyout?.Hide();
        if (GetValue(FlyoutBase.AttachedFlyoutProperty) is Flyout detailsFlyout)
        {
            detailsFlyout.Hide();
        }
    }

    private void Minimize(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Exit(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBarDragRegionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
            ScheduleOverlayAutoHeightRefresh();
        }
    }

    private void ScheduleOverlayAutoHeightRefresh()
    {
        if (_autoHeightRefreshQueued || WindowState == WindowState.Minimized)
        {
            return;
        }

        _autoHeightRefreshQueued = true;
        Dispatcher.UIThread.Post(RefreshOverlayAutoHeight, DispatcherPriority.Loaded);
    }

    private void RefreshOverlayAutoHeight()
    {
        _autoHeightRefreshQueued = false;
        if (_isNativeSizeMoveActive || WindowState == WindowState.Minimized || PlatformImpl is null)
        {
            return;
        }

        InvalidateOverlayLayout();

        if (TryMeasureOverlayContentHeight(out var contentHeight)
            && Math.Abs(Bounds.Height - contentHeight) > 0.5d)
        {
            SizeToContent = Avalonia.Controls.SizeToContent.Manual;
            Height = contentHeight;
        }

        SizeToContent = Avalonia.Controls.SizeToContent.Height;
    }

    private bool TryMeasureOverlayContentHeight(out double height)
    {
        height = 0;

        if (Content is not Control content)
        {
            return TryUseFiniteHeight(_overlayRoot?.DesiredSize.Height * _uiScale.Scale, out height);
        }

        var availableWidth = Bounds.Width > 0
            ? Bounds.Width
            : Width;
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
        {
            availableWidth = double.PositiveInfinity;
        }

        content.Measure(new Size(availableWidth, double.PositiveInfinity));
        if (!TryUseFiniteHeight(content.DesiredSize.Height, out height))
        {
            return TryUseFiniteHeight(content.Bounds.Height, out height);
        }

        return true;
    }

    private bool TryUseFiniteHeight(double? candidate, out double height)
    {
        height = 0;
        if (candidate is not { } value || !double.IsFinite(value) || value <= 0)
        {
            return false;
        }

        if (double.IsFinite(MinHeight) && MinHeight > 0)
        {
            value = Math.Max(MinHeight, value);
        }

        if (double.IsFinite(MaxHeight) && MaxHeight > 0)
        {
            value = Math.Min(MaxHeight, value);
        }

        height = value;
        return true;
    }

    private void InvalidateOverlayLayout()
    {
        InvalidateMeasure();
        InvalidateArrange();

        if (Content is Control content)
        {
            content.InvalidateMeasure();
            content.InvalidateArrange();
        }

        if (_overlayRoot is not null && !ReferenceEquals(Content, _overlayRoot))
        {
            _overlayRoot.InvalidateMeasure();
            _overlayRoot.InvalidateArrange();
        }
    }

    private void CombatantRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border { DataContext: CombatantRowViewModel combatant })
        {
            DataContext.SelectedCombatant = combatant;
            if (TryGetCombatantDetailsFlyout(out var flyout, out var flyoutView))
            {
                ConfigureCombatantDetailsFlyout(flyout, flyoutView);
                flyout.ShowAt(_overlayRoot ?? this);
            }
        }
    }

    private void FlyoutClosed(object? sender, EventArgs e)
    {
        DataContext.SelectCombatantCommand.Execute(null);
    }

    private void OnEncounterHistoryCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildEncounterHistoryMenuItems();
    }

    private void RebuildEncounterHistoryMenuItems()
    {
        if (this.FindControl<Button>("EncounterHistoryButton")?.Flyout is not MenuFlyout menu)
        {
            return;
        }

        menu.Items.Clear();
        if (DataContext.EncounterHistory.Count == 0)
        {
            var placeholder = new MenuItem
            {
                Header = DataContext.Localization["Empty_History"],
                IsEnabled = false
            };
            placeholder.Classes.Add("FlyoutMenuItem");
            placeholder.Classes.Add("FlyoutPanelRow");
            placeholder.Classes.Add("FlyoutMenuItemPlaceholder");
            menu.Items.Add(placeholder);
            return;
        }

        foreach (var item in DataContext.EncounterHistory)
        {
            var header = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto)
                ],
                ColumnSpacing = 6,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            DisplayContextProvider.SetDisplayContext(header, item.DisplayContext);
            var sceneName = new TextBlock
            {
                Text = $"[{item.SceneName}]",
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            sceneName.Classes.Add("SettingsRowLabel");
            Grid.SetColumn(sceneName, 0);
            header.Children.Add(sceneName);

            var archivedAt = new TextBlock
            {
                Text = item.ArchivedAtText,
                VerticalAlignment = VerticalAlignment.Center
            };
            archivedAt.Classes.Add("SettingsRowValue");
            Grid.SetColumn(archivedAt, 1);
            header.Children.Add(archivedAt);

            var playbackButton = new Button
            {
                Tag = item,
                VerticalAlignment = VerticalAlignment.Center,
                Content = new Avalonia.Controls.Shapes.Path
                {
                    Data = IconGeometries.Play
                }
            };
            ToolTip.SetTip(playbackButton, DataContext.Localization["Playback_Open"]);
            playbackButton.Classes.Add("FlyoutInlineButton");
            if (playbackButton.Content is Avalonia.Controls.Shapes.Path playbackIcon)
            {
                playbackIcon.Classes.Add("Glyph");
                playbackIcon.Classes.Add("GlyphSm");
            }

            playbackButton.Click += EncounterHistoryPlaybackButtonClicked;
            Grid.SetColumn(playbackButton, 2);
            header.Children.Add(playbackButton);

            var menuItem = new MenuItem
            {
                Header = header,
                Tag = item
            };
            menuItem.Classes.Add("FlyoutMenuItem");
            menuItem.Classes.Add("FlyoutPanelRow");
            menuItem.Click += EncounterHistoryMenuItemClicked;
            menu.Items.Add(menuItem);
        }
    }

    private void EncounterHistoryMenuItemClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: EncounterHistoryItemViewModel item })
        {
            DataContext.SelectedEncounterHistory = item;
        }
    }

    private void EncounterHistoryPlaybackButtonClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: EncounterHistoryItemViewModel item })
        {
            OpenPlayback(item);
        }
    }

    private void OpenSelectedPlayback(object? sender, RoutedEventArgs e)
    {
        if (DataContext.SelectedEncounterHistory is { } item)
        {
            OpenPlayback(item);
        }
    }

    private void OpenPlayback(EncounterHistoryItemViewModel item)
    {
        var viewModel = new ScenePlaybackViewModel(item.Record, item.DisplayContext, DataContext.Localization);
        var window = new ScenePlaybackWindow(viewModel);
        window.Show(this);
    }

    private void ConfigureCombatantDetailsFlyout(Flyout flyout, CombatantDetailsFlyoutView flyoutView)
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen is null)
        {
            return;
        }

        var topLeft = this.PointToScreen(new Point(0, 0));
        var bottomRight = this.PointToScreen(new Point(Bounds.Width, Bounds.Height));
        var workArea = screen.WorkingArea;

        var leftSpace = Math.Max(0, topLeft.X - workArea.X);
        var rightSpace = Math.Max(0, workArea.Right - bottomRight.X);
        var topSpace = Math.Max(0, topLeft.Y - workArea.Y);
        var bottomSpace = Math.Max(0, workArea.Bottom - bottomRight.Y);

        var placeRight = rightSpace >= leftSpace;
        var alignTop = bottomSpace >= topSpace;

        flyout.Placement = (placeRight, alignTop) switch
        {
            (true, true) => PlacementMode.RightEdgeAlignedTop,
            (true, false) => PlacementMode.RightEdgeAlignedBottom,
            (false, true) => PlacementMode.LeftEdgeAlignedTop,
            _ => PlacementMode.LeftEdgeAlignedBottom
        };

        var renderScale = (RenderScaling <= 0 ? 1d : RenderScaling) * _uiScale.Scale;
        var availableWidth = Math.Max(0d, (placeRight ? rightSpace : leftSpace) / renderScale - 16d);
        var availableHeight = Math.Max(
            0d,
            (alignTop ? workArea.Bottom - topLeft.Y : bottomRight.Y - workArea.Y) / renderScale - 16d);

        flyoutView.ConfigureViewport(availableWidth, availableHeight);
    }

    private bool TryGetCombatantDetailsFlyout(out Flyout flyout, out CombatantDetailsFlyoutView flyoutView)
    {
        if (GetValue(FlyoutBase.AttachedFlyoutProperty) is Flyout { Content: CombatantDetailsFlyoutView content } attachedFlyout)
        {
            flyout = attachedFlyout;
            flyoutView = content;
            return true;
        }

        flyout = null!;
        flyoutView = null!;
        return false;
    }
}
