using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Scene.Runtime;
using Cloris.Aion2Flow.Scene.Stores;

namespace Cloris.Aion2Flow.Tests.Scene;

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

        Assert.True(h.Focus.TryGetObservedBoss(10_000, 2_000, out var boss));
        Assert.True(boss.HasHp);
        Assert.Equal(3518, boss.InstanceId);
        Assert.Equal(152_000, boss.Hp);
        Assert.Equal(167_000, boss.MaxHp);
        Assert.Equal(1_200, boss.LastObservedAtMilliseconds);

        h.Battle(3518, false, 1_300);

        Assert.False(h.Focus.TryGetObservedBoss(1_400, 2_000, out _));
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
    public void ScenePath_ExitClearsAndIgnoresLaterHpUntilReentered()
    {
        var h = new Harness();

        h.Kind(3518, NpcKind.Boss);
        h.Battle(3518, true, 900);
        h.Hp(3518, 157_000, 1_000);
        h.Battle(3518, false, 1_050);
        h.Hp(3518, 166_500, 1_100);

        Assert.False(h.Focus.TryGetObservedBoss(1_200, 2_000, out _));

        h.Battle(3518, true, 1_300);

        Assert.True(h.Focus.TryGetObservedBoss(1_350, 2_000, out var boss));
        Assert.True(boss.HasHp);
        Assert.Equal(166_500, boss.Hp);
    }

    [Fact]
    public void ScenePath_ClearsWhenHpReachesZero()
    {
        var h = new Harness();

        h.Kind(3518, NpcKind.Boss);
        h.Battle(3518, true, 900);
        h.Hp(3518, 157_000, 1_000);

        Assert.True(h.Focus.TryGetObservedBoss(10_000, 2_000, out _));

        h.Hp(3518, 0, 1_100);
        h.Battle(3518, true, 1_200);
        h.Toggle(3518, 1_260);

        Assert.False(h.Focus.TryGetObservedBoss(1_300, 2_000, out _));
        Assert.True(h.Entities.TryGet(3518, out var entity));
        Assert.Equal(0, entity!.CurrentHp);
        Assert.False(entity.BattleActive);
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
        Assert.True(entity!.BattleActive);

        h.Toggle(3518, 1_200);

        Assert.False(h.Focus.TryGetObservedBoss(1_300, 2_000, out _));
        Assert.False(entity.BattleActive);
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
        Assert.Equal(900, battle.Raw.TimestampMilliseconds);

        var toggle = journal.Read(2);
        Assert.Equal(StateCodes.NpcBattleToggle, toggle.State!.Value.StateCode);

        var hp = journal.Read(3);
        Assert.Equal(ObservedEventDomain.Resource, hp.Domain);
        Assert.Equal(22_847, hp.Resource!.Value.CurrentValue);
        Assert.Equal(1_100, hp.Raw.TimestampMilliseconds);
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
            var active = !Entities.GetOrAdd(instanceId).BattleActive && CanActivate(instanceId);
            Entities.ApplyBattleToggle(instanceId, active);
            Focus.ApplyBattleToggle(instanceId, active, observedAtMilliseconds);
        }

        private bool CanActivate(int instanceId) =>
            !Entities.TryGet(instanceId, out var entity) || entity.CurrentHp != 0;
    }
}
