using System.Diagnostics;
using System.Threading.Channels;
using Cloris.Aion2Flow.SceneRuntime.Playback;

namespace Cloris.Aion2Flow.Services;

public sealed class AvaloniaScenePlaybackTickSourceFactory(AvaloniaFrameClockService frameClock) : IScenePlaybackTickSourceFactory
{
    public IScenePlaybackTickSource Create(TimeSpan interval) => new AvaloniaScenePlaybackTickSource(frameClock);
}

internal sealed class AvaloniaScenePlaybackTickSource : IScenePlaybackTickSource
{
    private readonly Lock _gate = new();
    private readonly AvaloniaFrameClockService _frameClock;
    private readonly Channel<ScenePlaybackTick> _ticks = Channel.CreateBounded<ScenePlaybackTick>(1);
    private long _lastTimestamp;
    private double _pendingElapsedMilliseconds;
    private bool _tickQueued;
    private bool _disposed;

    public AvaloniaScenePlaybackTickSource(AvaloniaFrameClockService frameClock)
    {
        _frameClock = frameClock;
        _lastTimestamp = Stopwatch.GetTimestamp();
        _frameClock.Frame += OnFrame;
    }

    public async ValueTask<ScenePlaybackTick> WaitForNextTickAsync(CancellationToken cancellationToken)
    {
        try
        {
            var tick = await _ticks.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
                _tickQueued = false;
            return tick;
        }
        catch (ChannelClosedException)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
                return ValueTask.CompletedTask;

            _disposed = true;
        }

        _frameClock.Frame -= OnFrame;
        _ticks.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private void OnFrame(object? sender, AvaloniaFrameEventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_lastTimestamp, now);
        _lastTimestamp = now;
        if (elapsed <= TimeSpan.Zero)
            return;

        ScenePlaybackTick tick;
        lock (_gate)
        {
            if (_disposed)
                return;

            _pendingElapsedMilliseconds += elapsed.TotalMilliseconds;
            if (_tickQueued)
                return;

            _tickQueued = true;
            tick = new ScenePlaybackTick(TimeSpan.FromMilliseconds(_pendingElapsedMilliseconds));
            _pendingElapsedMilliseconds = 0;
        }

        if (_ticks.Writer.TryWrite(tick))
            return;

        lock (_gate)
        {
            _pendingElapsedMilliseconds += tick.Elapsed.TotalMilliseconds;
            _tickQueued = false;
        }
    }
}
