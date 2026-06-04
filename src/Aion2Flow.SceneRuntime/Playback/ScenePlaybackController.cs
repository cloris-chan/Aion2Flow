namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public sealed class ScenePlaybackController : IDisposable, IAsyncDisposable
{
    public static readonly TimeSpan DefaultTickInterval = TimeSpan.FromMilliseconds(33);

    private readonly Lock _stateGate = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly IScenePlaybackTickSourceFactory _tickSourceFactory;
    private readonly TimeSpan _tickInterval;
    private CancellationTokenSource? _playbackCancellation;
    private CancellationTokenSource? _activeSeekCancellation;
    private Task? _playbackTask;
    private ScenePlaybackFrame _currentFrame;
    private long _positionMilliseconds;
    private long _durationMilliseconds;
    private double _speed = 1d;
    private bool _isPlaying;
    private bool _isLoading;
    private bool _disposed;
    private long _seekGeneration;

    public ScenePlaybackController(IScenePlaybackSource source) : this(source, PeriodicScenePlaybackTickSourceFactory.Shared, DefaultTickInterval)
    {
    }

    public ScenePlaybackController(IScenePlaybackSource source, IScenePlaybackTickSourceFactory tickSourceFactory, TimeSpan tickInterval)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tickSourceFactory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tickInterval, TimeSpan.Zero);

        Source = source;
        Session = new ScenePlaybackSession(source);
        _tickSourceFactory = tickSourceFactory;
        _tickInterval = tickInterval;
        _currentFrame = Session.Seek(0);
        _durationMilliseconds = _currentFrame.TimeRange.DurationMilliseconds;
    }

    public event EventHandler<ScenePlaybackFrameChangedEventArgs>? FrameChanged;

    public IScenePlaybackSource Source { get; }

    public ScenePlaybackSession Session { get; }

    public ScenePlaybackFrame CurrentFrame
    {
        get { lock (_stateGate) return _currentFrame; }
    }

    public long PositionMilliseconds
    {
        get { lock (_stateGate) return _positionMilliseconds; }
    }

    public long DurationMilliseconds
    {
        get { lock (_stateGate) return _durationMilliseconds; }
    }

    public double Speed
    {
        get { lock (_stateGate) return _speed; }
    }

    public bool IsPlaying
    {
        get { lock (_stateGate) return _isPlaying; }
    }

    public bool IsLoading
    {
        get { lock (_stateGate) return _isLoading; }
    }

    public ScenePlaybackControllerState State
    {
        get
        {
            lock (_stateGate)
                return CreateStateLocked();
        }
    }

    public void Play()
    {
        ThrowIfDisposed();
        lock (_stateGate)
        {
            if (_isPlaying)
                return;

            _isPlaying = true;
            _playbackCancellation = new CancellationTokenSource();
            _playbackTask = RunPlaybackLoopAsync(_playbackCancellation.Token);
        }
    }

    public void Pause()
    {
        CancellationTokenSource? cancellation;
        Task? playbackTask;
        lock (_stateGate)
        {
            _isPlaying = false;
            cancellation = _playbackCancellation;
            _playbackCancellation = null;
            playbackTask = _playbackTask;
            _playbackTask = null;
        }

        CancelAndDisposeAfterCompletion(cancellation, playbackTask);
    }

    public ScenePlaybackFrame Stop()
    {
        Pause();
        return Seek(0);
    }

    public ScenePlaybackFrame Seek(long positionMilliseconds) =>
        SeekAsync(positionMilliseconds).AsTask().GetAwaiter().GetResult();

    public ValueTask<ScenePlaybackFrame> SeekAsync(long positionMilliseconds, CancellationToken cancellationToken = default) =>
        SeekCoreAsync(positionMilliseconds, cancelPrevious: true, cancellationToken);

    public ScenePlaybackFrame Refresh() =>
        RefreshAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask<ScenePlaybackFrame> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var position = PositionMilliseconds;
        return SeekCoreAsync(position, cancelPrevious: true, cancellationToken);
    }

    public void SetSpeed(double speed)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(speed) || speed <= 0)
            throw new ArgumentOutOfRangeException(nameof(speed));

        lock (_stateGate)
            _speed = speed;

        Session.SetSpeed(speed);
    }

    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        Task? playbackTask;
        CancellationTokenSource? playbackCancellation;
        CancellationTokenSource? seekCancellation;
        lock (_stateGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _isPlaying = false;
            _isLoading = false;
            playbackTask = _playbackTask;
            _playbackTask = null;
            playbackCancellation = _playbackCancellation;
            _playbackCancellation = null;
            seekCancellation = _activeSeekCancellation;
            _activeSeekCancellation = null;
        }

        seekCancellation?.Cancel();
        playbackCancellation?.Cancel();
        if (playbackTask is not null)
        {
            try
            {
                await playbackTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        playbackCancellation?.Dispose();
    }

    private async Task RunPlaybackLoopAsync(CancellationToken cancellationToken)
    {
        await using var tickSource = _tickSourceFactory.Create(_tickInterval);
        while (!cancellationToken.IsCancellationRequested)
        {
            ScenePlaybackTick tick;
            try
            {
                tick = await tickSource.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (tick.Elapsed <= TimeSpan.Zero || IsLoading)
                continue;

            var nextPosition = ResolveNextPosition(tick.Elapsed);
            try
            {
                await SeekCoreAsync(nextPosition, cancelPrevious: false, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private long ResolveNextPosition(TimeSpan elapsed)
    {
        lock (_stateGate)
        {
            var advance = elapsed.TotalMilliseconds * _speed;
            if (advance <= 0)
                return _positionMilliseconds;

            var next = _positionMilliseconds + (long)Math.Round(advance, MidpointRounding.AwayFromZero);
            return next < _positionMilliseconds ? long.MaxValue : next;
        }
    }

    private async ValueTask<ScenePlaybackFrame> SeekCoreAsync(long positionMilliseconds, bool cancelPrevious, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        CancellationTokenSource linkedCancellation;
        CancellationTokenSource? previousCancellation = null;
        long generation;
        lock (_stateGate)
        {
            previousCancellation = cancelPrevious ? _activeSeekCancellation : null;
            linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (cancelPrevious)
                _activeSeekCancellation = linkedCancellation;
            generation = ++_seekGeneration;
            _isLoading = true;
        }

        previousCancellation?.Cancel();
        try
        {
            await Task.Yield();
            linkedCancellation.Token.ThrowIfCancellationRequested();
            await _operationGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
            try
            {
                var token = linkedCancellation.Token;
                var frame = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    return Session.Seek(positionMilliseconds);
                }, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!TryApplyFrame(frame, generation, out var playbackCancellation, out var playbackTask))
                    return frame;

                CancelAndDisposeAfterCompletion(playbackCancellation, playbackTask);
                PublishFrameChanged(frame);
                return frame;
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch
        {
            lock (_stateGate)
            {
                if (generation == _seekGeneration)
                    _isLoading = false;
            }

            throw;
        }
        finally
        {
            lock (_stateGate)
            {
                if (ReferenceEquals(_activeSeekCancellation, linkedCancellation))
                    _activeSeekCancellation = null;
            }

            linkedCancellation.Dispose();
        }
    }

    private bool TryApplyFrame(ScenePlaybackFrame frame, long generation, out CancellationTokenSource? playbackCancellation, out Task? playbackTask)
    {
        playbackCancellation = null;
        playbackTask = null;
        lock (_stateGate)
        {
            if (_disposed || generation != _seekGeneration)
                return false;

            _currentFrame = frame;
            _positionMilliseconds = frame.PositionMilliseconds;
            _durationMilliseconds = frame.TimeRange.DurationMilliseconds;
            _isLoading = false;
            if (Source.SourceKind == ScenePlaybackSourceKind.Archived &&
                _durationMilliseconds > 0 &&
                _positionMilliseconds >= _durationMilliseconds)
                StopPlaybackLocked(out playbackCancellation, out playbackTask);

            return true;
        }
    }

    private void PublishFrameChanged(ScenePlaybackFrame frame)
    {
        ScenePlaybackControllerState state;
        lock (_stateGate)
            state = CreateStateLocked();

        FrameChanged?.Invoke(this, new ScenePlaybackFrameChangedEventArgs(state, frame));
    }

    private ScenePlaybackControllerState CreateStateLocked() => new(
        Source.SourceKind,
        _positionMilliseconds,
        _durationMilliseconds,
        _speed,
        _isPlaying,
        _isLoading);

    private void ThrowIfDisposed()
    {
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }

    private void StopPlaybackLocked(out CancellationTokenSource? cancellation, out Task? playbackTask)
    {
        _isPlaying = false;
        cancellation = _playbackCancellation;
        _playbackCancellation = null;
        playbackTask = _playbackTask;
        _playbackTask = null;
    }

    private static void CancelAndDisposeAfterCompletion(CancellationTokenSource? cancellation, Task? playbackTask)
    {
        if (cancellation is null)
            return;

        cancellation.Cancel();
        if (playbackTask is null || playbackTask.IsCompleted)
        {
            cancellation.Dispose();
            return;
        }

        _ = playbackTask.ContinueWith(static (task, state) =>
        {
            task.Exception?.Handle(static _ => true);
            ((CancellationTokenSource)state!).Dispose();
        }, cancellation, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}

public readonly record struct ScenePlaybackControllerState(ScenePlaybackSourceKind SourceKind, long PositionMilliseconds, long DurationMilliseconds, double Speed, bool IsPlaying, bool IsLoading);

public sealed class ScenePlaybackFrameChangedEventArgs(ScenePlaybackControllerState state, ScenePlaybackFrame frame) : EventArgs
{
    public ScenePlaybackControllerState State { get; } = state;

    public ScenePlaybackFrame Frame { get; } = frame;
}
