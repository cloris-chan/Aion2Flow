using System.Collections.Concurrent;
using Cloris.Aion2Flow.Presentation;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Playback;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class ScenePlaybackViewModelTests
{
    [Fact]
    public async Task SeekCoordinator_RapidRequestsKeepOnlyActiveAndLatestPosition()
    {
        var positions = new ConcurrentQueue<long>();
        var errors = new ConcurrentQueue<Exception>();
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCanceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var latestCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var becameIdle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrentRequests = 0;
        var maximumConcurrentRequests = 0;
        await using var coordinator = new PlaybackSeekCoordinator(async (positionMilliseconds, cancellationToken) =>
        {
            positions.Enqueue(positionMilliseconds);
            var concurrency = Interlocked.Increment(ref concurrentRequests);
            lock (positions)
                maximumConcurrentRequests = Math.Max(maximumConcurrentRequests, concurrency);
            try
            {
                if (positionMilliseconds == 1)
                {
                    using var registration = cancellationToken.Register(() => firstCanceled.TrySetResult(true));
                    firstStarted.TrySetResult(true);
                    await releaseFirst.Task;
                    if (cancellationToken.IsCancellationRequested)
                        return;
                }

                if (positionMilliseconds == 1_000)
                    latestCompleted.TrySetResult(true);
            }
            finally
            {
                Interlocked.Decrement(ref concurrentRequests);
            }
        }, errors.Enqueue, () => becameIdle.TrySetResult(true));

        coordinator.Request(1);
        await firstStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        for (var positionMilliseconds = 2; positionMilliseconds <= 1_000; positionMilliseconds++)
            coordinator.Request(positionMilliseconds);

        await firstCanceled.Task.WaitAsync(TestContext.Current.CancellationToken);
        releaseFirst.TrySetResult(true);
        await latestCompleted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await becameIdle.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1L, 1_000L], positions.ToArray());
        Assert.Equal(1, maximumConcurrentRequests);
        Assert.False(coordinator.IsBusy);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task SeekCoordinator_FinalFailureReportsErrorAndBecomesIdle()
    {
        var errors = new ConcurrentQueue<Exception>();
        var becameIdle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = new PlaybackSeekCoordinator(
            static (_, _) => ValueTask.FromException(new InvalidOperationException("seek failed")),
            errors.Enqueue,
            () => becameIdle.TrySetResult(true));

        coordinator.Request(500);
        await becameIdle.Task.WaitAsync(TestContext.Current.CancellationToken);

        var error = Assert.Single(errors);
        Assert.Equal("seek failed", error.Message);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task SeekCoordinator_CancelPendingPreventsOlderSeekFromFollowingDirectNavigation()
    {
        var positions = new ConcurrentQueue<long>();
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCanceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var becameIdle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = new PlaybackSeekCoordinator(async (positionMilliseconds, cancellationToken) =>
        {
            positions.Enqueue(positionMilliseconds);
            using var registration = cancellationToken.Register(() => firstCanceled.TrySetResult(true));
            firstStarted.TrySetResult(true);
            await releaseFirst.Task;
        }, static _ => { }, () => becameIdle.TrySetResult(true));

        coordinator.Request(1);
        await firstStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        coordinator.Request(2);
        coordinator.CancelPending();

        await firstCanceled.Task.WaitAsync(TestContext.Current.CancellationToken);
        releaseFirst.TrySetResult(true);
        await becameIdle.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1L], positions.ToArray());
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public void DurationGrowthExtendsFullTimelineViewport()
    {
        var viewport = new PlaybackTimelineViewport(0, 2_000);

        var updated = ScenePlaybackViewModel.ResolveViewportAfterDurationChange(viewport, 2_000, 4_000);

        Assert.Equal(new PlaybackTimelineViewport(0, 4_000), updated);
    }

    [Fact]
    public void DurationGrowthPreservesZoomedTimelineViewport()
    {
        var viewport = new PlaybackTimelineViewport(500, 1_500);

        var updated = ScenePlaybackViewModel.ResolveViewportAfterDurationChange(viewport, 2_000, 4_000);

        Assert.Equal(viewport, updated);
    }

    [Fact]
    public void LiveIndexRefresh_FinalizesGrowingIndexWithoutAdditionalEntries()
    {
        var journal = new ObservedEventJournal();
        var growing = new SceneJournalSegment(journal, 0, 0, IsLiveGrowing: true).CreateBoundedSnapshot();
        var index = ScenePlaybackTrackIndex.Build(growing, TestContext.Current.CancellationToken);
        var finalized = new SceneJournalSegment(journal, 0, 0, IsLiveGrowing: false);

        Assert.True(index.IsSourceGrowing);
        Assert.True(ScenePlaybackViewModel.ShouldRefreshLiveIndex(index, finalized));
    }

    [Fact]
    public void LiveIndexRefresh_DoesNotRebuildCurrentGrowingIndex()
    {
        var journal = new ObservedEventJournal();
        var growing = new SceneJournalSegment(journal, 0, 0, IsLiveGrowing: true).CreateBoundedSnapshot();
        var index = ScenePlaybackTrackIndex.Build(growing, TestContext.Current.CancellationToken);

        Assert.False(ScenePlaybackViewModel.ShouldRefreshLiveIndex(index, growing));
    }

    [Fact]
    public void TimelineProjectionCancellation_CompletesWithoutThrowing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = ScenePlaybackTimelineBuilder.TryBuildTimelineStrips(
            default,
            new PlaybackTimelineViewport(0, 1_000),
            static _ => string.Empty,
            cancellation.Token);

        Assert.Null(result);
    }
}
