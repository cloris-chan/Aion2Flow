using System.Reflection;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Runtime;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class SceneRuntimePlaybackTests
{
    [Fact]
    public void RuntimeCheckpoint_Restore_Replays_To_Same_Snapshot()
    {
        var journal = CreateJournal();
        var owner = new SceneReadModelOwner(journal, Guid.NewGuid(), DateTimeOffset.Now);
        var baseline = owner.CreateSnapshot();
        var checkpoint = Assert.Single(owner.RuntimeCheckpoints, static c => c.Anchor.LastObservationOrdinal == -1);

        var restored = SceneReadModelOwner.RestoreFromCheckpoint(journal, checkpoint);
        var replayed = restored.CreateSnapshotAt(7_000);

        AssertSnapshotTotalsEqual(baseline, replayed, playerId: 100);
    }

    [Fact]
    public void RuntimeCheckpoint_Restore_From_Later_CombatState_Replays_To_Same_Snapshot()
    {
        var journal = CreateJournal();
        var owner = new SceneReadModelOwner(journal, Guid.NewGuid(), DateTimeOffset.Now);
        var baseline = owner.CreateSnapshot();
        var checkpoint = owner.RuntimeCheckpoints[^1];

        Assert.True(checkpoint.Anchor.LastObservationOrdinal >= 0);
        Assert.NotEmpty(checkpoint.State.Combat.Pairs);
        Assert.NotEmpty(checkpoint.State.Combat.Combatants);

        var restored = SceneReadModelOwner.RestoreFromCheckpoint(journal, checkpoint);
        var replayed = restored.CreateSnapshotAt(7_000);

        AssertSnapshotTotalsEqual(baseline, replayed, playerId: 100);
    }

    [Fact]
    public void RuntimeCheckpoint_Restore_Reads_Tail_Journal_Only()
    {
        var journal = CreateJournal();
        var owner = new SceneReadModelOwner(journal, Guid.NewGuid(), DateTimeOffset.Now);
        var baseline = owner.CreateSnapshot();
        var checkpoint = owner.RuntimeCheckpoints[^1];
        var tail = journal.ToArray().Where(e => e.Stamp.ObservationOrdinal > checkpoint.Anchor.LastObservationOrdinal).ToArray();
        var tailJournal = ObservedEventJournal.FromEntries(tail);

        var restored = SceneReadModelOwner.RestoreFromCheckpoint(tailJournal, checkpoint);
        var replayed = restored.CreateSnapshotAt(7_000);

        AssertSnapshotTotalsEqual(baseline, replayed, playerId: 100);
    }

    [Fact]
    public void RuntimeCheckpoint_Restore_Preserves_Metadata_Entity_And_BossFocus_State()
    {
        var journal = CreateJournal();
        var owner = new SceneReadModelOwner(journal, Guid.NewGuid(), DateTimeOffset.Now);
        _ = owner.CreateSnapshot();
        var checkpoint = owner.RuntimeCheckpoints[^1];
        var restored = SceneReadModelOwner.RestoreFromCheckpoint(ObservedEventJournal.FromEntries([]), checkpoint);

        restored.ReadLocked((entities, _, metadata, combat) =>
        {
            Assert.True(entities.TryGet(100, out var player));
            Assert.Equal("Tester", player.Nickname);
            Assert.True(metadata.TryGetPcMetadata(100, out var pc));
            Assert.Equal("Tester", pc.Nickname);
            Assert.True(metadata.TryGetNpcCode(200, out var npcCode));
            Assert.Equal(2_999_997, npcCode);
            Assert.True(combat.Combatants.ContainsKey(100));
            return 0;
        });

        var bosses = restored.BossFocus.GetObservedBosses(7_000, long.MaxValue);
        Assert.Contains(bosses, boss => boss.InstanceId == 200 && boss.HasHp);
    }

    [Fact]
    public void RuntimeCheckpoint_Restore_Preserves_Canonicalizer_Pending_State()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        const int playerId = 4086;
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendPeriodicRecovery(journal, sceneId, playerId, damage: 7634, mode: 1, ordinal: 0, observedAt: 1_000, batchOrdinal: 10);
        AppendState(journal, sceneId, playerId, 0, StateCodes.PlayerIdentity, 0, 0, "Tester", 1, 6_000);
        AppendPeriodicRecovery(journal, sceneId, playerId, damage: 7634, mode: 2, ordinal: 2, observedAt: 7_000, batchOrdinal: 11);
        journal.CompleteBatch(11);

        var full = new SceneReadModelOwner(journal, Guid.NewGuid(), DateTimeOffset.Now);
        _ = full.CreateSnapshot();
        var checkpoint = Assert.Single(full.RuntimeCheckpoints, static c => c.Anchor.LastObservationOrdinal == 1);
        var tailJournal = ObservedEventJournal.FromEntries(journal.ToArray().Where(e => e.Stamp.ObservationOrdinal > checkpoint.Anchor.LastObservationOrdinal).ToArray());
        var restored = SceneReadModelOwner.RestoreFromCheckpoint(tailJournal, checkpoint);
        _ = restored.CreateSnapshotAt(7_000);

        var expectedHealing = full.ReadLocked((_, _, _, combat) => combat.Combatants[playerId].OutgoingHealing);
        var actualHealing = restored.ReadLocked((_, _, _, combat) => combat.Combatants[playerId].OutgoingHealing);
        Assert.Equal(7634, expectedHealing);
        Assert.Equal(expectedHealing, actualHealing);
    }

    [Fact]
    public void ArchivePayload_Stores_Timeline_And_Checkpoints_Not_Ui_Snapshot()
    {
        var owner = new SceneReadModelOwner(CreateJournal(), Guid.NewGuid(), DateTimeOffset.Now);
        var payload = owner.CreateArchivePayload();

        Assert.Null(typeof(SceneArchivePayload).GetProperty("Snapshot"));
        Assert.NotEmpty(payload.Timeline);
        Assert.NotEmpty(payload.Checkpoints);
    }

    [Fact]
    public void RuntimeCheckpoint_DoesNotStore_JournalStoresOrEventLogs()
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Assert.DoesNotContain(typeof(SceneRuntimeCheckpoint).GetFields(flags), static field => IsForbiddenCheckpointStorageType(field.FieldType));
        Assert.DoesNotContain(typeof(SceneRuntimeCheckpoint).GetProperties(flags), static property => IsForbiddenCheckpointStorageType(property.PropertyType));
        Assert.Null(typeof(SceneRuntimeCheckpoint).GetProperty("Cursor"));
        Assert.Null(typeof(SceneRuntimeCheckpoint).GetProperty("NextCursor"));
        Assert.Null(typeof(SceneRuntimeCheckpoint).GetProperty("Snapshot"));

        static bool IsForbiddenCheckpointStorageType(Type type) =>
            IsForbiddenDirectType(type) || type.IsArray && type.GetElementType() is { } elementType && IsForbiddenDirectType(elementType);

        static bool IsForbiddenDirectType(Type type) =>
            type == typeof(ObservedEventJournal) ||
            type == typeof(ObservedEventEnvelope) ||
            type == typeof(IReadOnlyList<ObservedEventEnvelope>) ||
            type == typeof(EntityStore) ||
            type == typeof(SceneBoundaryStore) ||
            type == typeof(RuntimeMetadataRegistry) ||
            type == typeof(CombatStore) ||
            type == typeof(DomainEventApplier) ||
            type == typeof(SceneCombatSnapshot) ||
            type == typeof(CombatEventRecord) ||
            type == typeof(CombatSnapshotChange);
    }

    [Fact]
    public void ArchivePlayback_Seek_Generates_Partial_And_Final_Snapshots()
    {
        var owner = new SceneReadModelOwner(CreateJournal(), Guid.NewGuid(), DateTimeOffset.Now);
        var payload = owner.CreateArchivePayload();
        var playback = SceneRuntimePlayback.FromArchive(payload);

        var partial = playback.CreateSnapshotAt(2_000);
        var final = playback.CreateEndSnapshot();

        Assert.True(partial.Combatants.TryGetValue(100, out var partialMetrics));
        Assert.True(final.Combatants.TryGetValue(100, out var finalMetrics));
        Assert.Equal(1_000, partialMetrics.DamageAmount);
        Assert.Equal(1_250, finalMetrics.DamageAmount);
    }

    [Fact]
    public void RuntimeCheckpoints_Retain_Full_FiveSecond_Cadence()
    {
        CombatResourceRegistry.SetGameResources([], new Dictionary<int, NpcCatalogEntry>());

        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendState(journal, sceneId, 100, 0, StateCodes.PlayerIdentity, 0, 0, "Tester", 0, 1_000);
        for (var i = 1; i <= 20; i++)
            AppendCombat(journal, sceneId, 100, 200, 100, i, 1_000 + i * 31_000L);
        journal.CompleteBatch(1);

        var owner = new SceneReadModelOwner(journal, Guid.NewGuid(), DateTimeOffset.Now);
        _ = owner.CreateSnapshot();

        Assert.Equal(21, owner.RuntimeCheckpoints.Count);
        Assert.Contains(owner.RuntimeCheckpoints, static checkpoint => checkpoint.Anchor.LastObservationOrdinal == -1);

        var payload = owner.CreateArchivePayload();
        Assert.Equal(22, payload.Checkpoints.Count);
    }

    [Fact]
    public void PlaybackController_Supports_Seek_Advance_And_Speed()
    {
        var owner = new SceneReadModelOwner(CreateJournal(), Guid.NewGuid(), DateTimeOffset.Now);
        var controller = new ScenePlaybackController(SceneRuntimePlayback.FromArchive(owner.CreateArchivePayload()))
        {
            Speed = 2d
        };

        controller.SeekRatio(0);
        controller.Play();
        controller.Advance(500);

        Assert.True(controller.IsPlaying);
        Assert.Equal(1_000, controller.PositionMilliseconds);

        controller.Seek(controller.DurationMilliseconds);
        controller.Advance(1);

        Assert.False(controller.IsPlaying);
        var snapshot = controller.CreateSnapshot();
        Assert.True(snapshot.Combatants.TryGetValue(100, out var metrics));
        Assert.Equal(1_250, metrics.DamageAmount);
    }

    private static ObservedEventJournal CreateJournal()
    {
        CombatResourceRegistry.SetGameResources(
            [
                new Skill(11000010, "Strike", SkillCategory.Gladiator, SkillSourceType.PcSkill, "pc", null)
            ],
            new Dictionary<int, NpcCatalogEntry>
            {
                [2_999_997] = new(2_999_997, "Playback Boss", NpcCatalogKind.Boss)
            });

        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendState(journal, sceneId, 100, 0, StateCodes.PlayerIdentity, 0, 0, "Tester", 0, 1_000);
        AppendState(journal, sceneId, 200, 0, 2_999_997, 0, 0, null, 1, 1_001);
        AppendState(journal, sceneId, 200, 0, StateCodes.NpcKind, (int)NpcKind.Boss, 0, null, 2, 1_002);
        AppendResource(journal, sceneId, 200, 50_000, 100_000, 3, 1_003);
        AppendState(journal, sceneId, 200, 0, StateCodes.NpcBattle, 1, 0, null, 4, 1_004);
        AppendCombat(journal, sceneId, 100, 200, 750, 5, 1_500);
        AppendCombat(journal, sceneId, 100, 200, 250, 6, 2_000);
        AppendCombat(journal, sceneId, 100, 200, 250, 7, 7_000);
        journal.CompleteBatch(1);
        return journal;
    }

    private static void AssertSnapshotTotalsEqual(SceneCombatSnapshot expected, SceneCombatSnapshot actual, int playerId)
    {
        Assert.True(expected.Combatants.TryGetValue(playerId, out var expectedMetrics));
        Assert.True(actual.Combatants.TryGetValue(playerId, out var actualMetrics));
        Assert.Equal(expectedMetrics.DamageAmount, actualMetrics.DamageAmount);
        Assert.Equal(expectedMetrics.CharacterClass, actualMetrics.CharacterClass);
        Assert.Equal(expected.EncounterStartTime, actual.EncounterStartTime);
        Assert.Equal(expected.EncounterEndTime, actual.EncounterEndTime);
    }

    private static void AppendState(ObservedEventJournal journal, Guid sceneId, int sourceId, int targetId, int stateCode, int value0, int value1, string? text, long ordinal, long observedAt)
    {
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = ordinal, FrameOrdinal = ordinal + 1, BatchOrdinal = 1 },
            Domain = ObservedEventDomain.State,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference(0, 0, ordinal + 1, observedAt),
            State = new StateObservation(sourceId, stateCode, value0, value1, 0, text)
        });
    }

    private static void AppendResource(ObservedEventJournal journal, Guid sceneId, int entityId, long current, long maximum, long ordinal, long observedAt)
    {
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = ordinal, FrameOrdinal = ordinal + 1, BatchOrdinal = 1 },
            Domain = ObservedEventDomain.Resource,
            SourceEntityId = entityId,
            TargetEntityId = 0,
            Raw = new RawPacketReference(0, 0, ordinal + 1, observedAt),
            Resource = new ResourceObservation(entityId, current, maximum, null, 0)
        });
    }

    private static void AppendCombat(ObservedEventJournal journal, Guid sceneId, int sourceId, int targetId, int damage, long ordinal, long observedAt)
    {
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = ordinal, FrameOrdinal = ordinal + 1, BatchOrdinal = 1 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference(0x0438, 0, ordinal + 1, observedAt),
            Combat = new CombatObservation
            {
                SkillCode = 11000010,
                OriginalSkillCode = 11000010,
                Damage = damage,
                HitCount = 1,
                AttemptCount = 1,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });
    }

    private static void AppendPeriodicRecovery(ObservedEventJournal journal, Guid sceneId, int playerId, int damage, int mode, long ordinal, long observedAt, long batchOrdinal)
    {
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = ordinal, FrameOrdinal = batchOrdinal, BatchOrdinal = batchOrdinal },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = playerId,
            TargetEntityId = playerId,
            Raw = new RawPacketReference(0x0438, 0, ordinal + 1, observedAt),
            Combat = new CombatObservation
            {
                SkillCode = 190000131,
                OriginalSkillCode = 190000131,
                Damage = damage,
                HitCount = 1,
                AttemptCount = 1,
                PeriodicRelation = PeriodicEffectRelation.Self,
                PeriodicMode = mode,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });
    }
}
