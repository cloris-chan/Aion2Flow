using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Cloris.Aion2Flow.Controls;

internal static class SmoothScrollBehavior
{
    private const double WheelStep = 56;
    private static readonly ConditionalWeakTable<ScrollViewer, ViewerState> ViewerStates = new();
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        Control.LoadedEvent.AddClassHandler<ScrollViewer>(static (viewer, _) => GetState(viewer).Activate());
        Control.UnloadedEvent.AddClassHandler<ScrollViewer>(static (viewer, _) => GetState(viewer).Deactivate());
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<ScrollViewer>(static (viewer, _) => GetState(viewer).RefreshPresenter());
        InputElement.PointerWheelChangedEvent.AddClassHandler<ScrollViewer>(OnTunnelPointerWheelChanged, RoutingStrategies.Tunnel);
    }

    private static ViewerState GetState(ScrollViewer viewer)
        => ViewerStates.GetValue(viewer, static owner => new ViewerState(owner));

    private static void OnTunnelPointerWheelChanged(ScrollViewer viewer, PointerWheelEventArgs e)
    {
        var state = GetState(viewer);
        if (e.Source is Visual source && state.ContainsContentSource(source))
            return;

        state.TryHandlePointerWheel(e);
    }

    private static Vector CalculateTarget(
        ScrollViewer viewer,
        Vector origin,
        Vector wheelDelta,
        KeyModifiers modifiers)
    {
        var target = CalculateTarget(origin, viewer.Extent, viewer.Viewport, wheelDelta, modifiers, viewer.FlowDirection);
        return new Vector(
            viewer.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled ? origin.X : target.X,
            viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled ? origin.Y : target.Y);
    }

    internal static Vector CalculateTarget(
        Vector origin,
        Size extent,
        Size viewport,
        Vector wheelDelta,
        KeyModifiers modifiers,
        FlowDirection flowDirection = FlowDirection.LeftToRight)
    {
        var resolvedDelta = ResolveWheelDelta(wheelDelta, modifiers, flowDirection);

        return new Vector(
            Math.Clamp(origin.X - (resolvedDelta.X * WheelStep), 0, Math.Max(0, extent.Width - viewport.Width)),
            Math.Clamp(origin.Y - (resolvedDelta.Y * WheelStep), 0, Math.Max(0, extent.Height - viewport.Height)));
    }

    private static Vector ResolveWheelDelta(Vector wheelDelta, KeyModifiers modifiers, FlowDirection flowDirection)
    {
        if (modifiers == KeyModifiers.Shift && Math.Abs(wheelDelta.X) <= 0.01)
            return new Vector(wheelDelta.Y, wheelDelta.X);

        return flowDirection == FlowDirection.RightToLeft
            ? new Vector(-wheelDelta.X, wheelDelta.Y)
            : wheelDelta;
    }

    private static bool HasPendingMovementForWheel(
        ScrollViewer viewer,
        Vector current,
        Vector target,
        Vector wheelDelta,
        KeyModifiers modifiers)
    {
        var resolvedDelta = ResolveWheelDelta(wheelDelta, modifiers, viewer.FlowDirection);
        var hasHorizontalMovement = viewer.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled &&
                                    Math.Abs(resolvedDelta.X) > 0.01 &&
                                    Math.Abs(target.X - current.X) > 0.1;
        var hasVerticalMovement = viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled &&
                                  Math.Abs(resolvedDelta.Y) > 0.01 &&
                                  Math.Abs(target.Y - current.Y) > 0.1;
        return hasHorizontalMovement || hasVerticalMovement;
    }

    internal static Vector InterpolateOffset(Vector start, Vector target, double progress)
    {
        var clampedProgress = Math.Clamp(progress, 0, 1);
        var eased = 1 - Math.Pow(1 - clampedProgress, 3);
        return new Vector(
            start.X + ((target.X - start.X) * eased),
            start.Y + ((target.Y - start.Y) * eased));
    }

    internal static void AdvanceAnimation(ScrollViewer viewer, TimeSpan timestamp)
    {
        if (ViewerStates.TryGetValue(viewer, out var state))
            state.AdvanceAnimation(timestamp);
    }

    private sealed class ViewerState
    {
        private readonly ScrollViewer _viewer;
        private readonly ScrollAnimation _animation;
        private readonly EventHandler<PointerWheelEventArgs> _pointerWheelHandler;
        private ContentPresenter? _presenter;
        private Control? _contentRoot;
        private bool _isActive;

        public ViewerState(ScrollViewer viewer)
        {
            _viewer = viewer;
            _animation = new ScrollAnimation(viewer);
            _pointerWheelHandler = OnPointerWheelChanged;
        }

        public void Activate()
        {
            if (_isActive)
                return;

            _isActive = true;
            _viewer.PropertyChanged += OnViewerPropertyChanged;
            RefreshPresenter();
        }

        public void Deactivate()
        {
            if (!_isActive)
                return;

            _isActive = false;
            _viewer.PropertyChanged -= OnViewerPropertyChanged;
            _animation.Cancel();
            SetContentRoot(null);
            if (_presenter is not null)
                _presenter.PropertyChanged -= OnPresenterPropertyChanged;
            _presenter = null;
        }

        public void RefreshPresenter()
        {
            if (!_isActive)
                return;

            var presenter = _viewer.Presenter;
            if (ReferenceEquals(_presenter, presenter))
            {
                SetContentRoot(presenter?.Child);
                return;
            }

            SetContentRoot(null);
            if (_presenter is not null)
                _presenter.PropertyChanged -= OnPresenterPropertyChanged;
            _presenter = presenter;
            if (_presenter is not null)
            {
                _presenter.PropertyChanged += OnPresenterPropertyChanged;
                SetContentRoot(_presenter.Child);
            }
        }

        public bool ContainsContentSource(Visual source)
        {
            if (_contentRoot is null)
                return false;

            for (Visual? current = source; current is not null && !ReferenceEquals(current, _viewer); current = current.GetVisualParent())
            {
                if (ReferenceEquals(current, _contentRoot))
                    return true;
            }

            return false;
        }

        public void TryHandlePointerWheel(PointerWheelEventArgs e)
        {
            var origin = _animation.IsActive ? _animation.Target : _viewer.Offset;
            var target = CalculateTarget(_viewer, origin, e.Delta, e.KeyModifiers);
            if (target == origin)
            {
                if (_animation.IsActive &&
                    HasPendingMovementForWheel(_viewer, _viewer.Offset, _animation.Target, e.Delta, e.KeyModifiers))
                {
                    e.Handled = true;
                }
                return;
            }

            _animation.Start(target);
            e.Handled = true;
        }

        public void AdvanceAnimation(TimeSpan timestamp)
            => _animation.Advance(timestamp);

        private void SetContentRoot(Control? contentRoot)
        {
            if (ReferenceEquals(_contentRoot, contentRoot))
                return;

            _contentRoot?.RemoveHandler(InputElement.PointerWheelChangedEvent, _pointerWheelHandler);
            _contentRoot = contentRoot;
            _contentRoot?.AddHandler(InputElement.PointerWheelChangedEvent, _pointerWheelHandler, RoutingStrategies.Bubble);
        }

        private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
            => TryHandlePointerWheel(e);

        private void OnPresenterPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == ContentPresenter.ChildProperty)
                SetContentRoot(e.GetNewValue<Control?>());
        }

        private void OnViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == ScrollViewer.OffsetProperty)
                _animation.CancelForExternalOffsetChange();
        }
    }

    private sealed class ScrollAnimation
    {
        private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(150);
        private readonly ScrollViewer _viewer;
        private readonly Action<TimeSpan> _advance;
        private Vector _start;
        private TimeSpan? _startTimestamp;
        private TimeSpan? _lastTimestamp;
        private bool _isApplyingOffset;

        public ScrollAnimation(ScrollViewer viewer)
        {
            _viewer = viewer;
            _advance = Advance;
        }

        public bool IsActive { get; private set; }
        public Vector Target { get; private set; }

        public void Start(Vector target)
        {
            var wasActive = IsActive;
            _start = _viewer.Offset;
            Target = target;
            _startTimestamp = wasActive ? _lastTimestamp : null;
            IsActive = true;
            if (!wasActive)
                RequestFrame();
        }

        public void Cancel()
        {
            IsActive = false;
            Target = _viewer.Offset;
            _startTimestamp = null;
            _lastTimestamp = null;
        }

        public void CancelForExternalOffsetChange()
        {
            if (IsActive && !_isApplyingOffset)
                Cancel();
        }

        private void RequestFrame()
        {
            var topLevel = TopLevel.GetTopLevel(_viewer);
            if (topLevel is null)
            {
                ApplyOffset(Target);
                Cancel();
                return;
            }

            topLevel.RequestAnimationFrame(_advance);
        }

        public void Advance(TimeSpan timestamp)
        {
            if (!IsActive)
                return;

            _startTimestamp ??= timestamp;
            _lastTimestamp = timestamp;
            var progress = Math.Clamp((timestamp - _startTimestamp.Value).TotalMilliseconds / Duration.TotalMilliseconds, 0, 1);
            ApplyOffset(InterpolateOffset(_start, Target, progress));

            if (progress >= 1)
            {
                Cancel();
                return;
            }

            RequestFrame();
        }

        private void ApplyOffset(Vector offset)
        {
            _isApplyingOffset = true;
            try
            {
                _viewer.SetCurrentValue(ScrollViewer.OffsetProperty, offset);
            }
            finally
            {
                _isApplyingOffset = false;
            }
        }
    }
}
