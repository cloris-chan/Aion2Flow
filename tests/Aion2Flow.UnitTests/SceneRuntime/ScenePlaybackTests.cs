using System.Collections.Concurrent;
using System.Threading.Channels;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Playback;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class ScenePlaybackTests
{
    [Fact]
    public void EventId_CompareTo_DoesNotAllocate()
    {
        ScenePlaybackEventId[] eventIds =
        [
            new(ScenePlaybackEventFactKind.Metric, 1),
            new(ScenePlaybackEventFactKind.Mechanic, 2),
            new(ScenePlaybackEventFactKind.Resource, 3),
            new(ScenePlaybackEventFactKind.Observation, 4)
        ];
        _ = eventIds[0].CompareTo(eventIds[1]);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0;
        for (var i = 0; i < 100_000; i++)
            checksum += eventIds[i & 3].CompareTo(eventIds[(i + 1) & 3]) * ((i & 1) + 1);
        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.NotEqual(0, checksum);
        Assert.Equal(allocatedBefore, allocatedAfter);
    }

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

        var first = session.ReadNextTimelineBatch(2, entries => firstOrdinals = CopyOrdinals(entries));
        var second = session.ReadNextTimelineBatch(2, entries => secondOrdinals = CopyOrdinals(entries));

        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Equal([0L, 1L], firstOrdinals);
        Assert.Equal([2L, 3L], secondOrdinals);
        Assert.Equal(4, session.NextLoadedObservationOrdinal);
    }

    private static long[] CopyOrdinals(JournalEntryBatch entries)
    {
        var ordinals = new long[entries.Count];
        for (var i = 0; i < entries.Count; i++)
            ordinals[i] = entries[i].Stamp.ObservationOrdinal;
        return ordinals;
    }

    [Fact]
    public void Seek_Midpoint_ProjectsCombatEntityVitalAndTracks()
    {
        var record = CreateArchiveRecord();
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var frame = session.Seek(1_500);

        Assert.Equal(1_500, frame.PositionMilliseconds);
        Assert.Equal(300, frame.CombatTotals.TotalDamage);
        Assert.Single(frame.EntityVitals);
        Assert.Equal(30_000, frame.EntityVitals[0].CurrentHp);
        Assert.Equal(4, frame.AppliedSegment.EndObservationOrdinalExclusive);
        Assert.Contains(frame.Tracks, static track => track.Track == ScenePlaybackTrack.Combat && track.Count == 2);
        Assert.Contains(frame.Tracks, static track => track.Track == ScenePlaybackTrack.EntityVital && track.Count == 1);
    }

    [Fact]
    public void Session_AdvanceTo_ContinuesCurrentProjector()
    {
        var record = CreateArchiveRecord();
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var first = session.Seek(1_500);
        var second = session.AdvanceTo(2_500);

        Assert.Equal(300, first.CombatTotals.TotalDamage);
        Assert.Equal(600, second.CombatTotals.TotalDamage);
        Assert.Equal(5, second.AppliedSegment.EndObservationOrdinalExclusive);
        Assert.Equal([0L, 1L, 1L, 2L, 2L, 3L, 4L, 4L], ReadAppliedMarkers(record, second).Select(static marker => marker.ObservationOrdinal));
    }

    [Fact]
    public void TrackIndex_ReadWindow_DoesNotAllocateOrCopyMarkers()
    {
        var record = CreateArchiveRecord();
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));
        var index = session.CreateTrackIndex(TestContext.Current.CancellationToken);

        var window = index.ReadWindow(0, 2_500, 5, 3);
        Assert.Equal([3L, 4L, 4L], window.AsSpan().ToArray().Select(static marker => marker.ObservationOrdinal));

        _ = index.ReadWindow(0, 2_500, 5, 3).AsSpan().Length;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = 0;
        for (var i = 0; i < 10_000; i++)
            total += index.ReadWindow(0, 2_500, 5, 3).AsSpan().Length;
        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(30_000, total);
        Assert.Equal(allocatedBefore, allocatedAfter);
    }

    [Fact]
    public void Session_CreateCombatantDetail_UsesCurrentProjectionBoundary()
    {
        var record = CreateArchiveRecord();
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));
        var targetSession = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        session.Seek(1_500);
        var midpoint = session.CreateCombatantDetail(100);
        session.AdvanceTo(2_500);
        var completed = session.CreateCombatantDetail(100);
        targetSession.Seek(1_500);
        var target = targetSession.CreateCombatantDetail(200);

        Assert.Equal(1_500, midpoint.PositionMilliseconds);
        Assert.Equal(4, midpoint.EndObservationOrdinalExclusive);
        Assert.True(midpoint.Update.IsFullSnapshot);
        Assert.Equal(300, midpoint.Events.MetricEvents.Sum(static entry => entry.Amount));
        Assert.True(target.Update.IsFullSnapshot);
        Assert.Equal(300, target.Events.MetricEvents.Sum(static entry => entry.Amount));
        Assert.Equal(2_500, completed.PositionMilliseconds);
        Assert.Equal(5, completed.EndObservationOrdinalExclusive);
        Assert.True(completed.Update.IsFullSnapshot);
        Assert.Equal(600, completed.Events.MetricEvents.Sum(static entry => entry.Amount));
    }

    [Fact]
    public void Session_CreateCombatantDetail_UsesCurrentFrameScopeForEventsOutsideEncounterWindow()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        const int combatantId = 100;
        const int bossId = 200;
        AppendCombat(journal, sceneId, combatantId, 0, 400, 1, 500);
        AppendCombat(journal, sceneId, combatantId, bossId, 600, 2, 1_000);
        AppendCombat(journal, sceneId, combatantId, bossId, 300, 3, 1_400);
        AppendCombat(journal, sceneId, combatantId, bossId, 1_000, 4, 2_000);
        var record = CreateArchiveRecord(journal, sceneId);
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var frame = session.Seek(1_500);
        var projection = session.CreateCombatantDetail(combatantId);

        Assert.True(frame.Snapshot.Combatants.TryGetValue(combatantId, out var metrics));
        Assert.Equal(1_300, metrics.DamageAmount);
        Assert.Equal(metrics.DamageAmount, projection.Events.MetricEvents.Where(static entry => entry.SourceId == combatantId).Sum(static entry => entry.Amount));
        Assert.True(projection.Update.IsFullSnapshot);
    }

    [Fact]
    public void Session_CreateCombatantDetail_UsesValueEventKeysForCompactControlPackets()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        const int sourceId = 100;
        const int targetId = 200;
        const int headerSkillCode = 99999999;
        const int firstContributionSkillCode = 11001001;
        const int secondContributionSkillCode = 3000123;
        const int marker = 777;
        AppendCompactCombatValue(journal, sceneId, sourceId, targetId, firstContributionSkillCode, headerSkillCode, marker, 500, 1, 1_000, scopeId: 42, siblingIndex: 0, parentScopeId: 900);
        AppendCompactCombatValue(journal, sceneId, sourceId, targetId, secondContributionSkillCode, headerSkillCode, marker, 250, 2, 1_100, scopeId: 42, siblingIndex: 1, parentScopeId: 900);
        AppendCompactCombatOpener(journal, sceneId, sourceId, headerSkillCode, marker, 3, 1_200, scopeId: 43, siblingIndex: 2, parentScopeId: 900);
        journal.CompleteFlush(1);
        var record = CreateArchiveRecord(journal, sceneId);
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        session.Seek(1_200);
        var projection = session.CreateCombatantDetail(sourceId);

        Assert.True(projection.Update.IsFullSnapshot);
        Assert.Equal(750, projection.Events.MetricEvents.Sum(static entry => entry.Amount));
        Assert.DoesNotContain(projection.Events.MetricEvents, static entry => entry.EventKey == new CombatEventKey(headerSkillCode, default, default));
        Assert.Equal(
            [new CombatEventKey(firstContributionSkillCode, default, default), new CombatEventKey(secondContributionSkillCode, default, default)],
            projection.Events.MetricEvents.Select(static entry => entry.EventKey).ToArray());
        Assert.All(projection.Events.MetricEvents, static entry => Assert.Equal(CombatMetricKind.Damage, entry.Contribution.Metric));
        Assert.Equal([500L, 250L], projection.Events.MetricEvents.Select(static entry => entry.Contribution.Amount).ToArray());
    }

    [Fact]
    public void Session_MaterializedEventIndex_PreservesTypedFactsAndHierarchicalScopes()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        const int sourceId = 100;
        const int targetId = 200;
        const int damageSkillCode = 11000010;
        const int healingSkillCode = 13000010;
        const int manaSkillCode = 13000020;
        AppendCombatObservation(journal, sceneId, sourceId, targetId, new CombatWireObservation
        {
            SkillCode = damageSkillCode,
            Damage = 500,
            HitCount = 1,
            AttemptCount = 1
        }, 1, 1_000);
        AppendCombatObservation(journal, sceneId, sourceId, targetId, new CombatWireObservation
        {
            SkillCode = healingSkillCode,
            Damage = 300,
            ResourceKind = CombatResourceKind.Health
        }, 2, 1_200, opcode: 0x0538);
        AppendCombatObservation(journal, sceneId, sourceId, targetId, new CombatWireObservation
        {
            SkillCode = manaSkillCode,
            Damage = 75,
            ResourceKind = CombatResourceKind.Mana
        }, 3, 1_400, opcode: 0x0538);
        journal.CompleteFlush(1);
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(CreateArchiveRecord(journal, sceneId)));
        session.Seek(1_500);

        var all = ReadMaterializedEvents(session, ScenePlaybackEventScope.All, 0, 1_500);
        var outgoing = ReadMaterializedEvents(session, ScenePlaybackEventScope.ForRelation(sourceId, ScenePlaybackEventRelation.Outgoing), 0, 1_500);
        var damage = ReadMaterializedEvents(session, ScenePlaybackEventScope.ForCategory(sourceId, ScenePlaybackEventRelation.Outgoing, CombatContributionCategory.Damage), 0, 1_500);
        var healing = ReadMaterializedEvents(session, ScenePlaybackEventScope.ForCategory(sourceId, ScenePlaybackEventRelation.Outgoing, CombatContributionCategory.Healing), 0, 1_500);
        var damageSkill = ReadMaterializedEvents(
            session,
            ScenePlaybackEventScope.ForSkill(
                sourceId,
                ScenePlaybackEventRelation.Outgoing,
                CombatContributionCategory.Damage,
                SkillBaseKey.FromEventKey(new CombatEventKey(damageSkillCode, default, default))),
            0,
            1_500);

        Assert.Equal(5, all.Length);
        Assert.Equal(5, outgoing.Length);
        Assert.Equal(2, all.Count(static marker => marker.Id.Kind == ScenePlaybackEventFactKind.Metric));
        Assert.Single(all, static marker => marker.Id.Kind == ScenePlaybackEventFactKind.Mechanic);
        Assert.Equal(2, all.Count(static marker => marker.Id.Kind == ScenePlaybackEventFactKind.Resource));
        Assert.Equal([ScenePlaybackEventFactKind.Metric, ScenePlaybackEventFactKind.Mechanic], damage.Select(static marker => marker.Id.Kind));
        Assert.Single(healing, static marker => marker.Contribution?.Metric == CombatMetricKind.Healing);
        Assert.Equal([ScenePlaybackEventFactKind.Metric, ScenePlaybackEventFactKind.Mechanic], damageSkill.Select(static marker => marker.Id.Kind));
        Assert.Contains(all, static marker => marker.Resource is { Resource: CombatResourceKind.Health, Amount: 300 });
        Assert.Contains(all, static marker => marker.Resource is { Resource: CombatResourceKind.Mana, Amount: 75 });
    }

    [Fact]
    public void Session_MaterializedEventIndex_SortsMixedFactsOnRebuildAndIncrementalRefresh()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        const int sourceId = 100;
        const int targetId = 200;
        AppendCombatObservation(journal, sceneId, sourceId, targetId, new CombatWireObservation
        {
            SkillCode = 13000020,
            Damage = 25,
            ResourceKind = CombatResourceKind.Mana
        }, 1, 900, opcode: 0x0538);
        AppendCombat(journal, sceneId, sourceId, targetId, 100, 2, 1_000);
        AppendCombatObservation(journal, sceneId, sourceId, targetId, new CombatWireObservation
        {
            SkillCode = 13000020,
            Damage = 50,
            ResourceKind = CombatResourceKind.Mana
        }, 3, 1_200, opcode: 0x0538);
        AppendCombat(journal, sceneId, sourceId, targetId, 200, 4, 1_300);
        journal.CompleteFlush(1);
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(CreateArchiveRecord(journal, sceneId)));

        session.Seek(1_000);
        var rebuilt = ReadMaterializedEvents(session, ScenePlaybackEventScope.All, 0, 1_300);

        Assert.Equal(
            [
                (ScenePlaybackEventFactKind.Resource, 900L),
                (ScenePlaybackEventFactKind.Metric, 1_000L),
                (ScenePlaybackEventFactKind.Mechanic, 1_000L)
            ],
            rebuilt.Select(static marker => (marker.Id.Kind, marker.PositionMilliseconds)).ToArray());

        session.AdvanceTo(1_300);
        var refreshed = ReadMaterializedEvents(session, ScenePlaybackEventScope.All, 0, 1_300);

        Assert.Equal(
            [
                (ScenePlaybackEventFactKind.Resource, 900L),
                (ScenePlaybackEventFactKind.Metric, 1_000L),
                (ScenePlaybackEventFactKind.Mechanic, 1_000L),
                (ScenePlaybackEventFactKind.Resource, 1_200L),
                (ScenePlaybackEventFactKind.Metric, 1_300L),
                (ScenePlaybackEventFactKind.Mechanic, 1_300L)
            ],
            refreshed.Select(static marker => (marker.Id.Kind, marker.PositionMilliseconds)).ToArray());
        Assert.Equal(refreshed, ReadMaterializedEvents(session, ScenePlaybackEventScope.All, 0, 1_300));
    }

    [Fact]
    public void Session_Tracks_ProjectMaterializedFactsForCompactControlPackets()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        const int sourceId = 100;
        const int targetId = 200;
        const int headerSkillCode = 99999999;
        const int firstContributionSkillCode = 11001001;
        const int secondContributionSkillCode = 3000123;
        const int marker = 777;
        AppendCompactCombatValue(journal, sceneId, sourceId, targetId, firstContributionSkillCode, headerSkillCode, marker, 500, 1, 1_000, scopeId: 42, siblingIndex: 0, parentScopeId: 900);
        AppendCompactCombatValue(journal, sceneId, sourceId, targetId, secondContributionSkillCode, headerSkillCode, marker, 250, 2, 1_100, scopeId: 42, siblingIndex: 1, parentScopeId: 900);
        AppendCompactCombatOpener(journal, sceneId, sourceId, headerSkillCode, marker, 3, 1_200, scopeId: 43, siblingIndex: 2, parentScopeId: 900);
        journal.CompleteFlush(1);
        var record = CreateArchiveRecord(journal, sceneId);
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var frame = session.Seek(1_200);
        var appliedMarkers = ReadAppliedMarkers(record, frame);
        var combatMarkers = appliedMarkers.Where(static marker => marker.Track == ScenePlaybackTrack.Combat).ToArray();
        var mechanicMarkers = appliedMarkers.Where(static marker => marker.Track == ScenePlaybackTrack.Mechanic).ToArray();

        var combatTrack = Assert.Single(frame.Tracks, static track => track.Track == ScenePlaybackTrack.Combat);
        var mechanicTrack = Assert.Single(frame.Tracks, static track => track.Track == ScenePlaybackTrack.Mechanic);
        Assert.Equal(2, combatTrack.Count);
        Assert.Equal(0, combatTrack.StartObservationOrdinal);
        Assert.Equal(2, combatTrack.EndObservationOrdinalExclusive);
        Assert.Equal(2, mechanicTrack.Count);
        Assert.Equal([0L, 1L], combatMarkers.Select(static marker => marker.ObservationOrdinal).ToArray());
        Assert.Equal([firstContributionSkillCode, secondContributionSkillCode], combatMarkers.Select(static marker => marker.SkillCode).ToArray());
        Assert.Equal([500L, 250L], combatMarkers.Select(static marker => marker.Amount).ToArray());
        Assert.Equal([0L, 1L], mechanicMarkers.Select(static marker => marker.ObservationOrdinal).ToArray());
        Assert.DoesNotContain(appliedMarkers, static marker => marker.ObservationOrdinal == 2);
    }

    [Fact]
    public void Seek_EntityVitalWithoutMaximum_PreservesKnownMaxHp()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        const int bossId = 200;
        AppendEntityVital(journal, sceneId, bossId, 50_000, 50_000, 1, 1_000);
        AppendEntityVital(journal, sceneId, bossId, 30_000, null, 2, 1_500);
        journal.CompleteFlush(1);
        var owner = new SceneReadModelOwner(journal, sceneId, DateTimeOffset.Now);
        var snapshot = owner.CreateSnapshot();
        var record = new ArchivedEncounterRecord
        {
            EncounterId = sceneId,
            Snapshot = snapshot,
            ScenePayload = owner.CreateArchivePayload(snapshot)
        };
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var frame = session.Seek(1_500);

        var vital = Assert.Single(frame.EntityVitals);
        Assert.Equal(30_000, vital.CurrentHp);
        Assert.Equal(50_000, vital.MaxHp);
        var marker = Assert.Single(ReadAppliedMarkers(record, frame), static marker => marker.Track == ScenePlaybackTrack.EntityVital && marker.ObservationOrdinal == 1);
        Assert.Equal(30_000, marker.CurrentHp);
        Assert.Equal(50_000, marker.MaxHp);
    }

    [Fact]
    public void Seek_EntityVitalMaximum_DoesNotPromoteCurrentHp()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        const int bossId = 200;
        AppendEntityVital(journal, sceneId, bossId, 50_000, 30_000, 1, 1_000);
        journal.CompleteFlush(1);
        var owner = new SceneReadModelOwner(journal, sceneId, DateTimeOffset.Now);
        var snapshot = owner.CreateSnapshot();
        var record = new ArchivedEncounterRecord
        {
            EncounterId = sceneId,
            Snapshot = snapshot,
            ScenePayload = owner.CreateArchivePayload(snapshot)
        };
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var frame = session.Seek(1_000);

        var vital = Assert.Single(frame.EntityVitals);
        Assert.Equal(50_000, vital.CurrentHp);
        Assert.Equal(30_000, vital.MaxHp);
        var marker = Assert.Single(ReadAppliedMarkers(record, frame), static marker => marker.Track == ScenePlaybackTrack.EntityVital);
        Assert.Equal(50_000, marker.CurrentHp);
        Assert.Equal(30_000, marker.MaxHp);
    }

    [Fact]
    public void Seek_TrackWindows_PreserveFirstAndLastOrdinals()
    {
        var record = CreateArchiveRecord();
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var frame = session.Seek(1_500);

        var combat = Assert.Single(frame.Tracks, static track => track.Track == ScenePlaybackTrack.Combat);
        var entityVital = Assert.Single(frame.Tracks, static track => track.Track == ScenePlaybackTrack.EntityVital);
        Assert.Equal(1, combat.StartObservationOrdinal);
        Assert.Equal(3, combat.EndObservationOrdinalExclusive);
        Assert.Equal(2, combat.Count);
        Assert.Equal(3, entityVital.StartObservationOrdinal);
        Assert.Equal(4, entityVital.EndObservationOrdinalExclusive);
        Assert.Equal(1, entityVital.Count);
    }

    [Fact]
    public void Seek_Backwards_RebuildsAuraState()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendAuraOpen(journal, sceneId, 200, 100, 7, 1_000, 1, 0);
        AppendAuraRenew(journal, sceneId, 200, 100, 7, 2, 800);
        AppendAuraResult(journal, sceneId, 200, 7, 19, 3, 1_800);
        var record = CreateArchiveRecord(journal, sceneId);
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var afterRemove = session.Seek(1_800);
        var beforeRemove = session.Seek(1_799);

        Assert.Empty(afterRemove.ActiveAuras);
        Assert.Single(beforeRemove.ActiveAuras);
        Assert.Equal(200, beforeRemove.ActiveAuras[0].TargetEntityId);
        Assert.Equal(7, beforeRemove.ActiveAuras[0].InstanceSequenceId);
        Assert.Equal(1_800, beforeRemove.ActiveAuras[0].ExpiresAtMilliseconds);
    }

    [Fact]
    public void AuraLeaseExpiresWithoutResultAndRenewalExtendsIt()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendAuraOpen(journal, sceneId, 200, 100, 7, 1_000, 1, 0);
        AppendAuraRenew(journal, sceneId, 200, 100, 7, 2, 800);
        AppendCombat(journal, sceneId, 100, 200, 1, 3, 2_000);
        var record = CreateArchiveRecord(journal, sceneId);
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var beforeOriginalExpiry = session.Seek(999);
        var afterOriginalExpiry = session.Seek(1_799);
        var afterRenewedExpiry = session.Seek(1_800);

        Assert.Single(beforeOriginalExpiry.ActiveAuras);
        Assert.Single(afterOriginalExpiry.ActiveAuras);
        Assert.Empty(afterRenewedExpiry.ActiveAuras);
    }

    [Fact]
    public void AuraLeaseCanRenewAfterTemporaryExpiry()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendAuraOpen(journal, sceneId, 200, 100, 7, 1_000, 1, 0);
        AppendCombat(journal, sceneId, 100, 200, 1, 2, 1_000);
        AppendAuraRenew(journal, sceneId, 200, 100, 7, 3, 1_200);
        AppendCombat(journal, sceneId, 100, 200, 1, 4, 2_500);
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(CreateArchiveRecord(journal, sceneId)));

        var expired = session.Seek(1_000);
        var renewed = session.AdvanceTo(1_200);

        Assert.Empty(expired.ActiveAuras);
        Assert.Single(renewed.ActiveAuras);
        Assert.Equal(2_200, renewed.ActiveAuras[0].ExpiresAtMilliseconds);
    }

    [Fact]
    public void AuraLifecyclePacketsShareOnePlaybackTrack()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendAuraOpen(journal, sceneId, 200, 100, 7, 1_000, 1, 0);
        AppendAuraRenew(journal, sceneId, 200, 100, 7, 2, 500);
        AppendAuraResult(journal, sceneId, 200, 7, 6, 3, 1_500);
        var segment = CreateArchiveRecord(journal, sceneId).ScenePayload.TimelineSegment;

        var markers = ScenePlaybackTrackIndex.Build(segment, TestContext.Current.CancellationToken)
            .ReadWindow(0, 1_500, segment.CurrentEndObservationOrdinalExclusive, 10)
            .AsSpan()
            .ToArray();

        Assert.Equal(3, markers.Length);
        Assert.All(markers, static marker => Assert.Equal(ScenePlaybackTrack.Aura, marker.Track));
        Assert.Equal(AuraLifecycleEventKind.Open, markers[0].LifecycleEventKind);
        Assert.Equal(AuraLifecycleEventKind.Renew, markers[1].LifecycleEventKind);
        Assert.Equal(AuraLifecycleEventKind.Result, markers[2].LifecycleEventKind);
        Assert.Equal(7, markers[1].InstanceSequenceId);
        Assert.Equal(6, markers[2].ResultCode);
    }

    [Fact]
    public void AuraTimelineReader_ProjectsRefreshMarkersAndContinuousCoverageForSelectedTarget()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendAuraOpen(journal, sceneId, 200, 100, 7, 1_000, 1, 0, 16_300_020);
        AppendAuraOpen(journal, sceneId, 300, 100, 8, 2_000, 2, 200, 17_150_010);
        AppendAuraRenew(journal, sceneId, 200, 100, 7, 3, 800);
        AppendAuraResult(journal, sceneId, 200, 7, 6, 4, 1_500);
        AppendCombat(journal, sceneId, 100, 200, 1, 5, 2_000);
        var segment = CreateArchiveRecord(journal, sceneId).ScenePayload.TimelineSegment;

        var timeline = ScenePlaybackAuraTimelineReader.Read(segment, 200, 2_000, TestContext.Current.CancellationToken);

        var coverage = Assert.Single(timeline.Coverages);
        Assert.Equal(16_300_020u, coverage.DisplayResourceEffectRef.RawId);
        Assert.Equal(0, coverage.StartMilliseconds);
        Assert.Equal(1_500, coverage.EndMilliseconds);
        Assert.Equal(
            [AuraLifecycleEventKind.Open, AuraLifecycleEventKind.Renew],
            timeline.Applications.Select(static application => application.Kind));
        Assert.Equal([0L, 800L], timeline.Applications.Select(static application => application.PositionMilliseconds));
    }

    [Fact]
    public void AuraTimelineReader_PreservesGapWhenRefreshArrivesAfterExpiration()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendAuraOpen(journal, sceneId, 200, 100, 7, 500, 1, 0, 16_300_020);
        AppendAuraRenew(journal, sceneId, 200, 100, 7, 2, 800);
        AppendCombat(journal, sceneId, 100, 200, 1, 3, 1_500);
        var segment = CreateArchiveRecord(journal, sceneId).ScenePayload.TimelineSegment;

        var timeline = ScenePlaybackAuraTimelineReader.Read(segment, 200, 1_500, TestContext.Current.CancellationToken);

        Assert.Equal(2, timeline.Coverages.Count);
        Assert.Equal((0L, 500L), (timeline.Coverages[0].StartMilliseconds, timeline.Coverages[0].EndMilliseconds));
        Assert.Equal((800L, 1_300L), (timeline.Coverages[1].StartMilliseconds, timeline.Coverages[1].EndMilliseconds));
    }

    [Fact]
    public void AuraTimelineReader_BackfillsExpiredCoverageWhenRenewalResolvesResource()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendAuraOpen(journal, sceneId, 200, 100, 7, 500, 1, 0);
        AppendAuraRenew(journal, sceneId, 200, 100, 7, 2, 800, displayResourceEffectRefRaw: 16_300_020);
        AppendCombat(journal, sceneId, 100, 200, 1, 3, 1_500);
        var segment = CreateArchiveRecord(journal, sceneId).ScenePayload.TimelineSegment;

        var timeline = ScenePlaybackAuraTimelineReader.Read(segment, 200, 1_500, TestContext.Current.CancellationToken);

        Assert.Equal(2, timeline.Coverages.Count);
        Assert.All(timeline.Coverages, static coverage => Assert.Equal(16_300_020u, coverage.DisplayResourceEffectRef.RawId));
        Assert.All(timeline.Applications, static application => Assert.Equal(16_300_020u, application.DisplayResourceEffectRef.RawId));
    }

    [Fact]
    public void AuraTimelineReader_SplitsCoverageWhenRenewalChangesOrigin()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendAuraOpen(journal, sceneId, 200, 100, 7, 1_000, 1, 0, 16_300_020);
        AppendAuraRenew(journal, sceneId, 200, 101, 7, 2, 500);
        AppendAuraResult(journal, sceneId, 200, 7, 6, 3, 900);
        var segment = CreateArchiveRecord(journal, sceneId).ScenePayload.TimelineSegment;

        var timeline = ScenePlaybackAuraTimelineReader.Read(segment, 200, 1_000, TestContext.Current.CancellationToken);

        Assert.Equal(2, timeline.Coverages.Count);
        Assert.Equal((100, 0L, 500L), (timeline.Coverages[0].OriginEntityId, timeline.Coverages[0].StartMilliseconds, timeline.Coverages[0].EndMilliseconds));
        Assert.Equal((101, 500L, 900L), (timeline.Coverages[1].OriginEntityId, timeline.Coverages[1].StartMilliseconds, timeline.Coverages[1].EndMilliseconds));
    }

    [Fact]
    public void AuraPlayback_ExcludesGroup17ActionStateRecords()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendAuraOpen(journal, sceneId, 200, 100, 7, ushort.MaxValue, 1, 0, 3_629_313_792, groupCode: 17);
        AppendAuraOpen(journal, sceneId, 200, 100, 8, 1_000, 2, 100, 16_300_020);
        AppendAuraResult(journal, sceneId, 200, 7, 5, 3, 200);
        AppendCombat(journal, sceneId, 100, 200, 1, 4, 1_000);
        var record = CreateArchiveRecord(journal, sceneId);
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var frame = session.Seek(250);
        var timeline = ScenePlaybackAuraTimelineReader.Read(record.ScenePayload.TimelineSegment, 200, 1_000, TestContext.Current.CancellationToken);
        var segment = record.ScenePayload.TimelineSegment;
        var markers = ScenePlaybackTrackIndex.Build(segment, TestContext.Current.CancellationToken)
            .ReadWindow(0, 250, segment.CurrentEndObservationOrdinalExclusive, 10)
            .AsSpan()
            .ToArray();

        var active = Assert.Single(frame.ActiveAuras);
        Assert.Equal(8, active.InstanceSequenceId);
        Assert.Equal(16_300_020u, active.ResourceEffectRef.RawId);
        Assert.Equal([ScenePlaybackTrack.Action, ScenePlaybackTrack.Aura, ScenePlaybackTrack.Action], markers.Select(static marker => marker.Track));
        Assert.Equal(
            [AuraLifecycleEventKind.None, AuraLifecycleEventKind.Open, AuraLifecycleEventKind.None],
            markers.Select(static marker => marker.LifecycleEventKind));
        var coverage = Assert.Single(timeline.Coverages);
        Assert.Equal(16_300_020u, coverage.DisplayResourceEffectRef.RawId);
        Assert.Equal(8, coverage.InstanceSequenceId);
        Assert.Single(timeline.Applications);
    }

    [Fact]
    public void AuraPlayback_Group17ReopenReplacesTrackedSequence()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendAuraOpen(journal, sceneId, 200, 100, 7, 1_000, 1, 0, 16_300_020);
        AppendAuraOpen(journal, sceneId, 200, 100, 7, ushort.MaxValue, 2, 200, 3_629_313_792, groupCode: 17);
        AppendCombat(journal, sceneId, 100, 200, 1, 3, 1_000);
        var record = CreateArchiveRecord(journal, sceneId);
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var frame = session.Seek(200);
        var timeline = ScenePlaybackAuraTimelineReader.Read(record.ScenePayload.TimelineSegment, 200, 1_000, TestContext.Current.CancellationToken);

        Assert.Empty(frame.ActiveAuras);
        var coverage = Assert.Single(timeline.Coverages);
        Assert.Equal((0L, 200L), (coverage.StartMilliseconds, coverage.EndMilliseconds));
        Assert.Equal(16_300_020u, coverage.DisplayResourceEffectRef.RawId);
    }

    [Fact]
    public void AuraResultBatch_ClosesEverySequenceAndPublishesEveryMarker()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendAuraOpen(journal, sceneId, 200, 100, 7, 5_000, 1, 0);
        AppendAuraOpen(journal, sceneId, 200, 100, 8, 5_000, 2, 100);
        AppendAuraBatchResult(journal, sceneId, 200, 7, 7, 2, 0, 3, 1_000);
        AppendAuraBatchResult(journal, sceneId, 200, 8, 7, 2, 1, 4, 1_000);
        var record = CreateArchiveRecord(journal, sceneId);
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var frame = session.Seek(1_000);

        Assert.Empty(frame.ActiveAuras);
        var results = ReadAppliedMarkers(record, frame).Where(static marker => marker.LifecycleEventKind == AuraLifecycleEventKind.Result).ToArray();
        Assert.Equal(2, results.Length);
        Assert.Equal([7, 8], results.Select(static marker => marker.InstanceSequenceId));
    }

    [Fact]
    public void AuraLifecycleTrack_OnlyClassifiesMarkerBoundActionsAsRenewals()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendAuraOpen(journal, sceneId, 200, 100, 7, 1_000, 1, 0);
        AppendAuraRenew(journal, sceneId, 200, 100, 8, 2, 250);
        AppendAuraRenew(journal, sceneId, 200, 100, 7, 3, 500);
        AppendAuraResult(journal, sceneId, 200, 7, 1, 4, 1_500);
        var segment = CreateArchiveRecord(journal, sceneId).ScenePayload.TimelineSegment;

        var markers = ScenePlaybackTrackIndex.Build(segment, TestContext.Current.CancellationToken)
            .ReadWindow(0, 1_500, segment.CurrentEndObservationOrdinalExclusive, 10)
            .AsSpan()
            .ToArray();

        Assert.Equal(
            [ScenePlaybackTrack.Aura, ScenePlaybackTrack.Action, ScenePlaybackTrack.Aura, ScenePlaybackTrack.Aura],
            markers.Select(static marker => marker.Track));
        Assert.Equal(
            [AuraLifecycleEventKind.Open, AuraLifecycleEventKind.None, AuraLifecycleEventKind.Renew, AuraLifecycleEventKind.Result],
            markers.Select(static marker => marker.LifecycleEventKind));
    }

    [Fact]
    public void PlaybackSession_OnlyRenewsMatchingAuraInstances()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendAuraOpen(journal, sceneId, 200, 100, 7, 1_000, 1, 0);
        AppendAuraRenew(journal, sceneId, 200, 100, 8, 2, 250);
        AppendAuraRenew(journal, sceneId, 200, 100, 7, 3, 500);
        var record = CreateArchiveRecord(journal, sceneId);
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var frame = session.Seek(500);

        var aura = Assert.Single(frame.ActiveAuras);
        Assert.Equal(1_500, aura.ExpiresAtMilliseconds);
        Assert.Equal([ScenePlaybackTrack.Aura, ScenePlaybackTrack.Action, ScenePlaybackTrack.Aura], ReadAppliedMarkers(record, frame).Select(static marker => marker.Track));
    }

    [Fact]
    public void PlaybackSession_OnlyRenewsPhase19LifecycleSidecars()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendAuraOpen(journal, sceneId, 200, 100, 7, 1_000, 1, 0);
        AppendAuraRenew(journal, sceneId, 200, 100, 7, 2, 500, phase: 17);
        var record = CreateArchiveRecord(journal, sceneId);
        var session = new ScenePlaybackSession(new ArchivedScenePlaybackSource(record));

        var frame = session.Seek(500);

        var aura = Assert.Single(frame.ActiveAuras);
        Assert.Equal(1_000, aura.ExpiresAtMilliseconds);
        Assert.Equal([ScenePlaybackTrack.Aura, ScenePlaybackTrack.Action], ReadAppliedMarkers(record, frame).Select(static marker => marker.Track));
    }

    [Fact]
    public async Task Controller_InitialState_UsesPausedTimelineDuration()
    {
        await using var controller = CreateController(CreateArchiveRecord());

        Assert.False(controller.IsPlaying);
        Assert.False(controller.IsLoading);
        Assert.Equal(0, controller.PositionMilliseconds);
        Assert.Equal(2_500, controller.DurationMilliseconds);
        Assert.Equal(1d, controller.Speed);
        Assert.Equal(ScenePlaybackSourceKind.Archived, controller.State.SourceKind);
        Assert.Equal(0, controller.CurrentFrame.PositionMilliseconds);
    }

    [Fact]
    public async Task Controller_SetSpeed_RejectsInvalidValues()
    {
        await using var controller = CreateController(CreateArchiveRecord());

        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetSpeed(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetSpeed(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetSpeed(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetSpeed(double.PositiveInfinity));

        controller.SetSpeed(2.5);

        Assert.Equal(2.5, controller.Speed);
    }

    [Fact]
    public void Controller_OptionsRejectInvalidCheckpointInterval()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScenePlaybackController(
            new ArchivedScenePlaybackSource(CreateArchiveRecord()),
            new ManualTickSourceFactory(),
            new ScenePlaybackControllerOptions(TimeSpan.FromMilliseconds(33), 0, RebuildCheckpointsOnCreate: false)));
    }

    [Fact]
    public async Task Controller_StopAsync_SeeksToStartAndPauses()
    {
        await using var controller = CreateController(CreateArchiveRecord());
        await controller.SeekAsync(1_000, TestContext.Current.CancellationToken);
        controller.Play();

        var frame = await controller.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(controller.IsPlaying);
        Assert.Equal(0, controller.PositionMilliseconds);
        Assert.Equal(0, frame.PositionMilliseconds);
    }

    [Fact]
    public async Task Controller_StepEventAsync_MovesExactlyOneJournalEventAcrossEqualTimestamps()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendState(journal, sceneId, 100, StateCodes.PlayerIdentity, 0, 0, "Tester", 1, 500);
        AppendCombat(journal, sceneId, 100, 200, 100, 2, 1_000);
        AppendCombat(journal, sceneId, 100, 200, 200, 3, 1_000);
        AppendEntityVital(journal, sceneId, 200, 30_000, 50_000, 4, 2_000);
        var owner = new SceneReadModelOwner(journal, sceneId, DateTimeOffset.Now);
        var snapshot = owner.CreateSnapshot();
        var record = new ArchivedEncounterRecord
        {
            EncounterId = sceneId,
            Snapshot = snapshot,
            ScenePayload = owner.CreateArchivePayload(snapshot)
        };
        await using var controller = CreateController(record);

        Assert.Equal(0, controller.CurrentFrame.AppliedSegment.EndObservationOrdinalExclusive);

        var first = await controller.StepEventAsync(1, TestContext.Current.CancellationToken);
        var second = await controller.StepEventAsync(1, TestContext.Current.CancellationToken);
        var third = await controller.StepEventAsync(1, TestContext.Current.CancellationToken);
        var previous = await controller.StepEventAsync(-1, TestContext.Current.CancellationToken);
        var firstMarkers = ReadAppliedMarkers(record, first);
        var secondMarkers = ReadAppliedMarkers(record, second);
        var thirdMarkers = ReadAppliedMarkers(record, third);
        var previousMarkers = ReadAppliedMarkers(record, previous);

        Assert.Equal(1, first.AppliedSegment.EndObservationOrdinalExclusive);
        Assert.Equal(0, firstMarkers[^1].ObservationOrdinal);
        Assert.Equal(500, first.PositionMilliseconds);
        Assert.Equal(2, second.AppliedSegment.EndObservationOrdinalExclusive);
        Assert.Equal(1, secondMarkers[^1].ObservationOrdinal);
        Assert.Equal(100, Assert.Single(secondMarkers, static marker => marker.Track == ScenePlaybackTrack.Combat && marker.ObservationOrdinal == 1).Amount);
        Assert.Equal(1_000, second.PositionMilliseconds);
        Assert.Equal(3, third.AppliedSegment.EndObservationOrdinalExclusive);
        Assert.Equal(2, thirdMarkers[^1].ObservationOrdinal);
        Assert.Equal(200, Assert.Single(thirdMarkers, static marker => marker.Track == ScenePlaybackTrack.Combat && marker.ObservationOrdinal == 2).Amount);
        Assert.Equal(1_000, third.PositionMilliseconds);
        Assert.Equal(2, previous.AppliedSegment.EndObservationOrdinalExclusive);
        Assert.Equal(1, previousMarkers[^1].ObservationOrdinal);
        Assert.Equal(100, Assert.Single(previousMarkers, static marker => marker.Track == ScenePlaybackTrack.Combat && marker.ObservationOrdinal == 1).Amount);
        Assert.Equal(1_000, previous.PositionMilliseconds);
    }

    [Fact]
    public async Task Controller_StepEventAsync_RejectsNonUnitDirection()
    {
        await using var controller = CreateController(CreateArchiveRecord());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await controller.StepEventAsync(0, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await controller.StepEventAsync(2, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Controller_Play_AdvancesAndPausesAtArchivedEnd()
    {
        var tickFactory = new ManualTickSourceFactory();
        await using var controller = CreateController(CreateArchiveRecord(), tickFactory);
        var frames = new ConcurrentQueue<ScenePlaybackFrame>();
        controller.FrameChanged += (_, e) => frames.Enqueue(e.Frame);

        controller.Play();
        tickFactory.Source.Tick(TimeSpan.FromMilliseconds(500));
        await WaitUntil(() => controller.PositionMilliseconds == 500);
        tickFactory.Source.Tick(TimeSpan.FromMilliseconds(5_000));
        await WaitUntil(() => !controller.IsPlaying && controller.PositionMilliseconds == controller.DurationMilliseconds);
        await WaitUntil(() => frames.Count >= 2 && frames.Last().PositionMilliseconds == 2_500);

        Assert.Equal(2_500, controller.PositionMilliseconds);
        Assert.Equal(2_500, controller.DurationMilliseconds);
        Assert.True(frames.Count >= 2);
        Assert.Equal(2_500, frames.Last().PositionMilliseconds);
    }

    [Fact]
    public async Task Controller_Play_DoesNotCreateTickCheckpoints()
    {
        var tickFactory = new ManualTickSourceFactory();
        await using var controller = CreateController(CreateArchiveRecord(), tickFactory);

        controller.Play();
        tickFactory.Source.Tick(TimeSpan.FromMilliseconds(500));
        await WaitUntil(() => controller.PositionMilliseconds == 500);
        tickFactory.Source.Tick(TimeSpan.FromMilliseconds(500));
        await WaitUntil(() => controller.PositionMilliseconds == 1_000);

        Assert.Equal(1, controller.CheckpointCount);
    }

    [Fact]
    public async Task Controller_CreateCombatantDetailAsync_ProjectsCurrentFrame()
    {
        await using var controller = CreateController(CreateArchiveRecord());

        await controller.SeekAsync(1_500, TestContext.Current.CancellationToken);
        var projection = await controller.CreateCombatantDetailAsync(100, TestContext.Current.CancellationToken);

        Assert.Equal(controller.PositionMilliseconds, projection.PositionMilliseconds);
        Assert.Equal(controller.CurrentFrame.AppliedSegment.EndObservationOrdinalExclusive, projection.EndObservationOrdinalExclusive);
        Assert.Equal(300, projection.Events.MetricEvents.Sum(static entry => entry.Amount));

        await controller.SeekAsync(2_500, TestContext.Current.CancellationToken);
        var completed = await controller.CreateCombatantDetailAsync(100, TestContext.Current.CancellationToken);

        Assert.True(completed.Update.IsFullSnapshot);
        Assert.Equal(600, completed.Events.MetricEvents.Sum(static entry => entry.Amount));
    }

    [Fact]
    public async Task Controller_CreateCombatantDetailAsync_RebuildsCurrentFrameAfterCheckpointIndexing()
    {
        var record = CreateArchiveRecordWithLongCombatTimeline();
        await using var controller = new ScenePlaybackController(
            new ArchivedScenePlaybackSource(record),
            new ManualTickSourceFactory(),
            new ScenePlaybackControllerOptions(TimeSpan.FromMilliseconds(33), 1_000, RebuildCheckpointsOnCreate: false));
        await controller.RebuildCheckpointsAsync(TestContext.Current.CancellationToken);

        var frame = await controller.SeekAsync(3_500, TestContext.Current.CancellationToken);
        var projection = await controller.CreateCombatantDetailAsync(100, TestContext.Current.CancellationToken);

        Assert.True(frame.Snapshot.Combatants.TryGetValue(100, out var metrics));
        Assert.Equal(1_000, metrics.DamageAmount);
        Assert.Equal(metrics.DamageAmount, projection.Events.MetricEvents.Where(static entry => entry.SourceId == 100).Sum(static entry => entry.Amount));
        Assert.Equal(frame.AppliedSegment.EndObservationOrdinalExclusive, projection.EndObservationOrdinalExclusive);
    }

    [Fact]
    public async Task Controller_Play_MapsElapsedWallTimeThroughSpeed()
    {
        var tickFactory = new ManualTickSourceFactory();
        await using var controller = CreateController(CreateArchiveRecord(), tickFactory);

        controller.SetSpeed(2d);
        controller.Play();
        tickFactory.Source.Tick(TimeSpan.FromMilliseconds(250));
        await WaitUntil(() => controller.PositionMilliseconds == 500);
        tickFactory.Source.Tick(TimeSpan.FromMilliseconds(250));
        await WaitUntil(() => controller.PositionMilliseconds == 1_000);

        Assert.Equal(1_000, controller.PositionMilliseconds);
    }

    [Fact]
    public async Task Controller_SetSpeed_ReanchorsPlaybackClockAtCurrentPosition()
    {
        var tickFactory = new ManualTickSourceFactory();
        await using var controller = CreateController(CreateArchiveRecord(), tickFactory);

        controller.Play();
        tickFactory.Source.Tick(TimeSpan.FromMilliseconds(500));
        await WaitUntil(() => controller.PositionMilliseconds == 500);

        controller.SetSpeed(2d);
        tickFactory.Source.Tick(TimeSpan.FromMilliseconds(250));
        await WaitUntil(() => controller.PositionMilliseconds == 1_000);

        Assert.Equal(1_000, controller.PositionMilliseconds);
    }

    [Fact]
    public async Task Controller_Refresh_KeepsArchivedDurationFixedAfterJournalAppend()
    {
        var record = CreateArchiveRecord();
        await using var controller = CreateController(record);
        AppendCombat(record.ScenePayload.TimelineSegment.Journal!, record.EncounterId, 100, 200, 400, 6, 4_000);

        var frame = await controller.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2_500, controller.DurationMilliseconds);
        Assert.Equal(2_500, frame.TimeRange.DurationMilliseconds);
    }

    [Fact]
    public async Task Controller_Refresh_GrowsLiveDurationAfterAppend()
    {
        var scene = new SceneLiveReadModel();
        AppendCombat(scene.Journal, scene.SessionId, 100, 200, 100, 1, 1_000);
        AppendCombat(scene.Journal, scene.SessionId, 100, 200, 200, 2, 2_000);
        await using var controller = new ScenePlaybackController(new LiveScenePlaybackSource(scene), new ManualTickSourceFactory(), TimeSpan.FromMilliseconds(33));

        AppendCombat(scene.Journal, scene.SessionId, 100, 200, 300, 3, 4_000);
        var frame = await controller.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ScenePlaybackSourceKind.Live, controller.State.SourceKind);
        Assert.Equal(4_000, controller.DurationMilliseconds);
        Assert.Equal(4_000, frame.TimeRange.DurationMilliseconds);
    }

    [Fact]
    public async Task Controller_DefaultOptionsPrebuildArchivedCheckpoints()
    {
        await using var controller = new ScenePlaybackController(new ArchivedScenePlaybackSource(CreateArchiveRecord()));

        await WaitUntil(() => !controller.IsCheckpointing && controller.CheckpointCount == 2);

        Assert.False(controller.IsCheckpointing);
        Assert.Equal(2, controller.CheckpointCount);
        Assert.Equal(5_000, ScenePlaybackControllerOptions.Default.CheckpointIntervalMilliseconds);
    }

    [Fact]
    public async Task Controller_DefaultOptionsDoNotPrebuildLiveCheckpoints()
    {
        var scene = new SceneLiveReadModel();
        AppendCombat(scene.Journal, scene.SessionId, 100, 200, 100, 1, 1_000);
        AppendCombat(scene.Journal, scene.SessionId, 100, 200, 200, 2, 2_000);
        await using var controller = new ScenePlaybackController(new LiveScenePlaybackSource(scene));

        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.False(controller.IsCheckpointing);
        Assert.Equal(1, controller.CheckpointCount);
    }

    [Fact]
    public async Task Controller_RebuildCheckpoints_BuildsRuntimeCache()
    {
        await using var controller = new ScenePlaybackController(
            new ArchivedScenePlaybackSource(CreateArchiveRecord()),
            new ManualTickSourceFactory(),
            new ScenePlaybackControllerOptions(TimeSpan.FromMilliseconds(33), 1_000, RebuildCheckpointsOnCreate: false));

        await controller.RebuildCheckpointsAsync(TestContext.Current.CancellationToken);

        var checkpoints = controller.GetCheckpoints();
        Assert.False(controller.IsCheckpointing);
        Assert.Equal(4, checkpoints.Length);
        Assert.Equal([0L, 1_000L, 2_000L, 2_500L], checkpoints.Select(static checkpoint => checkpoint.PositionMilliseconds));
        Assert.Equal(4, controller.State.CheckpointCount);
        Assert.All(checkpoints, static checkpoint => Assert.True(checkpoint.JournalCursor.NextObservationOrdinal >= 0));
    }

    [Fact]
    public async Task Controller_SeekAfterCheckpointIndexing_ReplaysLiveJournalEntries()
    {
        var scene = new SceneLiveReadModel();
        AppendCombat(scene.Journal, scene.SessionId, 100, 200, 100, 1, 1_000);
        AppendCombat(scene.Journal, scene.SessionId, 100, 200, 200, 2, 2_000);
        await using var controller = new ScenePlaybackController(
            new LiveScenePlaybackSource(scene),
            new ManualTickSourceFactory(),
            new ScenePlaybackControllerOptions(TimeSpan.FromMilliseconds(33), 1_000, RebuildCheckpointsOnCreate: false));

        await controller.RebuildCheckpointsAsync(TestContext.Current.CancellationToken);
        var checkpoint = Assert.Single(controller.GetCheckpoints(), static checkpoint => checkpoint.PositionMilliseconds == 1_000);
        Assert.Equal(1, checkpoint.JournalCursor.NextObservationOrdinal);

        AppendCombat(scene.Journal, scene.SessionId, 100, 200, 300, 3, 3_000);

        var frame = await controller.SeekAsync(3_000, TestContext.Current.CancellationToken);

        Assert.Equal(600, frame.CombatTotals.TotalDamage);
        Assert.Equal(3, frame.AppliedSegment.EndObservationOrdinalExclusive);
    }

    [Fact]
    public async Task Controller_CreateTimelineSegment_DoesNotSkipMarkersAtCheckpointBoundary()
    {
        await using var controller = new ScenePlaybackController(
            new ArchivedScenePlaybackSource(CreateArchiveRecord()),
            new ManualTickSourceFactory(),
            new ScenePlaybackControllerOptions(TimeSpan.FromMilliseconds(33), 1_000, RebuildCheckpointsOnCreate: false));

        await controller.RebuildCheckpointsAsync(TestContext.Current.CancellationToken);

        var segment = controller.CreateTimelineSegment(1_000, 1_000);
        var markers = ScenePlaybackTrackIndex.Build(segment, TestContext.Current.CancellationToken)
            .ReadWindow(1_000, 1_000, segment.CurrentEndObservationOrdinalExclusive, 10)
            .AsSpan()
            .ToArray();

        Assert.NotEmpty(markers);
        Assert.All(markers, static marker => Assert.Equal(1, marker.ObservationOrdinal));
        Assert.Contains(markers, static marker => marker.Track == ScenePlaybackTrack.Combat);
    }

    [Fact]
    public async Task Controller_StartCheckpointRebuild_RunsInBackground()
    {
        await using var controller = new ScenePlaybackController(
            new ArchivedScenePlaybackSource(CreateArchiveRecord()),
            new ManualTickSourceFactory(),
            new ScenePlaybackControllerOptions(TimeSpan.FromMilliseconds(33), 1_000, RebuildCheckpointsOnCreate: false));

        controller.StartCheckpointRebuild();
        await WaitUntil(() => !controller.IsCheckpointing && controller.CheckpointCount == 4);

        Assert.Equal([0L, 1_000L, 2_000L, 2_500L], controller.GetCheckpoints().Select(static checkpoint => checkpoint.PositionMilliseconds));
    }

    [Fact]
    public async Task Controller_SeekAsync_FinalQuickSeekWins()
    {
        await using var controller = CreateController(CreateArchiveRecord());
        var frames = new ConcurrentQueue<ScenePlaybackFrame>();
        controller.FrameChanged += (_, e) => frames.Enqueue(e.Frame);
        var cancellationToken = TestContext.Current.CancellationToken;

        var first = controller.SeekAsync(500, cancellationToken).AsTask();
        var second = controller.SeekAsync(1_000, cancellationToken).AsTask();

        _ = await first;
        var frame = await second;
        var published = frames.ToArray();

        Assert.Equal(1_000, frame.PositionMilliseconds);
        Assert.Equal(1_000, controller.PositionMilliseconds);
        Assert.NotEmpty(published);
        Assert.Equal(1_000, published[^1].PositionMilliseconds);
    }

    [Fact]
    public async Task Controller_ManualSeekWinsOverPlaybackTick()
    {
        var tickFactory = new ManualTickSourceFactory();
        await using var controller = CreateController(CreateArchiveRecord(), tickFactory);

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
        AppendEntityVital(journal, sceneId, bossId, 30_000, 50_000, 4, 1_500);
        AppendCombat(journal, sceneId, playerId, bossId, 300, 5, 2_500);
        journal.CompleteFlush(1);
        return CreateArchiveRecord(journal, sceneId);
    }

    private static ArchivedEncounterRecord CreateArchiveRecordWithLongCombatTimeline()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendCombat(journal, sceneId, 100, 200, 100, 1, 500);
        AppendCombat(journal, sceneId, 100, 200, 200, 2, 1_500);
        AppendCombat(journal, sceneId, 100, 200, 300, 3, 2_500);
        AppendCombat(journal, sceneId, 100, 200, 400, 4, 3_500);
        journal.CompleteFlush(1);
        return CreateArchiveRecord(journal, sceneId);
    }

    private static ArchivedEncounterRecord CreateArchiveRecord(ObservedEventJournal journal, Guid sceneId)
    {
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

    private static ScenePlaybackTrackMarker[] ReadAppliedMarkers(ArchivedEncounterRecord record, ScenePlaybackFrame frame)
    {
        var index = ScenePlaybackTrackIndex.Build(record.ScenePayload.TimelineSegment, TestContext.Current.CancellationToken);
        return index.ReadWindow(
            0,
            frame.PositionMilliseconds,
            frame.AppliedSegment.EndObservationOrdinalExclusive,
            int.MaxValue).AsSpan().ToArray();
    }

    private static void AppendState(ObservedEventJournal journal, Guid sceneId, int entityId, int stateCode, int value0, int value1, string? text, long ordinal, long observedAt)
    {
        var header = CreateHeader(sceneId, entityId, 0, ordinal, observedAt, new RawPacketReference(0, 0, ordinal));
        var observation = new StateObservation(entityId, stateCode, value0, value1, 0, text);
        journal.Append(in header, in observation);
    }

    private static void AppendEntityVital(ObservedEventJournal journal, Guid sceneId, int entityId, long currentHp, long? maxHp, long ordinal, long observedAt)
    {
        var header = CreateHeader(sceneId, entityId, 0, ordinal, observedAt, new RawPacketReference(0, 0, ordinal));
        var observation = new EntityVitalObservation(entityId, currentHp, maxHp);
        journal.Append(in header, in observation);
    }

    private static void AppendCombat(ObservedEventJournal journal, Guid sceneId, int sourceId, int targetId, int damage, long ordinal, long observedAt, int skillCode = 11000010)
    {
        var observation = new CombatWireObservation
        {
            SkillCode = skillCode,
            Damage = damage,
            HitCount = 1,
            AttemptCount = 1
        };
        AppendCombatObservation(journal, sceneId, sourceId, targetId, in observation, ordinal, observedAt);
    }

    private static void AppendCombatObservation(
        ObservedEventJournal journal,
        Guid sceneId,
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        long ordinal,
        long observedAt,
        ushort opcode = 0x0438)
    {
        var header = CreateHeader(sceneId, sourceId, targetId, ordinal, observedAt, new RawPacketReference(opcode, 0, ordinal));
        journal.Append(in header, in observation);
    }

    private static ScenePlaybackEventMarker[] ReadMaterializedEvents(
        ScenePlaybackSession session,
        ScenePlaybackEventScope scope,
        long startPositionMilliseconds,
        long endPositionMilliseconds)
    {
        var buffer = new ScenePlaybackEventMarker[32];
        var read = session.CopyLatestMaterializedEvents(scope, startPositionMilliseconds, endPositionMilliseconds, buffer);
        return buffer.AsSpan(0, read.Count).ToArray();
    }

    private static void AppendCompactCombatValue(
        ObservedEventJournal journal,
        Guid sceneId,
        int sourceId,
        int targetId,
        int skillCode,
        int bodySkillVariantRaw,
        int marker,
        int damage,
        long ordinal,
        long observedAt,
        int scopeId,
        int siblingIndex,
        int parentScopeId)
    {
        var header = CreateHeader(
            sceneId,
            sourceId,
            targetId,
            ordinal,
            observedAt,
            CreateStructuredRaw(0x0438, ordinal, scopeId, siblingIndex, parentScopeId));
        var observation = new CombatWireObservation
        {
            SkillCode = skillCode,
            BodySkillVariantRaw = bodySkillVariantRaw,
            Damage = damage,
            HitCount = 1,
            AttemptCount = 1,
            Marker = marker,
            Type = 2,
            LayoutTag = 4,
            Loop = 1,
            ChainId = 9001
        };
        journal.Append(in header, in observation);
    }

    private static void AppendCompactCombatOpener(
        ObservedEventJournal journal,
        Guid sceneId,
        int sourceId,
        int skillCode,
        int marker,
        long ordinal,
        long observedAt,
        int scopeId,
        int siblingIndex,
        int parentScopeId)
    {
        var header = CreateHeader(
            sceneId,
            sourceId,
            0,
            ordinal,
            observedAt,
            CreateStructuredRaw(0x0238, ordinal, scopeId, siblingIndex, parentScopeId));
        var observation = new CombatWireObservation
        {
            SkillCode = skillCode,
            BodyCodeRaw = unchecked((uint)skillCode),
            Marker = marker,
            Type = 2
        };
        journal.Append(in header, in observation);
    }

    private static RawPacketReference CreateStructuredRaw(ushort opcode, long ordinal, int scopeId, int siblingIndex, int parentScopeId)
    {
        if (parentScopeId <= 0)
            return new RawPacketReference(opcode, 0, ordinal, PacketStructurePath.FromLeaf(new PacketStructureReference(PacketStructureKind.FrameBatchEntry, scopeId, 0, 1, siblingIndex, 0, 0, 0, 0)));

        var root = new PacketStructureReference(PacketStructureKind.TransportPacket, parentScopeId, 0, 1, 0, 0, 0, 0, 0);
        var leaf = new PacketStructureReference(PacketStructureKind.FrameBatchEntry, scopeId, parentScopeId, 2, siblingIndex, 0, 0, 0, 0);
        return new RawPacketReference(opcode, 0, ordinal, default(PacketStructurePath).Push(root).Push(leaf));
    }

    private static void AppendAuraOpen(ObservedEventJournal journal, Guid sceneId, int entityId, int originEntityId, int sequenceId, ushort durationMilliseconds, long ordinal, long observedAt, uint displayResourceEffectRefRaw = 0, int groupCode = 19)
    {
        var header = CreateHeader(sceneId, entityId, 0, ordinal, observedAt, new RawPacketReference(0x2A38, 0, ordinal));
        var observation = new AuraObservation
        {
            Kind = AuraObservationKind.Open,
            EntityId = entityId,
            EchoSourceEntityId = originEntityId,
            InstanceSequenceId = sequenceId,
            OpenMode = 1,
            GroupCode = groupCode,
            HeadValue = durationMilliseconds,
            StackCount = 1,
            BuffResourceEffectRef = ResourceEffectRef.FromRaw(displayResourceEffectRefRaw)
        };
        journal.Append(in header, in observation);
    }

    private static void AppendAuraRenew(ObservedEventJournal journal, Guid sceneId, int entityId, int originEntityId, int sequenceId, long ordinal, long observedAt, int phase = 19, uint displayResourceEffectRefRaw = 0)
    {
        var header = CreateHeader(sceneId, entityId, 0, ordinal, observedAt, new RawPacketReference(0x2B38, 0, ordinal));
        var observation = new ActionObservation
        {
            SourceEntityId = entityId,
            SourceEntityIdCopy = originEntityId,
            Phase = phase,
            InstanceSequenceId = sequenceId,
            ActionResourceEffectRef = ResourceEffectRef.FromRaw(displayResourceEffectRefRaw)
        };
        journal.Append(in header, in observation);
    }

    private static void AppendAuraResult(ObservedEventJournal journal, Guid sceneId, int entityId, int sequenceId, int resultCode, long ordinal, long observedAt)
        => AppendAuraBatchResult(journal, sceneId, entityId, sequenceId, resultCode, 1, 0, ordinal, observedAt);

    private static void AppendAuraBatchResult(ObservedEventJournal journal, Guid sceneId, int entityId, int sequenceId, int resultCode, int resultCount, int resultIndex, long ordinal, long observedAt)
    {
        var header = CreateHeader(sceneId, 0, entityId, ordinal, observedAt, new RawPacketReference(0x2C38, 0, ordinal));
        var observation = new AuraObservation
        {
            Kind = AuraObservationKind.Result,
            EntityId = entityId,
            InstanceSequenceId = sequenceId,
            ResultCount = resultCount,
            ResultIndex = resultIndex,
            ResultCode = resultCode
        };
        journal.Append(in header, in observation);
    }

    private static ObservedEventHeader CreateHeader(Guid sceneId, int sourceId, int targetId, long ordinal, long observedAt, RawPacketReference raw)
        => new(
            sceneId,
            new TimelineStamp { OffsetTicks = observedAt * TimeSpan.TicksPerMillisecond, ObservationOrdinal = ordinal - 1, FlushId = 1 },
            sourceId,
            targetId,
            raw);

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
