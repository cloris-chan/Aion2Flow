using Avalonia.Controls;

namespace Cloris.Aion2Flow.Views;

internal sealed class PlaybackWindowController
{
    private Window? _window;

    internal bool TryActivate()
    {
        var window = _window;
        if (window is null)
            return false;

        if (!window.IsVisible)
        {
            Untrack(window);
            return false;
        }

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
        return true;
    }

    internal void Show(Window window, Window owner)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(owner);
        if (_window is not null)
            throw new InvalidOperationException("A playback window is already open.");

        _window = window;
        window.Closed += WindowClosed;
        try
        {
            window.Show(owner);
        }
        catch
        {
            Untrack(window);
            throw;
        }
    }

    internal void Close()
    {
        var window = _window;
        if (window is null)
            return;

        Untrack(window);
        if (window.IsVisible)
            window.Close();
    }

    private void WindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window window && ReferenceEquals(_window, window))
            Untrack(window);
    }

    private void Untrack(Window window)
    {
        window.Closed -= WindowClosed;
        if (ReferenceEquals(_window, window))
            _window = null;
    }
}
