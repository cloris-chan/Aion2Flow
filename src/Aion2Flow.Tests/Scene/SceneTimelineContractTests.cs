using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Model;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Scene.Runtime;
using ParsedCombatPacket = Cloris.Aion2Flow.Combat.Metrics.ParsedCombatPacket;

namespace Cloris.Aion2Flow.Tests.Scene;

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
            Combat: new CombatObservation(SkillCode: 1234, Damage: 500, HitCount: 1, AttemptCount: 1, DetailRaw: 0));

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
        Assert.Equal(910035, envelope.Scene!.Value.MapId);
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
        Assert.Equal(910035, read.Scene!.Value.MapId);
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
        Assert.Equal(0, cursor0.Position);

        var cursor5 = journal.CreateCursor(5);
        Assert.Equal(5, cursor5.Position);

        var cursorPast = journal.CreateCursor(100);
        Assert.Equal(10, cursorPast.Position);
    }

    [Fact]
    public void Journal_GetEntries_ReturnsRequestedSlice()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        for (int i = 0; i < 10; i++)
            journal.Append(new ObservedEventEnvelope(sceneId,
                new TimelineStamp(i * 100, i, i, 0), ObservedEventDomain.Combat, i, 0, default));

        var cursor = journal.CreateCursor(3);
        var entries = journal.GetEntries(cursor, 4);

        Assert.Equal(4, entries.Length);
        Assert.Equal(3, entries[0].Stamp.ObservationOrdinal);
        Assert.Equal(6, entries[3].Stamp.ObservationOrdinal);
    }

    [Fact]
    public void Journal_GetEntries_ClampsAtEnd()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();

        for (int i = 0; i < 3; i++)
            journal.Append(new ObservedEventEnvelope(sceneId,
                new TimelineStamp(i * 100, i, i, 0), ObservedEventDomain.Combat, i, 0, default));

        var cursor = journal.CreateCursor(1);
        var entries = journal.GetEntries(cursor, 100);

        Assert.Equal(2, entries.Length);
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
    public void CompositeSink_ForwardsStageDestinationMapToBoth()
    {
        var legacy = new FakeRuntimeSink();
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var journaling = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());
        var composite = new CompositeRuntimeObservationSink(legacy, journaling);

        composite.StageDestinationMap(910035);

        Assert.Equal(910035u, legacy.LastStageDestinationMap);
        Assert.Equal(1, journal.Count);
        var entry = journal.Read(0);
        Assert.Equal(ObservedEventDomain.Scene, entry.Domain);
        Assert.Equal(910035, entry.Scene!.Value.MapId);
    }

    [Fact]
    public void CompositeSink_ForwardsMarkSceneArrivalToBoth()
    {
        var legacy = new FakeRuntimeSink();
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var journaling = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());
        var composite = new CompositeRuntimeObservationSink(legacy, journaling);

        composite.MarkSceneArrival();

        Assert.True(legacy.SceneArrivalCalled);
        Assert.Equal(1, journal.Count);
        Assert.Equal(ObservedEventDomain.Scene, journal.Read(0).Domain);
        Assert.Equal("scene-arrival", journal.Read(0).Scene!.Value.DiagnosticKey);
    }

    [Fact]
    public void CompositeSink_ForwardsAppendSummonToBoth()
    {
        var legacy = new FakeRuntimeSink();
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var journaling = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());
        var composite = new CompositeRuntimeObservationSink(legacy, journaling);

        composite.AppendSummon(100, 200);

        Assert.Equal((100, 200), legacy.LastAppendSummon);
        Assert.Equal(1, journal.Count);
        Assert.Equal(ObservedEventDomain.State, journal.Read(0).Domain);
    }

    [Fact]
    public void CompositeSink_LegacyIsQueryAuthority()
    {
        var legacy = new FakeRuntimeSink();
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(0);
        var journaling = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());
        var composite = new CompositeRuntimeObservationSink(legacy, journaling);

        legacy.KnownEntities.Add(42);
        Assert.True(composite.IsKnownEntity(42));
        Assert.False(composite.IsKnownEntity(99));
    }

    private sealed class FakeRuntimeSink : IRuntimeObservationSink
    {
        public int CurrentTarget { get; set; }
        public HashSet<int> KnownEntities { get; } = [];
        public uint LastStageDestinationMap;
        public bool SceneArrivalCalled;
        public (int OwnerId, int SummonId) LastAppendSummon;

        public int ResolveLifecycleId(int rawInstanceId) => rawInstanceId;
        public int RebindInstanceLifecycle(int rawInstanceId) => rawInstanceId;
        public bool IsKnownEntity(int id) => KnownEntities.Contains(id);
        public bool HasSummonOwner(int instanceId) => false;
        public bool TryGetNpcRuntimeState(int instanceId, out RuntimeNpcStateSnapshot state) { state = default; return false; }
        public int ResolveNpcObservationSource() => 0;
        public void RememberNpcObservationSource(int instanceId) { }
        public void StageDestinationMap(uint mapId) => LastStageDestinationMap = mapId;
        public void StageDestinationMapInstance(uint instanceId) { }
        public void MarkSceneArrival() => SceneArrivalCalled = true;
        public void AppendCombatPacket(ParsedCombatPacket packet) { }
        public void RegisterCompactValue0438(int t, int s, int sk, int m, int l, int tp, long ts, long fo, long bo) { }
        public void RegisterCompactValue0438(int t, int s, int sk, int m, int l, int tp, int v, long ts, long fo, long bo) { }
        public void RegisterCompactControl0238(int s, int sk, int m, long bo) { }
        public void RegisterCompactControl0638(int s, int sk, int m, long ts, long fo, long bo) { }
        public void RegisterPeriodicLink0538(int t, int s, int li, int si, int tr, long ts, long fo, long bo) { }
        public void RegisterObservation2A38(int s, int mo, int gc, int si, ushort hv, uint bc, long ts, long fo, long bo) { }
        public void RegisterObservation2C38(int ii, int mo, int si, int rc, int ts, int tsk, long ts2, long fo, long bo) { }
        public void AppendNickname(int uid, string nickname, int? originServerId = null) { }
        public void AppendNpcCode(int instanceId, int npcCode) { }
        public void AppendNpcName(int npcCode, string name) { }
        public void AppendNpcKind(int instanceId, NpcKind kind) { }
        public void AppendNpcHp(int instanceId, int hp, long observedAtMilliseconds) { }
        public void AppendNpcHp(int instanceId, int hp, int maxHp, long observedAtMilliseconds) { }
        public void SetNpcBattle(int instanceId, bool isActive, long observedAtMilliseconds) { }
        public void ToggleNpcBattle(int instanceId) { }
        public void AppendNpc2136State(int instanceId, uint sequence, uint value0) { }
        public void AppendNpc0140Value(int instanceId, uint value0) { }
        public void AppendNpc0240Value(int instanceId, uint value0) { }
        public void AppendNpc4636State(int instanceId, byte state0, byte state1) { }
        public void AppendSummon(int ownerId, int summonInstanceId) => LastAppendSummon = (ownerId, summonInstanceId);
    }

    [Fact]
    public void Boundary_StageDestinationMap_IgnoresZero()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(0);
        Assert.Equal(0u, svc.CurrentMapId);
    }

    [Fact]
    public void Boundary_StageDestinationMap_StagesAndArrivalCommits()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(910035);
        Assert.Equal(0u, svc.CurrentMapId);
        var kind = svc.MarkSceneArrival();
        Assert.Equal(SceneTransitionKind.MapChanged, kind);
        Assert.Equal(910035u, svc.CurrentMapId);
    }

    [Fact]
    public void Boundary_SameMap_DoesNotTriggerTransition()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(100);
        svc.MarkSceneArrival();
        svc.StageDestinationMap(100);
        var kind = svc.MarkSceneArrival();
        Assert.Equal(SceneTransitionKind.None, kind);
    }

    [Fact]
    public void Boundary_InstanceWithoutPendingMap_AppliesDirectly()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(200003);
        svc.MarkSceneArrival();
        svc.StageDestinationMapInstance(113515);
        Assert.Equal(113515u, svc.CurrentMapInstanceId);
    }

    [Fact]
    public void Boundary_MapChange_ResetsInstance()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(200003);
        svc.MarkSceneArrival();
        svc.StageDestinationMapInstance(113515);
        svc.StageDestinationMap(1010);
        svc.MarkSceneArrival();
        Assert.Equal(1010u, svc.CurrentMapId);
        Assert.Equal(0u, svc.CurrentMapInstanceId);
    }

    [Fact]
    public void Boundary_InstanceStagedAlongsideMap_CommitsTogether()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(200003);
        svc.StageDestinationMapInstance(515552);
        svc.MarkSceneArrival();
        Assert.Equal(200003u, svc.CurrentMapId);
        Assert.Equal(515552u, svc.CurrentMapInstanceId);
    }

    [Fact]
    public void Boundary_RedundantMapStage_DoesNotClobberInstance()
    {
        var svc = new SceneBoundaryService();
        svc.StageDestinationMap(910035);
        svc.MarkSceneArrival();
        svc.StageDestinationMapInstance(516446);
        svc.StageDestinationMap(910035);
        svc.MarkSceneArrival();
        Assert.Equal(910035u, svc.CurrentMapId);
        Assert.Equal(516446u, svc.CurrentMapInstanceId);
    }
}
