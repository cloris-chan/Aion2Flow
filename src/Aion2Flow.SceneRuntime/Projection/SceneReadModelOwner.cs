using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public sealed class SceneReadModelOwner(ObservedEventJournal journal, Guid encounterId, DateTimeOffset sceneStarted, EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat, TimeProvider? timeProvider = null)
{
    private const long BossFocusVisibilityTimeoutMilliseconds = 10_000;
    private readonly Lock _gate = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private DomainEventApplier _applier = new(entities, boundary, metadataRegistry, combat);
    private readonly SceneCombatSnapshotBuilder _snapshotBuilder = new();
    private readonly Dictionary<int, CombatDetailSubscription> _detailSubscriptions = [];
    private readonly Dictionary<int, CombatDetailDelta> _lastDetailDeltas = [];
    private readonly Dictionary<BossDamageContributionKey, long> _bossDamageContributionScratch = [];
    private readonly List<BossDamageContribution> _bossDamageContributionBuffer = [];
    private readonly ObservedEventEnvelope[] _entryBuffer = new ObservedEventEnvelope[256];
    private JournalCursor _cursor = journal.CreateCursor(0);
    private SceneCombatSnapshotAdapter? _adapter;
    private long _lastAppliedBatchOrdinal = -1;
    private long _appliedBatchOrdinal = -1;
    private SnapshotCacheKey _snapshotCacheKey;
    private SceneCombatSnapshot? _snapshotCache;
    private long _snapshotCacheValidUntilMilliseconds = -1;
    private ProjectionCacheStats _projectionCacheStats;

    public SceneReadModelOwner(ObservedEventJournal journal) : this(journal, Guid.NewGuid(), DateTimeOffset.Now)
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, Guid encounterId) : this(journal, encounterId, DateTimeOffset.Now)
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, Guid encounterId, DateTimeOffset sceneStarted) : this(journal, encounterId, sceneStarted, new EntityStore(), new SceneBoundaryStore(), new RuntimeMetadataRegistry(), new CombatStore())
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, Guid encounterId, DateTimeOffset sceneStarted, RuntimeMetadataRegistry metadataRegistry) : this(journal, encounterId, sceneStarted, new EntityStore(), new SceneBoundaryStore(), metadataRegistry, new CombatStore())
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, Guid encounterId, DateTimeOffset sceneStarted, RuntimeMetadataRegistry metadataRegistry, TimeProvider timeProvider) : this(journal, encounterId, sceneStarted, new EntityStore(), new SceneBoundaryStore(), metadataRegistry, new CombatStore(), timeProvider)
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, Guid encounterId, DateTimeOffset sceneStarted, EntityStore entities, SceneBoundaryStore boundary, CombatStore combat) : this(journal, encounterId, sceneStarted, entities, boundary, new RuntimeMetadataRegistry(), combat)
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, EntityStore entities, SceneBoundaryStore boundary, CombatStore combat) : this(journal, Guid.NewGuid(), DateTimeOffset.Now, entities, boundary, new RuntimeMetadataRegistry(), combat)
    {
    }

    public SceneReadModelOwner(ObservedEventJournal journal, EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, CombatStore combat) : this(journal, Guid.NewGuid(), DateTimeOffset.Now, entities, boundary, metadataRegistry, combat)
    {
    }

    public EntityStore Entities => entities;
    public SceneBoundaryStore Boundary => boundary;
    public RuntimeMetadataRegistry MetadataRegistry => metadataRegistry;
    public CombatStore Combat => combat;
    public DomainEventApplier Applier => _applier;
    public BossFocusStore BossFocus => _applier.BossFocus;
    public Guid EncounterId { get; private set; } = encounterId;
    public SceneKind Kind { get; private set; } = SceneKind.Standard;
    public DateTimeOffset SceneStarted { get; private set; } = sceneStarted;
    public long SceneStartObservationOrdinal { get; private set; } = journal.FirstObservationOrdinal;
    public long AppliedObservationOrdinal { get; private set; }
    public long AppliedNextObservationOrdinal => _cursor.NextObservationOrdinal;
    public long AppliedBatchOrdinal => _appliedBatchOrdinal;
    public ProjectionCacheStats ProjectionCacheStats => _projectionCacheStats;

    public SceneCombatSnapshot CreateSnapshot()
    {
        lock (_gate)
        {
            RefreshCore();
            return CreateSnapshotCore();
        }
    }

    public SceneReadModelFrame CreateFrame(int detailCombatantId = 0, bool forceDetailRefresh = false)
    {
        lock (_gate)
        {
            RefreshCore();
            return CreateFrameWithMarkers(detailCombatantId, forceDetailRefresh, null);
        }
    }

    public SceneReadModelFrame CreateFrame(int detailCombatantId, ICombatDetailEventWriter detailWriter, bool forceDetailRefresh = false)
    {
        lock (_gate)
        {
            RefreshCore();
            return CreateFrameWithMarkers(detailCombatantId, forceDetailRefresh, detailWriter);
        }
    }

    public SceneArchivePayload CreateArchivePayload(SceneCombatSnapshot snapshot)
    {
        lock (_gate)
        {
            RefreshCore();
            return SceneArchivePayload.CreateLocked(
                snapshot,
                SceneStarted,
                entities,
                boundary,
                metadataRegistry,
                _applier.BossFocus,
                CreateAdapter(),
                CreateTimelineSegment(isLiveGrowing: false));
        }
    }

    public SceneArchivePayload CreateArchivePayload()
    {
        lock (_gate)
        {
            RefreshCore();
            var snapshot = CreateSnapshotCore();
            return SceneArchivePayload.CreateLocked(
                snapshot,
                SceneStarted,
                entities,
                boundary,
                metadataRegistry,
                _applier.BossFocus,
                CreateAdapter(),
                CreateTimelineSegment(isLiveGrowing: false));
        }
    }

    public SceneArchiveCapture CreateArchiveCapture()
        => CreateArchiveCapture(long.MaxValue);

    internal SceneArchiveCapture CreateArchiveCapture(long endObservationOrdinalExclusive)
    {
        lock (_gate)
        {
            if (endObservationOrdinalExclusive == long.MaxValue)
            {
                RefreshCore();
            }
            else
            {
                if (endObservationOrdinalExclusive < _cursor.NextObservationOrdinal)
                    throw new InvalidOperationException("Cannot create an archive capture before the applied journal cursor.");
                RefreshCore(endObservationOrdinalExclusive, completeBatches: true);
            }
            var snapshot = CreateSnapshotCore();
            var payload = SceneArchivePayload.CreateLocked(snapshot, SceneStarted, entities, boundary, metadataRegistry, _applier.BossFocus, CreateAdapter(), CreateTimelineSegment(isLiveGrowing: false, endObservationOrdinalExclusive));
            return new SceneArchiveCapture(snapshot, payload);
        }
    }

    public SceneJournalSegment CreateLiveTimelineSegment()
    {
        lock (_gate)
        {
            RefreshCore();
            return CreateTimelineSegment(isLiveGrowing: true);
        }
    }

    public T ReadLocked<T>(Func<EntityStore, SceneBoundaryStore, RuntimeMetadataRegistry, CombatStore, T> reader)
    {
        lock (_gate)
        {
            RefreshCore();
            return reader(entities, boundary, metadataRegistry, combat);
        }
    }

    internal T ReadLocked<T>(Func<EntityStore, SceneBoundaryStore, RuntimeMetadataRegistry, CombatStore, SceneCombatSnapshotAdapter, T> reader)
    {
        lock (_gate)
        {
            RefreshCore();
            return reader(entities, boundary, metadataRegistry, combat, CreateAdapter());
        }
    }

    public CombatDetailDelta CreateDetailDelta(SceneCombatSnapshot snapshot, int combatantId, bool forceRefresh = false)
    {
        lock (_gate)
        {
            RefreshCore();
            var adapter = CreateAdapter();
            return CreateDetailDeltaCore(adapter, snapshot, combatantId, forceRefresh);
        }
    }

    public CombatSkillBreakdownSnapshot CreateSkillBreakdown(SceneCombatSnapshot snapshot, int combatantId)
    {
        lock (_gate)
        {
            RefreshCore();
            return CreateAdapter().CreateSkillBreakdown(snapshot, combatantId);
        }
    }

    public void Refresh()
    {
        lock (_gate)
        {
            RefreshCore();
        }
    }

    public int GetActiveBossFocusCount()
    {
        lock (_gate)
        {
            RefreshCore();
            return _applier.BossFocus.GetObservedBosses(GetSceneNowMilliseconds(), BossFocusVisibilityTimeoutMilliseconds).Count;
        }
    }

    public void SetBossFocusTracking(bool enabled)
    {
        lock (_gate)
        {
            RefreshCore();
            _applier.TrackBossFocus = enabled;
            _snapshotCache = null;
            _snapshotCacheValidUntilMilliseconds = -1;
        }
    }

    public void ObserveBossCombatTrigger(int bossInstanceId, long observedAtMilliseconds)
    {
        lock (_gate)
        {
            RefreshCore();
            if (_applier.TrackBossFocus)
                _applier.BossFocus.ApplyCombatActivity(bossInstanceId, observedAtMilliseconds, observedAtMilliseconds);
            _snapshotCache = null;
            _snapshotCacheValidUntilMilliseconds = -1;
        }
    }

    private void RefreshCore() => RefreshCore(long.MaxValue, completeBatches: true);

    private void RefreshCore(long stopBeforeObservationOrdinal, bool completeBatches)
    {
        var lastAppliedBatchOrdinal = _lastAppliedBatchOrdinal;
        while (true)
        {
            if (_cursor.NextObservationOrdinal >= stopBeforeObservationOrdinal)
                break;

            var result = journal.CopyEntries(_cursor, _entryBuffer);
            if (result.Count == 0)
                break;

            var entries = _entryBuffer.AsSpan(0, result.Count);
            var applied = 0;
            foreach (ref readonly var entry in entries)
            {
                if (entry.Stamp.ObservationOrdinal >= stopBeforeObservationOrdinal)
                    break;

                _applier.ApplyEntry(in entry);
                AppliedObservationOrdinal++;
                lastAppliedBatchOrdinal = Math.Max(lastAppliedBatchOrdinal, entry.Stamp.BatchOrdinal);
                applied++;
            }

            if (applied == 0)
                break;

            _cursor = applied == result.Count ? result.Cursor : journal.CreateCursor(entries[applied - 1].Stamp.ObservationOrdinal + 1);
        }

        _lastAppliedBatchOrdinal = lastAppliedBatchOrdinal;
        if (!completeBatches)
            return;

        var completedBatch = Math.Min(journal.LastCompletedBatchOrdinal, _lastAppliedBatchOrdinal);
        if (completedBatch > _appliedBatchOrdinal)
        {
            _applier.CompleteBatch(completedBatch);
            _appliedBatchOrdinal = completedBatch;
        }
    }

    private SceneReadModelFrame CreateFrameWithMarkers(int detailCombatantId, bool forceDetailRefresh, ICombatDetailEventWriter? detailWriter) => CreateFrameCore(detailCombatantId, forceDetailRefresh, detailWriter);

    private SceneReadModelFrame CreateFrameCore(int detailCombatantId, bool forceDetailRefresh, ICombatDetailEventWriter? detailWriter)
    {
        var snapshot = CreateSnapshotCore();
        SceneCombatSnapshotAdapter? adapter = null;
        var bossDamageContributions = snapshot.BossFocuses.Count == 0 ? [] : CreateBossDamageContributions(adapter = CreateAdapter(), snapshot);
        CombatDetailDelta? detail = null;
        CombatDetailUpdateResult detailUpdate = default;
        if (detailCombatantId > 0)
        {
            adapter ??= CreateAdapter();
            if (detailWriter is not null)
            {
                detailUpdate = CreateDetailUpdateCore(adapter, snapshot, detailCombatantId, forceDetailRefresh, detailWriter);
            }
            else
            {
                detail = CreateDetailDeltaCore(adapter, snapshot, detailCombatantId, forceDetailRefresh);
            }
        }

        return new SceneReadModelFrame
        {
            Snapshot = snapshot,
            ReadModelRevision = snapshot.ReadModelRevision,
            DetailCombatantId = detailCombatantId,
            Detail = detail,
            DetailUpdate = detailUpdate,
            BossFocuses = snapshot.BossFocuses,
            BossDamageContributions = bossDamageContributions
        };
    }

    private SceneCombatSnapshot CreateSnapshotCore()
    {
        var now = GetSceneNowMilliseconds();
        var key = SnapshotCacheKey.From(EncounterId, entities, boundary, combat, _applier.BossFocus);
        if (_snapshotCache is not null && _snapshotCacheKey == key && now <= _snapshotCacheValidUntilMilliseconds)
        {
            _projectionCacheStats = _projectionCacheStats.WithHit();
            return _snapshotCache;
        }

        var adapter = CreateAdapter();
        _snapshotBuilder.Reset(EncounterId, Kind, combat.Combatants.Count, 0);
        adapter.BuildSnapshot(_snapshotBuilder);
        ApplyBossFocusSnapshots(_snapshotBuilder, now);
        ApplyBossNpcCodes(_snapshotBuilder);
        var snapshot = _snapshotBuilder.ToSnapshot(combat.Revision);
        _snapshotCacheKey = SnapshotCacheKey.From(EncounterId, entities, boundary, combat, _applier.BossFocus);
        _snapshotCache = snapshot;
        _snapshotCacheValidUntilMilliseconds = GetSnapshotCacheValidUntilMilliseconds(snapshot);
        _projectionCacheStats = _projectionCacheStats.WithMiss();
        return snapshot;
    }

    private SceneCombatSnapshotAdapter CreateAdapter() => _adapter ??= new(entities, combat, boundary, _applier.BossFocus, EncounterId);

    private SceneJournalSegment CreateTimelineSegment(bool isLiveGrowing, long? endObservationOrdinalExclusive = null)
    {
        var end = Math.Clamp(endObservationOrdinalExclusive ?? AppliedNextObservationOrdinal, SceneStartObservationOrdinal, AppliedNextObservationOrdinal);
        return new SceneJournalSegment(journal, SceneStartObservationOrdinal, end, isLiveGrowing);
    }

    private BossDamageContribution[] CreateBossDamageContributions(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot)
    {
        if (snapshot.BossFocuses.Count == 0 || snapshot.Combatants.Count == 0)
            return [];

        _bossDamageContributionScratch.Clear();
        _bossDamageContributionBuffer.Clear();
        foreach (var pair in combat.Pairs.Values)
        {
            if (pair.TotalDamage <= 0 || !IsBossFocus(snapshot, pair.TargetId))
                continue;

            var sourceId = adapter.ResolveDetailCombatantId(pair.SourceId);
            if (sourceId <= 0 || !snapshot.Combatants.TryGetValue(sourceId, out var sourceMetrics) || !sourceMetrics.IsVisiblePlayerCombatant || sourceMetrics.CharacterClass is null)
                continue;

            var key = new BossDamageContributionKey(pair.TargetId, sourceId);
            ref var damage = ref CollectionsMarshal.GetValueRefOrAddDefault(_bossDamageContributionScratch, key, out _);
            damage += pair.TotalDamage;
        }

        if (_bossDamageContributionScratch.Count == 0)
            return [];

        foreach (var (key, damage) in _bossDamageContributionScratch)
            _bossDamageContributionBuffer.Add(new BossDamageContribution(key.BossId, key.SourceCombatantId, damage));
        _bossDamageContributionBuffer.Sort(static (left, right) =>
        {
            var cmp = left.BossId.CompareTo(right.BossId);
            return cmp != 0 ? cmp : right.DamageAmount.CompareTo(left.DamageAmount);
        });
        return [.. _bossDamageContributionBuffer];
    }

    private static bool IsBossFocus(SceneCombatSnapshot snapshot, int instanceId)
    {
        var bosses = snapshot.BossFocuses;
        for (var i = 0; i < bosses.Count; i++)
        {
            if (bosses[i].InstanceId == instanceId)
                return true;
        }

        return false;
    }

    private CombatDetailDelta CreateDetailDeltaCore(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot, int combatantId, bool forceRefresh)
    {
        var subscription = GetDetailSubscription(combatantId);
        if (forceRefresh || !_lastDetailDeltas.ContainsKey(combatantId))
        {
            var cold = subscription.CreateSnapshotDelta(adapter, snapshot);
            _lastDetailDeltas[combatantId] = cold;
            return cold;
        }

        if (subscription.Poll(adapter, snapshot) is { } delta)
        {
            _lastDetailDeltas[combatantId] = delta;
            return delta;
        }

        return _lastDetailDeltas[combatantId];
    }

    private CombatDetailUpdateResult CreateDetailUpdateCore(SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot, int combatantId, bool forceRefresh, ICombatDetailEventWriter writer)
    {
        var subscription = GetDetailSubscription(combatantId);
        return subscription.Update(adapter, snapshot, forceRefresh, writer);
    }

    private long GetSceneNowMilliseconds() =>
        Math.Max(0, (_timeProvider.GetUtcNow() - SceneStarted).Ticks / TimeSpan.TicksPerMillisecond);

    private static long GetSnapshotCacheValidUntilMilliseconds(SceneCombatSnapshot snapshot)
    {
        var validUntil = long.MaxValue;
        for (var i = 0; i < snapshot.BossFocuses.Count; i++)
        {
            var observedAt = snapshot.BossFocuses[i].LastObservedAtMilliseconds;
            var expiresAt = observedAt > long.MaxValue - BossFocusVisibilityTimeoutMilliseconds
                ? long.MaxValue
                : observedAt + BossFocusVisibilityTimeoutMilliseconds;
            validUntil = Math.Min(validUntil, expiresAt);
        }

        return validUntil;
    }

    private void ApplyBossFocusSnapshots(SceneCombatSnapshotBuilder builder, long now)
    {
        var bosses = _applier.BossFocus.GetObservedBosses(now, BossFocusVisibilityTimeoutMilliseconds);
        for (var i = 0; i < bosses.Count; i++)
        {
            var boss = bosses[i];
            builder.AddBossFocus(new SceneBossFocusSnapshot
            {
                InstanceId = boss.InstanceId,
                Hp = boss.Hp,
                MaxHp = boss.MaxHp,
                CumulativeLostHp = boss.CumulativeLostHp,
                LastObservedAtMilliseconds = boss.LastObservedAtMilliseconds,
                HasHp = boss.HasHp,
                HasMaxHp = boss.HasMaxHp
            });
        }
    }

    private void ApplyBossNpcCodes(SceneCombatSnapshotBuilder builder)
    {
        var bosses = _applier.BossFocus.GetEncounterBosses();
        for (var i = 0; i < bosses.Count; i++)
        {
            var instanceId = bosses[i].InstanceId;
            if (entities.TryGet(instanceId, out var entity) && entity.NpcCode is int npcCode)
            {
                builder.AddBossNpcCode(npcCode);
                continue;
            }

            if (metadataRegistry.TryGetNpcCode(instanceId, out var metadataNpcCode))
                builder.AddBossNpcCode(metadataNpcCode);
        }
    }

    private CombatDetailSubscription GetDetailSubscription(int combatantId)
    {
        if (!_detailSubscriptions.TryGetValue(combatantId, out var subscription))
        {
            subscription = new CombatDetailSubscription(combat, combatantId);
            _detailSubscriptions[combatantId] = subscription;
        }

        return subscription;
    }

    public void ResetCombat(Guid encounterId, long startOrdinal) => ResetCombat(encounterId, startOrdinal, DateTimeOffset.Now);

    public void ResetCombat(Guid encounterId, long startOrdinal, DateTimeOffset sceneStarted)
        => ResetCombat(encounterId, startOrdinal, sceneStarted, SceneKind.Standard, trackBossFocus: true);

    public void ResetCombat(Guid encounterId, long startOrdinal, DateTimeOffset sceneStarted, SceneKind kind, bool trackBossFocus)
    {
        lock (_gate)
        {
            RefreshCore(startOrdinal, completeBatches: false);
            EncounterId = encounterId;
            Kind = kind;
            SceneStarted = sceneStarted;
            SceneStartObservationOrdinal = startOrdinal;
            combat.Clear();
            _applier = new DomainEventApplier(entities, boundary, metadataRegistry, combat);
            _applier.TrackBossFocus = trackBossFocus;
            _adapter = null;
            _detailSubscriptions.Clear();
            _lastDetailDeltas.Clear();
            _cursor = journal.CreateCursor(startOrdinal);
            AppliedObservationOrdinal = 0;
            _appliedBatchOrdinal = journal.LastCompletedBatchOrdinal;
            _lastAppliedBatchOrdinal = _appliedBatchOrdinal;
            _snapshotCache = null;
            _snapshotCacheKey = default;
            _snapshotCacheValidUntilMilliseconds = -1;
        }
    }
}

