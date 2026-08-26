using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Views;

public partial class SkillMonitorSettingsView : UserControl
{
    public SkillMonitorSettingsView() => InitializeComponent();

    private void SkillMonitorScaleSliderLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Slider slider)
        {
            slider.RemoveHandler(InputElement.PointerReleasedEvent, SkillMonitorScaleSliderPointerReleased);
            slider.AddHandler(InputElement.PointerReleasedEvent, SkillMonitorScaleSliderPointerReleased, handledEventsToo: true);
        }
    }

    private void SkillMonitorScaleSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
        => ApplyScaleFromSlider(sender);

    private void SkillMonitorScaleSliderKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            ApplyScaleFromSlider(sender);
    }

    private void ApplyScaleFromSlider(object? sender)
    {
        if (sender is Slider slider && DataContext is SkillMonitorSettingsViewModel viewModel)
            viewModel.SkillMonitorScalePercent = (int)Math.Round(slider.Value, MidpointRounding.AwayFromZero);
    }
}
