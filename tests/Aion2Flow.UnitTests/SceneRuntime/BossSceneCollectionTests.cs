using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Playback;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class BossSceneCollectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WaitingBossSceneCollectsMetadataAndDropsCombat()
    {
        var scene = CreateBossScene();
        var sink = SceneSinkFactory.CreateForLive(scene)();
        AppendPlayer(sink, 100, "Player", 10);
        AppendNpc(sink, 200, 2_100_001, NpcKind.Monster, 20);
        AppendNpc(sink, 300, 2_100_002, NpcKind.Boss, 30);
        sink.ConfirmDestinationMap(Source(40), 910_035, false);

        AppendDamage(sink, 100, 200, 400, 50, 1);
        sink.AppendNpcHp(Source(55), 300, 100_000, 100_000);
        sink.SetNpcBattle(Source(58), 300, true);
        sink.RegisterObservation2A38(Source(60), 100, 1, 1, 1, 0, 0, 0, 0, 0, 0, 1, default, 0, 0, 0);
        sink.RegisterObservation2B38(Source(70), 100, 100, 1, 1, default, 0, 0, 0, 0);

        var snapshot = scene.CreateFrame().Snapshot;

        Assert.Equal(SceneKind.Boss, snapshot.Kind);
        Assert.Equal(BossSceneState.Waiting, scene.BossState);
        Assert.Empty(snapshot.Combatants);
        Assert.Empty(snapshot.BossFocuses);
        Assert.Equal(910_035u, snapshot.MapId);
        Assert.True(scene.Owner.MetadataRegistry.TryGetPcMetadata(100, out var player));
        Assert.Equal("Player", player.Nickname);
        Assert.True(scene.Owner.MetadataRegistry.TryGetNpcCode(300, out var bossCode));
        Assert.Equal(2_100_002, bossCode);
        Assert.DoesNotContain(ReadJournal(scene), static entry => entry.Domain is ObservedEventDomain.Combat or ObservedEventDomain.Resource or ObservedEventDomain.Aura or ObservedEventDomain.Action);
        Assert.DoesNotContain(ReadJournal(scene), static entry => entry.State?.StateCode is StateCodes.NpcBattle or StateCodes.NpcBattleToggle);
    }

    [Fact]
    public void DroppedWaitingResourceDoesNotConsumeJournalOrdinal()
    {
        var scene = CreateBossScene();
        var sink = SceneSinkFactory.CreateForLive(scene)();
        AppendPlayer(sink, 100, "Player", 10);
        AppendNpc(sink, 300, 2_100_002, NpcKind.Boss, 20);

        sink.AppendNpcHp(Source(30), 300, 100_000, 100_000);
        AppendNpc(sink, 301, 2_100_003, NpcKind.Boss, 40);

        var entries = ReadJournal(scene);
        for (var i = 0; i < entries.Length; i++)
            Assert.Equal(i, entries[i].Stamp.ObservationOrdinal);

        _ = scene.CreateFrame();
        Assert.True(scene.Owner.MetadataRegistry.TryGetNpcCode(301, out var code));
        Assert.Equal(2_100_003, code);
    }

    [Fact]
    public void FirstBossCombatStartsNewSceneAndPreservesCatalog()
    {
        var scene = CreateBossScene();
        var waitingEncounterId = scene.SessionId;
        var sink = SceneSinkFactory.CreateForLive(scene)();
        AppendPlayer(sink, 100, "Player", 10);
        AppendNpc(sink, 300, 2_100_002, NpcKind.Boss, 20);

        AppendDamage(sink, 100, 300, 500, 1_000, 1);
        AppendDamage(sink, 100, 300, 200, 1_200, 2);
        sink.CompleteBatch(2);
        var snapshot = scene.CreateFrame().Snapshot;

        Assert.NotEqual(waitingEncounterId, scene.SessionId);
        Assert.Equal(BossSceneState.Recording, scene.BossState);
        Assert.Equal(SceneKind.Boss, snapshot.Kind);
        Assert.Equal(700, snapshot.Combatants[100].DamageAmount);
        Assert.Equal([2_100_002], snapshot.BossNpcCodes.AsSpan().ToArray());
        Assert.True(scene.Owner.MetadataRegistry.TryGetPcMetadata(100, out var player));
        Assert.Equal("Player", player.Nickname);
        Assert.All(ReadJournal(scene).Where(entry => entry.Stamp.ObservationOrdinal >= scene.Owner.SceneStartObservationOrdinal), entry => Assert.Equal(scene.SessionId, entry.SceneSessionId));
    }

    [Fact]
    public void FirstTrainingDummyCombatStartsBossModeSceneWithoutClassifyingAsBoss()
    {
        var scene = CreateBossScene();
        var waitingEncounterId = scene.SessionId;
        var sink = SceneSinkFactory.CreateForLive(scene)();
        AppendPlayer(sink, 100, "Player", 10);
        AppendNpc(sink, 300, 2_500_075, NpcKind.TrainingDummy, 20);

        AppendDamage(sink, 100, 300, 500, 1_000, 1);
        AppendDamage(sink, 100, 300, 200, 1_200, 2);
        sink.CompleteBatch(2);
        var snapshot = scene.CreateFrame().Snapshot;

        Assert.NotEqual(waitingEncounterId, scene.SessionId);
        Assert.Equal(BossSceneState.Recording, scene.BossState);
        Assert.Equal(SceneKind.Boss, snapshot.Kind);
        Assert.Equal(700, snapshot.Combatants[100].DamageAmount);
        Assert.Equal([2_500_075], snapshot.BossNpcCodes.AsSpan().ToArray());
        var focus = Assert.Single(snapshot.BossFocuses.AsSpan().ToArray());
        Assert.Equal(300, focus.InstanceId);
        Assert.True(scene.Owner.Entities.TryGet(300, out var entity));
        Assert.Equal(NpcKind.TrainingDummy, entity.Kind);
    }

    [Fact]
    public void FirstCatalogResolvedCityTrainingDummyCombatStartsBossModeScene()
    {
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        CombatResourceRegistry.SetGameResources([], catalog);
        var scene = CreateBossScene();
        var waitingEncounterId = scene.SessionId;
        var sink = SceneSinkFactory.CreateForLive(scene)();
        var writer = new SceneObservationWriter(sink);
        AppendPlayer(sink, 100, "Player", 10);
        writer.ApplyNpcCatalog(Source(20), 300, 2_400_032, requireCatalogEntry: true);

        AppendDamage(sink, 100, 300, 500, 1_000, 1);
        AppendDamage(sink, 100, 300, 200, 1_200, 2);
        sink.CompleteBatch(2);
        var snapshot = scene.CreateFrame().Snapshot;

        Assert.NotEqual(waitingEncounterId, scene.SessionId);
        Assert.Equal(BossSceneState.Recording, scene.BossState);
        Assert.Equal(SceneKind.Boss, snapshot.Kind);
        Assert.Equal(700, snapshot.Combatants[100].DamageAmount);
        Assert.Equal([2_400_032], snapshot.BossNpcCodes.AsSpan().ToArray());
        var focus = Assert.Single(snapshot.BossFocuses.AsSpan().ToArray());
        Assert.Equal(300, focus.InstanceId);
        Assert.True(scene.Owner.Entities.TryGet(300, out var entity));
        Assert.Equal(NpcKind.TrainingDummy, entity.Kind);
    }

    [Fact]
    public void RecordingBossSceneIncludesMultipleBosses()
    {
        var scene = CreateBossScene();
        var sink = SceneSinkFactory.CreateForLive(scene)();
        AppendPlayer(sink, 100, "Player", 10);
        AppendNpc(sink, 300, 2_100_002, NpcKind.Boss, 20);
        AppendNpc(sink, 301, 2_100_003, NpcKind.Boss, 30);

        AppendDamage(sink, 100, 300, 500, 1_000, 1);
        AppendDamage(sink, 100, 301, 700, 2_000, 2);
        sink.CompleteBatch(2);
        var snapshot = scene.CreateFrame().Snapshot;

        Assert.Equal(BossSceneState.Recording, scene.BossState);
        Assert.Equal(1_200, snapshot.Combatants[100].DamageAmount);
        Assert.Equal([2_100_002, 2_100_003], snapshot.BossNpcCodes.AsSpan().ToArray());
        Assert.Equal(2, snapshot.BossFocuses.Count);
    }

    [Fact]
    public void EmptyBossFocusFreezesSceneAndDropsFollowingCombat()
    {
        var scene = CreateBossScene(out var timeProvider);
        var sink = SceneSinkFactory.CreateForLive(scene)();
        AppendPlayer(sink, 100, "Player", 10);
        AppendNpc(sink, 200, 2_100_001, NpcKind.Monster, 20);
        AppendNpc(sink, 300, 2_100_002, NpcKind.Boss, 30);
        AppendDamage(sink, 100, 300, 500, 1_000, 1);
        AppendDamage(sink, 100, 300, 200, 1_200, 2);
        sink.CompleteBatch(2);
        _ = scene.CreateFrame();

        sink.AppendNpcHp(Source(2_000), 300, 0, 100_000);
        var deathFrame = scene.CreateFrame();

        Assert.Equal(BossSceneState.Recording, scene.BossState);
        var deadBoss = Assert.Single(deathFrame.BossFocuses.AsSpan().ToArray());
        Assert.True(deadBoss.HasHp);
        Assert.Equal(0, deadBoss.Hp);

        timeProvider.SetUtcNow(Started.AddMilliseconds(12_001));
        _ = scene.CreateFrame();
        Assert.Equal(BossSceneState.Frozen, scene.BossState);
        var frozenCount = scene.Journal.Count;
        sink.SetNpcBattle(Source(2_500), 300, false);
        AppendDamage(sink, 100, 200, 900, 3_000, 3);
        sink.CompleteBatch(3);
        var snapshot = scene.CreateFrame().Snapshot;

        Assert.Equal(frozenCount, scene.Journal.Count);
        Assert.Equal(700, snapshot.Combatants[100].DamageAmount);
        Assert.Empty(snapshot.BossFocuses);
        Assert.Equal([2_100_002], snapshot.BossNpcCodes.AsSpan().ToArray());
    }

    [Fact]
    public void DeadBossSceneKeepsFocusUntilTimeoutAndDoesNotArchiveImmediately()
    {
        var scene = CreateBossScene(out var timeProvider);
        var sink = SceneSinkFactory.CreateForLive(scene)();
        AppendPlayer(sink, 100, "Player", 10);
        AppendNpc(sink, 300, 2_100_002, NpcKind.Boss, 20);
        AppendDamage(sink, 100, 300, 500, 1_000, 1);
        sink.CompleteBatch(1);
        _ = scene.CreateFrame();

        sink.AppendNpcHp(Source(2_000), 300, 0, 100_000);
        var deadFrame = scene.CreateFrame();
        Assert.Equal(BossSceneState.Recording, scene.BossState);
        Assert.Single(deadFrame.BossFocuses.AsSpan().ToArray());
        Assert.False(scene.TryDequeuePendingArchive(out _));

        timeProvider.SetUtcNow(Started.AddMilliseconds(12_001));
        var frozenFrame = scene.CreateFrame();

        Assert.Equal(BossSceneState.Frozen, scene.BossState);
        Assert.Empty(frozenFrame.BossFocuses);
        Assert.False(scene.TryDequeuePendingArchive(out _));
    }

    [Fact]
    public void NextBossCombatArchivesFrozenSceneBeforeStartingNewScene()
    {
        var scene = CreateBossScene(out var timeProvider);
        var sink = SceneSinkFactory.CreateForLive(scene)();
        AppendPlayer(sink, 100, "Player", 10);
        AppendNpc(sink, 300, 2_100_002, NpcKind.Boss, 20);
        AppendDamage(sink, 100, 300, 500, 1_000, 1);
        AppendDamage(sink, 100, 300, 200, 1_200, 2);
        sink.CompleteBatch(2);
        _ = scene.CreateFrame();
        sink.AppendNpcHp(Source(2_000), 300, 0, 100_000);
        timeProvider.SetUtcNow(Started.AddMilliseconds(12_001));
        _ = scene.CreateFrame();
        var frozenEnd = scene.Owner.AppliedNextObservationOrdinal;

        AppendNpc(sink, 301, 2_100_003, NpcKind.Boss, 3_000);
        AppendDamage(sink, 100, 301, 700, 4_000, 3);
        AppendDamage(sink, 100, 301, 300, 4_200, 4);
        sink.CompleteBatch(4);

        Assert.True(scene.TryDequeuePendingArchive(out var archived));
        Assert.Equal(SceneKind.Boss, archived.Snapshot.Kind);
        Assert.Equal(700, archived.Snapshot.Combatants[100].DamageAmount);
        Assert.Equal([2_100_002], archived.Payload.BossNpcCodes);
        Assert.Equal(frozenEnd, archived.Payload.TimelineSegment.EndObservationOrdinalExclusive);

        var current = scene.CreateFrame().Snapshot;
        Assert.Equal(BossSceneState.Recording, scene.BossState);
        Assert.Equal(1_000, current.Combatants[100].DamageAmount);
        Assert.Equal([2_100_003], current.BossNpcCodes.AsSpan().ToArray());
        Assert.False(scene.TryDequeuePendingArchive(out _));
    }

    [Fact]
    public async Task FrozenBossSceneArchiveCanOpenPlayback()
    {
        var scene = CreateBossScene(out var timeProvider);
        var sink = SceneSinkFactory.CreateForLive(scene)();
        AppendPlayer(sink, 100, "Player", 10);
        AppendNpc(sink, 300, 2_100_002, NpcKind.Boss, 20);
        AppendDamage(sink, 100, 300, 500, 1_000, 1);
        AppendDamage(sink, 100, 300, 200, 1_800, 2);
        sink.CompleteBatch(2);
        _ = scene.CreateFrame();
        sink.AppendNpcHp(Source(2_000), 300, 0, 100_000);
        timeProvider.SetUtcNow(Started.AddMilliseconds(12_001));
        _ = scene.CreateFrame();
        var archive = scene.CreateArchiveCapture();

        await using var controller = new ScenePlaybackController(
            new ArchivedScenePlaybackSource(new()
            {
                EncounterId = archive.Snapshot.EncounterId,
                Snapshot = archive.Snapshot,
                ScenePayload = archive.Payload
            }),
            new ManualTickSourceFactory(),
            TimeSpan.FromMilliseconds(33));

        var frame = await controller.SeekAsync(controller.DurationMilliseconds, TestContext.Current.CancellationToken);

        Assert.Equal(SceneKind.Boss, frame.Snapshot.Kind);
        Assert.Equal(700, frame.CombatTotals.TotalDamage);
        Assert.Equal(frame.TimeRange.DurationMilliseconds, frame.PositionMilliseconds);
        Assert.Equal(BossSceneState.Frozen, scene.BossState);
    }

    private static SceneLiveReadModel CreateBossScene() => CreateBossScene(out _);

    private static SceneLiveReadModel CreateBossScene(out MutableTimeProvider timeProvider)
    {
        timeProvider = new MutableTimeProvider(Started);
        var scene = new SceneLiveReadModel(Started, timeProvider);
        scene.ChangeKind(SceneKind.Boss, Started, archiveCurrent: false);
        return scene;
    }

    private static void AppendPlayer(IRuntimeObservationSink sink, int entityId, string name, long offsetMilliseconds)
    {
        sink.AppendNickname(Source(offsetMilliseconds), entityId, name, characterClass: CharacterClass.Gladiator);
    }

    private static void AppendNpc(IRuntimeObservationSink sink, int instanceId, int npcCode, NpcKind kind, long offsetMilliseconds)
    {
        var source = Source(offsetMilliseconds);
        sink.AppendNpcCode(in source, instanceId, npcCode);
        sink.AppendNpcKind(in source, instanceId, kind);
    }

    private static void AppendDamage(IRuntimeObservationSink sink, int sourceId, int targetId, int damage, long offsetMilliseconds, long batchOrdinal)
    {
        var source = Source(offsetMilliseconds, batchOrdinal);
        var observation = new CombatObservation
        {
            SkillCode = 11_000_010,
            Damage = damage,
            HitCount = 1,
            AttemptCount = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };
        sink.AppendCombatObservation(in source, sourceId, targetId, in observation);
    }

    private static PacketObservationSource Source(long offsetMilliseconds, long batchOrdinal = 0) =>
        new(Started.ToUnixTimeMilliseconds() + offsetMilliseconds, 0, batchOrdinal, 0, 0, offsetMilliseconds, default);

    private static ObservedEventEnvelope[] ReadJournal(SceneLiveReadModel scene)
    {
        var entries = new ObservedEventEnvelope[scene.Journal.Count];
        var result = scene.Journal.CopyEntries(scene.Journal.CreateCursor(scene.Journal.FirstObservationOrdinal), entries);
        return entries.AsSpan(0, result.Count).ToArray();
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void SetUtcNow(DateTimeOffset now) => _now = now;
    }

    private sealed class ManualTickSourceFactory : IScenePlaybackTickSourceFactory
    {
        public IScenePlaybackTickSource Create(TimeSpan interval) => new ManualTickSource();
    }

    private sealed class ManualTickSource : IScenePlaybackTickSource
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask<ScenePlaybackTick> WaitForNextTickAsync(CancellationToken cancellationToken)
            => new(new ScenePlaybackTick(TimeSpan.FromMilliseconds(33)));
    }
}
