using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public sealed class BossFocusStore(EntityStore entities, EntityVitalStore entityVitals)
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
        RemoveExpiredBosses(nowMilliseconds, visibilityTimeoutMilliseconds);
        if (_observed.Count == 0)
            return [];

        var result = new List<Snapshot>(_observed.Count);
        foreach (var snapshot in _observed.Values)
            result.Add(snapshot);

        result.Sort(static (a, b) => a.InstanceId.CompareTo(b.InstanceId));
        return result;
    }

    internal BossFocusGroupState GetGroupState(long nowMilliseconds, long visibilityTimeoutMilliseconds)
    {
        RemoveExpiredBosses(nowMilliseconds, visibilityTimeoutMilliseconds);
        if (_observed.Count == 0)
            return BossFocusGroupState.Empty;

        foreach (var snapshot in _observed.Values)
        {
            if (!snapshot.HasHp || snapshot.Hp > 0)
                return BossFocusGroupState.ActiveOrUnknown;
        }

        return BossFocusGroupState.AllDead;
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

    public void ApplyNpcHp(int instanceId, long hp, long maxHp, long observedAtMilliseconds)
    {
        var resolvedMaxHp = ResolveMaxHp(instanceId, maxHp);
        var hasMaxHp = resolvedMaxHp > 0;
        if (hp == 0)
        {
            if (_observed.ContainsKey(instanceId))
                Remember(instanceId, hp, resolvedMaxHp, hasMaxHp, observedAtMilliseconds);
            return;
        }

        if (_observed.ContainsKey(instanceId) || IsActiveFocusTargetInstance(instanceId))
            Remember(instanceId, hp, resolvedMaxHp, hasMaxHp, observedAtMilliseconds);
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

    internal void RestoreSnapshot(BossFocusStoreSnapshot snapshot)
    {
        _observed.Clear();
        _encounterBosses.Clear();
        _encounterBossOrder.Clear();
        _lastClearedAtMilliseconds.Clear();
        for (var i = 0; i < snapshot.Observed.Length; i++)
        {
            var observed = snapshot.Observed[i];
            _observed[observed.InstanceId] = observed.Snapshot;
        }

        for (var i = 0; i < snapshot.Cleared.Length; i++)
        {
            var cleared = snapshot.Cleared[i];
            _lastClearedAtMilliseconds[cleared.InstanceId] = cleared.ObservedAtMilliseconds;
        }
        for (var i = 0; i < snapshot.EncounterBosses.Length; i++)
        {
            var encounterBoss = snapshot.EncounterBosses[i];
            _encounterBossOrder.Add(encounterBoss.InstanceId);
            _encounterBosses[encounterBoss.InstanceId] = encounterBoss.Snapshot;
        }
        _revision = snapshot.Revision;
    }

    private void Clear(int instanceId, long observedAtMilliseconds)
    {
        var changed = _observed.Remove(instanceId);
        if (changed || IsFocusTargetInstance(instanceId) || _lastClearedAtMilliseconds.ContainsKey(instanceId))
            _lastClearedAtMilliseconds[instanceId] = Math.Max(0, observedAtMilliseconds);
        if (changed)
            _revision++;
    }

    private void RemoveExpiredBosses(long nowMilliseconds, long visibilityTimeoutMilliseconds)
    {
        List<int>? expired = null;
        foreach (var (instanceId, snapshot) in _observed)
        {
            var elapsed = Math.Max(0, nowMilliseconds - snapshot.LastObservedAtMilliseconds);
            if (elapsed > visibilityTimeoutMilliseconds)
                (expired ??= []).Add(instanceId);
        }

        if (expired is null)
            return;

        for (var i = 0; i < expired.Count; i++)
        {
            var instanceId = expired[i];
            var snapshot = _observed[instanceId];
            _lastClearedAtMilliseconds[instanceId] = Math.Max(nowMilliseconds, snapshot.LastObservedAtMilliseconds);
            _observed.Remove(instanceId);
        }
        _revision++;
    }

    private void RememberActivity(int instanceId, long observedAtMilliseconds)
    {
        var observedAt = Math.Max(0, observedAtMilliseconds);
        if (entityVitals.TryGet(instanceId, out var vital))
        {
            Remember(instanceId, vital.CurrentHp, vital.MaxHp ?? 0, vital.MaxHp.HasValue, observedAt);
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
            HasHp = false,
            HasMaxHp = false
        };
        if (!_observed.TryGetValue(instanceId, out var previous) || !previous.Equals(snapshot))
        {
            _observed[instanceId] = snapshot;
            RememberEncounterBoss(instanceId, in snapshot);
            _revision++;
        }
    }

    private void Remember(int instanceId, long hp, long maxHp, bool hasMaxHp, long observedAtMilliseconds)
    {
        var resolvedHp = Math.Max(0, hp);
        var resolvedMaxHp = hasMaxHp ? Math.Max(1, maxHp) : 0;
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
            HasHp = true,
            HasMaxHp = hasMaxHp
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

    private long ResolveMaxHp(int instanceId, long maxHp)
    {
        var resolved = maxHp;
        if (entityVitals.TryGet(instanceId, out var vital) && vital.MaxHp is long observedMaxHp)
            resolved = Math.Max(resolved, observedMaxHp);
        return resolved;
    }

    private bool IsFocusTargetInstance(int instanceId) => entities.TryGet(instanceId, out var entity) && IsFocusTargetKind(entity.Kind);

    private bool IsActiveFocusTargetInstance(int instanceId) => IsFocusTargetInstance(instanceId) && IsNpcCombatActive(instanceId);

    private static bool IsFocusTargetKind(NpcKind kind) => BossModeFocusTargets.IsFocusTarget(kind);

    private bool IsNpcCombatActive(int instanceId) => entities.TryGet(instanceId, out var entity) && entity.NpcCombatActive && !IsObservedDead(instanceId);

    private bool IsObservedDead(int instanceId) => entityVitals.TryGet(instanceId, out var vital) && vital.CurrentHp == 0;

    private bool IsAfterLastClear(int instanceId, long activityObservedAtMilliseconds) =>
        !_lastClearedAtMilliseconds.TryGetValue(instanceId, out var clearedAt) ||
        activityObservedAtMilliseconds > clearedAt;

    public readonly record struct Snapshot
    {
        public int InstanceId { get; init; }
        public long Hp { get; init; }
        public long MaxHp { get; init; }
        public long CumulativeLostHp { get; init; }
        public long LastObservedAtMilliseconds { get; init; }
        public bool HasHp { get; init; }
        public bool HasMaxHp { get; init; }
    }
}

internal enum BossFocusGroupState : byte
{
    Empty,
    ActiveOrUnknown,
    AllDead
}

internal sealed record BossFocusStoreSnapshot(BossFocusObservedSnapshot[] Observed, BossFocusClearedSnapshot[] Cleared, BossFocusObservedSnapshot[] EncounterBosses, long Revision);

internal readonly record struct BossFocusObservedSnapshot(int InstanceId, BossFocusStore.Snapshot Snapshot);

internal readonly record struct BossFocusClearedSnapshot(int InstanceId, long ObservedAtMilliseconds);
