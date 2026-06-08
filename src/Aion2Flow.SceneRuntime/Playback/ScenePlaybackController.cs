using Cloris.Aion2Flow.SceneRuntime.Journal;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public sealed class ScenePlaybackController : IAsyncDisposable
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
    private Task? _activeSeekTask;
    private Task? _checkpointTask;
    private ScenePlaybackFrame _currentFrame;
    private long _positionMilliseconds;
    private long _durationMilliseconds;
    private long _playbackClockAnchorMilliseconds;
    private double _playbackClockElapsedMilliseconds;
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
        _checkpoints.Upsert(Session.CreateCheckpoint());
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

            ResetPlaybackClockLocked(_positionMilliseconds);
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
            ResetPlaybackClockLocked(_positionMilliseconds);
            cancellation = _playbackCancellation;
            _playbackCancellation = null;
            playbackTask = _playbackTask;
            _playbackTask = null;
        }

        CancelAndDisposeAfterCompletion(cancellation, playbackTask);
    }

    public async ValueTask<ScenePlaybackFrame> StopAsync(CancellationToken cancellationToken = default)
    {
        Pause();
        return await SeekAsync(0, cancellationToken).ConfigureAwait(false);
    }

    public ScenePlaybackCheckpoint[] GetCheckpoints() => _checkpoints.Snapshot();

    public SceneJournalSegment CreateTimelineSegment() => Source.CreateTimelineSegment();

    public SceneJournalSegment CreateTimelineSegment(long startPositionMilliseconds, long endPositionMilliseconds)
    {
        var segment = Source.CreateTimelineSegment();
        if (segment.IsEmpty || _checkpoints.Count == 0)
            return segment;

        var startOrdinal = segment.StartObservationOrdinal;
        var endOrdinal = segment.CurrentEndObservationOrdinalExclusive;
        var startPosition = Math.Max(0, startPositionMilliseconds);
        if (_checkpoints.TryGetFloor(startPosition, out var floor) && floor is not null && floor.PositionMilliseconds < startPosition)
            startOrdinal = Math.Max(startOrdinal, floor.JournalCursor.NextObservationOrdinal);

        if (_checkpoints.TryGetCeiling(Math.Max(startPositionMilliseconds, endPositionMilliseconds), out var ceiling) && ceiling is not null)
            endOrdinal = Math.Min(endOrdinal, ceiling.JournalCursor.NextObservationOrdinal);

        startOrdinal = Math.Min(startOrdinal, endOrdinal);
        return new SceneJournalSegment(segment.Journal, startOrdinal, endOrdinal, IsLiveGrowing: false);
    }

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

    public ValueTask<ScenePlaybackFrame> SeekAsync(long positionMilliseconds, CancellationToken cancellationToken = default) =>
        SeekCoreAsync(positionMilliseconds, cancelPrevious: true, cancellationToken);

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
        {
            ResetPlaybackClockLocked(_positionMilliseconds);
            _speed = speed;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? playbackTask;
        CancellationTokenSource? playbackCancellation;
        CancellationTokenSource? seekCancellation;
        Task? seekTask;
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
            seekTask = _activeSeekTask;
            _activeSeekTask = null;
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

        if (seekTask is not null)
        {
            try
            {
                await seekTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        playbackCancellation?.Dispose();
        seekCancellation?.Dispose();
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

            var tickTarget = ResolveTickTarget(tick.Elapsed);
            if (tickTarget is null)
                continue;

            try
            {
                await AdvanceCoreAsync(tickTarget.Value.PositionMilliseconds, cancellationToken, tickTarget.Value.SeekGeneration).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private PlaybackTickTarget? ResolveTickTarget(TimeSpan elapsed)
    {
        lock (_stateGate)
        {
            if (_isLoading || !_isPlaying)
                return null;

            var elapsedMilliseconds = elapsed.TotalMilliseconds;
            if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds <= 0)
                return null;

            _playbackClockElapsedMilliseconds += elapsedMilliseconds;
            var target = _playbackClockAnchorMilliseconds + _playbackClockElapsedMilliseconds * _speed;
            var next = target >= long.MaxValue
                ? long.MaxValue
                : (long)Math.Round(target, MidpointRounding.AwayFromZero);
            if (next <= _positionMilliseconds)
                return null;

            return new PlaybackTickTarget(next, _seekGeneration);
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
        var operationTask = SeekOperationAsync(positionMilliseconds, linkedCancellation.Token, generation).AsTask();
        lock (_stateGate)
        {
            if (ReferenceEquals(_activeSeekCancellation, linkedCancellation))
                _activeSeekTask = operationTask;
        }

        try
        {
            return await operationTask.ConfigureAwait(false);
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
                if (ReferenceEquals(_activeSeekTask, operationTask))
                    _activeSeekTask = null;
            }

            linkedCancellation.Dispose();
        }
    }

    private async ValueTask<ScenePlaybackFrame> SeekOperationAsync(long positionMilliseconds, CancellationToken cancellationToken, long generation)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var frame = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var checkpoint = ResolveSeekCheckpoint(positionMilliseconds);
                return Session.Seek(positionMilliseconds, checkpoint);
            }, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (!TryApplyFrame(frame, generation, resetPlaybackClock: true, out var playbackCancellation, out var playbackTask))
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

    private async ValueTask<ScenePlaybackFrame> AdvanceCoreAsync(long positionMilliseconds, CancellationToken cancellationToken, long expectedGeneration)
    {
        lock (_stateGate)
        {
            if (_disposed)
                return _currentFrame;
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_stateGate)
            {
                if (_disposed || expectedGeneration != _seekGeneration)
                    return _currentFrame;
            }

            var frame = Session.AdvanceTo(positionMilliseconds);
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryApplyFrame(frame, expectedGeneration, resetPlaybackClock: false, out var playbackCancellation, out var playbackTask))
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

    private async Task RebuildCheckpointsCoreAsync(CancellationToken cancellationToken, long generation)
    {
        var publishStarted = false;
        lock (_stateGate)
        {
            if (_disposed || generation != _checkpointGeneration)
                return;

            _isCheckpointing = true;
            publishStarted = true;
        }

        if (publishStarted)
            PublishCurrentFrameChanged();

        try
        {
            var checkpoints = await Task.Run(() => BuildCheckpoints(cancellationToken), cancellationToken).ConfigureAwait(false);

            lock (_stateGate)
            {
                if (!_disposed && generation == _checkpointGeneration)
                    _checkpoints.Replace(checkpoints);
            }
        }
        finally
        {
            var publishFinished = false;
            lock (_stateGate)
            {
                if (generation == _checkpointGeneration)
                {
                    _isCheckpointing = false;
                    publishFinished = !_disposed;
                }
            }

            if (publishFinished)
                PublishCurrentFrameChanged();
        }
    }

    private ScenePlaybackCheckpoint? ResolveSeekCheckpoint(long positionMilliseconds)
    {
        if (!_checkpoints.TryGetFloor(positionMilliseconds, out var checkpoint) || checkpoint is null)
            return null;

        var currentEnd = Source.CreateTimelineSegment().CurrentEndObservationOrdinalExclusive;
        return checkpoint.JournalCursor.NextObservationOrdinal <= currentEnd ? checkpoint : null;
    }

    private IReadOnlyList<ScenePlaybackCheckpoint> BuildCheckpoints(CancellationToken cancellationToken)
    {
        var interval = _options.CheckpointIntervalMilliseconds;
        var session = new ScenePlaybackSession(Source);
        var checkpoints = new List<ScenePlaybackCheckpoint>();
        var first = session.Seek(0);
        checkpoints.Add(session.CreateCheckpoint());
        var duration = first.TimeRange.DurationMilliseconds;
        if (duration <= 0)
            return checkpoints;

        for (var position = interval; position < duration; position += interval)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.AdvanceTo(position);
            checkpoints.Add(session.CreateCheckpoint());
        }

        cancellationToken.ThrowIfCancellationRequested();
        session.AdvanceTo(duration);
        checkpoints.Add(session.CreateCheckpoint());
        return checkpoints;
    }

    private bool TryApplyFrame(ScenePlaybackFrame frame, long generation, bool resetPlaybackClock, out CancellationTokenSource? playbackCancellation, out Task? playbackTask)
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
            if (resetPlaybackClock || !_isPlaying)
                ResetPlaybackClockLocked(_positionMilliseconds);
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

    private void PublishCurrentFrameChanged()
    {
        ScenePlaybackFrame frame;
        lock (_stateGate)
            frame = _currentFrame;

        PublishFrameChanged(frame);
    }

    private ScenePlaybackControllerState CreateStateLocked() => new(Source.SourceKind, _positionMilliseconds, _durationMilliseconds, _speed, _isPlaying, _isLoading, _isCheckpointing, _checkpoints.Count);

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
        ResetPlaybackClockLocked(_positionMilliseconds);
        cancellation = _playbackCancellation;
        _playbackCancellation = null;
        playbackTask = _playbackTask;
        _playbackTask = null;
    }

    private void ResetPlaybackClockLocked(long positionMilliseconds)
    {
        _playbackClockAnchorMilliseconds = Math.Max(0, positionMilliseconds);
        _playbackClockElapsedMilliseconds = 0;
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
