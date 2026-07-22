namespace Cloris.Aion2Flow.ViewModels;

internal sealed class PlaybackSeekCoordinator(
    Func<long, CancellationToken, ValueTask> seekAsync,
    Action<Exception> reportError,
    Action becameIdle) : IAsyncDisposable
{
    private const long MinimumSeekIntervalMilliseconds = 33;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _activeCancellation;
    private Task? _workerTask;
    private long _pendingPositionMilliseconds;
    private long _nextAllowedStartTick;
    private bool _hasPendingRequest;
    private bool _isDisposed;

    public void Request(long positionMilliseconds)
    {
        lock (_gate)
        {
            if (_isDisposed)
                return;

            _pendingPositionMilliseconds = positionMilliseconds;
            _hasPendingRequest = true;
            _activeCancellation?.Cancel();
            _workerTask ??= Task.Run(ProcessRequestsAsync);
        }
    }

    public void CancelPending()
    {
        lock (_gate)
        {
            if (_isDisposed)
                return;

            _hasPendingRequest = false;
            _activeCancellation?.Cancel();
        }
    }

    public bool IsBusy
    {
        get
        {
            lock (_gate)
                return !_isDisposed && _workerTask is not null;
        }
    }

    private async Task ProcessRequestsAsync()
    {
        while (TryBeginRequest(out var positionMilliseconds, out var cancellation))
        {
            try
            {
                await WaitForNextStartAsync().ConfigureAwait(false);
                if (cancellation.IsCancellationRequested)
                    continue;

                await seekAsync(positionMilliseconds, cancellation.Token).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (IsDisposed)
            {
            }
            catch (Exception ex)
            {
                reportError(ex);
            }
            finally
            {
                CompleteRequest(cancellation);
            }
        }

        becameIdle();
    }

    private async Task WaitForNextStartAsync()
    {
        long delayMilliseconds;
        lock (_gate)
        {
            var now = Environment.TickCount64;
            var start = Math.Max(now, _nextAllowedStartTick);
            _nextAllowedStartTick = start + MinimumSeekIntervalMilliseconds;
            delayMilliseconds = start - now;
        }

        if (delayMilliseconds > 0)
            await Task.Delay(TimeSpan.FromMilliseconds(delayMilliseconds)).ConfigureAwait(false);
    }

    private bool TryBeginRequest(out long positionMilliseconds, out CancellationTokenSource cancellation)
    {
        lock (_gate)
        {
            if (_isDisposed || !_hasPendingRequest)
            {
                _workerTask = null;
                positionMilliseconds = 0;
                cancellation = null!;
                return false;
            }

            positionMilliseconds = _pendingPositionMilliseconds;
            _hasPendingRequest = false;
            cancellation = new CancellationTokenSource();
            _activeCancellation = cancellation;
            return true;
        }
    }

    private void CompleteRequest(CancellationTokenSource cancellation)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_activeCancellation, cancellation))
                _activeCancellation = null;
        }

        cancellation.Dispose();
    }

    private bool IsDisposed
    {
        get
        {
            lock (_gate)
                return _isDisposed;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? workerTask;
        lock (_gate)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _hasPendingRequest = false;
            _activeCancellation?.Cancel();
            workerTask = _workerTask;
        }

        if (workerTask is not null)
            await workerTask.ConfigureAwait(false);
    }
}
