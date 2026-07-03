using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace Cloris.Aion2Flow.Controls;

public sealed class FractionalProgressBar : Control
{
    public static readonly DirectProperty<FractionalProgressBar, double> ValueProperty = AvaloniaProperty.RegisterDirect<FractionalProgressBar, double>(nameof(Value), control => control.Value, (control, value) => control.Value = value);

    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<FractionalProgressBar, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> BackgroundProperty = AvaloniaProperty.Register<FractionalProgressBar, IBrush?>(nameof(Background));

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty = AvaloniaProperty.Register<FractionalProgressBar, CornerRadius>(nameof(CornerRadius));

    private ScrollViewer? _viewportOwner;

    static FractionalProgressBar()
    {
        AffectsRender<FractionalProgressBar>(ValueProperty, FillProperty, BackgroundProperty, CornerRadiusProperty);
    }

    public double Value
    {
        get;
        set => SetAndRaise(ValueProperty, ref field, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        AttachViewportOwner();
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        DetachViewportOwner();
        base.OnDetachedFromLogicalTree(e);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var viewport = ResolveViewport(bounds.Width);
        if (viewport.Width <= 0)
            return;

        var roundedRect = new RoundedRect(viewport, CornerRadius);
        using var clip = context.PushClip(roundedRect);

        var background = Background;
        if (background is not null)
            context.DrawRectangle(background, null, roundedRect);

        var ratio = double.IsFinite(Value) ? Math.Clamp(Value, 0d, 1d) : 0d;
        if (ratio <= 0)
            return;

        var fill = Fill;
        if (fill is null)
            return;

        context.FillRectangle(fill, new Rect(viewport.X, 0, viewport.Width * ratio, bounds.Height));
    }

    private Rect ResolveViewport(double boundsWidth)
    {
        var owner = _viewportOwner;
        if (owner is null)
            return new Rect(0, 0, boundsWidth, Bounds.Height);

        var left = Math.Clamp(owner.Offset.X, 0d, Math.Max(0d, boundsWidth));
        var width = owner.Viewport.Width;
        if (!double.IsFinite(width) || width <= 0)
            width = boundsWidth;

        return new Rect(left, 0, Math.Min(width, Math.Max(0d, boundsWidth - left)), Bounds.Height);
    }

    private void AttachViewportOwner()
    {
        DetachViewportOwner();

        var ancestor = this.GetLogicalAncestors().OfType<ScrollViewer>().FirstOrDefault();
        if (ancestor is null)
            return;

        _viewportOwner = ancestor;
        ancestor.ScrollChanged += OnViewportOwnerScrollChanged;
    }

    private void DetachViewportOwner()
    {
        if (_viewportOwner is not null)
        {
            _viewportOwner.ScrollChanged -= OnViewportOwnerScrollChanged;
            _viewportOwner = null;
        }
    }

    private void OnViewportOwnerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        InvalidateVisual();
    }
}
