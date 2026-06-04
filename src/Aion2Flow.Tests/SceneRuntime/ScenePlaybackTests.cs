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
}
