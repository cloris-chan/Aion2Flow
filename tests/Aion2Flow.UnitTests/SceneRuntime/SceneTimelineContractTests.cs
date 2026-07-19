using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Runtime;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public class SceneTimelineContractTests
{
    [Fact]
    public void Journal_CombatEntry_ExposesOnlyTypedCombatPayload()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        var raw = new RawPacketReference(0x0438, 32, 1);
        var header = new ObservedEventHeader(sceneId, new TimelineStamp(100, 0, 1), 100, 200, raw);
        var combat = new CombatWireObservation { SkillCode = 1234, Damage = 500, HitCount = 1, AttemptCount = 1 };

        journal.Append(in header, in combat);

        journal.ReadEntry(0, entry =>
        {
            Assert.Equal(ObservedEventDomain.Combat, entry.Domain);
            Assert.Equal(sceneId, entry.SceneSessionId);
            Assert.Equal(raw, entry.Raw);
            Assert.Equal(1234, entry.Combat.SkillCode);
            AssertStateAccessThrows(entry);
        });
    }

    [Fact]
    public void Journal_SceneEntry_ExposesTypedScenePayload()
    {
        var journal = new ObservedEventJournal();
        var header = CreateHeader(Guid.NewGuid(), 0);
        var scene = new SceneObservation(910035, 0, 0, 0, "test");

        journal.Append(in header, in scene);

        journal.ReadEntry(0, entry =>
        {
            Assert.Equal(ObservedEventDomain.Scene, entry.Domain);
            Assert.Equal(910035u, entry.Scene.MapId);
        });
    }

    [Fact]
    public void Journal_DiagnosticEntry_HasNoDomainPayload()
    {
        var journal = new ObservedEventJournal();
        var header = CreateHeader(Guid.NewGuid(), 0);

        journal.AppendDiagnostic(in header);

        journal.ReadEntry(0, entry =>
        {
            Assert.Equal(ObservedEventDomain.Diagnostic, entry.Domain);
            AssertCombatAccessThrows(entry);
        });
    }

    [Fact]
    public void RawPacketReference_PreservesPacketStructure()
    {
        var structure = new PacketStructureReference(
            PacketStructureKind.FrameBatchEntry,
            ScopeId: 2,
            ParentScopeId: 1,
            Depth: 2,
            SiblingIndex: 3,
            Offset: 16,
            Length: 64,
            BodyOffset: 4,
            BodyLength: 60);
        var raw = new RawPacketReference(0x0538, 64, 7, structure);

        Assert.Equal(structure, raw.Structure);
        Assert.Equal(structure, raw.StructurePath.Leaf);
        Assert.Equal(PacketStructureKind.FrameBatchEntry, raw.Structure.Kind);
        Assert.Equal(3, raw.Structure.SiblingIndex);
    }

    [Fact]
    public void RawPacketReference_PreservesPacketStructurePath()
    {
        var root = new PacketStructureReference(PacketStructureKind.TransportPacket, 1, 0, 1, 0, 0, 100, 0, 100);
        var frame = new PacketStructureReference(PacketStructureKind.FrameBatchEntry, 2, 1, 2, 0, 0, 30, 3, 27);
        var path = default(PacketStructurePath).Push(root).Push(frame);
        var raw = new RawPacketReference(0x0438, 30, 0, path);

        Assert.Equal(frame, raw.Structure);
        Assert.Equal(root, raw.StructurePath.Root);
        Assert.Equal(frame, raw.StructurePath.Leaf);
        Assert.Equal(2, raw.StructurePath.Depth);
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
    public void Journal_Append_AssignsMonotonicallyIncreasingOrdinals()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
            AppendCombat(journal, sceneId, i);

        Assert.Equal(10, journal.Count);
        Assert.Equal(10, journal.NextObservationOrdinal);
    }

    [Fact]
    public void Journal_Append_RejectsNonSequentialOrdinal()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        AppendCombat(journal, sceneId, 0);
        var badHeader = CreateHeader(sceneId, 2);
        var combat = default(CombatWireObservation);

        Assert.Throws<ArgumentException>(() => journal.Append(in badHeader, in combat));
    }

    [Fact]
    public void Journal_ReadEntry_ReturnsEntryAtOrdinal()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        var header = new ObservedEventHeader(sceneId, new TimelineStamp(500, 0, 1), 0, 0, default);
        var scene = new SceneObservation(910035, 0, 0, 0, "test");
        journal.Append(in header, in scene);

        journal.ReadEntry(0, entry =>
        {
            Assert.Equal(ObservedEventDomain.Scene, entry.Domain);
            Assert.Equal(910035u, entry.Scene.MapId);
            Assert.Equal(0, entry.Stamp.ObservationOrdinal);
        });
    }

    [Fact]
    public void Journal_ReadEntry_ThrowsOnOutOfBounds()
    {
        var journal = new ObservedEventJournal();
        Assert.Throws<ArgumentOutOfRangeException>(() => journal.ReadEntry(0, static _ => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => journal.ReadEntry(-1, static _ => { }));
        Assert.False(journal.TryReadEntry(0, static _ => { }));
    }

    [Fact]
    public void Journal_CompleteFlush_MonotonicallyIncreasing()
    {
        var journal = new ObservedEventJournal();

        journal.CompleteFlush(0);
        journal.CompleteFlush(1);
        journal.CompleteFlush(5);

        Assert.Equal(5, journal.LastCompletedFlushId);

        Assert.Throws<ArgumentException>(() => journal.CompleteFlush(5));
        Assert.Throws<ArgumentException>(() => journal.CompleteFlush(3));
    }

    [Fact]
    public void Journal_CreateCursor_FindsCorrectPosition()
    {
        var journal = new ObservedEventJournal();

        var cursor0 = journal.CreateCursor(0);
        Assert.Equal(0, cursor0.NextObservationOrdinal);

        var cursor5 = journal.CreateCursor(5);
        Assert.Equal(5, cursor5.NextObservationOrdinal);

        var cursorPast = journal.CreateCursor(100);
        Assert.Equal(100, cursorPast.NextObservationOrdinal);
    }

    [Fact]
    public void Journal_ReadEntries_ReturnsRequestedSliceAndNextCursor()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
            AppendCombat(journal, sceneId, i);

        var cursor = journal.CreateCursor(3);
        long[] ordinals = [];
        var result = journal.ReadEntries(cursor, 4, entries => ordinals = ReadOrdinals(entries));

        Assert.Equal(4, result.Count);
        Assert.Equal(7, result.Cursor.NextObservationOrdinal);
        Assert.Equal([3L, 4L, 5L, 6L], ordinals);
    }

    [Fact]
    public void Journal_ReadEntries_ClampsAtEnd()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
            AppendCombat(journal, sceneId, i);

        var cursor = journal.CreateCursor(1);
        var result = journal.ReadEntries(cursor, 100, static _ => { });

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result.Cursor.NextObservationOrdinal);
    }

    [Fact]
    public void Journal_ReadEntries_DoesNotAllocateAfterWarmup()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        for (var i = 0; i < 32; i++)
            AppendCombat(journal, sceneId, i);

        _ = journal.ReadEntries(journal.CreateCursor(0), 32, ConsumeJournalEntries);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var count = 0;
        for (var i = 0; i < 10_000; i++)
            count += journal.ReadEntries(journal.CreateCursor(0), 32, ConsumeJournalEntries).Count;
        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(320_000, count);
        Assert.Equal(allocatedBefore, allocatedAfter);
    }

    [Fact]
    public void Journal_ReadEntries_StopsAtEndExclusive()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
            AppendCombat(journal, sceneId, i);

        long[] ordinals = [];
        var result = journal.ReadEntries(journal.CreateCursor(3), 7, 10, entries => ordinals = ReadOrdinals(entries));

        Assert.Equal(4, result.Count);
        Assert.Equal(7, result.Cursor.NextObservationOrdinal);
        Assert.Equal([3L, 4L, 5L, 6L], ordinals);
    }

    [Fact]
    public void Journal_ReadEntries_CrossesPhysicalSegmentsWithoutCopying()
    {
        var journal = new ObservedEventJournal(1);
        var sceneId = Guid.NewGuid();

        var raw = new RawPacketReference(0x0438, 64, 1);
        for (var i = 0; i < ObservedEventJournal.SegmentCapacity + 3; i++)
            AppendCombat(journal, sceneId, i, raw);

        long[] firstOrdinals = [];
        var first = journal.ReadEntries(journal.CreateCursor(ObservedEventJournal.SegmentCapacity - 2), 10, entries => firstOrdinals = ReadOrdinals(entries));
        long[] secondOrdinals = [];
        var second = journal.ReadEntries(first.Cursor, 10, entries => secondOrdinals = ReadOrdinals(entries));

        Assert.Equal(2, first.Count);
        Assert.Equal([510L, 511L], firstOrdinals);
        Assert.Equal(3, second.Count);
        Assert.Equal([512L, 513L, 514L], secondOrdinals);
        Assert.Equal(2, journal.SegmentCount);
        Assert.Equal(1, journal.SceneSessionCount);
    }

    [Fact]
    public void Journal_RawPacketReferencesRemainExactAcrossStorageSegments()
    {
        var entryCount = ObservedEventJournal.SegmentCapacity + 1;
        var journal = new ObservedEventJournal(entryCount);
        var sceneId = Guid.NewGuid();

        for (var i = 0; i < entryCount; i++)
        {
            var raw = CreateStructuredRawReference(i);
            var header = CreateHeader(sceneId, i, raw);
            journal.AppendDiagnostic(in header);
        }

        Assert.Equal(CreateStructuredRawReference(0), ReadRaw(journal, 0));
        Assert.Equal(
            CreateStructuredRawReference(ObservedEventJournal.SegmentCapacity - 1),
            ReadRaw(journal, ObservedEventJournal.SegmentCapacity - 1));
        Assert.Equal(
            CreateStructuredRawReference(ObservedEventJournal.SegmentCapacity),
            ReadRaw(journal, ObservedEventJournal.SegmentCapacity));
    }

    [Fact]
    public void Journal_AppendingUniqueRawPacketReferences_UsesBoundedStorageAllocation()
    {
        const int entryCount = 16_384;
        const long maxAllocatedBytesPerEntry = 275;
        var warmup = new ObservedEventJournal(1);
        var warmupHeader = CreateHeader(Guid.Empty, 0, new RawPacketReference(0x0438, 64, 0));
        warmup.AppendDiagnostic(in warmupHeader);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var journal = new ObservedEventJournal(entryCount);
        for (var i = 0; i < entryCount; i++)
        {
            var raw = new RawPacketReference(0x0438, 64, i);
            var header = CreateHeader(Guid.Empty, i, raw);
            journal.AppendDiagnostic(in header);
        }
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(entryCount, journal.Count);
        Assert.True(
            allocatedBytes <= entryCount * maxAllocatedBytesPerEntry,
            $"Journal allocated {allocatedBytes} bytes for {entryCount} unique raw packet references.");
        GC.KeepAlive(journal);
    }

    [Fact]
    public void SceneJournalSegment_ReadEntries_ClampsToSegmentStart()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        for (var i = 0; i < 6; i++)
            AppendCombat(journal, sceneId, i);

        var segment = new SceneJournalSegment(journal, 2, 5, IsLiveGrowing: false);
        long[] ordinals = [];
        var result = segment.ReadEntries(journal.CreateCursor(0), 10, entries => ordinals = ReadOrdinals(entries));

        Assert.Equal(3, result.Count);
        Assert.Equal(5, result.Cursor.NextObservationOrdinal);
        Assert.Equal([2L, 3L, 4L], ordinals);
    }

    [Fact]
    public void Clock_CreateStamp_AssignsSequentialOrdinals()
    {
        var clock = new SceneRuntimeClock(sceneStartedAtMilliseconds: 0);

        var s1 = clock.CreateStamp(1000, 1);
        var s2 = clock.CreateStamp(2000, 1);
        var s3 = clock.CreateStamp(3000, 1);

        Assert.Equal(0, s1.ObservationOrdinal);
        Assert.Equal(1, s2.ObservationOrdinal);
        Assert.Equal(2, s3.ObservationOrdinal);
    }

    [Fact]
    public void Clock_CreateStamp_PreservesBatch()
    {
        var clock = new SceneRuntimeClock(sceneStartedAtMilliseconds: 0);

        var stamp = clock.CreateStamp(5000, flushId: 7);

        Assert.Equal(7, stamp.FlushId);
    }

    [Fact]
    public void Clock_CreateStamp_ComputesOffsetFromSceneStart()
    {
        var clock = new SceneRuntimeClock(sceneStartedAtMilliseconds: 5_000);

        var atStart = clock.CreateStamp(5_000, 0);
        var afterStart = clock.CreateStamp(10_000, 0);

        Assert.Equal(0, atStart.OffsetTicks);
        Assert.Equal(TimeSpan.FromSeconds(5).Ticks, afterStart.OffsetTicks);
    }

    [Fact]
    public void Clock_Reset_ChangesSceneRelativeOrigin()
    {
        var clock = new SceneRuntimeClock(sceneStartedAtMilliseconds: 0);
        clock.Reset(DateTimeOffset.FromUnixTimeMilliseconds(4_000));

        var stamp = clock.CreateStamp(5_000, flushId: 3);

        Assert.Equal(10_000_000, stamp.OffsetTicks);
        Assert.Equal(3, stamp.FlushId);
    }

    [Fact]
    public void CombatProjection_PreservesZeroSceneOffset()
    {
        var entities = new EntityStore();
        var entityVitals = new EntityVitalStore();
        var combat = new CombatStore();
        var mechanics = new MechanicStore();
        var resources = new ResourceStore();
        var observation = new CombatWireObservation
        {
            Damage = 100,
            HitCount = 1,
            AttemptCount = 1
        };
        var resolution = new CombatResolutionTrace(
            PacketRule: CombatPacketRule.DirectValue,
            SemanticMatch: CombatSemanticMatchKind.None,
            Authority: CombatResolutionAuthority.PacketDefault,
            Materialization: CombatMaterializationKind.Primary,
            Association: CombatAssociationKind.None,
            DirectSemantics: default,
            Semantics: default,
            ResourceEffectRef: default,
            ResourceNodeKind: default,
            ResourceNodeId: 0,
            ResourceSkillId: 0,
            EffectSlot: -1,
            ResourceCandidateSlotCount: 0);
        var contribution = new CombatContribution(
            Metric: CombatMetricKind.Damage,
            Delivery: CombatDeliveryKind.Direct,
            Amount: 100,
            Resolution: resolution);
        var mechanic = new CombatMechanicOccurrence(
            Modifiers: default,
            HitCount: 1,
            AttemptCount: 1,
            EvadeCount: 0,
            InvincibleCount: 0,
            MultiHitCount: 0,
            MultiHitSubCount: 0,
            Resolution: resolution);

        combat.ApplyCombat(10, 20, in observation, in contribution, 0);
        mechanics.Apply(10, 20, in observation, in mechanic, 0, CombatStore.UnknownSourceObservationOrdinal, default);
        combat.ApplyCombat(10, 20, in observation, in contribution, 1_000);
        mechanics.Apply(10, 20, in observation, in mechanic, 1_000, CombatStore.UnknownSourceObservationOrdinal, default);

        var pair = Assert.Single(combat.Pairs).Value;
        var snapshot = new SceneCombatSnapshotAdapter(entities, entityVitals, combat, mechanics, resources, new SceneBoundaryStore()).CreateSnapshot();

        Assert.Equal(0, pair.FirstObserved);
        Assert.Equal(1_000, pair.LastObserved);
        Assert.Equal(0, snapshot.EncounterStartTime);
        Assert.Equal(1_000, snapshot.EncounterEndTime);
        Assert.Equal(1_000, snapshot.EncounterTime);
    }

    [Fact]
    public void JournalingSink_NpcRuntimeStateUsesSceneRelativeTime()
    {
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(sceneStartedAtMilliseconds: 5_000);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());
        var source = new PacketObservationSource(6_250, 1, 0x008D, 16, 7, default);

        sink.AppendNpcHp(in source, 42, 9_000, 10_000);

        Assert.True(sink.TryGetNpcRuntimeState(42, out var state));
        Assert.Equal(1_250, state.HpObservedAtMilliseconds);
        Assert.Equal(TimeSpan.FromMilliseconds(1_250).Ticks, ReadStamp(journal, 0).OffsetTicks);
    }

    [Fact]
    public void JournalingSink_Preserves2A38OpenLease()
    {
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());
        var source = new PacketObservationSource(1_000, 1, 0x2A38, 41, 7, default);

        sink.RegisterObservation2A38(in source, 42, 1, 19, 95, 163_000_001, 3_000, 0x010203040506, 0x10203040, 414, 77, 2, ResourceEffectRef.FromRaw(16_300_243), 13, 0x0102030405060708, 0x090A0B0C0D);

        var aura = ReadAura(journal, 0);
        Assert.Equal(AuraObservationKind.Open, aura.Kind);
        Assert.Equal(42, aura.EntityId);
        Assert.Equal(95, aura.InstanceSequenceId);
        Assert.Equal(3_000, aura.HeadValue);
        Assert.Equal(77, aura.EchoSourceEntityId);
        Assert.Equal(2, aura.StackCount);
        Assert.Equal(16_300_243u, aura.BuffResourceEffectRef.RawId);
        Assert.Equal(13, aura.TailLength);
    }

    [Fact]
    public void JournalingSink_Preserves2B38RenewalIdentity()
    {
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());
        var source = new PacketObservationSource(1_000, 1, 0x2B38, 50, 7, default);

        sink.RegisterObservation2B38(in source, 42, 77, 19, 95, ResourceEffectRef.FromRaw(16_300_243), 123_456, 1, 2, 20);

        var action = ReadAction(journal, 0);
        Assert.Equal(42, action.SourceEntityId);
        Assert.Equal(77, action.SourceEntityIdCopy);
        Assert.Equal(95, action.InstanceSequenceId);
        Assert.Equal(123_456, action.SequenceValue);
        Assert.Equal(16_300_243u, action.ActionResourceEffectRef.RawId);
        Assert.Equal(20, action.TailLength);
    }

    [Fact]
    public void JournalingSink_Preserves2C38BatchResultWithoutInferringLifecycleIdentity()
    {
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());
        var source = new PacketObservationSource(1_000, 1, 0x2C38, 16, 7, default);

        Span<AuraResultRecord> results = stackalloc AuraResultRecord[4];
        results[0] = new AuraResultRecord(7, 93, 11, 23_771, 1, 2);
        results[1] = new AuraResultRecord(7, 94, 11, 23_771, 3, 4);
        results[2] = new AuraResultRecord(7, 95, 11, 23_771, 0x01020304, 0x05060708);
        results[3] = new AuraResultRecord(7, 96, 11, 23_771, 5, 6);
        sink.RegisterObservation2C38(in source, 42, results);

        Assert.Equal(4, journal.Count);
        var aura = ReadAura(journal, 2, out var sourceEntityId, out var targetEntityId);
        Assert.Equal(0, sourceEntityId);
        Assert.Equal(42, targetEntityId);
        Assert.Equal(AuraObservationKind.Result, aura.Kind);
        Assert.Equal(42, aura.EntityId);
        Assert.Equal(4, aura.ResultCount);
        Assert.Equal(2, aura.ResultIndex);
        Assert.Equal(95, aura.InstanceSequenceId);
        Assert.Equal(7, aura.StateCode);
        Assert.Equal(11, aura.ResultCode);
        Assert.Equal(23_771, aura.ResultDetailEntityId);
        Assert.Equal(0x01020304u, aura.ResultDetailValue0);
        Assert.Equal(0x05060708u, aura.ResultDetailValue1);
        Assert.False(sink.IsKnownEntity(23_771));
    }

    [Fact]
    public void JournalingSink_RecordsStageDestinationMap()
    {
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());

        sink.StageDestinationMap(910035);

        Assert.Equal(1, journal.Count);
        Assert.Equal(ObservedEventDomain.Scene, ReadDomain(journal, 0));
        Assert.Equal(910035u, ReadScene(journal, 0).MapId);
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
        Assert.Equal(ObservedEventDomain.State, ReadDomain(journal, 0));
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
        var combatSource = new PacketObservationSource(1_000, 0, 0x0438, 0, 0, default);
        var combat = new CombatWireObservation
        {
            SkillCode = 11000010,
            Damage = 500,
            HitCount = 1,
            AttemptCount = 1
        };
        sink.AppendCombatWireObservation(in combatSource, 100, 3518, in combat);

        Assert.Equal(reboundId, sink.ResolveLifecycleId(3518));
        Assert.Equal(reboundId, ReadSourceEntityId(journal, 0));
        Assert.Equal(reboundId, ReadState(journal, 0).EntityId);
        Assert.Equal(reboundId, ReadTargetEntityId(journal, 1));
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

    private static long s_observationChecksum;
    private static readonly JournalEntriesReader ConsumeJournalEntries = static entries =>
    {
        var checksum = 0L;
        for (var i = 0; i < entries.Count; i++)
            checksum += entries[i].Stamp.ObservationOrdinal;
        Volatile.Write(ref s_observationChecksum, checksum);
    };

    private static ObservedEventHeader CreateHeader(Guid sceneId, long ordinal, RawPacketReference raw = default)
        => new(sceneId, new TimelineStamp(ordinal * 100, ordinal, 0), (int)ordinal, 0, raw);

    private static RawPacketReference CreateStructuredRawReference(long captureSequence)
    {
        var root = new PacketStructureReference(PacketStructureKind.TransportPacket, 1, 0, 1, 0, 0, 100, 0, 100);
        var frame = new PacketStructureReference(PacketStructureKind.FrameBatchEntry, 2, 1, 2, 3, 16, 64, 4, 60);
        return new RawPacketReference(0x0438, 64, captureSequence, default(PacketStructurePath).Push(root).Push(frame));
    }

    private static void AppendCombat(ObservedEventJournal journal, Guid sceneId, long ordinal, RawPacketReference raw = default)
    {
        var header = CreateHeader(sceneId, ordinal, raw);
        var combat = new CombatWireObservation { SkillCode = (int)ordinal };
        journal.Append(in header, in combat);
    }

    private static long[] ReadOrdinals(JournalEntryBatch entries)
    {
        var result = new long[entries.Count];
        for (var i = 0; i < result.Length; i++)
            result[i] = entries[i].Stamp.ObservationOrdinal;
        return result;
    }

    private static TimelineStamp ReadStamp(ObservedEventJournal journal, long ordinal)
    {
        var result = default(TimelineStamp);
        journal.ReadEntry(ordinal, entry => result = entry.Stamp);
        return result;
    }

    private static RawPacketReference ReadRaw(ObservedEventJournal journal, long ordinal)
    {
        var result = default(RawPacketReference);
        journal.ReadEntry(ordinal, entry => result = entry.Raw);
        return result;
    }

    private static ObservedEventDomain ReadDomain(ObservedEventJournal journal, long ordinal)
    {
        var result = default(ObservedEventDomain);
        journal.ReadEntry(ordinal, entry => result = entry.Domain);
        return result;
    }

    private static int ReadSourceEntityId(ObservedEventJournal journal, long ordinal)
    {
        var result = 0;
        journal.ReadEntry(ordinal, entry => result = entry.SourceEntityId);
        return result;
    }

    private static int ReadTargetEntityId(ObservedEventJournal journal, long ordinal)
    {
        var result = 0;
        journal.ReadEntry(ordinal, entry => result = entry.TargetEntityId);
        return result;
    }

    private static StateObservation ReadState(ObservedEventJournal journal, long ordinal)
    {
        var result = default(StateObservation);
        journal.ReadEntry(ordinal, entry => result = entry.State);
        return result;
    }

    private static SceneObservation ReadScene(ObservedEventJournal journal, long ordinal)
    {
        var result = default(SceneObservation);
        journal.ReadEntry(ordinal, entry => result = entry.Scene);
        return result;
    }

    private static AuraObservation ReadAura(ObservedEventJournal journal, long ordinal)
        => ReadAura(journal, ordinal, out _, out _);

    private static AuraObservation ReadAura(ObservedEventJournal journal, long ordinal, out int sourceEntityId, out int targetEntityId)
    {
        var result = (Aura: default(AuraObservation), SourceEntityId: 0, TargetEntityId: 0);
        journal.ReadEntry(ordinal, entry => result = (entry.Aura, entry.SourceEntityId, entry.TargetEntityId));
        sourceEntityId = result.SourceEntityId;
        targetEntityId = result.TargetEntityId;
        return result.Aura;
    }

    private static ActionObservation ReadAction(ObservedEventJournal journal, long ordinal)
    {
        var result = default(ActionObservation);
        journal.ReadEntry(ordinal, entry => result = entry.Action);
        return result;
    }

    private static void AssertStateAccessThrows(ObservedEventEntry entry)
    {
        var threw = false;
        try
        {
            _ = entry.State.StateCode;
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        Assert.True(threw);
    }

    private static void AssertCombatAccessThrows(ObservedEventEntry entry)
    {
        var threw = false;
        try
        {
            _ = entry.Combat.SkillCode;
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        Assert.True(threw);
    }
}
