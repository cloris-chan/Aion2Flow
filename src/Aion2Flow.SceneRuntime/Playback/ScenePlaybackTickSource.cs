using System.Diagnostics;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public interface IScenePlaybackTickSourceFactory
{
    IScenePlaybackTickSource Create(TimeSpan interval);
}

public interface IScenePlaybackTickSource : IAsyncDisposable
{
    ValueTask<ScenePlaybackTick> WaitForNextTickAsync(CancellationToken cancellationToken);
}

public readonly record struct ScenePlaybackTick(TimeSpan Elapsed);

public sealed class PeriodicScenePlaybackTickSourceFactory : IScenePlaybackTickSourceFactory
{
    public static PeriodicScenePlaybackTickSourceFactory Shared { get; } = new();

    public IScenePlaybackTickSource Create(TimeSpan interval) => new PeriodicScenePlaybackTickSource(interval);
}

public sealed class PeriodicScenePlaybackTickSource : IScenePlaybackTickSource
{
    private readonly PeriodicTimer _timer;
    private long _lastTimestamp;

    public PeriodicScenePlaybackTickSource(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        _timer = new PeriodicTimer(interval);
        _lastTimestamp = Stopwatch.GetTimestamp();
    }

    public async ValueTask<ScenePlaybackTick> WaitForNextTickAsync(CancellationToken cancellationToken)
    {
        if (!await _timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            throw new OperationCanceledException(cancellationToken);

        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_lastTimestamp, now);
        _lastTimestamp = now;
        return new ScenePlaybackTick(elapsed);
    }

    public ValueTask DisposeAsync()
    {
        _timer.Dispose();
        return ValueTask.CompletedTask;
    }
}
