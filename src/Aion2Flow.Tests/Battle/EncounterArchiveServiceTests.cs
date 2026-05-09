using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;

namespace Cloris.Aion2Flow.Tests.Battle;

public sealed class EncounterArchiveServiceTests
{
    [Fact]
    public void Archive_Stores_DeepCloned_ScenePayload_And_Lookup()
    {
        var service = new EncounterArchiveService();
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var payload = SceneArchivePayload.Create(owner, owner.CreateSnapshot());
        var record = service.Archive(payload, "manual", isAutomatic: false);

        Assert.NotNull(record);
        Assert.Single(service.History);
        Assert.NotSame(payload, record!.ScenePayload);
        Assert.Equal("Archive Boss", record.Snapshot.TargetName);
        Assert.Equal("Tester", record.ScenePayload.DisplayNames[playerId]);
        Assert.Equal(payload.SceneStarted.ToLocalTime(), record.ArchivedAt);
        Assert.True(service.TryGetEncounter(record.EncounterId, out var archivedRecord));
        Assert.Same(record, archivedRecord);

        payload.Snapshot.TargetName = "Changed";
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
        var payload = SceneArchivePayload.Create(owner, owner.CreateSnapshot());

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
        var payload = SceneArchivePayload.Create(owner, owner.CreateSnapshot());

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
            var snapshot = new SceneCombatSnapshot
            {
                EncounterId = Guid.NewGuid(),
                TargetName = $"Boss {i}",
                EncounterStartTime = 1_000 + i,
                EncounterEndTime = 11_000 + (i * 2),
                EncounterTime = 10_000 + i
            };
            snapshot.Combatants[i + 1] = new SceneCombatantMetrics($"Tester {i}")
            {
                DamageContribution = 1,
                DamagePerSecond = 1_000 + i
            };

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

        var payload = SceneArchivePayload.Create(owner, snapshot);
        var delta = payload.CreateDetailDelta(playerId);

        Assert.Equal(snapshot.EncounterId, payload.Snapshot.EncounterId);
        Assert.Equal(2, payload.Events.Count);
        Assert.Equal(playerId, delta.CombatantId);
        Assert.Equal(2, delta.Events.Count);
        Assert.Equal(playerId, delta.Events[0].SourceId);
        Assert.Equal(bossId, delta.Events[0].TargetId);
        Assert.Equal(750, delta.Events[0].Packet.Damage);
        Assert.Equal("Tester", delta.DisplayNames[playerId]);
        Assert.Equal("Archive Boss", delta.DisplayNames[bossId]);
        Assert.Equal(751, delta.Combatant!.OutgoingDamage);
        Assert.Single(delta.OutgoingPairs);
    }

    [Fact]
    public void SceneArchivePayload_Is_Independent_Of_Live_Scene_Mutations()
    {
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var snapshot = owner.CreateSnapshot();

        var payload = SceneArchivePayload.Create(owner, snapshot);

        snapshot.TargetName = "Changed";
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
        Assert.Equal(750, delta.Events[0].Packet.Damage);
    }

    [Fact]
    public void SceneArchivePayload_Captures_Identity_And_Boss_Focus_Facts()
    {
        const int playerId = 100;
        const int bossId = 200;
        var owner = CreateSceneOwner(playerId, bossId);
        var snapshot = owner.CreateSnapshot();

        var payload = SceneArchivePayload.Create(owner, snapshot);

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
        var payload = SceneArchivePayload.Create(owner, owner.CreateSnapshot());

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
}
