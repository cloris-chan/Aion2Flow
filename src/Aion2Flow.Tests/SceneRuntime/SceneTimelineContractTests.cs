using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Runtime;
using ParsedCombatPacket = Cloris.Aion2Flow.SceneRuntime.Combat.ParsedCombatPacket;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public class SceneTimelineContractTests
{
    [Fact]
    public void TimelineStamp_ObservationOrdinal_IsPrimaryOrderingKey()
    {
        var earlier = new TimelineStamp(OffsetTicks: 1000, ObservationOrdinal: 1, FrameOrdinal: 10, BatchOrdinal: 5);
        var later = new TimelineStamp(OffsetTicks: 500, ObservationOrdinal: 2, FrameOrdinal: 10, BatchOrdinal: 5);

        Assert.True(earlier.ObservationOrdinal < later.ObservationOrdinal);
    }

    [Fact]
    public void TimelineStamp_Equality_IsStructural()
    {
        var a = new TimelineStamp(OffsetTicks: 100, ObservationOrdinal: 5, FrameOrdinal: 3, BatchOrdinal: 1);
        var b = new TimelineStamp(OffsetTicks: 100, ObservationOrdinal: 5, FrameOrdinal: 3, BatchOrdinal: 1);
        var c = new TimelineStamp(OffsetTicks: 100, ObservationOrdinal: 6, FrameOrdinal: 3, BatchOrdinal: 1);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void TimelineStamp_PreservesFrameAndBatchOrdinals()
    {
        var stamp = new TimelineStamp(OffsetTicks: 5000, ObservationOrdinal: 42, FrameOrdinal: 99, BatchOrdinal: 7);

        Assert.Equal(5000, stamp.OffsetTicks);
        Assert.Equal(42, stamp.ObservationOrdinal);
        Assert.Equal(99, stamp.FrameOrdinal);
        Assert.Equal(7, stamp.BatchOrdinal);
    }

    [Fact]
    public void Revision_DefaultIsZero()
    {
        var rev = default(Revision);

        Assert.Equal(0, rev.Global);
        Assert.Equal(0, rev.Journal);
        Assert.Equal(0, rev.Combat);
        Assert.Equal(0, rev.Entity);
        Assert.Equal(0, rev.Archive);
        Assert.Equal(0, rev.State);
    }

    [Fact]
    public void Revision_WithOperator_UpdatesSingleField()
    {
        var rev = new Revision(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        var updated = rev with { Combat = 99 };

        Assert.Equal(99, updated.Combat);
        Assert.Equal(1, updated.Global);
        Assert.Equal(2, updated.Journal);
    }

    [Fact]
    public void ObservedEventDomain_HasExpectedValues()
    {
        var domains = Enum.GetValues<ObservedEventDomain>();
        Assert.Contains(ObservedEventDomain.Combat, domains);
        Assert.Contains(ObservedEventDomain.Action, domains);
        Assert.Contains(ObservedEventDomain.State, domains);
        Assert.Contains(ObservedEventDomain.Resource, domains);
        Assert.Contains(ObservedEventDomain.Aura, domains);
        Assert.Contains(ObservedEventDomain.Scene, domains);
        Assert.Contains(ObservedEventDomain.Diagnostic, domains);
        Assert.Equal(7, domains.Length);
    }

    [Fact]
    public void ObservedEventEnvelope_CombatOnlyPopulatesCombatField()
    {
        var envelope = new ObservedEventEnvelope(
            SceneSessionId: Guid.NewGuid(),
            Stamp: new TimelineStamp(100, 0, 1, 1),
            Domain: ObservedEventDomain.Combat,
            SourceEntityId: 100,
            TargetEntityId: 200,
            Raw: new RawPacketReference(0x0438, 32, 1, 1000),
            Combat: new CombatObservation { SkillCode = 1234, Damage = 500, HitCount = 1, AttemptCount = 1, DetailRaw = 0 });

        Assert.Equal(ObservedEventDomain.Combat, envelope.Domain);
        Assert.NotNull(envelope.Combat);
        Assert.Null(envelope.State);
        Assert.Null(envelope.Scene);
        Assert.Null(envelope.Resource);
        Assert.Null(envelope.Aura);
        Assert.Equal(1234, envelope.Combat!.Value.SkillCode);
    }

    [Fact]
    public void ObservedEventEnvelope_SceneOnlyPopulatesSceneField()
    {
        var envelope = new ObservedEventEnvelope(
            SceneSessionId: Guid.NewGuid(),
            Stamp: new TimelineStamp(0, 0, 0, 0),
            Domain: ObservedEventDomain.Scene,
            SourceEntityId: 0,
            TargetEntityId: 0,
            Raw: default,
            Scene: new SceneObservation(MapId: 910035, MapInstanceId: 0, Value0: 0, Value1: 0, DiagnosticKey: "test"));

        Assert.Equal(ObservedEventDomain.Scene, envelope.Domain);
        Assert.NotNull(envelope.Scene);
        Assert.Equal(910035u, envelope.Scene!.Value.MapId);
        Assert.Null(envelope.Combat);
        Assert.Null(envelope.State);
    }

    [Fact]
    public void ObservedEventEnvelope_DefaultPayloadsAreNull()
    {
        var envelope = new ObservedEventEnvelope(
            SceneSessionId: Guid.NewGuid(),
            Stamp: new TimelineStamp(0, 0, 0, 0),
            Domain: ObservedEventDomain.Diagnostic,
            SourceEntityId: 0,
            TargetEntityId: 0,
            Raw: default);

        Assert.Null(envelope.Combat);
        Assert.Null(envelope.State);
        Assert.Null(envelope.Scene);
        Assert.Null(envelope.Resource);
        Assert.Null(envelope.Aura);
    }

    [Fact]
    public void RawPacketReference_PreservesAuditFields()
    {
        var raw = new RawPacketReference(Opcode: 0x0438, PayloadLength: 64, CaptureSequence: 42, TimestampMilliseconds: 1234567890);

        Assert.Equal(0x0438, raw.Opcode);
        Assert.Equal(64, raw.PayloadLength);
        Assert.Equal(42, raw.CaptureSequence);
        Assert.Equal(1234567890, raw.TimestampMilliseconds);
    }

    [Fact]
    public void SceneSession_HasStableIdentity()
    {
        var id = Guid.NewGuid();
        var session = new SceneSession
        {
            SceneSessionId = id,
            Started = DateTimeOffset.UtcNow,
            MapId = 910035,
            MapInstanceId = 515552,
            StartOrdinal = 0
        };

        Assert.Equal(id, session.SceneSessionId);
        Assert.Equal(910035, session.MapId);
        Assert.Equal(515552, session.MapInstanceId);
    }

    [Fact]
    public void SceneSession_ToTimeSpan_ConvertsStandardTicks()
    {
        var ts = TimeSpan.FromTicks(20000);
        Assert.Equal(TimeSpan.FromMilliseconds(2), ts);
        Assert.Equal(TimeSpan.FromTicks(20000), ts);
    }

    [Fact]
    public void SceneSession_ToDisplayTime_AddsOffsetToStartTime()
    {
        var start = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero);
        var session = new SceneSession
        {
            Started = start
        };

        var display = session.ToDisplayTime(3_000_000);
        Assert.Equal(start.AddMilliseconds(300), display);
    }

    [Fact]
    public void SceneSession_Revision_DefaultIsZero()
    {
        var session = new SceneSession { SceneSessionId = Guid.NewGuid() };
        Assert.Equal(default, session.Revision);
    }

    [Fact]
    public void SceneSession_Journal_IsNotNull()
    {
        var session = new SceneSession { SceneSessionId = Guid.NewGuid() };
        Assert.NotNull(session.Journal);
        Assert.Equal(0, session.Journal.Count);
    }

    [Fact]
    public void Journal_Append_AssignsMonotonicallyIncreasingOrdinals()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        for (int i = 0; i < 10; i++)
        {
            var stamp = new TimelineStamp(i * 100, i, i, 0);
            journal.Append(new ObservedEventEnvelope(sceneId, stamp, ObservedEventDomain.Combat, i, 0, default));
        }

        Assert.Equal(10, journal.Count);
        Assert.Equal(10, journal.NextObservationOrdinal);
    }

    [Fact]
    public void Journal_Append_RejectsNonSequentialOrdinal()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        journal.Append(new ObservedEventEnvelope(sceneId,
            new TimelineStamp(0, 0, 0, 0), ObservedEventDomain.Combat, 0, 0, default));

        var badEntry = new ObservedEventEnvelope(sceneId,
            new TimelineStamp(100, 2, 0, 0), ObservedEventDomain.Combat, 0, 0, default);

        Assert.Throws<ArgumentException>(() => journal.Append(badEntry));
    }

    [Fact]
    public void Journal_Read_ReturnsEntryAtOrdinal()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        var entry = new ObservedEventEnvelope(sceneId,
            new TimelineStamp(500, 0, 1, 1), ObservedEventDomain.Scene, 0, 0, default,
            Scene: new SceneObservation(910035, 0, 0, 0, "test"));
        journal.Append(entry);

        var read = journal.Read(0);
        Assert.Equal(ObservedEventDomain.Scene, read.Domain);
        Assert.Equal(910035u, read.Scene!.Value.MapId);
    }

    [Fact]
    public void Journal_Read_ThrowsOnOutOfBounds()
    {
        var journal = new ObservedEventJournal();
        Assert.Throws<ArgumentOutOfRangeException>(() => journal.Read(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => journal.Read(-1));
    }

    [Fact]
    public void Journal_CompleteBatch_MonotonicallyIncreasing()
    {
        var journal = new ObservedEventJournal();

        journal.CompleteBatch(0);
        journal.CompleteBatch(1);
        journal.CompleteBatch(5);

        Assert.Equal(5, journal.LastCompletedBatchOrdinal);

        Assert.Throws<ArgumentException>(() => journal.CompleteBatch(5));
        Assert.Throws<ArgumentException>(() => journal.CompleteBatch(3));
    }

    [Fact]
    public void Journal_CreateCursor_FindsCorrectPosition()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        for (int i = 0; i < 10; i++)
            journal.Append(new ObservedEventEnvelope(sceneId,
                new TimelineStamp(i * 100, i, i, 0), ObservedEventDomain.Combat, i, 0, default));

        var cursor0 = journal.CreateCursor(0);
        Assert.Equal(0, cursor0.NextObservationOrdinal);

        var cursor5 = journal.CreateCursor(5);
        Assert.Equal(5, cursor5.NextObservationOrdinal);

        var cursorPast = journal.CreateCursor(100);
        Assert.Equal(100, cursorPast.NextObservationOrdinal);
    }

    [Fact]
    public void Journal_CopyEntries_ReturnsRequestedSliceAndNextCursor()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        for (int i = 0; i < 10; i++)
            journal.Append(new ObservedEventEnvelope(sceneId,
                new TimelineStamp(i * 100, i, i, 0), ObservedEventDomain.Combat, i, 0, default));

        var cursor = journal.CreateCursor(3);
        var entries = new ObservedEventEnvelope[4];
        var result = journal.CopyEntries(cursor, entries);

        Assert.Equal(4, result.Count);
        Assert.Equal(7, result.Cursor.NextObservationOrdinal);
        Assert.Equal(3, entries[0].Stamp.ObservationOrdinal);
        Assert.Equal(6, entries[3].Stamp.ObservationOrdinal);
    }

    [Fact]
    public void Journal_CopyEntries_ClampsAtEnd()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        for (int i = 0; i < 3; i++)
            journal.Append(new ObservedEventEnvelope(sceneId,
                new TimelineStamp(i * 100, i, i, 0), ObservedEventDomain.Combat, i, 0, default));

        var cursor = journal.CreateCursor(1);
        var entries = new ObservedEventEnvelope[100];
        var result = journal.CopyEntries(cursor, entries);

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result.Cursor.NextObservationOrdinal);
    }

    [Fact]
    public void Journal_ReadEntries_DoesNotExposeInternalStorage()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        for (int i = 0; i < 3; i++)
            journal.Append(new ObservedEventEnvelope(sceneId,
                new TimelineStamp(i * 100, i, i, 0), ObservedEventDomain.Combat, i, 0, default));

        ObservedEventEnvelope[] copied = [];
        var result = journal.ReadEntries(journal.CreateCursor(1), 10, entries => copied = entries.ToArray());

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result.Cursor.NextObservationOrdinal);
        Assert.Equal([1L, 2L], copied.Select(static entry => entry.Stamp.ObservationOrdinal));
    }

    [Fact]
    public void Clock_CreateStamp_AssignsSequentialOrdinals()
    {
        var clock = new SceneRuntimeClock(startMonotonicTicks: 0);

        var s1 = clock.CreateStamp(1000, 1, 1);
        var s2 = clock.CreateStamp(2000, 2, 1);
        var s3 = clock.CreateStamp(3000, 3, 1);

        Assert.Equal(0, s1.ObservationOrdinal);
        Assert.Equal(1, s2.ObservationOrdinal);
        Assert.Equal(2, s3.ObservationOrdinal);
    }

    [Fact]
    public void Clock_CreateStamp_PreservesFrameAndBatch()
    {
        var clock = new SceneRuntimeClock(startMonotonicTicks: 0);

        var stamp = clock.CreateStamp(5000, frameOrdinal: 42, batchOrdinal: 7);

        Assert.Equal(42, stamp.FrameOrdinal);
        Assert.Equal(7, stamp.BatchOrdinal);
    }

    [Fact]
    public void Clock_CreateStamp_ComputesOffsetFromSceneStart()
    {
        var clock = new SceneRuntimeClock(startMonotonicTicks: 50_000);

        var stamp = clock.CreateStamp(10000, 0, 0);
        Assert.Equal(99_950_000, stamp.OffsetTicks);
    }

    [Fact]
    public void Clock_CreateStampFromOffset_UsesExplicitOffset()
    {
        var clock = new SceneRuntimeClock(startMonotonicTicks: 0);

        var stamp = clock.CreateStampFromOffset(offsetTicks: 9999, frameOrdinal: 5, batchOrdinal: 3);

        Assert.Equal(9999, stamp.OffsetTicks);
        Assert.Equal(5, stamp.FrameOrdinal);
        Assert.Equal(3, stamp.BatchOrdinal);
    }

    [Fact]
    public void JournalingSink_RecordsStageDestinationMap()
    {
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        sink.StageDestinationMap(910035);

        Assert.Equal(1, journal.Count);
        var entry = journal.Read(0);
        Assert.Equal(ObservedEventDomain.Scene, entry.Domain);
        Assert.Equal(910035u, entry.Scene!.Value.MapId);
    }

    [Fact]
    public void JournalingSink_RecordsAppendSummonAndState()
    {
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        sink.AppendSummon(100, 200);

        Assert.True(sink.HasSummonOwner(200));
        Assert.Equal(1, journal.Count);
        Assert.Equal(ObservedEventDomain.State, journal.Read(0).Domain);
    }

    [Fact]
    public void JournalingSink_TracksKnownEntities()
    {
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        sink.AppendNpcCode(42, 2000002);
        Assert.True(sink.IsKnownEntity(42));
        Assert.False(sink.IsKnownEntity(99));
    }

    [Fact]
    public void JournalingSink_RebindLifecycle_SyncsJournalEntityIds()
    {
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        var reboundId = sink.RebindInstanceLifecycle(3518);

        sink.AppendNpcCode(3518, 2000002);
        sink.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = 100,
            TargetId = 3518,
            SkillCode = 11000010,
            Damage = 500,
            HitContribution = 1,
            AttemptContribution = 1,
            Timestamp = 1_000
        });

        Assert.Equal(reboundId, sink.ResolveLifecycleId(3518));
        Assert.Equal(reboundId, journal.Read(0).SourceEntityId);
        Assert.Equal(reboundId, journal.Read(0).State!.Value.EntityId);
        Assert.Equal(reboundId, journal.Read(1).TargetEntityId);
    }

    [Fact]
    public void Boundary_StageDestinationMap_IgnoresZero()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(0);
        Assert.Equal(0u, svc.CurrentMapId);
    }

    [Fact]
    public void Boundary_StageDestinationMap_CommitsImmediately()
    {
        var svc = new SceneBoundaryService();
        Assert.True(svc.StageDestinationMap(910035));
        Assert.Equal(910035u, svc.CurrentMapId);
        Assert.Equal(1, svc.SceneTransitionRevision);
        Assert.Equal(910035u, svc.CurrentMapId);
    }

    [Fact]
    public void Boundary_PendingDestinationMap_DoesNotCommitUntilConfirmed()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(1010);
        var before = svc.SceneTransitionRevision;

        Assert.True(svc.StagePendingDestinationMap(500015, allowSameMapReload: true));
        Assert.Equal(1010u, svc.CurrentMapId);
        Assert.Equal(before, svc.SceneTransitionRevision);

        Assert.True(svc.ConfirmDestinationMap(500015, allowSameMapReload: true));
        Assert.Equal(500015u, svc.CurrentMapId);
        Assert.Equal(before + 1, svc.SceneTransitionRevision);
    }

    [Fact]
    public void Boundary_PendingDestinationMap_CommitsOnArrivalSignal()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(1010);
        var before = svc.SceneTransitionRevision;

        Assert.True(svc.StagePendingDestinationMap(500015, allowSameMapReload: true));
        Assert.True(svc.ConfirmPendingDestinationMapArrival());

        Assert.Equal(500015u, svc.CurrentMapId);
        Assert.Equal(0u, svc.CurrentMapInstanceId);
        Assert.Equal(before + 1, svc.SceneTransitionRevision);
    }

    [Fact]
    public void Boundary_PendingEventMap_CommitsWithConfirmedInstance()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(1010);
        var before = svc.SceneTransitionRevision;

        Assert.True(svc.StagePendingDestinationMap(500015, allowSameMapReload: true));
        Assert.Equal(1010u, svc.CurrentMapId);

        Assert.True(svc.ConfirmDestinationMapInstance(719460));
        Assert.Equal(500015u, svc.CurrentMapId);
        Assert.Equal(719460u, svc.CurrentMapInstanceId);
        Assert.Equal(before + 1, svc.SceneTransitionRevision);
    }

    [Fact]
    public void Boundary_SameMap_DoesNotTriggerTransition()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(100);
        var before = svc.SceneTransitionRevision;
        svc.StageDestinationMap(100);
        Assert.Equal(before, svc.SceneTransitionRevision);
    }

    [Fact]
    public void Boundary_InstanceWithoutPendingMap_AppliesDirectly()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(200003);
        svc.StageDestinationMapInstance(113515);
        Assert.Equal(113515u, svc.CurrentMapInstanceId);
    }

    [Fact]
    public void Boundary_MapChange_ResetsInstance()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(200003);
        svc.StageDestinationMapInstance(113515);
        svc.StageDestinationMap(1010);
        Assert.Equal(1010u, svc.CurrentMapId);
        Assert.Equal(0u, svc.CurrentMapInstanceId);
    }

    [Fact]
    public void Boundary_SameMapReloadCandidate_AdvancesTransitionRevision()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(1010);
        var before = svc.SceneTransitionRevision;

        Assert.True(svc.StagePendingDestinationMap(1010, allowSameMapReload: true));
        Assert.True(svc.ConfirmDestinationMap(1010, allowSameMapReload: true));

        Assert.Equal(1010u, svc.CurrentMapId);
        Assert.Equal(0u, svc.CurrentMapInstanceId);
        Assert.Equal(before + 1, svc.SceneTransitionRevision);
    }

    [Fact]
    public void Boundary_SameMapReloadCandidate_WithInstance_KeepsInstance()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(154001);
        svc.StageDestinationMapInstance(89730);
        var before = svc.SceneTransitionRevision;

        Assert.True(svc.StagePendingDestinationMap(154001, allowSameMapReload: true));
        Assert.True(svc.ConfirmDestinationMap(154001, allowSameMapReload: true));

        Assert.Equal(154001u, svc.CurrentMapId);
        Assert.Equal(89730u, svc.CurrentMapInstanceId);
        Assert.Equal(before + 1, svc.SceneTransitionRevision);
    }

    [Fact]
    public void Boundary_TransportBoundary_WithoutPendingScene_DoesNotChangeInstancedMap()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(1010);
        Assert.Equal(SceneTransitionKind.None, svc.MarkSceneTransportBoundary());

        svc.StageDestinationMap(500015, allowSameMapReload: true);
        svc.StageDestinationMapInstance(622949);
        Assert.Equal(500015u, svc.CurrentMapId);
        Assert.Equal(622949u, svc.CurrentMapInstanceId);

        var before = svc.SceneTransitionRevision;
        var kind = svc.MarkSceneTransportBoundary();

        Assert.Equal(SceneTransitionKind.None, kind);
        Assert.Equal(500015u, svc.CurrentMapId);
        Assert.Equal(622949u, svc.CurrentMapInstanceId);
        Assert.Equal(before, svc.SceneTransitionRevision);
    }

    [Fact]
    public void Boundary_TransportBoundary_AfterImmediateMapCommit_DoesNotChangeState()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(1010);
        Assert.Equal(SceneTransitionKind.None, svc.MarkSceneTransportBoundary());

        svc.StageDestinationMap(600011, allowSameMapReload: true);
        svc.StageDestinationMapInstance(679398);
        Assert.Equal(SceneTransitionKind.None, svc.MarkSceneTransportBoundary());

        svc.StageDestinationMap(1010, allowSameMapReload: true);
        var before = svc.SceneTransitionRevision;
        var kind = svc.MarkSceneTransportBoundary();

        Assert.Equal(SceneTransitionKind.None, kind);
        Assert.Equal(1010u, svc.CurrentMapId);
        Assert.Equal(0u, svc.CurrentMapInstanceId);
        Assert.Equal(before, svc.SceneTransitionRevision);
    }

    [Fact]
    public void Boundary_TransportBoundary_WithPendingSameMapReload_DoesNotConfirmArrival()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(1010);
        Assert.Equal(SceneTransitionKind.None, svc.MarkSceneTransportBoundary());

        svc.StageDestinationMap(600011, allowSameMapReload: true);
        svc.StageDestinationMapInstance(679398);
        Assert.Equal(SceneTransitionKind.None, svc.MarkSceneTransportBoundary());

        svc.StagePendingDestinationMap(600011, allowSameMapReload: true);
        var before = svc.SceneTransitionRevision;
        var kind = svc.MarkSceneTransportBoundary();

        Assert.Equal(SceneTransitionKind.None, kind);
        Assert.Equal(600011u, svc.CurrentMapId);
        Assert.Equal(679398u, svc.CurrentMapInstanceId);
        Assert.Equal(before, svc.SceneTransitionRevision);
    }

    [Fact]
    public void Boundary_TransportBoundary_WithoutPendingScene_DoesNotChangeEventMap()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(1010);
        Assert.Equal(SceneTransitionKind.None, svc.MarkSceneTransportBoundary());

        svc.StageDestinationMap(500020, allowSameMapReload: true);
        Assert.Equal(500020u, svc.CurrentMapId);
        Assert.Equal(0u, svc.CurrentMapInstanceId);

        var before = svc.SceneTransitionRevision;
        var kind = svc.MarkSceneTransportBoundary();

        Assert.Equal(SceneTransitionKind.None, kind);
        Assert.Equal(500020u, svc.CurrentMapId);
        Assert.Equal(0u, svc.CurrentMapInstanceId);
        Assert.Equal(before, svc.SceneTransitionRevision);
    }

    [Fact]
    public void Boundary_InstanceStagedAlongsideMap_CommitsTogether()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(200003);
        svc.StageDestinationMapInstance(515552);
        Assert.Equal(200003u, svc.CurrentMapId);
        Assert.Equal(515552u, svc.CurrentMapInstanceId);
    }

    [Fact]
    public void Boundary_RedundantMapStage_DoesNotClobberInstance()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(910035);
        svc.StageDestinationMapInstance(516446);
        svc.StageDestinationMap(910035);
        Assert.Equal(910035u, svc.CurrentMapId);
        Assert.Equal(516446u, svc.CurrentMapInstanceId);
    }
}
