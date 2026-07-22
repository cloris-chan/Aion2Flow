using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Cloris.Aion2Flow.Views;

public partial class ScenePlaybackWindow : Window
{
    private readonly AvaloniaFrameClockService _frameClock;
    private readonly UiScaleService _uiScale;
    private bool _frameClockAttached;

    public new ScenePlaybackViewModel? DataContext { get => (ScenePlaybackViewModel?)base.DataContext; set => base.DataContext = value; }

    public ScenePlaybackWindow()
    {
        _frameClock = Ioc.Default.GetRequiredService<AvaloniaFrameClockService>();
        _uiScale = Ioc.Default.GetRequiredService<UiScaleService>();
        AvaloniaXamlLoader.Load(this);
    }

    public ScenePlaybackWindow(ScenePlaybackViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _uiScale.RegisterWindow(this);
        if (_frameClockAttached)
            return;

        _frameClockAttached = true;
        _frameClock.Frame += OnAnimationFrame;
        _frameClock.Attach(this);
    }

    private void OnAnimationFrame(object? sender, AvaloniaFrameEventArgs e)
    {
        DataContext?.ProcessUiFrame(e.Timestamp);
    }

    protected override async void OnClosed(EventArgs e)
    {
        if (_frameClockAttached)
        {
            _frameClock.Frame -= OnAnimationFrame;
            _frameClock.Detach(this);
            _frameClockAttached = false;
        }

        base.OnClosed(e);
        var dataContext = DataContext;
        DataContext = null;
        if (dataContext is not null)
            await dataContext.DisposeAsync();
    }

    private void TimelineSeekRequested(object? sender, PlaybackSeekRequestedEventArgs e)
    {
        DataContext?.RequestSeek(e.PositionMilliseconds);
    }

    private void CombatDetailSelectionRequested(object? sender, CombatDetailSelectionRequestedEventArgs e)
    {
        DataContext?.SelectCombatDetail(e.Category, e.SkillBaseKey, e.SkillDisplayName);
    }

    private void TimelinePointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var viewModel = DataContext;
        if (viewModel is null || sender is not Control control || control.Bounds.Width <= 0d || Math.Abs(e.Delta.Y) <= double.Epsilon)
            return;

        var viewport = viewModel.TimelineViewport;
        if (viewport.IsEmpty)
            return;

        var ratio = Math.Clamp(e.GetPosition(control).X / control.Bounds.Width, 0d, 1d);
        var anchor = viewport.StartMilliseconds + viewport.DurationMilliseconds * ratio;
        viewModel.ZoomTimelineAt(e.Delta.Y > 0d ? 0.5d : 2d, anchor);
        e.Handled = true;
    }
}
