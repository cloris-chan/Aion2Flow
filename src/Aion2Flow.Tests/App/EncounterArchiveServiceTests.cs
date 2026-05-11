using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.Tests.SceneRuntime;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class EncounterArchiveServiceTests
{
    [Fact]
    public void Archive_Stores_DeepCloned_ScenePayload_And_Lookup()
    {
        var service = new EncounterArchiveService();
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var payload = owner.CreateArchivePayload(owner.CreateSnapshot());
        var record = service.Archive(payload, "manual", isAutomatic: false);

        Assert.NotNull(record);
        Assert.Single(service.History);
        Assert.NotSame(payload, record!.ScenePayload);
        Assert.Equal("Archive Boss", record.Snapshot.TargetName);
        Assert.Equal("Tester", record.ScenePayload.DisplayNames[playerId]);
        Assert.Equal(payload.SceneStarted.ToLocalTime(), record.ArchivedAt);
        Assert.True(service.TryGetEncounter(record.EncounterId, out var archivedRecord));
        Assert.Same(record, archivedRecord);

        owner.Entities.ApplyNickname(playerId, "Changed");

        Assert.Equal("Archive Boss", record.Snapshot.TargetName);
        Assert.Equal("Tester", record.ScenePayload.DisplayNames[playerId]);
    }

    [Fact]
    public void Archive_Skips_Equivalent_Immediate_Duplicates()
    {
        var service = new EncounterArchiveService();
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var payload = owner.CreateArchivePayload(owner.CreateSnapshot());

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
        var payload = owner.CreateArchivePayload(owner.CreateSnapshot());

        var record = service.Archive(payload, "manual", isAutomatic: false);

        Assert.NotNull(record);
        Assert.Equal(sceneStarted.ToLocalTime(), record!.ArchivedAt);
        Assert.NotEqual(DateTimeOffset.FromUnixTimeMilliseconds(record.Snapshot.EncounterStartTime).ToLocalTime(), record.ArchivedAt);
    }

    [Fact]
    public void Archive_Trims_History_And_Removes_Lookup_For_Evicted_Record()
    {
        var service = new EncounterArchiveService();
        Guid firstEncounterId = default;

        for (var i = 0; i < 101; i++)
        {
            var snapshot = SceneSnapshotTestFactory.Create(
                encounterId: Guid.NewGuid(),
                targetName: $"Boss {i}",
                encounterStartTime: 1_000 + i,
                encounterEndTime: 11_000 + (i * 2),
                encounterTime: 10_000 + i,
                combatants:
                [
                    SceneSnapshotTestFactory.Combatant(
                        i + 1,
                        new SceneCombatantMetrics($"Tester {i}")
                        {
                            DamageContribution = 1,
                            DamagePerSecond = 1_000 + i
                        })
                ]);

            var payload = new SceneArchivePayload
            {
                Snapshot = snapshot,
                SceneStarted = DateTimeOffset.FromUnixTimeMilliseconds(snapshot.EncounterStartTime).ToLocalTime()
            };
            var record = service.Archive(payload, "manual", isAutomatic: false);
            Assert.NotNull(record);

            if (i == 0)
            {
                firstEncounterId = record!.EncounterId;
            }
        }

        Assert.Equal(100, service.History.Count);
        Assert.False(service.TryGetEncounter(firstEncounterId, out _));
    }

    [Fact]
    public void SceneArchivePayload_Captures_Detail_Delta_Without_Live_Store()
    {
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var snapshot = owner.CreateSnapshot();

        var payload = owner.CreateArchivePayload(snapshot);
        var delta = payload.CreateDetailDelta(playerId);

        Assert.Equal(snapshot.EncounterId, payload.Snapshot.EncounterId);
        Assert.Equal(2, payload.Events.Count);
        Assert.Equal(playerId, delta.CombatantId);
        Assert.Equal(2, delta.Events.Count);
        Assert.Equal(playerId, delta.Events[0].SourceId);
        Assert.Equal(bossId, delta.Events[0].TargetId);
        Assert.Equal(750, delta.Events[0].Amount);
        Assert.Equal("Tester", delta.DisplayNames[playerId]);
        Assert.Equal("Archive Boss", delta.DisplayNames[bossId]);
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

        var payload = owner.CreateArchivePayload(snapshot);
        var delta = payload.CreateDetailDelta(bossId);

        Assert.False(snapshot.Combatants.ContainsKey(bossId));
        Assert.Equal(2, payload.EventIndicesByCombatant[bossId].Length);
        Assert.Equal(2, delta.Events.Count);
        Assert.Equal(bossId, delta.CombatantId);
        Assert.Equal(751, delta.Combatant!.Value.IncomingDamage);
        Assert.Empty(delta.OutgoingPairs);
        var incomingPair = Assert.Single(delta.IncomingPairs);
        Assert.Equal(new DirectedPairKey(playerId, bossId), incomingPair);
    }

    [Fact]
    public void SceneArchivePayload_Detail_Index_Selects_Combatant_Events_And_Survives_Clone()
    {
        const int playerId = 100;
        const int bossId = 200;
        const int addId = 300;
        var payload = new SceneArchivePayload
        {
            Events =
            [
                CreateArchiveEvent(addId, bossId, 400, 3, 3_000),
                CreateArchiveEvent(playerId, bossId, 100, 1, 1_000),
                CreateArchiveEvent(bossId, playerId, 75, 2, 2_000)
            ],
            Pairs =
            [
                CreatePair(playerId, bossId, 100, 1_000, 1_000, 1),
                CreatePair(bossId, playerId, 75, 2_000, 2_000, 2),
                CreatePair(addId, bossId, 400, 3_000, 3_000, 3)
            ],
            Combatants =
            [
                new CombatantSummary { CombatantId = playerId, OutgoingDamage = 100, IncomingDamage = 75, Revision = 2 },
                new CombatantSummary { CombatantId = bossId, OutgoingDamage = 75, IncomingDamage = 500, Revision = 3 },
                new CombatantSummary { CombatantId = addId, OutgoingDamage = 400, Revision = 3 }
            ],
            DisplayNames = new Dictionary<int, string>
            {
                [playerId] = "Tester",
                [bossId] = "Archive Boss",
                [addId] = "Add"
            }
        };

        var delta = payload.CreateDetailDelta(playerId);
        var clone = payload.DeepClone();
        var cloneDelta = clone.CreateDetailDelta(playerId);

        Assert.Equal([1L, 2L], delta.Events.Select(static e => e.Revision));
        Assert.Equal([1, 2], payload.EventIndicesByCombatant[playerId]);
        Assert.Equal([new DirectedPairKey(playerId, bossId)], delta.OutgoingPairs);
        Assert.Equal([new DirectedPairKey(bossId, playerId)], delta.IncomingPairs);
        Assert.Equal(2, delta.Revision);
        Assert.Equal(100, delta.Combatant!.Value.OutgoingDamage);
        Assert.Equal(75, delta.Combatant.Value.IncomingDamage);
        Assert.Equal([1L, 2L], cloneDelta.Events.Select(static e => e.Revision));
        Assert.Equal([1, 2], clone.EventIndicesByCombatant[playerId]);
        Assert.Equal(payload.Events[1], clone.Events[1]);
    }

    [Fact]
    public void SceneArchivePayload_Is_Independent_Of_Live_Scene_Mutations()
    {
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var snapshot = owner.CreateSnapshot();

        var payload = owner.CreateArchivePayload(snapshot);

        owner.Entities.ApplyNickname(playerId, "Changed");
        owner.Combat.ApplyCombat(playerId, bossId, new CombatObservation
        {
            SkillCode = 11000011,
            Damage = 250,
            HitCount = 1,
            AttemptCount = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        }, 2_000);

        var delta = payload.CreateDetailDelta(playerId);

        Assert.Equal("Archive Boss", payload.Snapshot.TargetName);
        Assert.Equal("Tester", payload.DisplayNames[playerId]);
        Assert.Equal(2, payload.Events.Count);
        Assert.Equal(2, delta.Events.Count);
        Assert.Equal(750, delta.Events[0].Amount);
    }

    [Fact]
    public void SceneArchivePayload_Captures_Identity_And_Boss_Focus_Facts()
    {
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var snapshot = owner.CreateSnapshot();

        var payload = owner.CreateArchivePayload(snapshot);

        var bossIdentity = Assert.Single(payload.Entities, e => e.EntityId == bossId);
        Assert.Equal(2_999_997, bossIdentity.NpcCode);
        Assert.Equal(NpcKind.Boss, bossIdentity.Kind);
        Assert.True(payload.NpcNamesByCode.ContainsKey(2_999_997));
        var focus = Assert.Single(payload.Bosses, b => b.InstanceId == bossId);
        Assert.True(focus.HasHp);
        Assert.Equal(50_000, focus.Hp);
    }

    [Fact]
    public void Archive_Stores_ScenePayload()
    {
        const int playerId = 100;
        const int bossId = 200;
        var service = new EncounterArchiveService();
        var owner = CreateSceneOwner(playerId, bossId);
        var payload = owner.CreateArchivePayload(owner.CreateSnapshot());

        var record = service.Archive(payload, "manual", isAutomatic: false);

        Assert.NotNull(record);
        Assert.NotSame(payload, record!.ScenePayload);
        Assert.Equal(payload.Events.Count, record.ScenePayload.Events.Count);
        Assert.Equal(payload.Snapshot.EncounterId, record.EncounterId);
    }

    private static SceneReadModelOwner CreateSceneOwner(int playerId, int bossId)
        => CreateSceneOwner(playerId, bossId, DateTimeOffset.Now);

    private static SceneReadModelOwner CreateSceneOwner(int playerId, int bossId, DateTimeOffset sceneStarted)
    {
        const int bossCode = 2_999_997;
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendState(journal, sceneId, playerId, 0, StateCodes.PlayerIdentity, 0, 0, "Tester", 1, 1_000);
        AppendState(journal, sceneId, bossId, 0, bossCode, 0, 0, null, 2, 1_001);
        AppendState(journal, sceneId, bossCode, 0, StateCodes.NpcName, 0, 0, "Archive Boss", 3, 1_002);
        AppendState(journal, sceneId, bossId, 0, StateCodes.NpcKind, (int)NpcKind.Boss, 0, null, 4, 1_003);
        AppendResource(journal, sceneId, bossId, 50_000, 100_000, 5, 1_004);
        AppendState(journal, sceneId, bossId, 0, StateCodes.NpcBattle, 1, 0, null, 6, 1_005);
        AppendCombat(journal, sceneId, playerId, bossId, 750, 7, 1_500);
        AppendCombat(journal, sceneId, playerId, bossId, 1, 8, 1_501);
        journal.CompleteBatch(1);

        var owner = new SceneReadModelOwner(journal, Guid.NewGuid(), sceneStarted);
        owner.Refresh();
        return owner;
    }

    private static void AppendState(ObservedEventJournal journal, Guid sceneId, int sourceId, int targetId, int stateCode, int value0, int value1, string? text, long ordinal, long observedAt)
    {
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = ordinal - 1, FrameOrdinal = ordinal, BatchOrdinal = 1 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference(0, 0, ordinal, observedAt),
            State = new StateObservation(sourceId, stateCode, value0, value1, 0, text)
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

    private static SceneArchiveCombatEvent CreateArchiveEvent(int sourceId, int targetId, int damage, long revision, long timestamp) => new()
    {
        SourceId = sourceId,
        TargetId = targetId,
        Revision = revision,
        ObservedAtMilliseconds = timestamp,
        Observation = new CombatObservation
        {
            SkillCode = 11000010,
            Damage = damage,
            HitCount = 1,
            AttemptCount = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        }
    };

    private static DirectedPairSnapshot CreatePair(int sourceId, int targetId, long damage, long firstObserved, long lastObserved, long revision) => new()
    {
        Key = new DirectedPairKey(sourceId, targetId),
        TotalDamage = damage,
        HitCount = 1,
        AttemptCount = 1,
        FirstObserved = firstObserved,
        LastObserved = lastObserved,
        Revision = revision
    };
}
