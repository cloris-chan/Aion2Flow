using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public sealed class BossFocusStore(EntityStore entities)
{
    private readonly Dictionary<int, Snapshot> _observed = [];
    private readonly Dictionary<int, Snapshot> _encounterBosses = [];
    private readonly List<int> _encounterBossOrder = [];
    private readonly Dictionary<int, long> _lastClearedAtMilliseconds = [];
    private long _revision;

    public long Revision => _revision;

    public bool TryGetObservedBoss(long nowMilliseconds, long visibilityTimeoutMilliseconds, out Snapshot snapshot)
    {
        var snapshots = GetObservedBosses(nowMilliseconds, visibilityTimeoutMilliseconds);
        if (snapshots.Count == 0)
        {
            snapshot = default;
            return false;
        }

        snapshot = snapshots[0];
        for (var i = 1; i < snapshots.Count; i++)
        {
            var candidate = snapshots[i];
            if (candidate.LastObservedAtMilliseconds > snapshot.LastObservedAtMilliseconds)
                snapshot = candidate;
        }
        return true;
    }

    public IReadOnlyList<Snapshot> GetObservedBosses(long nowMilliseconds, long visibilityTimeoutMilliseconds)
    {
        if (_observed.Count == 0)
            return [];

        List<int>? expired = null;
        var result = new List<Snapshot>(_observed.Count);
        foreach (var (instanceId, snapshot) in _observed)
        {
            var elapsed = Math.Max(0, nowMilliseconds - snapshot.LastObservedAtMilliseconds);
            if (elapsed <= visibilityTimeoutMilliseconds)
                result.Add(snapshot);
            else
                (expired ??= []).Add(instanceId);
        }

        if (expired is not null)
        {
            foreach (var id in expired)
            {
                if (_observed.TryGetValue(id, out var snapshot))
                    _lastClearedAtMilliseconds[id] = Math.Max(nowMilliseconds, snapshot.LastObservedAtMilliseconds);
                _observed.Remove(id);
            }
            _revision++;
        }

        result.Sort(static (a, b) => a.InstanceId.CompareTo(b.InstanceId));
        return result;
    }

    public IReadOnlyList<Snapshot> GetEncounterBosses()
    {
        if (_encounterBossOrder.Count == 0)
            return [];

        var result = new Snapshot[_encounterBossOrder.Count];
        for (var i = 0; i < result.Length; i++)
            result[i] = _encounterBosses[_encounterBossOrder[i]];
        return result;
    }

    public void ApplyNpcKind(int instanceId, NpcKind kind, long observedAtMilliseconds)
    {
        if (!IsFocusTargetKind(kind))
        {
            Clear(instanceId, observedAtMilliseconds);
            return;
        }

        if (IsNpcCombatActive(instanceId))
            RememberActivity(instanceId, observedAtMilliseconds);
    }

    public void ApplyNpcHp(int instanceId, int hp, int maxHp, long observedAtMilliseconds)
    {
        if (hp == 0)
        {
            if (_observed.ContainsKey(instanceId))
                Remember(instanceId, hp, ResolveMaxHp(instanceId, hp, maxHp), observedAtMilliseconds);
            return;
        }

        if (_observed.ContainsKey(instanceId) || IsActiveFocusTargetInstance(instanceId))
            Remember(instanceId, hp, ResolveMaxHp(instanceId, hp, maxHp), observedAtMilliseconds);
    }

    public bool ApplyBattle(int instanceId, bool isActive, long observedAtMilliseconds)
    {
        if (isActive && IsFocusTargetInstance(instanceId))
        {
            RememberActivity(instanceId, observedAtMilliseconds);
            return true;
        }

        if (_observed.ContainsKey(instanceId))
            RememberActivity(instanceId, observedAtMilliseconds);
        return false;
    }

    public bool ApplyBattleToggle(int instanceId, bool isActive, long observedAtMilliseconds) => ApplyBattle(instanceId, isActive, observedAtMilliseconds);

    public void ApplyCombatActivity(int instanceId, long activityObservedAtMilliseconds, long observedAtMilliseconds)
    {
        if (!IsFocusTargetInstance(instanceId) ||
            !IsAfterLastClear(instanceId, activityObservedAtMilliseconds))
        {
            return;
        }

        RememberActivity(instanceId, observedAtMilliseconds);
    }

    internal BossFocusStoreSnapshot CreateSnapshot()
    {
        var observed = new BossFocusObservedSnapshot[_observed.Count];
        var index = 0;
        foreach (var (instanceId, snapshot) in _observed)
            observed[index++] = new BossFocusObservedSnapshot(instanceId, snapshot);
        var cleared = new BossFocusClearedSnapshot[_lastClearedAtMilliseconds.Count];
        index = 0;
        foreach (var (instanceId, observedAtMilliseconds) in _lastClearedAtMilliseconds)
            cleared[index++] = new BossFocusClearedSnapshot(instanceId, observedAtMilliseconds);
        var encounterBosses = new BossFocusObservedSnapshot[_encounterBossOrder.Count];
        for (var i = 0; i < encounterBosses.Length; i++)
        {
            var instanceId = _encounterBossOrder[i];
            encounterBosses[i] = new BossFocusObservedSnapshot(instanceId, _encounterBosses[instanceId]);
        }
        return new BossFocusStoreSnapshot(observed, cleared, encounterBosses, _revision);
    }

    internal static BossFocusStore FromSnapshot(EntityStore entities, BossFocusStoreSnapshot snapshot)
    {
        var store = new BossFocusStore(entities) { _revision = snapshot.Revision };
        for (var i = 0; i < snapshot.Observed.Length; i++)
        {
            var observed = snapshot.Observed[i];
            store._observed[observed.InstanceId] = observed.Snapshot;
        }

        for (var i = 0; i < snapshot.Cleared.Length; i++)
        {
            var cleared = snapshot.Cleared[i];
            store._lastClearedAtMilliseconds[cleared.InstanceId] = cleared.ObservedAtMilliseconds;
        }
        for (var i = 0; i < snapshot.EncounterBosses.Length; i++)
        {
            var encounterBoss = snapshot.EncounterBosses[i];
            store._encounterBossOrder.Add(encounterBoss.InstanceId);
            store._encounterBosses[encounterBoss.InstanceId] = encounterBoss.Snapshot;
        }
        return store;
    }

    private void Clear(int instanceId, long observedAtMilliseconds)
    {
        var changed = _observed.Remove(instanceId);
        if (changed || IsFocusTargetInstance(instanceId) || _lastClearedAtMilliseconds.ContainsKey(instanceId))
            _lastClearedAtMilliseconds[instanceId] = Math.Max(0, observedAtMilliseconds);
        if (changed)
            _revision++;
    }

    private void RememberActivity(int instanceId, long observedAtMilliseconds)
    {
        var observedAt = Math.Max(0, observedAtMilliseconds);
        if (entities.TryGet(instanceId, out var entity) && entity.CurrentHp is int hp)
        {
            Remember(instanceId, hp, Math.Max(entity.MaxHp ?? hp, hp), observedAt);
            return;
        }

        if (_observed.TryGetValue(instanceId, out var current) && current.HasHp)
        {
            var next = current with { LastObservedAtMilliseconds = observedAt };
            if (!next.Equals(current))
            {
                _observed[instanceId] = next;
                RememberEncounterBoss(instanceId, in next);
                _revision++;
            }
            return;
        }

        var snapshot = new Snapshot
        {
            InstanceId = instanceId,
            Hp = 0,
            MaxHp = 1,
            LastObservedAtMilliseconds = observedAt,
            HasHp = false
        };
        if (!_observed.TryGetValue(instanceId, out var previous) || !previous.Equals(snapshot))
        {
            _observed[instanceId] = snapshot;
            RememberEncounterBoss(instanceId, in snapshot);
            _revision++;
        }
    }

    private void Remember(int instanceId, int hp, int maxHp, long observedAtMilliseconds)
    {
        var resolvedHp = Math.Max(0, hp);
        var resolvedMaxHp = Math.Max(1, maxHp);
        var cumulativeLostHp = 0L;
        if (_observed.TryGetValue(instanceId, out var previous) && previous.HasHp)
        {
            cumulativeLostHp = previous.CumulativeLostHp;
            if (resolvedHp < previous.Hp)
                cumulativeLostHp += previous.Hp - resolvedHp;
        }

        var snapshot = new Snapshot
        {
            InstanceId = instanceId,
            Hp = resolvedHp,
            MaxHp = resolvedMaxHp,
            CumulativeLostHp = cumulativeLostHp,
            LastObservedAtMilliseconds = Math.Max(0, observedAtMilliseconds),
            HasHp = true
        };
        if (!_observed.TryGetValue(instanceId, out var current) || !current.Equals(snapshot))
        {
            _observed[instanceId] = snapshot;
            RememberEncounterBoss(instanceId, in snapshot);
            _revision++;
        }
    }

    private void RememberEncounterBoss(int instanceId, in Snapshot snapshot)
    {
        if (!_encounterBosses.ContainsKey(instanceId))
            _encounterBossOrder.Add(instanceId);
        _encounterBosses[instanceId] = snapshot;
    }

    private int ResolveMaxHp(int instanceId, int hp, int maxHp)
    {
        var resolved = Math.Max(maxHp, hp);
        if (entities.TryGet(instanceId, out var entity) && entity.MaxHp is int entityMaxHp)
            resolved = Math.Max(resolved, entityMaxHp);
        return resolved;
    }

    private bool IsFocusTargetInstance(int instanceId) => entities.TryGet(instanceId, out var entity) && IsFocusTargetKind(entity.Kind);

    private bool IsActiveFocusTargetInstance(int instanceId) => IsFocusTargetInstance(instanceId) && IsNpcCombatActive(instanceId);

    private static bool IsFocusTargetKind(NpcKind kind) => BossModeFocusTargets.IsFocusTarget(kind);

    private bool IsNpcCombatActive(int instanceId) => entities.TryGet(instanceId, out var entity) && entity.NpcCombatActive && !IsObservedDead(instanceId);

    private bool IsObservedDead(int instanceId) => entities.TryGet(instanceId, out var entity) && entity.CurrentHp == 0;

    private bool IsAfterLastClear(int instanceId, long activityObservedAtMilliseconds) =>
        !_lastClearedAtMilliseconds.TryGetValue(instanceId, out var clearedAt) ||
        activityObservedAtMilliseconds > clearedAt;

    public readonly record struct Snapshot
    {
        public int InstanceId { get; init; }
        public int Hp { get; init; }
        public int MaxHp { get; init; }
        public long CumulativeLostHp { get; init; }
        public long LastObservedAtMilliseconds { get; init; }
        public bool HasHp { get; init; }
    }
}

internal sealed record BossFocusStoreSnapshot(BossFocusObservedSnapshot[] Observed, BossFocusClearedSnapshot[] Cleared, BossFocusObservedSnapshot[] EncounterBosses, long Revision);

internal readonly record struct BossFocusObservedSnapshot(int InstanceId, BossFocusStore.Snapshot Snapshot);

internal readonly record struct BossFocusClearedSnapshot(int InstanceId, long ObservedAtMilliseconds);
