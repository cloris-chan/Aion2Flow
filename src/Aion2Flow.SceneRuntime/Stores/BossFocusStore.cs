using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public sealed class BossFocusStore(EntityStore entities)
{
    private readonly Dictionary<int, Snapshot> _observed = [];
    private readonly HashSet<int> _focused = [];
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
            if (elapsed <= visibilityTimeoutMilliseconds || _focused.Contains(instanceId))
                result.Add(snapshot);
            else
                (expired ??= []).Add(instanceId);
        }

        if (expired is not null)
        {
            foreach (var id in expired)
                _observed.Remove(id);
            _revision++;
        }

        result.Sort(static (a, b) => a.InstanceId.CompareTo(b.InstanceId));
        return result;
    }

    public void ApplyNpcKind(int instanceId, NpcKind kind, long observedAtMilliseconds)
    {
        if (kind != NpcKind.Boss)
        {
            var changed = _focused.Remove(instanceId);
            changed |= _observed.Remove(instanceId);
            if (changed)
                _revision++;
            return;
        }

        if (_focused.Contains(instanceId) || IsNpcCombatActive(instanceId))
        {
            _focused.Add(instanceId);
            RememberActivity(instanceId, observedAtMilliseconds);
        }
    }

    public void ApplyNpcHp(int instanceId, int hp, int maxHp, long observedAtMilliseconds)
    {
        if (hp == 0)
        {
            var changed = _focused.Remove(instanceId);
            changed |= _observed.Remove(instanceId);
            if (changed)
                _revision++;
            return;
        }

        if (_focused.Contains(instanceId) || _observed.ContainsKey(instanceId))
            Remember(instanceId, hp, ResolveMaxHp(instanceId, hp, maxHp), observedAtMilliseconds);
    }

    public bool ApplyBattle(int instanceId, bool isActive, long observedAtMilliseconds)
    {
        if (isActive && IsBossInstance(instanceId) && !IsObservedDead(instanceId))
        {
            _focused.Add(instanceId);
            RememberActivity(instanceId, observedAtMilliseconds);
            return true;
        }

        var removed = _focused.Remove(instanceId);
        removed |= _observed.Remove(instanceId);
        if (removed)
            _revision++;
        return false;
    }

    public bool ApplyBattleToggle(int instanceId, bool isActive, long observedAtMilliseconds) => ApplyBattle(instanceId, isActive, observedAtMilliseconds);

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
            _revision++;
        }
    }

    private void Remember(int instanceId, int hp, int maxHp, long observedAtMilliseconds)
    {
        var snapshot = new Snapshot
        {
            InstanceId = instanceId,
            Hp = hp,
            MaxHp = Math.Max(1, maxHp),
            LastObservedAtMilliseconds = Math.Max(0, observedAtMilliseconds),
            HasHp = true
        };
        if (!_observed.TryGetValue(instanceId, out var current) || !current.Equals(snapshot))
        {
            _observed[instanceId] = snapshot;
            _revision++;
        }
    }

    private int ResolveMaxHp(int instanceId, int hp, int maxHp)
    {
        var resolved = Math.Max(maxHp, hp);
        if (entities.TryGet(instanceId, out var entity) && entity.MaxHp is int entityMaxHp)
            resolved = Math.Max(resolved, entityMaxHp);
        return resolved;
    }

    private bool IsBossInstance(int instanceId) =>
        entities.TryGet(instanceId, out var entity) && entity.Kind == NpcKind.Boss;

    private bool IsNpcCombatActive(int instanceId) =>
        entities.TryGet(instanceId, out var entity) && entity.NpcCombatActive && !IsObservedDead(instanceId);

    private bool IsObservedDead(int instanceId) =>
        entities.TryGet(instanceId, out var entity) && entity.CurrentHp == 0;

    internal BossFocusStoreStateSnapshot CreateStateSnapshot()
    {
        var observed = new BossFocusObservedStateSnapshot[_observed.Count];
        var index = 0;
        foreach (var pair in _observed)
            observed[index++] = new BossFocusObservedStateSnapshot(pair.Key, pair.Value);
        Array.Sort(observed, static (left, right) => left.InstanceId.CompareTo(right.InstanceId));

        var focused = new int[_focused.Count];
        index = 0;
        foreach (var id in _focused)
            focused[index++] = id;
        Array.Sort(focused);

        return new BossFocusStoreStateSnapshot(_revision, observed, focused);
    }

    internal void RestoreState(BossFocusStoreStateSnapshot snapshot)
    {
        _observed.Clear();
        _focused.Clear();
        _observed.EnsureCapacity(snapshot.Observed.Length);
        foreach (ref readonly var observed in snapshot.Observed.AsSpan())
            _observed.Add(observed.InstanceId, observed.Snapshot);
        _focused.EnsureCapacity(snapshot.Focused.Length);
        foreach (var id in snapshot.Focused)
            _focused.Add(id);
        _revision = snapshot.Revision;
    }

    public readonly record struct Snapshot
    {
        public int InstanceId { get; init; }
        public int Hp { get; init; }
        public int MaxHp { get; init; }
        public long LastObservedAtMilliseconds { get; init; }
        public bool HasHp { get; init; }
    }
}

internal sealed class BossFocusStoreStateSnapshot(long revision, BossFocusObservedStateSnapshot[] observed, int[] focused)
{
    public long Revision { get; } = revision;
    public BossFocusObservedStateSnapshot[] Observed { get; } = observed;
    public int[] Focused { get; } = focused;

    public BossFocusStoreStateSnapshot DeepClone()
    {
        var observed = new BossFocusObservedStateSnapshot[Observed.Length];
        Array.Copy(Observed, observed, observed.Length);
        var focused = new int[Focused.Length];
        Array.Copy(Focused, focused, focused.Length);
        return new BossFocusStoreStateSnapshot(Revision, observed, focused);
    }
}

internal readonly record struct BossFocusObservedStateSnapshot(int InstanceId, BossFocusStore.Snapshot Snapshot);
