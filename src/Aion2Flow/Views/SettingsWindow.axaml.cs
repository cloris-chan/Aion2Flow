using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Hotkeys;
using Cloris.Aion2Flow.Services.Settings;
using Cloris.Aion2Flow.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Cloris.Aion2Flow.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly GameResourceService _resources;
    private readonly SettingsService _settings;
    private SkillMonitorSettingsViewModel? _monitorSettings;

    public SettingsWindow() : this(
        Ioc.Default.GetRequiredService<SettingsViewModel>(),
        Ioc.Default.GetRequiredService<GameResourceService>(),
        Ioc.Default.GetRequiredService<SettingsService>())
    {
    }

    internal SettingsWindow(SettingsViewModel viewModel, GameResourceService resources, SettingsService settings)
    {
        _viewModel = viewModel;
        _resources = resources;
        _settings = settings;
        InitializeComponent();
        DataContext = viewModel;
        UiScaleSlider.AddHandler(PointerReleasedEvent, UiScaleSliderPointerReleased, handledEventsToo: true);
        Deactivated += (_, _) => CancelHotkeyCapture();
    }

    protected override void OnClosed(EventArgs e)
    {
        CancelHotkeyCapture();
        _monitorSettings?.Dispose();
        base.OnClosed(e);
    }

    private void CategoryChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender))
            CancelHotkeyCapture();
    }

    private void SkillMonitorSettingsLoaded(object? sender, RoutedEventArgs e)
        => MonitorConfiguration.DataContext = _monitorSettings ??= new SkillMonitorSettingsViewModel(_resources, _settings, _viewModel.Localization);

    private void UiScaleSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
        => ApplyUiScale();

    private void UiScaleSliderKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            ApplyUiScale();
    }

    private void ApplyUiScale() => _viewModel.UiScalePercent = (int)UiScaleSlider.Value;

    private void HotkeyEditorPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: GlobalHotkeyAction action } editor ||
            !e.GetCurrentPoint(editor).Properties.IsLeftButtonPressed)
            return;

        editor.Focus();
        _viewModel.BeginCaptureHotkey(action);
        e.Handled = true;
    }

    private void HotkeyEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Border { Tag: GlobalHotkeyAction action })
            return;

        if (_viewModel.CapturingHotkeyAction != action)
        {
            if (e.Key is Key.Enter or Key.Space && e.KeyModifiers == KeyModifiers.None)
            {
                _viewModel.BeginCaptureHotkey(action);
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Escape)
        {
            _viewModel.CancelCaptureHotkey(action);
            e.Handled = true;
        }
        else if (HotkeyDefinition.FromKeyEvent(e.KeyModifiers, e.Key) is { } hotkey)
        {
            _viewModel.ApplyCapturedHotkey(action, hotkey);
            e.Handled = true;
        }
    }

    private void HotkeyEditorLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is Border { Tag: GlobalHotkeyAction action })
            _viewModel.CancelCaptureHotkey(action);
    }

    private void CancelHotkeyCapture()
    {
        if (_viewModel.CapturingHotkeyAction is { } action)
            _viewModel.CancelCaptureHotkey(action);
    }
}
