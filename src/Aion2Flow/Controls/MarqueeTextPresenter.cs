using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Cloris.Aion2Flow.Services;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Cloris.Aion2Flow.Controls;

internal sealed class MarqueeTextPresenter : Decorator
{
    private const double OverflowTolerance = 0.5;

    private readonly Grid _content;
    private readonly TranslateTransform _translation = new();
    private AvaloniaFrameClockService? _frameClock;
    private TimeSpan? _cycleStartedAt;
    private double _overflowDistance;
    private bool _isOverflowing;
    private bool _isFrameSubscribed;

    public MarqueeTextPresenter()
    {
        ClipToBounds = true;
        TextBlock = new TextBlock
        {
            Name = "PART_Text",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap
        };
        TextBlock.Classes.Add("IconTextDisplayText");
        _content = new Grid
        {
            RenderTransform = _translation,
            Children = { TextBlock }
        };
        Child = _content;
    }

    public TextBlock TextBlock { get; }

    public void Restart()
    {
        _cycleStartedAt = null;
        ResetVisualState();
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _content.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        var desired = _content.DesiredSize;
        var width = double.IsInfinity(availableSize.Width)
            ? desired.Width
            : Math.Min(desired.Width, Math.Max(0, availableSize.Width));
        return new Size(width, desired.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var contentWidth = Math.Max(finalSize.Width, _content.DesiredSize.Width);
        _content.Arrange(new Rect(0, 0, contentWidth, finalSize.Height));
        UpdateOverflow(Math.Max(0, contentWidth - finalSize.Width), finalSize.Width > 0);
        return finalSize;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateFrameSubscription();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SetFrameSubscription(false);
        _cycleStartedAt = null;
        ResetVisualState();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnAnimationFrame(object? sender, AvaloniaFrameEventArgs e)
    {
        if (!_isOverflowing)
            return;

        var timestamp = e.Timestamp;
        if (_cycleStartedAt is null || timestamp < _cycleStartedAt.Value)
            _cycleStartedAt = timestamp;

        var elapsed = (timestamp - _cycleStartedAt.Value).TotalMilliseconds;
        var state = MarqueeAnimationCycle.Resolve(elapsed, _overflowDistance);
        _translation.X = state.Offset;
        _content.Opacity = state.Opacity;
    }

    private void UpdateOverflow(double overflowDistance, bool hasViewport)
    {
        var isOverflowing = hasViewport && overflowDistance > OverflowTolerance;
        if (_isOverflowing != isOverflowing || Math.Abs(_overflowDistance - overflowDistance) > OverflowTolerance)
        {
            _isOverflowing = isOverflowing;
            _overflowDistance = overflowDistance;
            _cycleStartedAt = null;
            ResetVisualState();
        }

        UpdateFrameSubscription();
    }

    private void UpdateFrameSubscription()
    {
        SetFrameSubscription(_isOverflowing && TopLevel.GetTopLevel(this) is not null);
    }

    private void SetFrameSubscription(bool subscribe)
    {
        if (_isFrameSubscribed == subscribe)
            return;

        _frameClock ??= Ioc.Default.GetRequiredService<AvaloniaFrameClockService>();
        if (subscribe)
            _frameClock.Frame += OnAnimationFrame;
        else
            _frameClock.Frame -= OnAnimationFrame;
        _isFrameSubscribed = subscribe;
    }

    private void ResetVisualState()
    {
        _translation.X = 0;
        _content.Opacity = 1;
    }
}

internal static class MarqueeAnimationCycle
{
    internal const double StartHoldMilliseconds = 1_000;
    internal const double EndHoldMilliseconds = 600;
    internal const double FadeOutMilliseconds = 280;
    internal const double FadeInMilliseconds = 320;
    internal const double PixelsPerSecond = 32;
    internal const double MinimumScrollMilliseconds = 600;

    public static MarqueeVisualState Resolve(double elapsedMilliseconds, double overflowDistance)
    {
        if (overflowDistance <= 0 || !double.IsFinite(overflowDistance))
            return new MarqueeVisualState(0, 1);

        var scrollMilliseconds = ResolveScrollDuration(overflowDistance);
        var total = StartHoldMilliseconds + scrollMilliseconds + EndHoldMilliseconds + FadeOutMilliseconds + FadeInMilliseconds;
        var elapsed = double.IsFinite(elapsedMilliseconds) ? Math.Max(0, elapsedMilliseconds) % total : 0;

        if (elapsed < StartHoldMilliseconds)
            return new MarqueeVisualState(0, 1);

        elapsed -= StartHoldMilliseconds;
        if (elapsed < scrollMilliseconds)
            return new MarqueeVisualState(-overflowDistance * elapsed / scrollMilliseconds, 1);

        elapsed -= scrollMilliseconds;
        if (elapsed < EndHoldMilliseconds)
            return new MarqueeVisualState(-overflowDistance, 1);

        elapsed -= EndHoldMilliseconds;
        if (elapsed < FadeOutMilliseconds)
            return new MarqueeVisualState(-overflowDistance, 1 - elapsed / FadeOutMilliseconds);

        elapsed -= FadeOutMilliseconds;
        return new MarqueeVisualState(0, Math.Clamp(elapsed / FadeInMilliseconds, 0, 1));
    }

    public static double ResolveScrollDuration(double overflowDistance)
        => Math.Max(MinimumScrollMilliseconds, Math.Max(0, overflowDistance) / PixelsPerSecond * 1_000);
}

internal readonly record struct MarqueeVisualState(double Offset, double Opacity);
