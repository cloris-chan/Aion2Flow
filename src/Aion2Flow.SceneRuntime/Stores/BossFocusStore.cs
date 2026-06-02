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
