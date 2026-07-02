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
    private CancellationTokenSource? _activeNavigationCancellation;
    private CancellationTokenSource? _checkpointCancellation;
    private Task? _playbackTask;
    private Task? _activeNavigationTask;
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

        var endOrdinal = segment.CurrentEndObservationOrdinalExclusive;

        if (_checkpoints.TryGetCeiling(Math.Max(startPositionMilliseconds, endPositionMilliseconds), out var ceiling) && ceiling is not null)
            endOrdinal = Math.Min(endOrdinal, ceiling.JournalCursor.NextObservationOrdinal);

        return new SceneJournalSegment(segment.Journal, Math.Min(segment.StartObservationOrdinal, endOrdinal), endOrdinal, IsLiveGrowing: false);
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
            _checkpointTask = RebuildCheckpointsCoreAsync(generation, cancellation.Token);
        }

        CancelAndDisposeAfterCompletion(previousCancellation, previousTask);
    }

    public Task RebuildCheckpointsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        long generation;
        lock (_stateGate)
            generation = ++_checkpointGeneration;
        return RebuildCheckpointsCoreAsync(generation, cancellationToken);
    }

    public ValueTask<ScenePlaybackFrame> SeekAsync(long positionMilliseconds, CancellationToken cancellationToken = default) =>
        NavigateAsync(operation => SeekOperationAsync(positionMilliseconds, operation), cancellationToken);

    public ValueTask<ScenePlaybackFrame> StepEventAsync(int direction, CancellationToken cancellationToken = default)
    {
        if (direction is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(direction));

        return NavigateAsync(operation => StepEventOperationAsync(direction, operation), cancellationToken);
    }

    public ValueTask<ScenePlaybackFrame> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var position = PositionMilliseconds;
        return SeekAsync(position, cancellationToken);
    }

    public async ValueTask<ScenePlaybackCombatantDetail> CreateCombatantDetailAsync(int combatantId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(combatantId);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Session.CreateCombatantDetail(combatantId);
        }
        finally
        {
            _operationGate.Release();
        }
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
        CancellationTokenSource? navigationCancellation;
        Task? navigationTask;
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
            navigationCancellation = _activeNavigationCancellation;
            navigationCancellation?.Cancel();
            _activeNavigationCancellation = null;
            navigationTask = _activeNavigationTask;
            _activeNavigationTask = null;
            checkpointTask = _checkpointTask;
            _checkpointTask = null;
            checkpointCancellation = _checkpointCancellation;
            _checkpointCancellation = null;
        }

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

        if (navigationTask is not null)
        {
            try
            {
                await navigationTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        playbackCancellation?.Dispose();
        navigationCancellation?.Dispose();
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
                await AdvanceCoreAsync(tickTarget.Value.PositionMilliseconds, tickTarget.Value.SeekGeneration, cancellationToken).ConfigureAwait(false);
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

    private async ValueTask<ScenePlaybackFrame> NavigateAsync(Func<NavigationOperation, ValueTask<ScenePlaybackFrame>> operation, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        CancellationTokenSource navigationCancellation;
        CancellationTokenSource? previousCancellation;
        long generation;
        lock (_stateGate)
        {
            previousCancellation = _activeNavigationCancellation;
            previousCancellation?.Cancel();
            navigationCancellation = new CancellationTokenSource();
            _activeNavigationCancellation = navigationCancellation;
            generation = ++_seekGeneration;
            _isLoading = true;
        }

        var operationTask = operation(new NavigationOperation(generation, navigationCancellation.Token, cancellationToken)).AsTask();
        lock (_stateGate)
        {
            if (ReferenceEquals(_activeNavigationCancellation, navigationCancellation))
                _activeNavigationTask = operationTask;
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
                if (ReferenceEquals(_activeNavigationCancellation, navigationCancellation))
                    _activeNavigationCancellation = null;
                if (ReferenceEquals(_activeNavigationTask, operationTask))
                    _activeNavigationTask = null;
            }

            navigationCancellation.Dispose();
        }
    }

    private async ValueTask<ScenePlaybackFrame> StepEventOperationAsync(int direction, NavigationOperation operation)
    {
        await Task.Yield();
        operation.CallerCancellationToken.ThrowIfCancellationRequested();
        if (IsNavigationSuperseded(operation))
            return CompleteSupersededNavigation(operation);

        await _operationGate.WaitAsync(operation.CallerCancellationToken).ConfigureAwait(false);
        try
        {
            operation.CallerCancellationToken.ThrowIfCancellationRequested();
            if (IsNavigationSuperseded(operation))
                return CompleteSupersededNavigation(operation);

            ScenePlaybackFrame currentFrame;
            lock (_stateGate)
                currentFrame = _currentFrame;

            var segment = Source.CreateTimelineSegment();
            var current = currentFrame.AppliedSegment.EndObservationOrdinalExclusive;
            var target = direction > 0
                ? Math.Min(segment.CurrentEndObservationOrdinalExclusive, current + 1)
                : Math.Max(segment.StartObservationOrdinal, current - 1);
            var frame = await Task.Run(() =>
            {
                operation.CallerCancellationToken.ThrowIfCancellationRequested();
                if (IsNavigationSuperseded(operation))
                    return CompleteSupersededNavigation(operation);

                return Session.SeekObservationOrdinal(target);
            }, operation.CallerCancellationToken).ConfigureAwait(false);

            operation.CallerCancellationToken.ThrowIfCancellationRequested();
            if (IsNavigationSuperseded(operation))
                return CompleteSupersededNavigation(operation);

            if (!TryApplyFrame(frame, operation.Generation, resetPlaybackClock: true, out var playbackCancellation, out var playbackTask))
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

    private async ValueTask<ScenePlaybackFrame> SeekOperationAsync(long positionMilliseconds, NavigationOperation operation)
    {
        await Task.Yield();
        operation.CallerCancellationToken.ThrowIfCancellationRequested();
        if (IsNavigationSuperseded(operation))
            return CompleteSupersededNavigation(operation);

        await _operationGate.WaitAsync(operation.CallerCancellationToken).ConfigureAwait(false);
        try
        {
            operation.CallerCancellationToken.ThrowIfCancellationRequested();
            if (IsNavigationSuperseded(operation))
                return CompleteSupersededNavigation(operation);

            var frame = await Task.Run(() =>
            {
                operation.CallerCancellationToken.ThrowIfCancellationRequested();
                if (IsNavigationSuperseded(operation))
                    return CompleteSupersededNavigation(operation);

                return Session.Seek(positionMilliseconds);
            }, operation.CallerCancellationToken).ConfigureAwait(false);

            operation.CallerCancellationToken.ThrowIfCancellationRequested();
            if (IsNavigationSuperseded(operation))
                return CompleteSupersededNavigation(operation);

            if (!TryApplyFrame(frame, operation.Generation, resetPlaybackClock: true, out var playbackCancellation, out var playbackTask))
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

    private bool IsNavigationSuperseded(NavigationOperation operation)
    {
        if (operation.SupersessionToken.IsCancellationRequested)
            return true;

        lock (_stateGate)
            return _disposed || operation.Generation != _seekGeneration;
    }

    private ScenePlaybackFrame CompleteSupersededNavigation(NavigationOperation operation)
    {
        lock (_stateGate)
        {
            if (operation.Generation == _seekGeneration)
                _isLoading = false;

            return _currentFrame;
        }
    }

    private async ValueTask<ScenePlaybackFrame> AdvanceCoreAsync(long positionMilliseconds, long expectedGeneration, CancellationToken cancellationToken)
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

    private async Task RebuildCheckpointsCoreAsync(long generation, CancellationToken cancellationToken)
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

    private List<ScenePlaybackCheckpoint> BuildCheckpoints(CancellationToken cancellationToken)
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

internal readonly record struct NavigationOperation(long Generation, CancellationToken SupersessionToken, CancellationToken CallerCancellationToken);

public sealed class ScenePlaybackFrameChangedEventArgs(ScenePlaybackControllerState state, ScenePlaybackFrame frame) : EventArgs
{
    public ScenePlaybackControllerState State { get; } = state;

    public ScenePlaybackFrame Frame { get; } = frame;
}

public readonly record struct ScenePlaybackControllerOptions(TimeSpan TickInterval, long CheckpointIntervalMilliseconds, bool RebuildCheckpointsOnCreate)
{
    public static ScenePlaybackControllerOptions Default { get; } = new(ScenePlaybackController.DefaultTickInterval, ScenePlaybackController.DefaultCheckpointIntervalMilliseconds, RebuildCheckpointsOnCreate: true);
}
