using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Playback;
using Cloris.Aion2Flow.SceneRuntime.Projection;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class ScenePlaybackTypedTrackProjectionTests
{
    [Fact]
    public void ResourceOnlyObservation_NeverExposesRawCombatTrack()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendCombat(
            journal,
            sceneId,
            ordinal: 0,
            observedAtMilliseconds: 100,
            new CombatWireObservation
            {
                SkillCode = 4_001,
                Damage = 75,
                HitCount = 1,
                AttemptCount = 1,
                ResourceKind = CombatResourceKind.Mana
            });
        AppendState(journal, sceneId, ordinal: 1, observedAtMilliseconds: 200);
        journal.CompleteFlush(1);

        var (segment, snapshot) = CreatePlaybackState(journal, sceneId);
        var index = ScenePlaybackTrackIndex.Build(segment, TestContext.Current.CancellationToken);
        var observationTracks = index.ReadWindow(0, 200, segment.CurrentEndObservationOrdinalExclusive, 10)
            .AsSpan()
            .ToArray()
            .Where(static marker => marker.ObservationOrdinal == 0)
            .Select(static marker => marker.Track)
            .ToArray();
        var frame = new ScenePlaybackSession(new TestPlaybackSource(sceneId, segment, snapshot)).Seek(100, TestContext.Current.CancellationToken);

        Assert.Equal([ScenePlaybackTrack.Resource], observationTracks);
        Assert.DoesNotContain(
            index.ReadWindow(0, 200, segment.CurrentEndObservationOrdinalExclusive, 10).AsSpan().ToArray(),
            static marker => marker.Track == ScenePlaybackTrack.Combat);
        Assert.Contains(frame.Tracks, static track => track.Track == ScenePlaybackTrack.Resource && track.Count == 1);
        Assert.DoesNotContain(frame.Tracks, static track => track.Track == ScenePlaybackTrack.Combat);
        Assert.DoesNotContain(frame.Tracks, static track => track.Track == ScenePlaybackTrack.Mechanic);
    }

    [Fact]
    public void PureAvoidanceObservation_ProjectsMechanicWithoutCombatTrack()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendCombat(
            journal,
            sceneId,
            ordinal: 0,
            observedAtMilliseconds: 100,
            new CombatWireObservation
            {
                SkillCode = 11_380_050,
                AttemptCount = 1,
                Modifiers = DamageModifiers.Evade
            });
        AppendState(journal, sceneId, ordinal: 1, observedAtMilliseconds: 200);
        journal.CompleteFlush(1);

        var (segment, snapshot) = CreatePlaybackState(journal, sceneId);
        var index = ScenePlaybackTrackIndex.Build(segment, TestContext.Current.CancellationToken);
        var observationTracks = index.ReadWindow(0, 200, segment.CurrentEndObservationOrdinalExclusive, 10)
            .AsSpan()
            .ToArray()
            .Where(static marker => marker.ObservationOrdinal == 0)
            .Select(static marker => marker.Track)
            .ToArray();
        var frame = new ScenePlaybackSession(new TestPlaybackSource(sceneId, segment, snapshot)).Seek(100, TestContext.Current.CancellationToken);

        Assert.Equal([ScenePlaybackTrack.Mechanic], observationTracks);
        Assert.Contains(frame.Tracks, static track => track.Track == ScenePlaybackTrack.Mechanic && track.Count == 1);
        Assert.DoesNotContain(frame.Tracks, static track => track.Track == ScenePlaybackTrack.Combat);
    }

    [Fact]
    public void TrackIndex_ReadWindow_RespectsObservationBoundaryAndPositionOrderingForExpandedFacts()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendState(journal, sceneId, ordinal: 0, observedAtMilliseconds: 100);
        AppendCombat(
            journal,
            sceneId,
            ordinal: 1,
            observedAtMilliseconds: 200,
            new CombatWireObservation
            {
                SkillCode = 11_000_020,
                Damage = 500,
                HitCount = 1,
                AttemptCount = 1
            });
        AppendCombat(
            journal,
            sceneId,
            ordinal: 2,
            observedAtMilliseconds: 300,
            new CombatWireObservation
            {
                SkillCode = 4_001,
                Damage = 75,
                HitCount = 1,
                AttemptCount = 1,
                ResourceKind = CombatResourceKind.Mana
            });
        AppendState(journal, sceneId, ordinal: 3, observedAtMilliseconds: 400);
        journal.CompleteFlush(1);

        var (segment, _) = CreatePlaybackState(journal, sceneId);
        var index = ScenePlaybackTrackIndex.Build(segment, TestContext.Current.CancellationToken);
        var beforeResource = index.ReadWindow(0, 400, endObservationOrdinalExclusive: 2, maxMarkers: 10).AsSpan().ToArray();
        var afterDamage = index.ReadWindow(201, 400, segment.CurrentEndObservationOrdinalExclusive, 10).AsSpan().ToArray();

        Assert.Equal([0L, 1L, 1L], beforeResource.Select(static marker => marker.ObservationOrdinal));
        Assert.Equal(
            [ScenePlaybackTrack.State, ScenePlaybackTrack.Combat, ScenePlaybackTrack.Mechanic],
            beforeResource.Select(static marker => marker.Track));
        Assert.Equal([2L, 3L], afterDamage.Select(static marker => marker.ObservationOrdinal));
        Assert.Equal([ScenePlaybackTrack.Resource, ScenePlaybackTrack.State], afterDamage.Select(static marker => marker.Track));
        Assert.True(index.Count > segment.CurrentEndObservationOrdinalExclusive - segment.StartObservationOrdinal);
        Assert.Equal(segment.CurrentEndObservationOrdinalExclusive, index.EndObservationOrdinalExclusive);
    }

    private static (SceneJournalSegment Segment, SceneCombatSnapshot Snapshot) CreatePlaybackState(
        ObservedEventJournal journal,
        Guid sceneId)
    {
        var owner = new SceneReadModelOwner(journal, sceneId, DateTimeOffset.UnixEpoch);
        var snapshot = owner.CreateSnapshot();
        return (owner.CreateArchivePayload().TimelineSegment, snapshot);
    }

    private static void AppendCombat(
        ObservedEventJournal journal,
        Guid sceneId,
        long ordinal,
        long observedAtMilliseconds,
        CombatWireObservation observation)
    {
        var header = CreateHeader(sceneId, ordinal, observedAtMilliseconds, sourceEntityId: 100, targetEntityId: 200);
        journal.Append(in header, in observation);
    }

    private static void AppendState(
        ObservedEventJournal journal,
        Guid sceneId,
        long ordinal,
        long observedAtMilliseconds)
    {
        var header = CreateHeader(sceneId, ordinal, observedAtMilliseconds, sourceEntityId: 100, targetEntityId: 0);
        var observation = new StateObservation(100, 99_999, 0, 0, 0, null);
        journal.Append(in header, in observation);
    }

    private static ObservedEventHeader CreateHeader(
        Guid sceneId,
        long ordinal,
        long observedAtMilliseconds,
        int sourceEntityId,
        int targetEntityId)
        => new(
            sceneId,
            new TimelineStamp
            {
                OffsetTicks = observedAtMilliseconds * TimeSpan.TicksPerMillisecond,
                ObservationOrdinal = ordinal,
                FlushId = 1
            },
            sourceEntityId,
            targetEntityId,
            new RawPacketReference(0x0438, 0, ordinal));

    private sealed class TestPlaybackSource(
        Guid encounterId,
        SceneJournalSegment segment,
        SceneCombatSnapshot snapshot) : IScenePlaybackSource
    {
        public Guid EncounterId { get; } = encounterId;
        public DateTimeOffset SceneStarted => DateTimeOffset.UnixEpoch;
        public ScenePlaybackSourceKind SourceKind => ScenePlaybackSourceKind.Archived;
        public SceneJournalSegment CreateTimelineSegment() => segment;
        public SceneCombatSnapshot CreateSnapshot() => snapshot;

        public void Dispose()
        {
        }
    }
}
