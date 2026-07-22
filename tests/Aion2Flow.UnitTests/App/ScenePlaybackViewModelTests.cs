using Cloris.Aion2Flow.Presentation;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Playback;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class ScenePlaybackViewModelTests
{
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
