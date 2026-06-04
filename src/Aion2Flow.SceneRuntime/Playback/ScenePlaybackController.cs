namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public sealed class ScenePlaybackController : IDisposable, IAsyncDisposable
{
    public static readonly TimeSpan DefaultTickInterval = TimeSpan.FromMilliseconds(33);
    public const long DefaultCheckpointIntervalMilliseconds = 5_000;

    private readonly Lock _stateGate = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly IScenePlaybackTickSourceFactory _tickSourceFactory;
    private readonly ScenePlaybackControllerOptions _options;
    private readonly ScenePlaybackCheckpointCache _checkpoints = new();
    private CancellationTokenSource? _playbackCancellation;
    private CancellationTokenSource? _activeSeekCancellation;
    private CancellationTokenSource? _checkpointCancellation;
    private Task? _playbackTask;
    private Task? _checkpointTask;
    private ScenePlaybackFrame _currentFrame;
    private long _positionMilliseconds;
    private long _durationMilliseconds;
    private double _speed = 1d;
    private bool _isPlaying;
    private bool _isLoading;
    private bool _isCheckpointing;
    private bool _disposed;
    private long _seekGeneration;
    private long _checkpointGeneration;

    public ScenePlaybackController(IScenePlaybackSource source) : this(source, PeriodicScenePlaybackTickSourceFactory.Shared, ScenePlaybackControllerOptions.Default)
    {
    }

    public ScenePlaybackController(IScenePlaybackSource source, IScenePlaybackTickSourceFactory tickSourceFactory, TimeSpan tickInterval)
        : this(source, tickSourceFactory, new ScenePlaybackControllerOptions(tickInterval, DefaultCheckpointIntervalMilliseconds, RebuildCheckpointsOnCreate: false))
    {
    }

    public ScenePlaybackController(IScenePlaybackSource source, IScenePlaybackTickSourceFactory tickSourceFactory, ScenePlaybackControllerOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tickSourceFactory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.TickInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.CheckpointIntervalMilliseconds, 0);

        Source = source;
        Session = new ScenePlaybackSession(source);
        _tickSourceFactory = tickSourceFactory;
        _options = options;
        _currentFrame = Session.Seek(0);
        _durationMilliseconds = _currentFrame.TimeRange.DurationMilliseconds;
        _checkpoints.Upsert(CreateCheckpoint(_currentFrame));
        if (options.RebuildCheckpointsOnCreate && source.SourceKind == ScenePlaybackSourceKind.Archived)
            StartCheckpointRebuild();
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

    public bool IsCheckpointing
    {
        get { lock (_stateGate) return _isCheckpointing; }
    }

    public int CheckpointCount => _checkpoints.Count;

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

    public ScenePlaybackCheckpoint[] GetCheckpoints() => _checkpoints.Snapshot();

    public void StartCheckpointRebuild()
    {
        ThrowIfDisposed();
        CancellationTokenSource? previousCancellation;
        Task? previousTask;
        var cancellation = new CancellationTokenSource();
        long generation;
        lock (_stateGate)
        {
            previousCancellation = _checkpointCancellation;
            previousTask = _checkpointTask;
            generation = ++_checkpointGeneration;
            _checkpointCancellation = cancellation;
            _checkpointTask = RebuildCheckpointsCoreAsync(cancellation.Token, generation);
        }

        CancelAndDisposeAfterCompletion(previousCancellation, previousTask);
    }

    public Task RebuildCheckpointsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        long generation;
        lock (_stateGate)
            generation = ++_checkpointGeneration;
        return RebuildCheckpointsCoreAsync(cancellationToken, generation);
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
        Task? checkpointTask;
        CancellationTokenSource? checkpointCancellation;
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
            checkpointTask = _checkpointTask;
            _checkpointTask = null;
            checkpointCancellation = _checkpointCancellation;
            _checkpointCancellation = null;
        }

        seekCancellation?.Cancel();
        playbackCancellation?.Cancel();
        checkpointCancellation?.Cancel();
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

        if (checkpointTask is not null)
        {
            try
            {
                await checkpointTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        playbackCancellation?.Dispose();
        checkpointCancellation?.Dispose();
    }

    private async Task RunPlaybackLoopAsync(CancellationToken cancellationToken)
    {
        await using var tickSource = _tickSourceFactory.Create(_options.TickInterval);
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

            var tickTarget = ResolveNextPosition(tick.Elapsed);
            if (tickTarget is null)
                continue;

            try
            {
                await SeekCoreAsync(tickTarget.Value.PositionMilliseconds, cancelPrevious: false, cancellationToken, tickTarget.Value.SeekGeneration).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private PlaybackTickTarget? ResolveNextPosition(TimeSpan elapsed)
    {
        lock (_stateGate)
        {
            if (_isLoading)
                return null;

            var advance = elapsed.TotalMilliseconds * _speed;
            if (advance <= 0)
                return new PlaybackTickTarget(_positionMilliseconds, _seekGeneration);

            var next = _positionMilliseconds + (long)Math.Round(advance, MidpointRounding.AwayFromZero);
            return new PlaybackTickTarget(next < _positionMilliseconds ? long.MaxValue : next, _seekGeneration);
        }
    }

    private async ValueTask<ScenePlaybackFrame> SeekCoreAsync(long positionMilliseconds, bool cancelPrevious, CancellationToken cancellationToken)
        => await SeekCoreAsync(positionMilliseconds, cancelPrevious, cancellationToken, null).ConfigureAwait(false);

    private async ValueTask<ScenePlaybackFrame> SeekCoreAsync(long positionMilliseconds, bool cancelPrevious, CancellationToken cancellationToken, long? expectedGeneration)
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
            generation = cancelPrevious ? ++_seekGeneration : expectedGeneration ?? _seekGeneration;
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
                ScenePlaybackFrame frame;
                if (Source.SourceKind == ScenePlaybackSourceKind.Archived && _checkpoints.TryGet(Math.Max(0, positionMilliseconds), out var checkpoint))
                {
                    frame = checkpoint.Frame;
                }
                else
                {
                    frame = await Task.Run(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        return Session.Seek(positionMilliseconds);
                    }, token).ConfigureAwait(false);
                    _checkpoints.Upsert(CreateCheckpoint(frame));
                }

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

    private async Task RebuildCheckpointsCoreAsync(CancellationToken cancellationToken, long generation)
    {
        lock (_stateGate)
        {
            if (_disposed || generation != _checkpointGeneration)
                return;

            _isCheckpointing = true;
        }

        try
        {
            var interval = _options.CheckpointIntervalMilliseconds;
            var session = new ScenePlaybackSession(Source);
            var checkpoints = new List<ScenePlaybackCheckpoint>();
            var first = await Task.Run(() => session.Seek(0), cancellationToken).ConfigureAwait(false);
            checkpoints.Add(CreateCheckpoint(first));
            var duration = first.TimeRange.DurationMilliseconds;
            if (duration > 0)
            {
                for (var position = interval; position < duration; position += interval)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var frame = await Task.Run(() => session.Seek(position), cancellationToken).ConfigureAwait(false);
                    checkpoints.Add(CreateCheckpoint(frame));
                }

                cancellationToken.ThrowIfCancellationRequested();
                var last = await Task.Run(() => session.Seek(duration), cancellationToken).ConfigureAwait(false);
                checkpoints.Add(CreateCheckpoint(last));
            }

            lock (_stateGate)
            {
                if (!_disposed && generation == _checkpointGeneration)
                    _checkpoints.Replace(checkpoints);
            }
        }
        finally
        {
            lock (_stateGate)
            {
                if (generation == _checkpointGeneration)
                    _isCheckpointing = false;
            }
        }
    }

    private static ScenePlaybackCheckpoint CreateCheckpoint(ScenePlaybackFrame frame) => new(frame.PositionMilliseconds, frame.AppliedSegment.EndObservationOrdinalExclusive, frame);

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
        _isLoading,
        _isCheckpointing,
        _checkpoints.Count);

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

public readonly record struct ScenePlaybackControllerState(ScenePlaybackSourceKind SourceKind, long PositionMilliseconds, long DurationMilliseconds, double Speed, bool IsPlaying, bool IsLoading, bool IsCheckpointing, int CheckpointCount);

internal readonly record struct PlaybackTickTarget(long PositionMilliseconds, long SeekGeneration);

public sealed class ScenePlaybackFrameChangedEventArgs(ScenePlaybackControllerState state, ScenePlaybackFrame frame) : EventArgs
{
    public ScenePlaybackControllerState State { get; } = state;

    public ScenePlaybackFrame Frame { get; } = frame;
}

public readonly record struct ScenePlaybackControllerOptions(TimeSpan TickInterval, long CheckpointIntervalMilliseconds, bool RebuildCheckpointsOnCreate)
{
    public static ScenePlaybackControllerOptions Default { get; } = new(ScenePlaybackController.DefaultTickInterval, ScenePlaybackController.DefaultCheckpointIntervalMilliseconds, RebuildCheckpointsOnCreate: true);
}
