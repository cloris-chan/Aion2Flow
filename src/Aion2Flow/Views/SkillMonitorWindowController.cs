using Avalonia.Controls;
using Avalonia.Threading;
using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Overlay;
using Cloris.Aion2Flow.Services.Settings;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Views;

internal sealed class SkillMonitorWindowController
{
    private readonly WinDivertCaptureService _captureService;
    private readonly GameResourceService _resources;
    private readonly LocalizationService _localization;
    private readonly SettingsService _settingsService;
    private readonly UiScaleService _uiScale;
    private readonly SettingsFlyoutViewModel _settings;
    private readonly ProcessForegroundWatcher _foregroundWatcher;
    private SkillMonitorWindow? _window;
    private Window? _owner;

    public SkillMonitorWindowController(
        WinDivertCaptureService captureService,
        GameResourceService resources,
        LocalizationService localization,
        SettingsService settingsService,
        UiScaleService uiScale,
        SettingsFlyoutViewModel settings,
        ProcessForegroundWatcher foregroundWatcher)
    {
        _captureService = captureService;
        _resources = resources;
        _localization = localization;
        _settingsService = settingsService;
        _uiScale = uiScale;
        _settings = settings;
        _foregroundWatcher = foregroundWatcher;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
        _settingsService.Changed += OnSettingsChanged;
        _foregroundWatcher.ForegroundChanged += OnForegroundChanged;
    }

    public void ShowOrActivate(Window owner)
    {
        if (!ReferenceEquals(_owner, owner))
            ClearOwner();
        _owner = owner;
        AttachOwnerEvents(owner);
        if (!_settings.SkillMonitorEnabled)
        {
            CloseWindow(clearOwner: false);
            return;
        }

        if (_window is { } existing)
        {
            if (!existing.IsVisible)
            {
                Untrack(existing);
            }
            else
            {
                if (existing.WindowState == WindowState.Minimized)
                    existing.WindowState = WindowState.Normal;
                SynchronizeWindowState(existing);
                existing.Activate();
                return;
            }
        }

        var window = new SkillMonitorWindow(
            new SkillMonitorViewModel(_captureService, _resources, _localization, _settingsService));
        _window = window;
        window.Closed += WindowClosed;
        window.Activated += WindowFocusChanged;
        window.Deactivated += WindowFocusChanged;
        try
        {
            window.Show(owner);
            SynchronizeWindowState(window);
        }
        catch
        {
            Untrack(window);
            throw;
        }
    }

    public void Close() => CloseWindow(clearOwner: true);

    private void CloseWindow(bool clearOwner)
    {
        var window = _window;
        if (window is null)
        {
            if (clearOwner)
                ClearOwner();
            return;
        }

        Untrack(window);
        if (window.IsVisible)
            window.Close();
        if (clearOwner)
            ClearOwner();
    }

    private void WindowClosed(object? sender, EventArgs e)
    {
        if (sender is SkillMonitorWindow window && ReferenceEquals(_window, window))
            Untrack(window);
    }

    private void Untrack(SkillMonitorWindow window)
    {
        window.Closed -= WindowClosed;
        window.Activated -= WindowFocusChanged;
        window.Deactivated -= WindowFocusChanged;
        if (ReferenceEquals(_window, window))
            _window = null;
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsFlyoutViewModel.SkillMonitorEnabled))
        {
            if (_settings.SkillMonitorEnabled && _owner is { } owner)
                ShowOrActivate(owner);
            else
                CloseWindow(clearOwner: false);
        }
        else if (e.PropertyName == nameof(SettingsFlyoutViewModel.IsAlwaysOnTop) && _window is { } window)
        {
            SynchronizeWindowState(window);
        }
        else if (e.PropertyName == nameof(SettingsFlyoutViewModel.EncounterTimeDisplayFormat) && _window is { } formatWindow)
        {
            formatWindow.DataContext.EncounterTimeDisplayFormat = _settings.EncounterTimeDisplayFormat;
        }
    }

    private void AttachOwnerEvents(Window owner)
    {
        owner.Activated -= WindowFocusChanged;
        owner.Deactivated -= WindowFocusChanged;
        owner.Activated += WindowFocusChanged;
        owner.Deactivated += WindowFocusChanged;
    }

    private void ClearOwner()
    {
        if (_owner is { } owner)
        {
            owner.Activated -= WindowFocusChanged;
            owner.Deactivated -= WindowFocusChanged;
        }

        _owner = null;
    }

    private void WindowFocusChanged(object? sender, EventArgs e)
    {
        if (_window is { } window)
            SynchronizeWindowState(window);
    }

    private void OnForegroundChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_window is { } window)
                SynchronizeWindowState(window);
        });
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_window is { } window)
                _uiScale.RefreshWindow(window);
        });
    }

    private void SynchronizeWindowState(SkillMonitorWindow window)
    {
        NativeOverlayWindowStyles.SetTopmostBand(window, _settings.IsAlwaysOnTop);
        window.SetAppFocusPresentation(IsApplicationFocused(window));
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_window, window))
            {
                NativeOverlayWindowStyles.SetTopmostBand(window, _settings.IsAlwaysOnTop);
                window.SetAppFocusPresentation(IsApplicationFocused(window));
            }
        });
    }

    private bool IsApplicationFocused(Window window)
        => NativeOverlayWindowStyles.IsCurrentProcessForeground() ||
           _owner?.IsActive == true ||
           window.IsActive;
}
