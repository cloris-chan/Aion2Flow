using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Threading;
using Cloris.Aion2Flow.Services.Settings;

namespace Cloris.Aion2Flow.Services;

public sealed class UiScaleService(SettingsService settingsService) : IDisposable
{
    private readonly ConditionalWeakTable<Window, SurfaceScaleState> _windowStates = [];
    private readonly List<WeakReference<Window>> _windows = [];
    private int _scalePercent = settingsService.Current.UiScalePercent;
    private bool _disposed;

    public void RegisterWindow(Window window)
    {
        if (_disposed)
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => RegisterWindow(window));
            return;
        }

        if (!IsWindowUsable(window))
            return;

        if (!_windowStates.TryGetValue(window, out var state))
        {
            state = SurfaceScaleState.Create(window);
            _windowStates.Add(window, state);
        }

        if (!state.IsClosedSubscribed)
        {
            window.Closed += OnWindowClosed;
            state.IsClosedSubscribed = true;
        }

        if (!ContainsWindow(window))
            _windows.Add(new WeakReference<Window>(window));

        ApplyWindowScale(window, state);
    }

    public void SetScalePercent(int percent)
    {
        if (_scalePercent == percent)
            return;

        _scalePercent = percent;
        ApplyScaleToRegisteredWindows();
    }

    public double Scale => GetScale();

    public void Dispose()
    {
        _disposed = true;
        for (var i = _windows.Count - 1; i >= 0; i--)
        {
            if (_windows[i].TryGetTarget(out var window))
                DetachWindow(window);
        }

        _windows.Clear();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
            RemoveWindow(window);
    }

    private void RemoveWindow(Window window)
    {
        DetachWindow(window);
        for (var i = _windows.Count - 1; i >= 0; i--)
        {
            if (!_windows[i].TryGetTarget(out var existing) || ReferenceEquals(existing, window))
                _windows.RemoveAt(i);
        }
    }

    private void DetachWindow(Window window)
    {
        if (_windowStates.TryGetValue(window, out var state))
        {
            if (state.IsClosedSubscribed)
                window.Closed -= OnWindowClosed;

            state.Detach(window);
        }

        _windowStates.Remove(window);
    }

    private bool IsWindowUsable(Window window) => !_disposed && window.PlatformImpl is not null;

    private bool ContainsWindow(Window window)
    {
        for (var i = _windows.Count - 1; i >= 0; i--)
        {
            if (!_windows[i].TryGetTarget(out var existing))
            {
                _windows.RemoveAt(i);
                continue;
            }

            if (!IsWindowUsable(existing))
            {
                DetachWindow(existing);
                _windows.RemoveAt(i);
                continue;
            }

            if (ReferenceEquals(existing, window))
                return true;
        }

        return false;
    }

    private void ApplyScaleToRegisteredWindows()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ApplyScaleToRegisteredWindows);
            return;
        }

        for (var i = _windows.Count - 1; i >= 0; i--)
        {
            if (_windows[i].TryGetTarget(out var window) && IsWindowUsable(window) && _windowStates.TryGetValue(window, out var state))
            {
                ApplyWindowScale(window, state);
            }
            else
            {
                if (_windows[i].TryGetTarget(out window))
                    DetachWindow(window);
                _windows.RemoveAt(i);
            }
        }
    }

    private void ApplyWindowScale(Window window, SurfaceScaleState state)
    {
        var scale = GetScale();
        state.Host.LayoutTransform = new ScaleTransform(scale, scale);
        state.ApplyWindowScale(window, scale);
        state.Host.InvalidateMeasure();
    }

    private double GetScale() => _scalePercent / 100.0;

    private sealed class SurfaceScaleState
    {
        private readonly object? _originalContent;
        private readonly Control? _originalControl;
        private readonly double _baseWidth;
        private readonly double _baseHeight;
        private readonly double _baseMinWidth;
        private readonly double _baseMinHeight;

        private SurfaceScaleState(Window window, object? originalContent, Control? originalControl, LayoutTransformControl host)
        {
            _originalContent = originalContent;
            _originalControl = originalControl;
            Host = host;
            _baseWidth = window.Width;
            _baseHeight = window.Height;
            _baseMinWidth = window.MinWidth;
            _baseMinHeight = window.MinHeight;
        }

        public LayoutTransformControl Host { get; }

        public bool IsClosedSubscribed { get; set; }

        public static SurfaceScaleState Create(Window window)
        {
            var originalContent = window.Content;
            var originalControl = originalContent as Control;
            var host = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(1d, 1d),
                UseRenderTransform = false
            };

            window.Content = null;
            host.Child = originalControl ?? (originalContent is null ? null : new ContentPresenter { Content = originalContent });
            window.Content = host;
            return new SurfaceScaleState(window, originalContent, originalControl, host);
        }

        public void ApplyWindowScale(Window window, double scale)
        {
            if (double.IsFinite(_baseWidth) && _baseWidth > 0d)
                window.Width = _baseWidth * scale;
            if (double.IsFinite(_baseHeight) && _baseHeight > 0d)
                window.Height = _baseHeight * scale;
            if (double.IsFinite(_baseMinWidth) && _baseMinWidth > 0d)
                window.MinWidth = _baseMinWidth * scale;
            if (double.IsFinite(_baseMinHeight) && _baseMinHeight > 0d)
                window.MinHeight = _baseMinHeight * scale;
        }

        public void Detach(Window window)
        {
            if (ReferenceEquals(window.Content, Host))
            {
                Host.Child = null;
                window.Content = _originalContent;
            }
            else if (_originalControl is not null && ReferenceEquals(Host.Child, _originalControl))
            {
                Host.Child = null;
            }
        }
    }
}
