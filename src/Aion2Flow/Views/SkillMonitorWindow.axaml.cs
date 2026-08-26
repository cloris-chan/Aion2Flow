using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Cloris.Aion2Flow.Services.Overlay;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Settings;
using Cloris.Aion2Flow.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Cloris.Aion2Flow.Views;

public partial class SkillMonitorWindow : Window
{
    private readonly AvaloniaFrameClockService _frameClock;
    private readonly LocalizationService _localization;
    private readonly UiScaleService _uiScale;
    private readonly SettingsService _settingsService;
    private readonly DispatcherTimer _persistGeometryTimer;
    private bool _frameClockAttached;
    private bool _geometryPersistenceAttached;
    private double _observedWidth;

    public void SetAppFocusPresentation(bool appFocused)
    {
        MonitorRoot.Classes.Set("unfocused", !appFocused);
        MonitorRoot.Classes.Set("interactive", appFocused);
        CanResize = appFocused;
        NativeOverlayWindowStyles.SetInputTransparent(this, !appFocused);
    }

    public new SkillMonitorViewModel DataContext
    {
        get => (SkillMonitorViewModel)base.DataContext!;
        set => base.DataContext = value;
    }

    public SkillMonitorWindow()
    {
        _frameClock = Ioc.Default.GetRequiredService<AvaloniaFrameClockService>();
        _localization = Ioc.Default.GetRequiredService<LocalizationService>();
        _uiScale = Ioc.Default.GetRequiredService<UiScaleService>();
        _settingsService = Ioc.Default.GetRequiredService<SettingsService>();
        _persistGeometryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _persistGeometryTimer.Tick += PersistGeometryTimerTick;
        InitializeComponent();
        _localization.LanguageChanged += OnLanguageChanged;
        RefreshLocalizedText();
    }

    public SkillMonitorWindow(SkillMonitorViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RestoreGeometry();
        _uiScale.RegisterWindow(this, () => _settingsService.Current.SkillMonitorScalePercent);
        AttachGeometryPersistence();
        if (_frameClockAttached)
            return;

        _frameClockAttached = true;
        _frameClock.Frame += OnAnimationFrame;
        _frameClock.Attach(this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        PersistWindowGeometry();
        _persistGeometryTimer.Stop();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        if (_frameClockAttached)
        {
            _frameClock.Frame -= OnAnimationFrame;
            _frameClock.Detach(this);
            _frameClockAttached = false;
        }

        base.OnClosed(e);
    }

    private void OnAnimationFrame(object? sender, AvaloniaFrameEventArgs e) => DataContext.ProcessUiFrame(e.Timestamp);

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshLocalizedText();

    private void RefreshLocalizedText() => MonitorInteractionTitle.Text = _localization["SkillMonitor_Title"];

    private void DragWindow(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            BeginMoveDrag(e);
        }
    }

    private void ResizeWindow(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            BeginResizeDrag(WindowEdge.East, e);
        }
    }

    private void RestoreGeometry()
    {
        var settings = _settingsService.Current;
        Width = Math.Max(MinWidth, settings.SkillMonitorWidth);

        if (settings.SkillMonitorPosition is { } position)
            Position = new PixelPoint(position.X, position.Y);
    }

    private void AttachGeometryPersistence()
    {
        if (_geometryPersistenceAttached)
            return;

        _geometryPersistenceAttached = true;
        _observedWidth = ResolveWindowWidth();
        PositionChanged += (_, _) => ScheduleGeometryPersistence();
        SizeChanged += WindowSizeChanged;
    }

    private void WindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _uiScale.UpdateWindowBaseSize(this);
        var width = ResolveWindowWidth();
        if (!double.IsFinite(width) || width <= 0d || Math.Abs(width - _observedWidth) <= 0.5d)
            return;

        _observedWidth = width;
        ScheduleGeometryPersistence();
    }

    private void ScheduleGeometryPersistence()
    {
        _persistGeometryTimer.Stop();
        _persistGeometryTimer.Start();
    }

    private void PersistGeometryTimerTick(object? sender, EventArgs e)
    {
        _persistGeometryTimer.Stop();
        PersistWindowGeometry();
    }

    private void PersistWindowGeometry()
    {
        if (!_geometryPersistenceAttached)
            return;

        var scale = _uiScale.GetWindowScale(this);
        var windowWidth = ResolveWindowWidth();
        if (!double.IsFinite(scale) || scale <= 0d || !double.IsFinite(windowWidth))
            return;

        var width = (int)Math.Round(windowWidth / scale, MidpointRounding.AwayFromZero);
        if (width <= 0)
            return;

        _settingsService.Update(settings =>
        {
            settings.SkillMonitorPosition = new System.Drawing.Point(Position.X, Position.Y);
            settings.SkillMonitorWidth = width;
        });
    }

    private double ResolveWindowWidth()
        => double.IsFinite(Bounds.Width) && Bounds.Width > 0d
            ? Bounds.Width
            : Width;
}
