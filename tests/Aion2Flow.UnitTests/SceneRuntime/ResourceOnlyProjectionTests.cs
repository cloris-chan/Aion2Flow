using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Playback;
using Cloris.Aion2Flow.SceneRuntime.Projection;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class ResourceOnlyProjectionTests
{
    [Fact]
    public void ReplaySummary_ResourceOnlyOccurrencesPreserveTypedDetailsWithoutMetricTotals()
    {
        const int sourceId = 100;
        const int targetId = 200;
        var owner = CreateOwner(sourceId, targetId, out var journal);
        var snapshot = owner.CreateSnapshot();
        var summaries = owner.ReadLocked((entities, _, metadataRegistry, combat, mechanics, resources, adapter) =>
            PacketLogReplayService.BuildCombatantSummaries(combat, mechanics, resources, entities, metadataRegistry, adapter, snapshot));
        var replay = new PacketLogReplayResult(
            "resource-only",
            0,
            0,
            0,
            snapshot,
            journal,
            owner,
            summaries,
            owner.Resources.Events,
            new Dictionary<string, int>(),
            new Dictionary<string, int>());

        var source = Assert.Single(replay.Combatants, static summary => summary.CombatantId == sourceId);
        var target = Assert.Single(replay.Combatants, static summary => summary.CombatantId == targetId);
        AssertSummaryHasNoMetrics(source);
        AssertSummaryHasNoMetrics(target);

        Assert.Collection(
            replay.ResourceEvents,
            first =>
            {
                Assert.Equal(sourceId, first.SourceId);
                Assert.Equal(targetId, first.TargetId);
                Assert.Equal(4_001, first.Observation.SkillCode);
                Assert.Equal(100, first.ObservedAtMilliseconds);
                Assert.Equal(CombatResourceKind.Mana, first.Resource.Resource);
                Assert.Equal(CombatResourceFlowKind.Unknown, first.Resource.Flow);
                Assert.Equal(CombatResourceDeliveryKind.Direct, first.Resource.Delivery);
                Assert.Equal(75, first.Resource.Amount);
            },
            second =>
            {
                Assert.Equal(sourceId, second.SourceId);
                Assert.Equal(targetId, second.TargetId);
                Assert.Equal(4_002, second.Observation.SkillCode);
                Assert.Equal(200, second.ObservedAtMilliseconds);
                Assert.Equal(CombatResourceKind.Mana, second.Resource.Resource);
                Assert.Equal(CombatResourceFlowKind.Unknown, second.Resource.Flow);
                Assert.Equal(CombatResourceDeliveryKind.Direct, second.Resource.Delivery);
                Assert.Equal(25, second.Resource.Amount);
            });
    }

    [Fact]
    public void LiveProjection_ResourceOnlyOccurrencesPopulateSnapshotAndDetail()
    {
        const int sourceId = 100;
        const int targetId = 200;
        var owner = CreateOwner(sourceId, targetId);

        var snapshot = owner.CreateSnapshot();
        Assert.Equal(2, owner.Resources.Events.Count);
        Assert.Empty(owner.Combat.Events);
        Assert.Empty(owner.Mechanics.Events);
        var sourceDetail = owner.CreateDetailDelta(snapshot, sourceId, forceRefresh: true);
        var targetDetail = owner.CreateDetailDelta(snapshot, targetId, forceRefresh: true);

        Assert.Equal(100, snapshot.EncounterStartTime);
        Assert.Equal(200, snapshot.EncounterEndTime);
        Assert.Equal(100, snapshot.EncounterTime);
        Assert.True(snapshot.Combatants.ContainsKey(sourceId));
        Assert.True(snapshot.Combatants.ContainsKey(targetId));
        Assert.Equal(0, snapshot.Combatants[sourceId].DamageAmount);
        Assert.Equal(0, snapshot.Combatants[sourceId].HealingAmount);
        Assert.Equal(0, snapshot.Combatants[targetId].DamageAmount);
        Assert.Equal(2, sourceDetail.ResourceEvents.Count);
        Assert.Equal(2, targetDetail.ResourceEvents.Count);
        Assert.Empty(sourceDetail.MetricEvents);
        Assert.Empty(sourceDetail.MechanicEvents);
        Assert.Equal([new DirectedPairKey(sourceId, targetId)], sourceDetail.OutgoingPairs);
        Assert.Equal([new DirectedPairKey(sourceId, targetId)], targetDetail.IncomingPairs);
        Assert.Equal(0, sourceDetail.Combatant!.Value.OutgoingDamage);
        Assert.Equal(0, targetDetail.Combatant!.Value.IncomingDamage);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void LiveAndPlayback_SameTimestampResourceOccurrencesPreserveRosterAndDetail(int eventCount)
    {
        const int sourceId = 100;
        const int targetId = 200;
        var owner = CreateSameTimestampOwner(sourceId, targetId, eventCount);

        var snapshot = owner.CreateSnapshot();
        var sourceDetail = owner.CreateDetailDelta(snapshot, sourceId, forceRefresh: true);
        var targetDetail = owner.CreateDetailDelta(snapshot, targetId, forceRefresh: true);

        Assert.Equal(100, snapshot.EncounterStartTime);
        Assert.Equal(100, snapshot.EncounterEndTime);
        Assert.Equal(0, snapshot.EncounterTime);
        Assert.True(snapshot.Encounter.IsActive);
        Assert.True(snapshot.Combatants.ContainsKey(sourceId));
        Assert.True(snapshot.Combatants.ContainsKey(targetId));
        Assert.Equal(eventCount, sourceDetail.ResourceEvents.Count);
        Assert.Equal(eventCount, targetDetail.ResourceEvents.Count);

        var payload = owner.CreateArchivePayload(snapshot);
        var playback = new ScenePlaybackSession(new TestPlaybackSource(owner.EncounterId, payload.TimelineSegment, snapshot));
        var frame = playback.Seek(100, TestContext.Current.CancellationToken);
        var playbackDetail = playback.CreateCombatantDetail(targetId);

        Assert.True(frame.Snapshot.Combatants.ContainsKey(sourceId));
        Assert.True(frame.Snapshot.Combatants.ContainsKey(targetId));
        Assert.Equal(0, frame.Snapshot.EncounterTime);
        Assert.Equal(eventCount, playbackDetail.Events.ResourceEvents.Count);
        Assert.Equal(eventCount, playbackDetail.Update.AddedResourceEventCount);
    }

    [Fact]
    public void ArchiveRoundTrip_ResourceOnlyPairPreservesRosterAndZeroMetricTotals()
    {
        const int sourceId = 100;
        const int targetId = 200;
        var owner = CreateOwner(sourceId, targetId);
        var snapshot = owner.CreateSnapshot();
        var payload = owner.CreateArchivePayload(snapshot);
        var service = new EncounterArchiveService();

        var record = service.Archive(snapshot, payload, "resource-only", isAutomatic: false);

        Assert.NotNull(record);
        Assert.True(service.TryGetEncounter(record!.EncounterId, out var restored));
        var pair = Assert.Single(restored!.ScenePayload.Pairs);
        Assert.Equal(new DirectedPairKey(sourceId, targetId), pair.Key);
        Assert.Equal(0, pair.TotalDamage);
        Assert.Equal(0, pair.TotalHealing);
        Assert.Equal(0, pair.HitCount);
        Assert.Equal(4_002, pair.LastSkillCode);
        Assert.Equal(2, restored.ScenePayload.Combatants.Count);
        Assert.Equal(2, restored.ScenePayload.ResourceEvents.Count);

        var sourceDetail = restored.ScenePayload.CreateDetailDelta(sourceId);
        var targetDetail = restored.ScenePayload.CreateDetailDelta(targetId);
        Assert.Equal(2, sourceDetail.ResourceEvents.Count);
        Assert.Equal(2, targetDetail.ResourceEvents.Count);
        Assert.Equal([pair.Key], sourceDetail.OutgoingPairs);
        Assert.Equal([pair.Key], targetDetail.IncomingPairs);
        Assert.Equal(0, sourceDetail.Combatant!.Value.OutgoingDamage);
        Assert.Equal(0, targetDetail.Combatant!.Value.IncomingDamage);
    }

    [Fact]
    public void ArchiveRoundTrip_UnknownResourceSourcePreservesPositiveTargetRoster()
    {
        const int targetId = 200;
        var owner = CreateOwner(sourceId: 0, targetId);
        var snapshot = owner.CreateSnapshot();
        var payload = owner.CreateArchivePayload(snapshot);
        var service = new EncounterArchiveService();

        var record = service.Archive(snapshot, payload, "unknown-resource-source", isAutomatic: false);

        Assert.NotNull(record);
        Assert.True(service.TryGetEncounter(record!.EncounterId, out var restored));
        Assert.Empty(restored!.ScenePayload.Pairs);
        var target = Assert.Single(restored.ScenePayload.Combatants);
        Assert.Equal(targetId, target.CombatantId);
        Assert.Equal(100, target.FirstObserved);
        Assert.Equal(200, target.LastObserved);
        Assert.Equal(2, target.Revision);

        var detail = restored.ScenePayload.CreateDetailDelta(targetId);
        Assert.Equal(2, detail.ResourceEvents.Count);
        Assert.NotNull(detail.Combatant);
        Assert.Empty(detail.OutgoingPairs);
        Assert.Empty(detail.IncomingPairs);
    }

    private static SceneReadModelOwner CreateOwner(int sourceId, int targetId) =>
        CreateOwner(sourceId, targetId, out _);

    private static SceneReadModelOwner CreateOwner(int sourceId, int targetId, out ObservedEventJournal journal)
    {
        journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendResource(journal, sceneId, sourceId, targetId, amount: 75, skillCode: 4_001, ordinal: 0, observedAtMilliseconds: 100);
        AppendResource(journal, sceneId, sourceId, targetId, amount: 25, skillCode: 4_002, ordinal: 1, observedAtMilliseconds: 200);
        journal.CompleteFlush(1);
        return new SceneReadModelOwner(journal, sceneId, DateTimeOffset.Now);
    }

    private static SceneReadModelOwner CreateSameTimestampOwner(int sourceId, int targetId, int eventCount)
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        for (var i = 0; i < eventCount; i++)
        {
            AppendResource(
                journal,
                sceneId,
                sourceId,
                targetId,
                amount: 75 - i,
                skillCode: 4_001 + i,
                ordinal: i,
                observedAtMilliseconds: 100);
        }
        journal.CompleteFlush(1);
        return new SceneReadModelOwner(journal, sceneId, DateTimeOffset.Now);
    }

    private static void AppendResource(
        ObservedEventJournal journal,
        Guid sceneId,
        int sourceId,
        int targetId,
        long amount,
        int skillCode,
        long ordinal,
        long observedAtMilliseconds)
    {
        var header = new ObservedEventHeader(
            sceneId,
            new TimelineStamp
            {
                OffsetTicks = observedAtMilliseconds * TimeSpan.TicksPerMillisecond,
                ObservationOrdinal = ordinal,
                FlushId = 1
            },
            sourceId,
            targetId,
            new RawPacketReference(0x0438, 0, ordinal));
        var observation = new CombatWireObservation
        {
            SkillCode = skillCode,
            Damage = amount,
            HitCount = 1,
            AttemptCount = 1,
            ResourceKind = CombatResourceKind.Mana
        };
        journal.Append(in header, in observation);
    }

    private static void AssertSummaryHasNoMetrics(PacketLogCombatantSummary summary)
    {
        Assert.Equal(0, summary.OutgoingDamage);
        Assert.Equal(0, summary.IncomingDamage);
        Assert.Equal(0, summary.OutgoingHealing);
        Assert.Equal(0, summary.IncomingHealing);
        Assert.Equal(0, summary.OutgoingShield);
        Assert.Equal(0, summary.IncomingShield);
        Assert.Equal(0, summary.OutgoingShieldAbsorbed);
        Assert.Equal(0, summary.IncomingShieldAbsorbed);
        Assert.Equal(0, summary.OutgoingHits);
        Assert.Equal(0, summary.IncomingHits);
        Assert.Equal(0, summary.OutgoingAttempts);
        Assert.Equal(0, summary.IncomingAttempts);
    }

    private sealed class TestPlaybackSource(Guid encounterId, SceneJournalSegment segment, SceneCombatSnapshot snapshot) : IScenePlaybackSource
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