public readonly record struct SceneArchiveCapture(SceneCombatSnapshot Snapshot, SceneArchivePayload Payload);

public readonly record struct BossDamageContribution(int BossId, int SourceCombatantId, long DamageAmount);

internal readonly record struct BossDamageContributionKey(int BossId, int SourceCombatantId);

internal readonly record struct SnapshotCacheKey(Guid EncounterId, long CombatRevision, long EntityRevision, long BoundaryRevision, long SceneTransitionRevision, long BossFocusRevision)
{
    public static SnapshotCacheKey From(Guid encounterId, EntityStore entities, SceneBoundaryStore boundary, CombatStore combat, BossFocusStore bossFocus) =>
        new(encounterId, combat.Revision, entities.Revision, boundary.Revision, boundary.SceneTransitionRevision, bossFocus.Revision);
}

public readonly record struct ProjectionCacheStats(long SnapshotBuilds, long SnapshotCacheHits)
{
    public ProjectionCacheStats WithMiss() => new(SnapshotBuilds + 1, SnapshotCacheHits);
    public ProjectionCacheStats WithHit() => new(SnapshotBuilds, SnapshotCacheHits + 1);
}

public readonly record struct SceneReadModelFrame
{
    public SceneReadModelFrame()
    {
        Snapshot = SceneCombatSnapshot.Empty;
        ReadModelRevision = 0;
        DetailCombatantId = 0;
        Detail = null;
        DetailUpdate = default;
        BossFocuses = default;
        BossDamageContributions = [];
    }

    public SceneCombatSnapshot Snapshot { get; init; }
    public long ReadModelRevision { get; init; }
    public int DetailCombatantId { get; init; }
    public CombatDetailDelta? Detail { get; init; }
    public CombatDetailUpdateResult DetailUpdate { get; init; }
    public SnapshotList<SceneBossFocusSnapshot> BossFocuses { get; init; }
    public IReadOnlyList<BossDamageContribution> BossDamageContributions { get; init; }
}
