using System.Threading.Channels;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Playback;
using Cloris.Aion2Flow.SceneRuntime.Projection;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class ScenePlaybackTests
{
    [Fact]
    public void ArchivedSource_UsesFixedTimelineSegment()
    {
        var record = CreateArchiveRecord();
        var source = new ArchivedScenePlaybackSource(record);
        var segment = source.CreateTimelineSegment();

        Assert.False(segment.IsLiveGrowing);
        Assert.Same(record.ScenePayload.TimelineSegment.Journal, segment.Journal);
        Assert.Equal(record.ScenePayload.TimelineSegment.StartObservationOrdinal, segment.StartObservationOrdinal);
        Assert.Equal(record.ScenePayload.TimelineSegment.EndObservationOrdinalExclusive, segment.EndObservationOrdinalExclusive);
    }

    [Fact]
    public void LiveSource_TimelineSegmentGrowsWithJournalAppend()
    {
        var scene = new SceneLiveReadModel();
        var source = new LiveScenePlaybackSource(scene);
        var sceneId = scene.SessionId;
        AppendCombat(scene.Journal, sceneId, 100, 200, 100, 1, 1_000);
        var first = source.CreateTimelineSegment();
        var firstEndBeforeAppend = first.CurrentEndObservationOrdinalExclusive;

        AppendCombat(scene.Journal, sceneId, 100, 200, 200, 2, 2_000);
        var second = source.CreateTimelineSegment();

        Assert.True(first.IsLiveGrowing);
        Assert.Equal(1, firstEndBeforeAppend);
        Assert.Equal(2, first.CurrentEndObservationOrdinalExclusive);
        Assert.Equal(2, second.CurrentEndObservationOrdinalExclusive);
    }

    [Fact]
    public void Session_ReadNextTimelineBatch_ContinuesFromLoadedOrdinal()
    {
        var record = CreateArchiveRecord();
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));
        long[] firstOrdinals = [];
        long[] secondOrdinals = [];

        var first = session.ReadNextTimelineBatch(2, entries => firstOrdinals = [.. entries.ToArray().Select(static entry => entry.Stamp.ObservationOrdinal)]);
        var second = session.ReadNextTimelineBatch(2, entries => secondOrdinals = [.. entries.ToArray().Select(static entry => entry.Stamp.ObservationOrdinal)]);

        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Equal([0L, 1L], firstOrdinals);
        Assert.Equal([2L, 3L], secondOrdinals);
        Assert.Equal(4, session.NextLoadedObservationOrdinal);
    }

    [Fact]
    public void Seek_Midpoint_ProjectsCombatResourceAndTracks()
    {
        var record = CreateArchiveRecord();
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var frame = session.Seek(1_000);

        Assert.Equal(1_000, frame.PositionMilliseconds);
        Assert.Equal(300, frame.CombatTotals.TotalDamage);
        Assert.Single(frame.Resources);
        Assert.Equal(30_000, frame.Resources[0].CurrentValue);
        Assert.Equal(4, frame.AppliedSegment.EndObservationOrdinalExclusive);
        Assert.Contains(frame.Tracks, static track => track.Track == ScenePlaybackTrack.Combat && track.Count == 2);
        Assert.Contains(frame.Tracks, static track => track.Track == ScenePlaybackTrack.Resource && track.Count == 1);
    }

    [Fact]
    public void Seek_TrackWindows_PreserveFirstAndLastOrdinals()
    {
        var record = CreateArchiveRecord();
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var frame = session.Seek(1_000);

        var combat = Assert.Single(frame.Tracks, static track => track.Track == ScenePlaybackTrack.Combat);
        var resource = Assert.Single(frame.Tracks, static track => track.Track == ScenePlaybackTrack.Resource);
        Assert.Equal(1, combat.StartObservationOrdinal);
        Assert.Equal(3, combat.EndObservationOrdinalExclusive);
        Assert.Equal(2, combat.Count);
        Assert.Equal(3, resource.StartObservationOrdinal);
        Assert.Equal(4, resource.EndObservationOrdinalExclusive);
        Assert.Equal(1, resource.Count);
    }

    [Fact]
    public void Seek_Backwards_RebuildsAuraState()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendAura(journal, sceneId, 100, 200, 501, 7, 0, 0, 1, 1_000);
        AppendAura(journal, sceneId, 100, 200, 501, 7, 1, 1, 2, 2_000);
        var owner = new SceneReadModelOwner(journal, sceneId, DateTimeOffset.Now);
        var snapshot = owner.CreateSnapshot();
        var payload = owner.CreateArchivePayload(snapshot);
        var record = new ArchivedEncounterRecord
        {
            EncounterId = sceneId,
            Snapshot = snapshot,
            ScenePayload = payload
        };
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var afterRemove = session.Seek(1_000);
        var beforeRemove = session.Seek(0);

        Assert.Empty(afterRemove.ActiveAuras);
        Assert.Single(beforeRemove.ActiveAuras);
        Assert.Equal(501, beforeRemove.ActiveAuras[0].SkillCode);
    }

    [Fact]
    public void Controller_InitialState_UsesPausedTimelineDuration()
    {
        using var controller = CreateController(CreateArchiveRecord());

        Assert.False(controller.IsPlaying);
        Assert.False(controller.IsLoading);
        Assert.Equal(0, controller.PositionMilliseconds);
        Assert.Equal(2_000, controller.DurationMilliseconds);
        Assert.Equal(1d, controller.Speed);
        Assert.Equal(ScenePlaybackSourceKind.Archived, controller.State.SourceKind);
        Assert.Equal(0, controller.CurrentFrame.PositionMilliseconds);
    }

    [Fact]
    public void Controller_SetSpeed_RejectsInvalidValues()
    {
        using var controller = CreateController(CreateArchiveRecord());

        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetSpeed(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetSpeed(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetSpeed(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetSpeed(double.PositiveInfinity));

        controller.SetSpeed(2.5);

        Assert.Equal(2.5, controller.Speed);
        Assert.Equal(2.5, controller.Session.Speed);
    }

    [Fact]
    public void Controller_Stop_SeeksToStartAndPauses()
    {
        using var controller = CreateController(CreateArchiveRecord());
        controller.Seek(1_000);
        controller.Play();

        var frame = controller.Stop();

        Assert.False(controller.IsPlaying);
        Assert.Equal(0, controller.PositionMilliseconds);
        Assert.Equal(0, frame.PositionMilliseconds);
    }

    [Fact]
    public async Task Controller_Play_AdvancesAndPausesAtArchivedEnd()
    {
        var tickFactory = new ManualTickSourceFactory();
        using var controller = CreateController(CreateArchiveRecord(), tickFactory);
        var frames = new List<ScenePlaybackFrame>();
        controller.FrameChanged += (_, e) => frames.Add(e.Frame);

        controller.Play();
        tickFactory.Source.Tick(TimeSpan.FromMilliseconds(500));
        await WaitUntil(() => controller.PositionMilliseconds == 500);
        tickFactory.Source.Tick(TimeSpan.FromMilliseconds(5_000));
        await WaitUntil(() => !controller.IsPlaying && controller.PositionMilliseconds == controller.DurationMilliseconds);

        Assert.Equal(2_000, controller.PositionMilliseconds);
        Assert.Equal(2_000, controller.DurationMilliseconds);
        Assert.True(frames.Count >= 2);
        Assert.Equal(2_000, frames[^1].PositionMilliseconds);
    }

    [Fact]
    public void Controller_Refresh_KeepsArchivedDurationFixedAfterJournalAppend()
    {
        var record = CreateArchiveRecord();
        using var controller = CreateController(record);
        AppendCombat(record.ScenePayload.TimelineSegment.Journal!, record.EncounterId, 100, 200, 400, 6, 4_000);

        var frame = controller.Refresh();

        Assert.Equal(2_000, controller.DurationMilliseconds);
        Assert.Equal(2_000, frame.TimeRange.DurationMilliseconds);
    }

    [Fact]
    public void Controller_Refresh_GrowsLiveDurationAfterAppend()
    {
        var scene = new SceneLiveReadModel();
        AppendCombat(scene.Journal, scene.SessionId, 100, 200, 100, 1, 1_000);
        AppendCombat(scene.Journal, scene.SessionId, 100, 200, 200, 2, 2_000);
        using var controller = new ScenePlaybackController(new LiveScenePlaybackSource(scene), new ManualTickSourceFactory(), TimeSpan.FromMilliseconds(33));

        AppendCombat(scene.Journal, scene.SessionId, 100, 200, 300, 3, 4_000);
        var frame = controller.Refresh();

        Assert.Equal(ScenePlaybackSourceKind.Live, controller.State.SourceKind);
        Assert.Equal(3_000, controller.DurationMilliseconds);
        Assert.Equal(3_000, frame.TimeRange.DurationMilliseconds);
    }

    [Fact]
    public async Task Controller_SeekAsync_PublishesOnlyLatestQuickSeek()
    {
        using var controller = CreateController(CreateArchiveRecord());
        var frames = new List<ScenePlaybackFrame>();
        controller.FrameChanged += (_, e) => frames.Add(e.Frame);
        var cancellationToken = TestContext.Current.CancellationToken;

        var first = controller.SeekAsync(500, cancellationToken).AsTask();
        var second = controller.SeekAsync(1_000, cancellationToken).AsTask();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var frame = await second;

        Assert.Equal(1_000, frame.PositionMilliseconds);
        Assert.Equal(1_000, controller.PositionMilliseconds);
        Assert.Single(frames);
        Assert.Equal(1_000, frames[0].PositionMilliseconds);
    }

    [Fact]
    public async Task Controller_ManualSeekWinsOverPlaybackTick()
    {
        var tickFactory = new ManualTickSourceFactory();
        using var controller = CreateController(CreateArchiveRecord(), tickFactory);

        controller.Play();
        tickFactory.Source.Tick(TimeSpan.FromMilliseconds(500));
        var frame = await controller.SeekAsync(1_000, TestContext.Current.CancellationToken);

        Assert.Equal(1_000, frame.PositionMilliseconds);
        Assert.Equal(1_000, controller.PositionMilliseconds);
    }

    [Fact]
    public async Task Controller_DisposeStopsPlaybackLoop()
    {
        var tickFactory = new ManualTickSourceFactory();
        var controller = CreateController(CreateArchiveRecord(), tickFactory);
        var frames = new List<ScenePlaybackFrame>();
        controller.FrameChanged += (_, e) => frames.Add(e.Frame);

        controller.Play();
        await controller.DisposeAsync();
        tickFactory.Source.Tick(TimeSpan.FromMilliseconds(500));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.False(controller.IsPlaying);
        Assert.Equal(0, controller.PositionMilliseconds);
        Assert.Empty(frames);
    }

    private static ArchivedEncounterRecord CreateArchiveRecord()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        const int playerId = 100;
        const int bossId = 200;
        AppendState(journal, sceneId, playerId, StateCodes.PlayerIdentity, 0, 0, "Tester", 1, 500);
        AppendCombat(journal, sceneId, playerId, bossId, 100, 2, 1_000);
        AppendCombat(journal, sceneId, playerId, bossId, 200, 3, 1_400);
        AppendResource(journal, sceneId, bossId, 30_000, 50_000, 4, 1_500);
        AppendCombat(journal, sceneId, playerId, bossId, 300, 5, 2_500);
        journal.CompleteBatch(1);
        var owner = new SceneReadModelOwner(journal, sceneId, DateTimeOffset.Now);
        var snapshot = owner.CreateSnapshot();
        var payload = owner.CreateArchivePayload(snapshot);
        return new ArchivedEncounterRecord
        {
            EncounterId = snapshot.EncounterId,
            Snapshot = snapshot,
            ScenePayload = payload
        };
    }

    private static void AppendState(ObservedEventJournal journal, Guid sceneId, int entityId, int stateCode, int value0, int value1, string? text, long ordinal, long observedAt)
    {
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = ordinal - 1, FrameOrdinal = ordinal, BatchOrdinal = 1 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = entityId,
            TargetEntityId = 0,
            Raw = new RawPacketReference(0, 0, ordinal, observedAt),
            State = new StateObservation(entityId, stateCode, value0, value1, 0, text)
        });
    }

    private static void AppendResource(ObservedEventJournal journal, Guid sceneId, int entityId, long current, long maximum, long ordinal, long observedAt)
    {
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = ordinal - 1, FrameOrdinal = ordinal, BatchOrdinal = 1 },
            Domain = ObservedEventDomain.Resource,
            SourceEntityId = entityId,
            TargetEntityId = 0,
            Raw = new RawPacketReference(0, 0, ordinal, observedAt),
            Resource = new ResourceObservation(entityId, current, maximum, null, 0)
        });
    }

    private static void AppendCombat(ObservedEventJournal journal, Guid sceneId, int sourceId, int targetId, int damage, long ordinal, long observedAt)
    {
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = ordinal - 1, FrameOrdinal = ordinal, BatchOrdinal = 1 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference(0x0438, 0, ordinal, observedAt),
            Combat = new CombatObservation
            {
                SkillCode = 11000010,
                Damage = damage,
                HitCount = 1,
                AttemptCount = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });
    }

    private static void AppendAura(ObservedEventJournal journal, Guid sceneId, int sourceId, int targetId, int skillCode, int sequenceId, int mode, int resultCode, long ordinal, long observedAt)
    {
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = ordinal - 1, FrameOrdinal = ordinal, BatchOrdinal = 1 },
            Domain = ObservedEventDomain.Aura,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference(0x2C38, 0, ordinal, observedAt),
            Aura = new AuraObservation(sourceId, targetId, skillCode, 0, sequenceId, 0, resultCode, mode)
        });
    }

    private static ScenePlaybackController CreateController(ArchivedEncounterRecord record)
        => CreateController(record, new ManualTickSourceFactory());

    private static ScenePlaybackController CreateController(ArchivedEncounterRecord record, ManualTickSourceFactory tickSourceFactory)
        => new(new ArchivedScenePlaybackSource(record), tickSourceFactory, TimeSpan.FromMilliseconds(33));

    private static async Task WaitUntil(Func<bool> predicate)
    {
        for (var i = 0; i < 100; i++)
        {
            if (predicate())
                return;

            await Task.Delay(20);
        }

        Assert.True(predicate());
    }

    private sealed class ManualTickSourceFactory : IScenePlaybackTickSourceFactory
    {
        public ManualTickSource Source { get; } = new();

        public IScenePlaybackTickSource Create(TimeSpan interval) => Source;
    }

    private sealed class ManualTickSource : IScenePlaybackTickSource
    {
        private readonly Channel<ScenePlaybackTick> _channel = Channel.CreateUnbounded<ScenePlaybackTick>();

        public void Tick(TimeSpan elapsed)
        {
            _channel.Writer.TryWrite(new ScenePlaybackTick(elapsed));
        }

        public async ValueTask<ScenePlaybackTick> WaitForNextTickAsync(CancellationToken cancellationToken)
            => await _channel.Reader.ReadAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            _channel.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
