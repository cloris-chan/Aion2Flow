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

    public void ApplyNpcKindState(int instanceId, NpcKind kind, long observedAtMilliseconds)
    {
        if (!IsFocusTargetKind(kind))
            Clear(instanceId, observedAtMilliseconds);
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

    public void ApplyNpcHpState(int instanceId, long hp, long maxHp)
    {
        if (!_observed.ContainsKey(instanceId))
            return;

        var resolvedMaxHp = ResolveMaxHp(instanceId, maxHp);
        RememberState(instanceId, hp, resolvedMaxHp, resolvedMaxHp > 0);
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

    internal void ReconcileActivity(Func<int, long?> resolveActivity)
    {
        ArgumentNullException.ThrowIfNull(resolveActivity);
        if (_observed.Count == 0 && _encounterBossOrder.Count == 0 && entities.Entities.Count == 0)
            return;

        var candidates = new HashSet<int>(_encounterBossOrder);
        foreach (var instanceId in _observed.Keys)
            candidates.Add(instanceId);
        foreach (var entity in entities.Entities.Values)
        {
            if (IsFocusTargetKind(entity.Kind))
                candidates.Add(entity.EntityId);
        }

        var changed = false;
        foreach (var instanceId in candidates)
        {
            var activityObservedAtMilliseconds = resolveActivity(instanceId);
            if (activityObservedAtMilliseconds is not long observedAt || !IsAfterLastClear(instanceId, observedAt))
            {
                changed |= _observed.Remove(instanceId);
                continue;
            }

            var nextSnapshot = ResolveActivitySnapshot(instanceId, Math.Max(0, observedAt));

            if (!_observed.TryGetValue(instanceId, out var snapshot))
            {
                _observed[instanceId] = nextSnapshot;
                RememberEncounterBoss(instanceId, in nextSnapshot);
                changed = true;
                continue;
            }

            var resolved = nextSnapshot with
            {
                Hp = nextSnapshot.HasHp ? nextSnapshot.Hp : snapshot.Hp,
                MaxHp = nextSnapshot.HasMaxHp ? nextSnapshot.MaxHp : snapshot.MaxHp,
                CumulativeLostHp = Math.Max(snapshot.CumulativeLostHp, nextSnapshot.CumulativeLostHp),
                HasHp = snapshot.HasHp || nextSnapshot.HasHp,
                HasMaxHp = snapshot.HasMaxHp || nextSnapshot.HasMaxHp
            };
            if (resolved.Equals(snapshot))
                continue;

            _observed[instanceId] = resolved;
            RememberEncounterBoss(instanceId, in resolved);
            changed = true;
        }

        if (changed)
            _revision++;
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
            // Expiry only removes the current visibility projection. A later scope
            // reconciliation may legitimately restore activity from encounter history.
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

    private void Remember(int instanceId, long hp, long maxHp, bool hasMaxHp, long observedAtMilliseconds) =>
        RememberCore(instanceId, hp, maxHp, hasMaxHp, Math.Max(0, observedAtMilliseconds));

    private void RememberState(int instanceId, long hp, long maxHp, bool hasMaxHp)
    {
        if (_observed.TryGetValue(instanceId, out var previous))
            RememberCore(instanceId, hp, maxHp, hasMaxHp, previous.LastObservedAtMilliseconds);
    }

    private Snapshot ResolveActivitySnapshot(int instanceId, long observedAtMilliseconds)
    {
        _encounterBosses.TryGetValue(instanceId, out var historical);
        if (entityVitals.TryGet(instanceId, out var vital))
        {
            var hp = Math.Max(0, vital.CurrentHp);
            var maxHp = ResolveMaxHp(instanceId, vital.MaxHp ?? 0);
            var cumulativeLostHp = historical.HasHp && hp < historical.Hp
                ? historical.CumulativeLostHp + historical.Hp - hp
                : historical.CumulativeLostHp;
            return new Snapshot
            {
                InstanceId = instanceId,
                Hp = hp,
                MaxHp = maxHp,
                CumulativeLostHp = cumulativeLostHp,
                LastObservedAtMilliseconds = observedAtMilliseconds,
                HasHp = true,
                HasMaxHp = maxHp > 0
            };
        }

        if (historical.InstanceId != 0)
            return historical with { LastObservedAtMilliseconds = observedAtMilliseconds };

        return new Snapshot
        {
            InstanceId = instanceId,
            Hp = 0,
            MaxHp = 1,
            LastObservedAtMilliseconds = observedAtMilliseconds,
            HasHp = false,
            HasMaxHp = false
        };
    }

    private void RememberCore(int instanceId, long hp, long maxHp, bool hasMaxHp, long observedAtMilliseconds)
    {
        var resolvedHp = Math.Max(0, hp);
        var resolvedMaxHp = hasMaxHp ? Math.Max(1, maxHp) : 0;
        var cumulativeLostHp = 0L;
        var hasPrevious = _observed.TryGetValue(instanceId, out var previous);
        if (hasPrevious && previous.HasHp)
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
            LastObservedAtMilliseconds = observedAtMilliseconds,
            HasHp = true,
            HasMaxHp = hasMaxHp
        };
        if (!hasPrevious || !previous.Equals(snapshot))
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
