using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Runtime;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class BossFocusStoreTests
{
    [Fact]
    public void ScenePath_TracksBossFocusWithBattleAndHp()
    {
        var h = new Harness();

        h.Kind(3518, NpcKind.Boss);
        h.Battle(3518, true, 900);
        h.Hp(3518, 156_500, 1_000);
        h.Hp(3518, 167_000, 1_100);
        h.Hp(3518, 152_000, 1_200);

        Assert.True(h.Focus.TryGetObservedBoss(1_400, 2_000, out var boss));
        Assert.True(boss.HasHp);
        Assert.Equal(3518, boss.InstanceId);
        Assert.Equal(152_000, boss.Hp);
        Assert.Equal(167_000, boss.MaxHp);
        Assert.Equal(1_200, boss.LastObservedAtMilliseconds);

        h.Battle(3518, false, 1_300);

        Assert.True(h.Focus.TryGetObservedBoss(3_300, 2_000, out var stopped));
        Assert.Equal(1_300, stopped.LastObservedAtMilliseconds);
        Assert.False(h.Focus.TryGetObservedBoss(3_301, 2_000, out _));
    }

    [Fact]
    public void ScenePath_TracksTrainingDummyFocusWithBattleAndHp()
    {
        var h = new Harness();

        h.Kind(3518, NpcKind.TrainingDummy);
        h.Battle(3518, true, 900);
        h.Hp(3518, 156_500, 1_000);

        Assert.True(h.Focus.TryGetObservedBoss(1_100, 2_000, out var focus));
        Assert.True(focus.HasHp);
        Assert.Equal(3518, focus.InstanceId);
        Assert.Equal(156_500, focus.Hp);
        Assert.Equal(156_500, focus.MaxHp);
    }

    [Fact]
    public void ScenePath_IgnoresSpawnOnlyBossUntilBattleActive()
    {
        var h = new Harness();

        h.Kind(3518, NpcKind.Boss);
        h.Hp(3518, 49_200, 900, 49_200);

        Assert.False(h.Focus.TryGetObservedBoss(1_000, 2_000, out _));

        h.Battle(3518, true, 1_100);

        Assert.True(h.Focus.TryGetObservedBoss(1_200, 2_000, out var boss));
        Assert.True(boss.HasHp);
        Assert.Equal(49_200, boss.Hp);
        Assert.Equal(49_200, boss.MaxHp);
    }

    [Fact]
    public void ScenePath_CanShowBossBeforeHpWhenBattleActive()
    {
        var h = new Harness();

        h.Kind(3518, NpcKind.Boss);
        h.Battle(3518, true, 900);

        Assert.True(h.Focus.TryGetObservedBoss(950, 2_000, out var unknownHpBoss));
        Assert.False(unknownHpBoss.HasHp);
        Assert.Equal(3518, unknownHpBoss.InstanceId);

        h.Hp(3518, 157_000, 1_000);

        Assert.True(h.Focus.TryGetObservedBoss(1_100, 2_000, out var boss));
        Assert.True(boss.HasHp);
        Assert.Equal(157_000, boss.Hp);
    }

    [Fact]
    public void ScenePath_ExpiresWhenBossPacketActivityStops()
    {
        var h = new Harness();

        h.Kind(3518, NpcKind.Boss);
        h.Battle(3518, true, 900);
        h.Hp(3518, 157_000, 1_000);

        Assert.True(h.Focus.TryGetObservedBoss(3_000, 2_000, out _));
        Assert.False(h.Focus.TryGetObservedBoss(3_001, 2_000, out _));
        Assert.True(h.Entities.TryGet(3518, out var entity));
        Assert.Equal(NpcKind.Boss, entity.Kind);
    }

    [Fact]
    public void ScenePath_PreservesExplicitMaxHpAcrossRemainHpUpdates()
    {
        var h = new Harness();

        h.Kind(3518, NpcKind.Boss);
        h.Hp(3518, 49_200, 900, 49_200);
        h.Battle(3518, true, 1_000);
        h.Hp(3518, 22_847, 1_100);

        Assert.True(h.Focus.TryGetObservedBoss(1_200, 2_000, out var boss));
        Assert.Equal(22_847, boss.Hp);
        Assert.Equal(49_200, boss.MaxHp);
    }

    [Fact]
    public void ScenePath_TracksCumulativeLostHpAcrossHealing()
    {
        var h = new Harness();

        h.Kind(3518, NpcKind.Boss);
        h.Battle(3518, true, 900);
        h.Hp(3518, 1_000, 1_000, 1_000);
        h.Hp(3518, 700, 1_100, 1_000);
        h.Hp(3518, 850, 1_200, 1_000);
        h.Hp(3518, 400, 1_300, 1_000);

        Assert.True(h.Focus.TryGetObservedBoss(1_400, 2_000, out var boss));
        Assert.Equal(400, boss.Hp);
        Assert.Equal(1_000, boss.MaxHp);
        Assert.Equal(750, boss.CumulativeLostHp);
    }

    [Fact]
    public void ScenePath_PromotesExistingHpWhenBossLaterBecomesActive()
    {
        var h = new Harness();

        h.Hp(3518, 156_500, 1_000);

        Assert.False(h.Focus.TryGetObservedBoss(1_000, 2_000, out _));

        h.Kind(3518, NpcKind.Boss);
        h.Battle(3518, true, 1_100);

        Assert.True(h.Focus.TryGetObservedBoss(1_500, 2_000, out var boss));
        Assert.True(boss.HasHp);
        Assert.Equal(156_500, boss.Hp);
        Assert.Equal(156_500, boss.MaxHp);
    }

    [Fact]
    public void ScenePath_ExitRetainsFocusAndRefreshesUntilTimeout()
    {
        var h = new Harness();

        h.Kind(3518, NpcKind.Boss);
        h.Battle(3518, true, 900);
        h.Hp(3518, 157_000, 1_000);
        h.Battle(3518, false, 1_050);
        h.Hp(3518, 166_500, 1_100);

        Assert.True(h.Focus.TryGetObservedBoss(1_200, 2_000, out var retained));
        Assert.Equal(166_500, retained.Hp);
        Assert.Equal(1_100, retained.LastObservedAtMilliseconds);
        Assert.False(h.Focus.TryGetObservedBoss(3_101, 2_000, out _));

        h.Battle(3518, true, 3_200);

        Assert.True(h.Focus.TryGetObservedBoss(3_250, 2_000, out var boss));
        Assert.True(boss.HasHp);
        Assert.Equal(166_500, boss.Hp);
        Assert.Equal(3_200, boss.LastObservedAtMilliseconds);
    }

    [Fact]
    public void ScenePath_DeathRetainsFocusUntilTimeout()
    {
        var h = new Harness();

        h.Kind(3518, NpcKind.Boss);
        h.Battle(3518, true, 900);
        h.Hp(3518, 157_000, 1_000);

        Assert.True(h.Focus.TryGetObservedBoss(1_050, 2_000, out _));

        h.Hp(3518, 0, 1_100);
        h.Battle(3518, true, 1_200);
        h.Toggle(3518, 1_260);

        Assert.True(h.Focus.TryGetObservedBoss(3_260, 2_000, out var boss));
        Assert.Equal(0, boss.Hp);
        Assert.Equal(1_260, boss.LastObservedAtMilliseconds);
        Assert.False(h.Focus.TryGetObservedBoss(3_261, 2_000, out _));
        Assert.True(h.Entities.TryGet(3518, out var entity));
        Assert.Equal(0, entity!.CurrentHp);
        Assert.False(entity.NpcCombatActive);
    }

    [Fact]
    public void ScenePath_ClearsByLaterNonBossKind()
    {
        var h = new Harness();

        h.Kind(3518, NpcKind.Boss);
        h.Battle(3518, true, 900);
        h.Hp(3518, 157_000, 1_000);
        h.Kind(3518, NpcKind.Monster);

        Assert.False(h.Focus.TryGetObservedBoss(1_100, 2_000, out _));
    }

    [Fact]
    public void ScenePath_ToggleFollowsEntityBattleState()
    {
        var h = new Harness();

        h.Kind(3518, NpcKind.Boss);
        h.Toggle(3518, 900);
        h.Hp(3518, 157_000, 1_000);

        Assert.True(h.Focus.TryGetObservedBoss(1_100, 2_000, out var boss));
        Assert.True(boss.HasHp);
        Assert.True(h.Entities.TryGet(3518, out var entity));
        Assert.True(entity!.NpcCombatActive);

        h.Toggle(3518, 1_200);

        Assert.True(h.Focus.TryGetObservedBoss(1_300, 2_000, out var stopped));
        Assert.Equal(1_200, stopped.LastObservedAtMilliseconds);
        Assert.False(h.Focus.TryGetObservedBoss(3_201, 2_000, out _));
        Assert.False(entity.NpcCombatActive);
    }

    [Fact]
    public void ScenePath_PromotesActiveNpcWhenBossKindArrives()
    {
        var h = new Harness();

        h.Battle(3518, true, 900);
        h.Hp(3518, 156_500, 1_000);
        h.Kind(3518, NpcKind.Boss, 1_100);

        Assert.True(h.Focus.TryGetObservedBoss(1_200, 2_000, out var boss));
        Assert.True(boss.HasHp);
        Assert.Equal(156_500, boss.Hp);
        Assert.Equal(1_100, boss.LastObservedAtMilliseconds);
    }

    [Fact]
    public void ReadModel_PromotesBossFocusFromConfirmedCombatActivityWithoutBattleToggle()
    {
        using var scene = new SceneTestHarness();

        scene.AppendNpcKind(3518, NpcKind.Boss);
        scene.AppendNpcHp(3518, 156_500, 167_000, 1_000);

        var spawnOnly = scene.CreateSnapshot();
        Assert.Empty(spawnOnly.BossFocuses);

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = 100,
            TargetId = 3518,
            Damage = 500,
            HitContribution = 1,
            AttemptContribution = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage,
            Timestamp = 1_100
        });

        var snapshot = scene.CreateSnapshot();
        var boss = Assert.Single(snapshot.BossFocuses);
        Assert.Equal(3518, boss.InstanceId);
        Assert.True(boss.HasHp);
        Assert.Equal(156_500, boss.Hp);
        Assert.Equal(167_000, boss.MaxHp);
    }

    [Fact]
    public void ReadModel_DoesNotRestoreExpiredBossFocusFromPreviousCombatActivity()
    {
        var sceneStarted = new DateTimeOffset(2026, 6, 14, 13, 30, 8, TimeSpan.Zero);
        var timeProvider = new MutableTimeProvider(sceneStarted);
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(sceneStarted.ToUnixTimeMilliseconds());
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());
        var owner = new SceneReadModelOwner(journal, Guid.NewGuid(), sceneStarted, new RuntimeMetadataRegistry(), timeProvider);
        var kindSource = new PacketObservationSource(sceneStarted.ToUnixTimeMilliseconds() + 100, 0, 0, 0, 0, 0, default);
        var hpSource = new PacketObservationSource(sceneStarted.ToUnixTimeMilliseconds() + 200, 0, 0, 0, 0, 0, default);
        var combatSource = new PacketObservationSource(sceneStarted.ToUnixTimeMilliseconds() + 300, 0, 1, 0, 0, 0, default);
        var deathSource = new PacketObservationSource(sceneStarted.ToUnixTimeMilliseconds() + 1_200, 0, 2, 0, 0, 0, default);
        var restoredHpSource = new PacketObservationSource(sceneStarted.ToUnixTimeMilliseconds() + 12_500, 0, 3, 0, 0, 0, default);
        var combat = new CombatObservation
        {
            Damage = 500,
            HitCount = 1,
            AttemptCount = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };

        sink.AppendNpcKind(in kindSource, 3518, NpcKind.Boss);
        sink.AppendNpcHp(in hpSource, 3518, 156_500, 167_000);
        sink.AppendCombatObservation(in combatSource, 100, 3518, in combat);
        sink.CompleteBatch(1);
        timeProvider.SetUtcNow(sceneStarted.AddMilliseconds(300));
        Assert.Single(owner.CreateSnapshot().BossFocuses);

        sink.AppendNpcHp(in deathSource, 3518, 0, 167_000);
        sink.CompleteBatch(2);
        timeProvider.SetUtcNow(sceneStarted.AddMilliseconds(1_200));
        Assert.Single(owner.CreateSnapshot().BossFocuses);

        timeProvider.SetUtcNow(sceneStarted.AddMilliseconds(11_201));
        Assert.Empty(owner.CreateSnapshot().BossFocuses);

        sink.AppendNpcHp(in restoredHpSource, 3518, 167_000, 167_000);
        sink.CompleteBatch(3);

        Assert.Empty(owner.CreateSnapshot().BossFocuses);
    }

    [Fact]
    public void ReadModel_ExpiresBossFocusAgainstSceneRelativeClockWithoutNewEvents()
    {
        var sceneStarted = new DateTimeOffset(2026, 6, 12, 13, 37, 31, TimeSpan.Zero);
        var timeProvider = new MutableTimeProvider(sceneStarted);
        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(sceneStarted.ToUnixTimeMilliseconds());
        var sink = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());
        var owner = new SceneReadModelOwner(journal, Guid.NewGuid(), sceneStarted, new RuntimeMetadataRegistry(), timeProvider);
        var kindSource = new PacketObservationSource(sceneStarted.ToUnixTimeMilliseconds() + 100, 0, 0, 0, 0, 0, default);
        var hpSource = new PacketObservationSource(sceneStarted.ToUnixTimeMilliseconds() + 200, 0, 0, 0, 0, 0, default);
        var combatSource = new PacketObservationSource(sceneStarted.ToUnixTimeMilliseconds() + 300, 0, 0, 0, 0, 0, default);
        var combat = new CombatObservation
        {
            Damage = 500,
            HitCount = 1,
            AttemptCount = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };

        sink.AppendNpcKind(in kindSource, 3518, NpcKind.Boss);
        sink.AppendNpcHp(in hpSource, 3518, 157_000, 167_000);
        sink.AppendCombatObservation(in combatSource, 100, 3518, in combat);
        timeProvider.SetUtcNow(sceneStarted.AddMilliseconds(300));

        Assert.Single(owner.CreateSnapshot().BossFocuses);

        timeProvider.SetUtcNow(sceneStarted.AddMilliseconds(10_301));

        Assert.Empty(owner.CreateSnapshot().BossFocuses);
        Assert.True(owner.Entities.TryGet(3518, out var entity));
        Assert.Equal(NpcKind.Boss, entity.Kind);
    }

    [Fact]
    public void ScenePath_RestoresActiveBossFromEntityStateAfterFocusStoreReset()
    {
        var entities = new EntityStore();
        entities.ApplyNpcKind(3518, NpcKind.Boss);
        entities.ApplyNpcHp(3518, 157_000, 167_000);
        entities.ApplyBattleToggle(3518, true);
        var focus = new BossFocusStore(entities);

        entities.ApplyNpcHp(3518, 152_000, 167_000);
        focus.ApplyNpcHp(3518, 152_000, 167_000, 100);

        Assert.True(focus.TryGetObservedBoss(200, 2_000, out var boss));
        Assert.Equal(3518, boss.InstanceId);
        Assert.Equal(152_000, boss.Hp);
        Assert.Equal(167_000, boss.MaxHp);
    }

    [Fact]
    public void ScenePath_DoesNotRestoreActiveMonsterFromEntityStateAfterFocusStoreReset()
    {
        var entities = new EntityStore();
        entities.ApplyNpcKind(3518, NpcKind.Monster);
        entities.ApplyNpcHp(3518, 157_000, 167_000);
        entities.ApplyBattleToggle(3518, true);
        var focus = new BossFocusStore(entities);

        entities.ApplyNpcHp(3518, 152_000, 167_000);
        focus.ApplyNpcHp(3518, 152_000, 167_000, 100);

        Assert.False(focus.TryGetObservedBoss(200, 2_000, out _));
    }

    [Fact]
    public void JournalingSink_RecordsNpcKindBattleAndHpProtocolFields()
    {
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());

        sink.AppendNpcKind(3518, NpcKind.Boss);
        sink.SetNpcBattle(3518, true, 900);
        sink.ToggleNpcBattle(3518);
        sink.AppendNpcHp(3518, 22_847, 1_100);

        Assert.Equal(4, journal.Count);

        var kind = journal.Read(0);
        Assert.Equal(ObservedEventDomain.State, kind.Domain);
        Assert.Equal(StateCodes.NpcKind, kind.State!.Value.StateCode);
        Assert.Equal((int)NpcKind.Boss, kind.State.Value.Value0);

        var battle = journal.Read(1);
        Assert.Equal(StateCodes.NpcBattle, battle.State!.Value.StateCode);
        Assert.Equal(1, battle.State.Value.Value0);
        Assert.Equal(900, battle.Stamp.OffsetTicks / TimeSpan.TicksPerMillisecond);

        var toggle = journal.Read(2);
        Assert.Equal(StateCodes.NpcBattleToggle, toggle.State!.Value.StateCode);

        var hp = journal.Read(3);
        Assert.Equal(ObservedEventDomain.Resource, hp.Domain);
        Assert.Equal(22_847, hp.Resource!.Value.CurrentValue);
        Assert.Equal(1_100, hp.Stamp.OffsetTicks / TimeSpan.TicksPerMillisecond);
    }

    private sealed class Harness
    {
        public EntityStore Entities { get; } = new();
        public BossFocusStore Focus { get; }

        public Harness() => Focus = new BossFocusStore(Entities);

        public void Kind(int instanceId, NpcKind kind, long observedAtMilliseconds = 0)
        {
            Entities.ApplyNpcKind(instanceId, kind);
            Focus.ApplyNpcKind(instanceId, kind, observedAtMilliseconds);
        }

        public void Hp(int instanceId, int hp, long observedAtMilliseconds, int maxHp = 0)
        {
            Entities.ApplyNpcHp(instanceId, hp, maxHp);
            Focus.ApplyNpcHp(instanceId, hp, maxHp, observedAtMilliseconds);
        }

        public void Battle(int instanceId, bool isActive, long observedAtMilliseconds)
        {
            var active = isActive && CanActivate(instanceId);
            Entities.ApplyBattleToggle(instanceId, active);
            Focus.ApplyBattle(instanceId, active, observedAtMilliseconds);
        }

        public void Toggle(int instanceId, long observedAtMilliseconds)
        {
            var active = !Entities.GetOrAdd(instanceId).NpcCombatActive && CanActivate(instanceId);
            Entities.ApplyBattleToggle(instanceId, active);
            Focus.ApplyBattleToggle(instanceId, active, observedAtMilliseconds);
        }

        private bool CanActivate(int instanceId) =>
            !Entities.TryGet(instanceId, out var entity) || entity.CurrentHp != 0;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }
}
