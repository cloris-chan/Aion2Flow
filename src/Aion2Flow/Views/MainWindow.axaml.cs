using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Cloris.Aion2Flow.Assets.Icons;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Services.Hotkeys;
using Cloris.Aion2Flow.Services.Settings;
using Cloris.Aion2Flow.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Cloris.Aion2Flow.Views;

public partial class MainWindow : Window
{
    private readonly GlobalHotkeyService _globalHotkeyService;
    private readonly SettingsService _settingsService;
    private readonly UiFrameBatchService _frameBatchService;
    private readonly Action<TimeSpan> _animationFrameCallback;
    private bool _hotkeyAttached;
    private bool _frameLoopRunning;

    public new MainViewModel DataContext { get => (MainViewModel)base.DataContext!; set => base.DataContext = value; }

    public MainWindow()
    {
        DataContext = Ioc.Default.GetRequiredService<MainViewModel>();
        _globalHotkeyService = Ioc.Default.GetRequiredService<GlobalHotkeyService>();
        _settingsService = Ioc.Default.GetRequiredService<SettingsService>();
        _frameBatchService = Ioc.Default.GetRequiredService<UiFrameBatchService>();
        _animationFrameCallback = OnAnimationFrame;
        DataContext.InitializeAsync().ConfigureAwait(false);
        AvaloniaXamlLoader.Load(this);
        DataContext.EncounterHistory.CollectionChanged += OnEncounterHistoryCollectionChanged;
        RebuildEncounterHistoryMenuItems();
        _globalHotkeyService.Triggered += OnGlobalHotkeyTriggered;
        if (_settingsService.Current.MainWindowPosition.HasValue)
        {
            Position = new(_settingsService.Current.MainWindowPosition.Value.X, _settingsService.Current.MainWindowPosition.Value.Y);
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RebuildEncounterHistoryMenuItems();
        AttachGlobalHotkeyHook();
        StartFrameLoop();
    }

    private void StartFrameLoop()
    {
        if (_frameLoopRunning)
        {
            return;
        }

        _frameLoopRunning = true;
        RequestAnimationFrame(_animationFrameCallback);
    }

    private void OnAnimationFrame(TimeSpan timestamp)
    {
        if (!_frameLoopRunning)
        {
            return;
        }

        try
        {
            DataContext.ProcessUiFrame();
            _frameBatchService.FlushFrame();
        }
        finally
        {
            if (_frameLoopRunning)
            {
                RequestAnimationFrame(_animationFrameCallback);
            }
        }
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
        _hotkeyAttached = true;
    }

    private nint WndProcHook(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == GlobalHotkeyService.WmHotkey)
        {
            _globalHotkeyService.HandleWindowMessage(msg, wParam);
        }
        return default;
    }

    private void OnGlobalHotkeyTriggered()
    {
        if (!DataContext.IsCapturing)
        {
            return;
        }
        if (DataContext.ResetCommand.CanExecute(null))
        {
            DataContext.ResetCommand.Execute(null);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        DataContext.EncounterHistory.CollectionChanged -= OnEncounterHistoryCollectionChanged;
        _globalHotkeyService.Triggered -= OnGlobalHotkeyTriggered;
        _globalHotkeyService.SetHotkey(null);
        _settingsService.Update(settings => settings.MainWindowPosition = new(Position.X, Position.Y));
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _frameLoopRunning = false;
        base.OnClosed(e);
        DataContext.DisposeAsync().AsTask().ConfigureAwait(false);
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
                flyout.ShowAt(this);
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
            placeholder.Classes.Add("FlyoutMenuItemPlaceholder");
            menu.Items.Add(placeholder);
            return;
        }

        foreach (var item in DataContext.EncounterHistory)
        {
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            DisplayContextProvider.SetDisplayContext(header, item.DisplayContext);
            header.Children.Add(new MapDisplay
            {
                MapId = item.MapId,
                UseBrackets = true,
                VerticalAlignment = VerticalAlignment.Center
            });
            header.Children.Add(new TextBlock
            {
                Text = item.ArchivedAtText,
                VerticalAlignment = VerticalAlignment.Center
            });
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
            playbackButton.Classes.Add("HistoryPlaybackButton");
            if (playbackButton.Content is Avalonia.Controls.Shapes.Path playbackIcon)
            {
                playbackIcon.Classes.Add("Glyph");
                playbackIcon.Classes.Add("GlyphSm");
            }

            playbackButton.Click += EncounterHistoryPlaybackButtonClicked;
            header.Children.Add(playbackButton);

            var menuItem = new MenuItem
            {
                Header = header,
                Tag = item
            };
            menuItem.Classes.Add("FlyoutMenuItem");
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

        var renderScale = RenderScaling <= 0 ? 1d : RenderScaling;
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
