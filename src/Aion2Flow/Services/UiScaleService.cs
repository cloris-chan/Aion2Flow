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
    private int _disposeState;

    public void RegisterWindow(Window window) => RegisterWindow(window, null);

    public void RegisterWindow(Window window, Func<int>? scalePercentProvider)
    {
        if (Volatile.Read(ref _disposeState) != 0)
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => RegisterWindow(window, scalePercentProvider));
            return;
        }

        if (!IsWindowUsable(window))
            return;

        if (!_windowStates.TryGetValue(window, out var state))
        {
            state = SurfaceScaleState.Create(window, scalePercentProvider ?? (() => _scalePercent));
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

    public void RefreshWindow(Window window)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => RefreshWindow(window));
            return;
        }

        if (_windowStates.TryGetValue(window, out var state) && IsWindowUsable(window) && Math.Abs(state.Scale - state.GetRequestedScale()) > 0.0001d)
            ApplyWindowScale(window, state);
    }

    public void UpdateWindowBaseSize(Window window)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => UpdateWindowBaseSize(window));
            return;
        }

        if (_windowStates.TryGetValue(window, out var state) && IsWindowUsable(window))
            state.UpdateBaseSize(window, state.Scale);
    }

    public double GetWindowScale(Window window)
        => _windowStates.TryGetValue(window, out var state) ? state.Scale : 1d;

    public void SetScalePercent(int percent)
    {
        if (Volatile.Read(ref _disposeState) != 0 || _scalePercent == percent)
            return;

        _scalePercent = percent;
        ApplyScaleToRegisteredWindows();
    }

    public double Scale => GetScale();

    public void Dispose()
    {
        if (Volatile.Read(ref _disposeState) != 0)
            return;

        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

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

    private bool IsWindowUsable(Window window) => Volatile.Read(ref _disposeState) == 0 && window.PlatformImpl is not null;

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
        var scale = state.GetRequestedScale();
        state.Host.LayoutTransform = new ScaleTransform(scale, scale);
        state.ApplyWindowScale(window, scale);
        state.Host.InvalidateMeasure();
    }

    private double GetScale() => _scalePercent / 100.0;

    private sealed class SurfaceScaleState
    {
        private readonly object? _originalContent;
        private readonly Control? _originalControl;
        private double _baseWidth;
        private double _baseHeight;
        private double _baseMinWidth;
        private double _baseMinHeight;
        private double _scale;

        private SurfaceScaleState(Window window, object? originalContent, Control? originalControl, LayoutTransformControl host, Func<int> scalePercentProvider)
        {
            _originalContent = originalContent;
            _originalControl = originalControl;
            Host = host;
            ScalePercentProvider = scalePercentProvider;
            _baseWidth = window.Width;
            _baseHeight = window.Height;
            _baseMinWidth = window.MinWidth;
            _baseMinHeight = window.MinHeight;
        }

        public LayoutTransformControl Host { get; }

        private Func<int> ScalePercentProvider { get; }

        public double Scale => _scale;

        public bool IsClosedSubscribed { get; set; }

        public static SurfaceScaleState Create(Window window, Func<int> scalePercentProvider)
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
            return new SurfaceScaleState(window, originalContent, originalControl, host, scalePercentProvider);
        }

        public double GetRequestedScale() => Math.Clamp(ScalePercentProvider(), 50, 200) / 100d;

        public void UpdateBaseSize(Window window, double scale)
        {
            if (double.IsFinite(window.Width) && window.Width > 0d)
                _baseWidth = window.Width / scale;
            if (double.IsFinite(window.Height) && window.Height > 0d)
                _baseHeight = window.Height / scale;
        }

        public void ApplyWindowScale(Window window, double scale)
        {
            _scale = scale;
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
