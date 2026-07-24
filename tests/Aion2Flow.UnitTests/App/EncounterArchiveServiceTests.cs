using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class EncounterArchiveServiceTests
{
    [Fact]
    public void Archive_Stores_Frozen_ScenePayload_And_Lookup()
    {
        var service = new EncounterArchiveService();
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var snapshot = owner.CreateSnapshot();
        var payload = owner.CreateArchivePayload();
        var record = service.Archive(payload, "manual", isAutomatic: false);

        Assert.NotNull(record);
        Assert.Single(service.History);
        Assert.Same(payload, record!.ScenePayload);
        Assert.Equal(bossId, record.ScenePayload.Snapshot.TargetObservation?.InstanceId);
        Assert.True(record.ScenePayload.IdentityScope.TryGetPcMetadata(playerId, out var archivedPc));
        Assert.Equal("Tester", archivedPc.Nickname);
        Assert.Equal(payload.SceneStarted.ToLocalTime(), record.ArchivedAt);
        Assert.True(service.TryGetEncounter(record.ScenePayload.Snapshot.EncounterId, out var archivedRecord));
        Assert.Same(record, archivedRecord);

        owner.Entities.ApplyNickname(playerId, "Changed");

        Assert.Equal(bossId, record.ScenePayload.Snapshot.TargetObservation?.InstanceId);
        Assert.True(record.ScenePayload.IdentityScope.TryGetPcMetadata(playerId, out archivedPc));
        Assert.Equal("Tester", archivedPc.Nickname);
    }

    [Fact]
    public void Archive_Skips_Equivalent_Immediate_Duplicates()
    {
        var service = new EncounterArchiveService();
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var snapshot = owner.CreateSnapshot();
        var payload = owner.CreateArchivePayload();

        var first = service.Archive(payload, "manual", isAutomatic: false);
        var second = service.Archive(payload, "manual", isAutomatic: false);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Single(service.History);
    }

    [Fact]
    public void Archive_Uses_Scene_Start_As_Display_Time_Not_Combat_Start()
    {
        var service = new EncounterArchiveService();
        var sceneStarted = new DateTimeOffset(2026, 5, 9, 13, 14, 15, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 5, 9)));
        var owner = CreateSceneOwner(100, 200, sceneStarted);
        var snapshot = owner.CreateSnapshot();
        var payload = owner.CreateArchivePayload();

        var record = service.Archive(payload, "manual", isAutomatic: false);

        Assert.NotNull(record);
        Assert.Equal(sceneStarted.ToLocalTime(), record!.ArchivedAt);
        Assert.NotEqual(DateTimeOffset.FromUnixTimeMilliseconds(record.ScenePayload.Snapshot.EncounterStartTime).ToLocalTime(), record.ArchivedAt);
    }

    [Fact]
    public void Archive_Trims_History_And_Removes_Lookup_For_Evicted_Record()
    {
        var service = new EncounterArchiveService();
        Guid firstEncounterId = default;

        for (var i = 0; i < EncounterArchiveService.MaxHistoryCount + 1; i++)
        {
            var owner = CreateSceneOwner(i + 1, 10_000 + i, DateTimeOffset.FromUnixTimeMilliseconds(1_000 + i).ToLocalTime());
            var snapshot = owner.CreateSnapshot();
            var payload = owner.CreateArchivePayload();
            var record = service.Archive(payload, "manual", isAutomatic: false);
            Assert.NotNull(record);

            if (i == 0)
            {
                firstEncounterId = record!.ScenePayload.Snapshot.EncounterId;
            }
        }

        Assert.Equal(EncounterArchiveService.MaxHistoryCount, service.History.Count);
        Assert.False(service.TryGetEncounter(firstEncounterId, out _));
    }

    [Fact]
    public void SceneArchivePayload_Captures_Detail_Delta_Without_Live_Store()
    {
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var snapshot = owner.CreateSnapshot();

        var payload = owner.CreateArchivePayload();
        var delta = payload.CreateDetailDelta(playerId);
        var timelineCount = 0;
        var timelineRead = payload.TimelineSegment.ReadEntries(payload.TimelineSegment.CreateCursor(), 64, entries => timelineCount = entries.Count);

        Assert.Equal(2, payload.CombatEvents.Count);
        Assert.Equal(8, timelineCount);
        Assert.Equal(8, timelineRead.Cursor.NextObservationOrdinal);
        Assert.Equal(playerId, delta.CombatantId);
        Assert.Equal(2, delta.MetricEvents.Count);
        Assert.Equal(playerId, delta.MetricEvents[0].SourceId);
        Assert.Equal(bossId, delta.MetricEvents[0].TargetId);
        Assert.Equal(750, delta.MetricEvents[0].Amount);
        Assert.Equal(751, delta.Combatant!.Value.OutgoingDamage);
        Assert.Single(delta.OutgoingPairs);
    }

    [Fact]
    public void SceneArchivePayload_Captures_Target_Detail_Delta_From_Index()
    {
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var snapshot = owner.CreateSnapshot();

        var payload = owner.CreateArchivePayload();
        var delta = payload.CreateDetailDelta(bossId);

        Assert.False(snapshot.Combatants.ContainsKey(bossId));
        Assert.Equal(2, payload.MetricEventOrdinalsByCombatant[bossId].Length);
        Assert.Equal(2, delta.MetricEvents.Count);
        Assert.Equal(bossId, delta.CombatantId);
        Assert.Equal(751, delta.Combatant!.Value.IncomingDamage);
        Assert.Empty(delta.OutgoingPairs);
        var incomingPair = Assert.Single(delta.IncomingPairs);
        Assert.Equal(new DirectedPairKey(playerId, bossId), incomingPair);
    }

    [Fact]
    public void SceneArchivePayload_Detail_Index_Selects_Combatant_Events_In_Journal_Order()
    {
        const int playerId = 100;
        const int bossId = 200;
        const int addId = 300;
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendCombat(journal, sceneId, playerId, bossId, 100, 1, 1_000);
        AppendCombat(journal, sceneId, bossId, playerId, 75, 2, 2_000);
        AppendCombat(journal, sceneId, addId, bossId, 400, 3, 3_000);
        journal.CompleteFlush(1);
        var owner = new SceneReadModelOwner(journal, Guid.NewGuid(), DateTimeOffset.Now);
        var payload = owner.CreateArchivePayload();

        var delta = payload.CreateDetailDelta(playerId);

        Assert.Equal([1L, 2L], delta.MetricEvents.Select(static e => e.Revision));
        Assert.Equal([1L, 2L], delta.MechanicEvents.Select(static e => e.Revision));
        Assert.Empty(delta.ResourceEvents);
        Assert.Equal([0L, 1L], payload.MetricEventOrdinalsByCombatant[playerId]);
        Assert.Equal([new DirectedPairKey(playerId, bossId)], delta.OutgoingPairs);
        Assert.Equal([new DirectedPairKey(bossId, playerId)], delta.IncomingPairs);
        Assert.Equal(delta.MetricEvents[^1].Revision + delta.MechanicEvents[^1].Revision, delta.Revision);
        Assert.Equal(100, delta.Combatant!.Value.OutgoingDamage);
        Assert.Equal(75, delta.Combatant.Value.IncomingDamage);
    }

    [Fact]
    public void SceneArchivePayload_TimelineSegment_IsFixed_AfterJournalAppend()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        const int playerId = 100;
        const int bossId = 200;
        AppendCombat(journal, sceneId, playerId, bossId, 100, 1, 1_000);
        AppendCombat(journal, sceneId, playerId, bossId, 200, 2, 2_000);
        journal.CompleteFlush(1);
        var owner = new SceneReadModelOwner(journal, Guid.NewGuid(), DateTimeOffset.Now);
        var snapshot = owner.CreateSnapshot();
        var payload = owner.CreateArchivePayload();
        var end = payload.TimelineSegment.CurrentEndObservationOrdinalExclusive;

        AppendCombat(journal, sceneId, playerId, bossId, 300, 3, 3_000);

        var count = 0;
        payload.TimelineSegment.ReadEntries(payload.TimelineSegment.CreateCursor(), 64, entries => count = entries.Count);

        Assert.False(payload.TimelineSegment.IsLiveGrowing);
        Assert.Equal(2, end);
        Assert.Equal(end, payload.TimelineSegment.CurrentEndObservationOrdinalExclusive);
        Assert.Equal(2, count);
    }

    [Fact]
    public void SceneArchivePayload_Is_Independent_Of_Live_Scene_Mutations()
    {
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var snapshot = owner.CreateSnapshot();

        var payload = owner.CreateArchivePayload();

        owner.Entities.ApplyNickname(playerId, "Changed");
        var appendedObservation = new CombatWireObservation
        {
            SkillCode = 11000011,
            Damage = 250,
            HitCount = 1,
            AttemptCount = 1
        };
        var appendedContribution = ResolveContribution(playerId, bossId, in appendedObservation);
        owner.Combat.ApplyCombat(playerId, bossId, in appendedObservation, in appendedContribution, 2_000);

        owner.Combat.Clear();
        var replacementObservation = new CombatWireObservation
        {
            SkillCode = 11000011,
            Damage = 500,
            HitCount = 1,
            AttemptCount = 1
        };
        var replacementContribution = ResolveContribution(playerId, bossId, in replacementObservation);
        owner.Combat.ApplyCombat(playerId, bossId, in replacementObservation, in replacementContribution, 3_000);

        var delta = payload.CreateDetailDelta(playerId);

        Assert.Equal(bossId, snapshot.TargetObservation?.InstanceId);
        Assert.True(payload.IdentityScope.TryGetPcMetadata(playerId, out var archivedPc));
        Assert.Equal("Tester", archivedPc.Nickname);
        Assert.Equal(2, payload.CombatEvents.Count);
        Assert.Equal(2, delta.MetricEvents.Count);
        Assert.Equal(750, delta.MetricEvents[0].Amount);
        Assert.Single(owner.Combat.Events);
    }

    [Fact]
    public void SceneArchivePayload_Captures_Identity_And_Boss_Focus_Facts()
    {
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var snapshot = owner.CreateSnapshot();

        var payload = owner.CreateArchivePayload();

        var bossIdentity = Assert.Single(payload.Entities, e => e.EntityId == bossId);
        Assert.Equal(2_999_997, bossIdentity.NpcCode);
        Assert.Equal(NpcKind.Boss, bossIdentity.Kind);
        Assert.True(payload.IdentityScope.TryGetNpcCode(bossId, out var scopedNpcCode));
        Assert.Equal(2_999_997, scopedNpcCode);
        var focus = Assert.Single(payload.Bosses, b => b.InstanceId == bossId);
        Assert.True(focus.HasHp);
        Assert.Equal(50_000, focus.Hp);
    }

    [Fact]
    public void SceneArchivePayload_Freezes_GlobalPcMetadata_For_CombatOnly_Entity()
    {
        const int playerId = 100;
        const int targetId = 200;
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertPcMetadata(playerId, "Global Tester");
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendCombat(journal, sceneId, playerId, targetId, 100, 1, 1_000);
        AppendCombat(journal, sceneId, playerId, targetId, 1, 2, 1_001);
        journal.CompleteFlush(1);
        var owner = new SceneReadModelOwner(journal, Guid.NewGuid(), DateTimeOffset.Now, registry);
        owner.Refresh();

        var payload = owner.CreateArchivePayload();

        Assert.True(payload.IdentityScope.TryGetPcMetadata(playerId, out var archivedPc));
        Assert.Equal("Global Tester", archivedPc.Nickname);
    }

    private static SceneReadModelOwner CreateSceneOwner(int playerId, int bossId)
        => CreateSceneOwner(playerId, bossId, DateTimeOffset.Now);

    private static SceneReadModelOwner CreateSceneOwner(int playerId, int bossId, DateTimeOffset sceneStarted)
    {
        const int bossCode = 2_999_997;
        CombatResourceTestFixture.SetResources([], new Dictionary<int, NpcDisplayEntry>
        {
            [bossCode] = new(bossCode, "Archive Boss", NpcCatalogKind.Boss, NpcHpDisplayScale.Normal)
        });

        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendState(journal, sceneId, playerId, 0, StateCodes.PlayerIdentity, 0, 0, "Tester", 1, 1_000);
        AppendState(journal, sceneId, bossId, 0, bossCode, 0, 0, null, 2, 1_001);
        AppendState(journal, sceneId, bossCode, 0, StateCodes.LocalizedNpcName, 0, 0, "Archive Boss", 3, 1_002);
        AppendState(journal, sceneId, bossId, 0, StateCodes.NpcKind, (int)NpcKind.Boss, 0, null, 4, 1_003);
        AppendEntityVital(journal, sceneId, bossId, 50_000, 100_000, 5, 1_004);
        AppendState(journal, sceneId, bossId, 0, StateCodes.NpcBattle, 1, 0, null, 6, 1_005);
        AppendCombat(journal, sceneId, playerId, bossId, 750, 7, 1_500);
        AppendCombat(journal, sceneId, playerId, bossId, 1, 8, 1_501);
        journal.CompleteFlush(1);

        var owner = new SceneReadModelOwner(journal, Guid.NewGuid(), sceneStarted);
        owner.Refresh();
        return owner;
    }

    private static void AppendState(ObservedEventJournal journal, Guid sceneId, int sourceId, int targetId, int stateCode, int value0, int value1, string? text, long ordinal, long observedAt)
    {
        var header = CreateHeader(sceneId, sourceId, targetId, ordinal, observedAt, 0);
        var observation = new StateObservation(sourceId, stateCode, value0, value1, 0, text);
        journal.Append(in header, in observation);
    }

    private static void AppendEntityVital(ObservedEventJournal journal, Guid sceneId, int entityId, long currentHp, long maxHp, long ordinal, long observedAt)
    {
        var header = CreateHeader(sceneId, entityId, 0, ordinal, observedAt, 0);
        var observation = new EntityVitalObservation(entityId, currentHp, maxHp);
        journal.Append(in header, in observation);
    }

    private static void AppendCombat(ObservedEventJournal journal, Guid sceneId, int sourceId, int targetId, int damage, long ordinal, long observedAt)
    {
        var header = CreateHeader(sceneId, sourceId, targetId, ordinal, observedAt, 0x0438);
        var observation = new CombatWireObservation
        {
            SkillCode = 11000010,
            Damage = damage,
            HitCount = 1,
            AttemptCount = 1
        };
        journal.Append(in header, in observation);
    }

    private static CombatContribution ResolveContribution(
        int sourceId,
        int targetId,
        in CombatWireObservation observation)
    {
        var occurrence = CombatOccurrenceResolution.Primary;
        Assert.True(CombatContributionResolver.TryResolve(
            sourceId,
            targetId,
            in observation,
            in occurrence,
            out var contribution));
        return contribution;
    }

    private static ObservedEventHeader CreateHeader(Guid sceneId, int sourceId, int targetId, long ordinal, long observedAt, ushort opcode)
        => new(
            sceneId,
            new TimelineStamp { OffsetTicks = observedAt * TimeSpan.TicksPerMillisecond, ObservationOrdinal = ordinal - 1, FlushId = 1 },
            sourceId,
            targetId,
            new RawPacketReference(opcode, 0, ordinal));

}
