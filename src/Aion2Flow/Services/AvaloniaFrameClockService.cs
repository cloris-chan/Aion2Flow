using Avalonia.Controls;

namespace Cloris.Aion2Flow.Services;

public sealed class AvaloniaFrameClockService : IDisposable
{
    private readonly List<TopLevel> _topLevels = [];
    private bool _isRunning;
    private bool _isDisposed;
    private TopLevel? _activeTopLevel;
    private long _requestGeneration;

    public event EventHandler<AvaloniaFrameEventArgs>? Frame;

    public event EventHandler<AvaloniaFrameEventArgs>? FrameCompleted;

    public void Attach(TopLevel topLevel)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!_topLevels.Contains(topLevel))
            _topLevels.Add(topLevel);
        _activeTopLevel = topLevel;
        if (_isRunning)
            RequestNextFrame();
        else
            StartLoopIfNeeded();
    }

    public void Detach(TopLevel topLevel)
    {
        _topLevels.Remove(topLevel);
        if (_activeTopLevel == topLevel)
            _activeTopLevel = null;
        if (_topLevels.Count == 0)
        {
            _isRunning = false;
            _requestGeneration++;
        }
        else if (_isRunning)
        {
            RequestNextFrame();
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        _isRunning = false;
        _activeTopLevel = null;
        _topLevels.Clear();
    }

    private void StartLoopIfNeeded()
    {
        if (_isRunning || _topLevels.Count == 0)
            return;

        _isRunning = true;
        RequestNextFrame();
    }

    private void OnAnimationFrame(TimeSpan timestamp, long generation)
    {
        if (!_isRunning || _isDisposed || generation != _requestGeneration)
            return;

        try
        {
            var args = new AvaloniaFrameEventArgs(timestamp);
            Frame?.Invoke(this, args);
            FrameCompleted?.Invoke(this, args);
        }
        finally
        {
            if (_isRunning && !_isDisposed && _topLevels.Count > 0)
                RequestNextFrame();
            else
                _isRunning = false;
        }
    }

    private void RequestNextFrame()
    {
        var topLevel = ResolveTopLevel();
        if (topLevel is null)
        {
            _isRunning = false;
            return;
        }

        var generation = ++_requestGeneration;
        topLevel.RequestAnimationFrame(timestamp => OnAnimationFrame(timestamp, generation));
    }

    private TopLevel? ResolveTopLevel()
    {
        if (_activeTopLevel is not null && _topLevels.Contains(_activeTopLevel))
            return _activeTopLevel;

        _activeTopLevel = _topLevels.Count > 0 ? _topLevels[^1] : null;
        return _activeTopLevel;
    }
}

public sealed class AvaloniaFrameEventArgs(TimeSpan timestamp) : EventArgs
{
    public TimeSpan Timestamp { get; } = timestamp;
}
