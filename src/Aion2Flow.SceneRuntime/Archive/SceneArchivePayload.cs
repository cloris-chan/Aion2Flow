using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Archive;

public sealed class SceneArchivePayload
{
    private readonly ArchivePayloadIndex _detailIndex;

    private SceneArchivePayload(
        SceneKind kind,
        DateTimeOffset sceneStarted,
        SceneJournalSegment timelineSegment,
        CombatEventSegment combatEvents,
        CombatMechanicEventRecord[] mechanicEvents,
        CombatResourceEventRecord[] resourceEvents,
        SceneIdentityScope identityScope,
        DirectedPairSnapshot[] pairs,
        CombatantSummary[] combatants,
        SceneArchiveEntityIdentity[] entities,
        SceneArchiveEntityVital[] entityVitals,
        SceneArchiveBossFocus[] bosses,
        int[] bossNpcCodes,
        ArchivePayloadIndex detailIndex)
    {
        Kind = kind;
        SceneStarted = sceneStarted;
        TimelineSegment = timelineSegment;
        CombatEvents = combatEvents;
        MechanicEvents = new SnapshotList<CombatMechanicEventRecord>(mechanicEvents);
        ResourceEvents = new SnapshotList<CombatResourceEventRecord>(resourceEvents);
        IdentityScope = identityScope;
        Pairs = new SnapshotList<DirectedPairSnapshot>(pairs);
        Combatants = new SnapshotList<CombatantSummary>(combatants);
        Entities = new SnapshotList<SceneArchiveEntityIdentity>(entities);
        EntityVitals = new SnapshotList<SceneArchiveEntityVital>(entityVitals);
        Bosses = new SnapshotList<SceneArchiveBossFocus>(bosses);
        BossNpcCodes = new SnapshotList<int>(bossNpcCodes);
        _detailIndex = detailIndex;
    }

    public SceneKind Kind { get; }
    public DateTimeOffset SceneStarted { get; }
    public SceneJournalSegment TimelineSegment { get; }
    public CombatEventSegment CombatEvents { get; }
    public SnapshotList<CombatMechanicEventRecord> MechanicEvents { get; }
    public SnapshotList<CombatResourceEventRecord> ResourceEvents { get; }
    public SceneIdentityScope IdentityScope { get; }
    public SnapshotList<DirectedPairSnapshot> Pairs { get; }
    public SnapshotList<CombatantSummary> Combatants { get; }
    public SnapshotList<SceneArchiveEntityIdentity> Entities { get; }
    public SnapshotList<SceneArchiveEntityVital> EntityVitals { get; }
    public SnapshotList<SceneArchiveBossFocus> Bosses { get; }
    public SnapshotList<int> BossNpcCodes { get; }

    internal IReadOnlyDictionary<int, long[]> MetricEventOrdinalsByCombatant => DetailIndex.MetricEventOrdinalsByCombatant;
    internal IReadOnlyDictionary<int, int[]> MechanicEventIndexesByCombatant => DetailIndex.MechanicEventIndexesByCombatant;
    internal IReadOnlyDictionary<int, int[]> ResourceEventIndexesByCombatant => DetailIndex.ResourceEventIndexesByCombatant;
    internal IReadOnlyDictionary<int, DirectedPairKey[]> OutgoingPairsByCombatant => DetailIndex.OutgoingPairsByCombatant;
    internal IReadOnlyDictionary<int, DirectedPairKey[]> IncomingPairsByCombatant => DetailIndex.IncomingPairsByCombatant;
    internal IReadOnlyDictionary<int, CombatantSummary> CombatantsById => DetailIndex.CombatantsById;

    private ArchivePayloadIndex DetailIndex => _detailIndex;

    internal static SceneArchivePayload CreateLocked(SceneCombatSnapshot snapshot, DateTimeOffset sceneStarted, EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, BossFocusStore bossFocus, EntityVitalStore entityVitals, CombatStore combat, MechanicStore mechanics, ResourceStore resources, SceneCombatSnapshotAdapter adapter, SceneJournalSegment timelineSegment)
    {
        var archivedSnapshot = snapshot;
        var entityIds = new HashSet<int>();

        var snapshotCombatants = archivedSnapshot.Combatants.AsSpan();
        foreach (ref readonly var combatant in snapshotCombatants)
        {
            var combatantId = combatant.Id;
            AddEntity(entityIds, combatantId);
        }

        if (archivedSnapshot.TargetObservation?.InstanceId is int targetId)
            AddEntity(entityIds, targetId);

        var combatEvents = combat.FreezeEventSegment();
        var mechanicEvents = mechanics.Events.ToArray();
        var resourceEvents = resources.Events.ToArray();
        var combatArchive = BuildCombatArchive(archivedSnapshot, combatEvents, mechanicEvents, resourceEvents, adapter, entityIds);
        AddResourceEventEntities(entityIds, resourceEvents);
        AddKnownEntities(entityIds, entities);
        AddKnownEntityVitals(entityIds, entityVitals);

        var identities = BuildIdentities(entities, entityIds);
        var vitals = BuildEntityVitals(entityVitals, entityIds);
        var identityScope = BuildIdentityScope(entityIds, identities, boundary, metadataRegistry);
        var pairs = combatArchive.Pairs;
        var combatants = BuildCombatants(pairs, combatArchive.ResourceEventIndexesByCombatant, resourceEvents);
        var bosses = BuildBosses(bossFocus);
        var detailIndex = ArchivePayloadIndex.Create(
            combatArchive.MetricEventOrdinalsByCombatant,
            combatArchive.MechanicEventIndexesByCombatant,
            combatArchive.ResourceEventIndexesByCombatant,
            combatArchive.ProjectedMetricSources,
            combatArchive.ProjectedMechanicSources,
            combatArchive.ProjectedResourceSources,
            pairs,
            combatants);

        return new SceneArchivePayload(
            archivedSnapshot.Kind,
            sceneStarted,
            timelineSegment,
            combatEvents,
            mechanicEvents,
            resourceEvents,
            identityScope,
            pairs,
            combatants,
            identities,
            vitals,
            bosses,
            archivedSnapshot.BossNpcCodes.AsSpan().ToArray(),
            detailIndex);
    }

    public CombatDetailDelta CreateDetailDelta(int combatantId)
    {
        if (combatantId <= 0)
        {
            return new CombatDetailDelta
            {
                CombatantId = combatantId
            };
        }

        var detailIndex = DetailIndex;
        var metricEventOrdinals = detailIndex.GetMetricEventOrdinals(combatantId);
        var mechanicEventIndexes = detailIndex.GetMechanicEventIndexes(combatantId);
        var resourceEventIndexes = detailIndex.GetResourceEventIndexes(combatantId);
        var combatant = detailIndex.GetCombatant(combatantId);
        if (metricEventOrdinals.Length == 0 &&
            mechanicEventIndexes.Length == 0 &&
            resourceEventIndexes.Length == 0 &&
            combatant is null)
        {
            return new CombatDetailDelta
            {
                CombatantId = combatantId
            };
        }

        var metricEvents = new CombatMetricDetailEvent[metricEventOrdinals.Length];
        var metricRevision = 0L;
        for (var i = 0; i < metricEventOrdinals.Length; i++)
        {
            var eventOrdinal = metricEventOrdinals[i];
            ref readonly var record = ref CombatEvents.GetEvent(eventOrdinal);
            var sourceId = detailIndex.ResolveMetricSourceId(eventOrdinal, record.SourceId);
            metricEvents[i] = new CombatMetricDetailEvent(
                CombatDetailFact.Create(
                    record.Observation,
                    sourceId,
                    record.TargetId,
                    record.ObservedAtMilliseconds,
                    record.SourceObservationOrdinal,
                    record.Revision,
                    record.EventKey,
                    record.Raw),
                record.Contribution);
            metricRevision = Math.Max(metricRevision, record.Revision);
        }

        var mechanicEvents = new CombatMechanicDetailEvent[mechanicEventIndexes.Length];
        var mechanicRevision = 0L;
        for (var i = 0; i < mechanicEventIndexes.Length; i++)
        {
            var eventIndex = mechanicEventIndexes[i];
            var record = MechanicEvents[eventIndex];
            var sourceId = detailIndex.ResolveMechanicSourceId(eventIndex, record.SourceId);
            mechanicEvents[i] = new CombatMechanicDetailEvent(
                CombatDetailFact.Create(
                    record.Observation,
                    sourceId,
                    record.TargetId,
                    record.ObservedAtMilliseconds,
                    record.SourceObservationOrdinal,
                    record.Revision,
                    record.EventKey,
                    record.Raw),
                record.Mechanic);
            mechanicRevision = Math.Max(mechanicRevision, record.Revision);
        }

        var resourceEvents = new CombatResourceDetailEvent[resourceEventIndexes.Length];
        var resourceRevision = 0L;
        for (var i = 0; i < resourceEventIndexes.Length; i++)
        {
            var eventIndex = resourceEventIndexes[i];
            var record = ResourceEvents[eventIndex];
            var sourceId = detailIndex.ResolveResourceSourceId(eventIndex, record.SourceId);
            resourceEvents[i] = new CombatResourceDetailEvent(
                CombatDetailFact.Create(
                    record.Observation,
                    sourceId,
                    record.TargetId,
                    record.ObservedAtMilliseconds,
                    record.SourceObservationOrdinal,
                    record.Revision,
                    record.EventKey,
                    record.Raw),
                record.Resource);
            resourceRevision = Math.Max(resourceRevision, record.Revision);
        }

        return new CombatDetailDelta
        {
            CombatantId = combatantId,
            Revision = SaturatingAdd(metricRevision, mechanicRevision, resourceRevision),
            OutgoingPairs = detailIndex.GetOutgoingPairs(combatantId),
            IncomingPairs = detailIndex.GetIncomingPairs(combatantId),
            MetricEvents = metricEvents,
            MechanicEvents = mechanicEvents,
            ResourceEvents = resourceEvents,
            Combatant = combatant
        };
    }

    private static long SaturatingAdd(long first, long second, long third)
    {
        var sum = first > long.MaxValue - second ? long.MaxValue : first + second;
        return sum > long.MaxValue - third ? long.MaxValue : sum + third;
    }

    private static void AddEntity(HashSet<int> entityIds, int entityId)
    {
        if (entityId > 0)
            entityIds.Add(entityId);
    }

    private static void AddKnownEntities(HashSet<int> entityIds, EntityStore entities)
    {
        foreach (var entityId in entities.Entities.Keys)
            AddEntity(entityIds, entityId);
    }

    private static void AddKnownEntityVitals(HashSet<int> entityIds, EntityVitalStore entityVitals)
    {
        foreach (var entityId in entityVitals.States.Keys)
            AddEntity(entityIds, entityId);
    }

    private static void AddResourceEventEntities(HashSet<int> entityIds, CombatResourceEventRecord[] resourceEvents)
    {
        for (var i = 0; i < resourceEvents.Length; i++)
        {
            AddEntity(entityIds, resourceEvents[i].SourceId);
            AddEntity(entityIds, resourceEvents[i].TargetId);
        }
    }

    private static SceneArchiveEntityVital[] BuildEntityVitals(EntityVitalStore entityVitals, HashSet<int> entityIds)
    {
        if (entityVitals.States.Count == 0 || entityIds.Count == 0)
            return [];

        var result = new SceneArchiveEntityVital[Math.Min(entityVitals.States.Count, entityIds.Count)];
        var count = 0;
        foreach (var entityId in entityIds)
        {
            if (!entityVitals.TryGet(entityId, out var vital))
                continue;

            result[count++] = SceneArchiveEntityVital.From(in vital);
        }

        if (count != result.Length)
            Array.Resize(ref result, count);
        Array.Sort(result, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return result;
    }

    private static SceneArchiveEntityIdentity[] BuildIdentities(EntityStore entities, HashSet<int> entityIds)
    {
        if (entityIds.Count == 0)
            return [];

        var ids = new int[entityIds.Count];
        var idIndex = 0;
        foreach (var entityId in entityIds)
            ids[idIndex++] = entityId;
        Array.Sort(ids);

        var result = new SceneArchiveEntityIdentity[ids.Length];
        var count = 0;
        for (var i = 0; i < ids.Length; i++)
        {
            var entityId = ids[i];
            if (!entities.TryGet(entityId, out var entity))
                continue;

            result[count++] = SceneArchiveEntityIdentity.From(entity);
        }

        if (count == result.Length)
            return result;

        Array.Resize(ref result, count);
        return result;
    }

    private static SceneIdentityScope BuildIdentityScope(HashSet<int> entityIds, SceneArchiveEntityIdentity[] identities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry)
    {
        var builder = new SceneIdentityScopeBuilder();
        builder.Reset(entityIds.Count);
        foreach (var entityId in entityIds)
        {
            if (metadataRegistry.TryGetPcMetadata(entityId, out var pcMetadata))
                builder.AddPcMetadata(pcMetadata);

            if (metadataRegistry.TryGetNpcCode(entityId, out var registryNpcCode))
                builder.AddNpcCode(entityId, registryNpcCode);
        }

        for (var i = 0; i < identities.Length; i++)
        {
            var identity = identities[i];
            if (!metadataRegistry.TryGetNpcCode(identity.EntityId, out _) && identity.NpcCode is int entityNpcCode)
                builder.AddNpcCode(identity.EntityId, entityNpcCode);
        }

        if (boundary.CurrentMapInstanceId > 0 && boundary.CurrentMapId > 0)
            builder.AddMapCode(boundary.CurrentMapInstanceId, boundary.CurrentMapId);

        return builder.ToScope();
    }

    private static CombatArchiveBuild BuildCombatArchive(
        SceneCombatSnapshot snapshot,
        CombatEventSegment events,
        CombatMechanicEventRecord[] mechanicEvents,
        CombatResourceEventRecord[] resourceEvents,
        SceneCombatSnapshotAdapter adapter,
        HashSet<int> entityIds)
    {
        var metricEventOrdinalBuilders = new Dictionary<int, LongArrayBuilder>();
        var mechanicEventIndexBuilders = new Dictionary<int, IntArrayBuilder>();
        var resourceEventIndexBuilders = new Dictionary<int, IntArrayBuilder>();
        var projectedMetricSources = new Dictionary<long, int>();
        var projectedMechanicSources = new Dictionary<int, int>();
        var projectedResourceSources = new Dictionary<int, int>();
        var pairs = new Dictionary<DirectedPairKey, PairAccumulator>();
        for (var eventOrdinal = events.StartEventOrdinal; eventOrdinal < events.EndEventOrdinalExclusive; eventOrdinal++)
        {
            ref readonly var record = ref events.GetEvent(eventOrdinal);
            if (!adapter.TryResolveMetricDetailEventSource(snapshot, in record, out var sourceId))
                continue;

            var targetId = record.TargetId;
            AddEntity(entityIds, sourceId);
            AddEntity(entityIds, targetId);
            AddEventOrdinal(metricEventOrdinalBuilders, sourceId, eventOrdinal);
            if (targetId != sourceId)
                AddEventOrdinal(metricEventOrdinalBuilders, targetId, eventOrdinal);

            if (sourceId != record.SourceId)
                projectedMetricSources.Add(eventOrdinal, sourceId);

            if (sourceId <= 0 || targetId <= 0)
                continue;

            var key = new DirectedPairKey(sourceId, targetId);
            if (!pairs.TryGetValue(key, out var pair))
            {
                pair = new PairAccumulator(key);
                pairs[key] = pair;
            }

            pair.Apply(in record);
        }

        for (var i = 0; i < mechanicEvents.Length; i++)
        {
            ref readonly var record = ref mechanicEvents[i];
            if (record.ObservedAtMilliseconds < snapshot.EncounterStartTime ||
                record.ObservedAtMilliseconds > snapshot.EncounterEndTime ||
                adapter.IsSummonMechanicTarget(record.SourceId, record.TargetId, record.Mechanic))
            {
                continue;
            }

            var sourceId = adapter.ResolveDetailCombatantId(record.SourceId);
            var targetId = record.TargetId;
            AddEntity(entityIds, sourceId);
            AddEntity(entityIds, targetId);
            AddEventIndex(mechanicEventIndexBuilders, sourceId, i);
            if (targetId != sourceId)
                AddEventIndex(mechanicEventIndexBuilders, targetId, i);
            if (sourceId != record.SourceId)
                projectedMechanicSources.Add(i, sourceId);
            if (sourceId <= 0 || targetId <= 0)
                continue;

            var key = new DirectedPairKey(sourceId, targetId);
            if (!pairs.TryGetValue(key, out var pair))
            {
                pair = new PairAccumulator(key);
                pairs[key] = pair;
            }

            pair.Apply(in record);
        }

        for (var i = 0; i < resourceEvents.Length; i++)
        {
            ref readonly var record = ref resourceEvents[i];
            if (record.ObservedAtMilliseconds < snapshot.EncounterStartTime ||
                record.ObservedAtMilliseconds > snapshot.EncounterEndTime)
            {
                continue;
            }

            var sourceId = adapter.ResolveDetailCombatantId(record.SourceId);
            var targetId = record.TargetId;
            AddEntity(entityIds, sourceId);
            AddEntity(entityIds, targetId);
            AddEventIndex(resourceEventIndexBuilders, sourceId, i);
            if (targetId != sourceId)
                AddEventIndex(resourceEventIndexBuilders, targetId, i);
            if (sourceId != record.SourceId)
                projectedResourceSources.Add(i, sourceId);
            if (sourceId <= 0 || targetId <= 0)
                continue;

            var key = new DirectedPairKey(sourceId, targetId);
            if (!pairs.TryGetValue(key, out var pair))
            {
                pair = new PairAccumulator(key);
                pairs[key] = pair;
            }

            pair.Apply(in record);
        }

        var metricEventOrdinalsByCombatant = FreezeEventOrdinals(metricEventOrdinalBuilders);
        var mechanicEventIndexesByCombatant = FreezeEventIndexes(mechanicEventIndexBuilders);
        var resourceEventIndexesByCombatant = FreezeEventIndexes(resourceEventIndexBuilders);

        DirectedPairSnapshot[] pairSnapshots;
        if (pairs.Count == 0)
        {
            pairSnapshots = [];
        }
        else
        {
            pairSnapshots = new DirectedPairSnapshot[pairs.Count];
            var index = 0;
            foreach (var pair in pairs.Values)
                pairSnapshots[index++] = pair.ToSnapshot();

            Array.Sort(pairSnapshots, static (left, right) =>
            {
                var cmp = left.Key.SourceId.CompareTo(right.Key.SourceId);
                return cmp != 0 ? cmp : left.Key.TargetId.CompareTo(right.Key.TargetId);
            });
        }

        return new CombatArchiveBuild(
            pairSnapshots,
            metricEventOrdinalsByCombatant,
            mechanicEventIndexesByCombatant,
            resourceEventIndexesByCombatant,
            projectedMetricSources,
            projectedMechanicSources,
            projectedResourceSources);
    }

    private static void AddEventOrdinal(Dictionary<int, LongArrayBuilder> builders, int combatantId, long eventOrdinal)
    {
        if (combatantId <= 0)
            return;

        ref var builder = ref CollectionsMarshal.GetValueRefOrAddDefault(builders, combatantId, out _);
        builder.Add(eventOrdinal);
    }

    private static void AddEventIndex(Dictionary<int, IntArrayBuilder> builders, int combatantId, int eventIndex)
    {
        if (combatantId <= 0)
            return;

        ref var builder = ref CollectionsMarshal.GetValueRefOrAddDefault(builders, combatantId, out _);
        builder.Add(eventIndex);
    }

    private static Dictionary<int, long[]> FreezeEventOrdinals(Dictionary<int, LongArrayBuilder> builders)
    {
        var result = new Dictionary<int, long[]>(builders.Count);
        foreach (var (combatantId, builder) in builders)
            result.Add(combatantId, builder.ToArray());
        return result;
    }

    private static Dictionary<int, int[]> FreezeEventIndexes(Dictionary<int, IntArrayBuilder> builders)
    {
        var result = new Dictionary<int, int[]>(builders.Count);
        foreach (var (combatantId, builder) in builders)
            result.Add(combatantId, builder.ToArray());
        return result;
    }

    private static CombatantSummary[] BuildCombatants(
        DirectedPairSnapshot[] pairs,
        IReadOnlyDictionary<int, int[]> resourceEventIndexesByCombatant,
        CombatResourceEventRecord[] resourceEvents)
    {
        var combatants = new Dictionary<int, CombatantAccumulator>();
        for (var i = 0; i < pairs.Length; i++)
        {
            var pair = pairs[i];
            if (pair.Key.SourceId > 0)
            {
                if (!combatants.TryGetValue(pair.Key.SourceId, out var source))
                {
                    source = new CombatantAccumulator(pair.Key.SourceId);
                    combatants[pair.Key.SourceId] = source;
                }

                source.ApplyOutgoing(pair);
            }

            if (pair.Key.TargetId > 0)
            {
                if (!combatants.TryGetValue(pair.Key.TargetId, out var target))
                {
                    target = new CombatantAccumulator(pair.Key.TargetId);
                    combatants[pair.Key.TargetId] = target;
                }

                target.ApplyIncoming(pair);
            }
        }

        foreach (var (combatantId, eventIndexes) in resourceEventIndexesByCombatant)
        {
            if (!combatants.TryGetValue(combatantId, out var combatant))
            {
                combatant = new CombatantAccumulator(combatantId);
                combatants.Add(combatantId, combatant);
            }
            for (var i = 0; i < eventIndexes.Length; i++)
                combatant.Apply(in resourceEvents[eventIndexes[i]]);
        }

        if (combatants.Count == 0)
            return [];

        var result = new CombatantSummary[combatants.Count];
        var index = 0;
        foreach (var combatant in combatants.Values)
            result[index++] = combatant.ToSummary();

        Array.Sort(result, static (left, right) => left.CombatantId.CompareTo(right.CombatantId));
        return result;
    }

    private static SceneArchiveBossFocus[] BuildBosses(BossFocusStore bossFocus)
    {
        var bosses = bossFocus.GetEncounterBosses();
        if (bosses.Count == 0)
            return [];

        var result = new SceneArchiveBossFocus[bosses.Count];
        for (var i = 0; i < bosses.Count; i++)
        {
            var boss = bosses[i];
            result[i] = new SceneArchiveBossFocus
            {
                InstanceId = boss.InstanceId,
                Hp = boss.Hp,
                MaxHp = boss.MaxHp,
                CumulativeLostHp = boss.CumulativeLostHp,
                LastObservedAtMilliseconds = boss.LastObservedAtMilliseconds,
                HasHp = boss.HasHp,
                HasMaxHp = boss.HasMaxHp
            };
        }

        return result;
    }

    private sealed class PairAccumulator(DirectedPairKey key)
    {
        private long _totalDamage;
        private long _totalHealing;
        private long _totalShield;
        private long _totalShieldAbsorbed;
        private int _shieldCount;
        private int _shieldAbsorbedCount;
        private int _hitCount;
        private int _attemptCount;
        private int _evadeCount;
        private int _invincibleCount;
        private int _multiHitCount;
        private int _multiHitSubCount;
        private int _lastSkillCode;
        private long _firstObserved;
        private long _lastObserved;
        private bool _hasObserved;
        private long _revision;

        public void Apply(in CombatEventRecord e)
        {
            var observation = e.Observation;
            var contribution = e.Contribution;

            switch (contribution.Metric)
            {
                case CombatMetricKind.Damage:
                    _totalDamage += contribution.Amount;
                    break;
                case CombatMetricKind.Healing:
                    _totalHealing += contribution.Amount;
                    break;
                case CombatMetricKind.ShieldGranted:
                    _totalShield += contribution.Amount;
                    _shieldCount++;
                    break;
                case CombatMetricKind.ShieldAbsorbed:
                    _totalShieldAbsorbed += contribution.Amount;
                    _shieldAbsorbedCount++;
                    break;
            }
            _lastSkillCode = observation.SkillCode;
            _revision = Math.Max(_revision, e.Revision);
            ApplyObserved(e.ObservedAtMilliseconds);
        }

        public void Apply(in CombatMechanicEventRecord e)
        {
            var mechanic = e.Mechanic;
            _hitCount += mechanic.HitCount;
            _attemptCount += mechanic.AttemptCount;
            _evadeCount += mechanic.EvadeCount;
            _invincibleCount += mechanic.InvincibleCount;
            _multiHitCount += mechanic.MultiHitCount;
            _multiHitSubCount += mechanic.MultiHitSubCount;
            _lastSkillCode = e.Observation.SkillCode;
            _revision = Math.Max(_revision, e.Revision);
            ApplyObserved(e.ObservedAtMilliseconds);
        }

        public void Apply(in CombatResourceEventRecord e)
        {
            _lastSkillCode = e.Observation.SkillCode;
            _revision = Math.Max(_revision, e.Revision);
            ApplyObserved(e.ObservedAtMilliseconds);
        }

        private void ApplyObserved(long observedAt)
        {
            if (_hasObserved)
            {
                _firstObserved = Math.Min(_firstObserved, observedAt);
                _lastObserved = Math.Max(_lastObserved, observedAt);
            }
            else
            {
                _firstObserved = observedAt;
                _lastObserved = observedAt;
                _hasObserved = true;
            }
        }

        public DirectedPairSnapshot ToSnapshot() => new()
        {
            Key = key,
            TotalDamage = _totalDamage,
            TotalHealing = _totalHealing,
            TotalShield = _totalShield,
            TotalShieldAbsorbed = _totalShieldAbsorbed,
            ShieldCount = _shieldCount,
            ShieldAbsorbedCount = _shieldAbsorbedCount,
            HitCount = _hitCount,
            AttemptCount = _attemptCount,
            EvadeCount = _evadeCount,
            InvincibleCount = _invincibleCount,
            MultiHitCount = _multiHitCount,
            MultiHitSubCount = _multiHitSubCount,
            LastSkillCode = _lastSkillCode,
            FirstObserved = _firstObserved,
            LastObserved = _lastObserved,
            Revision = _revision
        };
    }

    private sealed class CombatantAccumulator(int combatantId)
    {
        private long _outgoingDamage;
        private int _outgoingHits;
        private int _outgoingAttempts;
        private int _outgoingEvades;
        private int _outgoingInvincibles;
        private int _outgoingMultiHits;
        private long _incomingDamage;
        private int _incomingHits;
        private int _incomingAttempts;
        private int _incomingEvades;
        private int _incomingInvincibles;
        private int _incomingMultiHits;
        private long _outgoingHealing;
        private long _incomingHealing;
        private long _outgoingShield;
        private long _incomingShield;
        private long _outgoingShieldAbsorbed;
        private long _incomingShieldAbsorbed;
        private int _outgoingShieldCount;
        private int _incomingShieldCount;
        private int _outgoingShieldAbsorbedCount;
        private int _incomingShieldAbsorbedCount;
        private long _firstObserved;
        private long _lastObserved;
        private bool _hasObserved;
        private long _revision;

        public void ApplyOutgoing(DirectedPairSnapshot pair)
        {
            _outgoingDamage += pair.TotalDamage;
            _outgoingHits += pair.HitCount;
            _outgoingAttempts += pair.AttemptCount;
            _outgoingEvades += pair.EvadeCount;
            _outgoingInvincibles += pair.InvincibleCount;
            _outgoingMultiHits += pair.MultiHitCount;
            _outgoingHealing += pair.TotalHealing;
            _outgoingShield += pair.TotalShield;
            _outgoingShieldAbsorbed += pair.TotalShieldAbsorbed;
            _outgoingShieldCount += pair.ShieldCount;
            _outgoingShieldAbsorbedCount += pair.ShieldAbsorbedCount;
            ApplyObserved(pair);
        }

        public void ApplyIncoming(DirectedPairSnapshot pair)
        {
            _incomingDamage += pair.TotalDamage;
            _incomingHits += pair.HitCount;
            _incomingAttempts += pair.AttemptCount;
            _incomingEvades += pair.EvadeCount;
            _incomingInvincibles += pair.InvincibleCount;
            _incomingMultiHits += pair.MultiHitCount;
            _incomingHealing += pair.TotalHealing;
            _incomingShield += pair.TotalShield;
            _incomingShieldAbsorbed += pair.TotalShieldAbsorbed;
            _incomingShieldCount += pair.ShieldCount;
            _incomingShieldAbsorbedCount += pair.ShieldAbsorbedCount;
            ApplyObserved(pair);
        }

        public void Apply(in CombatResourceEventRecord resourceEvent) =>
            ApplyObserved(resourceEvent.ObservedAtMilliseconds, resourceEvent.ObservedAtMilliseconds, resourceEvent.Revision);

        public CombatantSummary ToSummary() => new()
        {
            CombatantId = combatantId,
            OutgoingDamage = _outgoingDamage,
            OutgoingHits = _outgoingHits,
            OutgoingAttempts = _outgoingAttempts,
            OutgoingEvades = _outgoingEvades,
            OutgoingInvincibles = _outgoingInvincibles,
            OutgoingMultiHits = _outgoingMultiHits,
            IncomingDamage = _incomingDamage,
            IncomingHits = _incomingHits,
            IncomingAttempts = _incomingAttempts,
            IncomingEvades = _incomingEvades,
            IncomingInvincibles = _incomingInvincibles,
            IncomingMultiHits = _incomingMultiHits,
            OutgoingHealing = _outgoingHealing,
            IncomingHealing = _incomingHealing,
            OutgoingShield = _outgoingShield,
            IncomingShield = _incomingShield,
            OutgoingShieldAbsorbed = _outgoingShieldAbsorbed,
            IncomingShieldAbsorbed = _incomingShieldAbsorbed,
            OutgoingShieldCount = _outgoingShieldCount,
            IncomingShieldCount = _incomingShieldCount,
            OutgoingShieldAbsorbedCount = _outgoingShieldAbsorbedCount,
            IncomingShieldAbsorbedCount = _incomingShieldAbsorbedCount,
            FirstObserved = _firstObserved,
            LastObserved = _lastObserved,
            Revision = _revision
        };

        private void ApplyObserved(DirectedPairSnapshot pair) =>
            ApplyObserved(pair.FirstObserved, pair.LastObserved, pair.Revision);

        private void ApplyObserved(long firstObserved, long lastObserved, long revision)
        {
            if (_hasObserved)
            {
                _firstObserved = Math.Min(_firstObserved, firstObserved);
                _lastObserved = Math.Max(_lastObserved, lastObserved);
            }
            else
            {
                _firstObserved = firstObserved;
                _lastObserved = lastObserved;
                _hasObserved = true;
            }
            _revision = Math.Max(_revision, revision);
        }
    }

    private readonly record struct CombatArchiveBuild(
        DirectedPairSnapshot[] Pairs,
        Dictionary<int, long[]> MetricEventOrdinalsByCombatant,
        Dictionary<int, int[]> MechanicEventIndexesByCombatant,
        Dictionary<int, int[]> ResourceEventIndexesByCombatant,
        Dictionary<long, int> ProjectedMetricSources,
        Dictionary<int, int> ProjectedMechanicSources,
        Dictionary<int, int> ProjectedResourceSources);

    private sealed class ArchivePayloadIndex
    {
        private static readonly long[] EmptyEventOrdinals = [];
        private static readonly int[] EmptyEventIndexes = [];
        private static readonly DirectedPairKey[] EmptyPairs = [];

        private readonly Dictionary<int, long[]> _metricEventOrdinalsByCombatant;
        private readonly Dictionary<int, int[]> _mechanicEventIndexesByCombatant;
        private readonly Dictionary<int, int[]> _resourceEventIndexesByCombatant;
        private readonly Dictionary<long, int> _projectedMetricSources;
        private readonly Dictionary<int, int> _projectedMechanicSources;
        private readonly Dictionary<int, int> _projectedResourceSources;
        private readonly Dictionary<int, DirectedPairKey[]> _outgoingPairsByCombatant;
        private readonly Dictionary<int, DirectedPairKey[]> _incomingPairsByCombatant;
        private readonly Dictionary<int, CombatantSummary> _combatantsById;

        private ArchivePayloadIndex(
            Dictionary<int, long[]> metricEventOrdinalsByCombatant,
            Dictionary<int, int[]> mechanicEventIndexesByCombatant,
            Dictionary<int, int[]> resourceEventIndexesByCombatant,
            Dictionary<long, int> projectedMetricSources,
            Dictionary<int, int> projectedMechanicSources,
            Dictionary<int, int> projectedResourceSources,
            Dictionary<int, DirectedPairKey[]> outgoingPairsByCombatant,
            Dictionary<int, DirectedPairKey[]> incomingPairsByCombatant,
            Dictionary<int, CombatantSummary> combatantsById)
        {
            _metricEventOrdinalsByCombatant = metricEventOrdinalsByCombatant;
            _mechanicEventIndexesByCombatant = mechanicEventIndexesByCombatant;
            _resourceEventIndexesByCombatant = resourceEventIndexesByCombatant;
            _projectedMetricSources = projectedMetricSources;
            _projectedMechanicSources = projectedMechanicSources;
            _projectedResourceSources = projectedResourceSources;
            _outgoingPairsByCombatant = outgoingPairsByCombatant;
            _incomingPairsByCombatant = incomingPairsByCombatant;
            _combatantsById = combatantsById;
        }

        public IReadOnlyDictionary<int, long[]> MetricEventOrdinalsByCombatant => _metricEventOrdinalsByCombatant;
        public IReadOnlyDictionary<int, int[]> MechanicEventIndexesByCombatant => _mechanicEventIndexesByCombatant;
        public IReadOnlyDictionary<int, int[]> ResourceEventIndexesByCombatant => _resourceEventIndexesByCombatant;
        public IReadOnlyDictionary<int, DirectedPairKey[]> OutgoingPairsByCombatant => _outgoingPairsByCombatant;
        public IReadOnlyDictionary<int, DirectedPairKey[]> IncomingPairsByCombatant => _incomingPairsByCombatant;
        public IReadOnlyDictionary<int, CombatantSummary> CombatantsById => _combatantsById;

        public static ArchivePayloadIndex Create(
            Dictionary<int, long[]> metricEventOrdinalsByCombatant,
            Dictionary<int, int[]> mechanicEventIndexesByCombatant,
            Dictionary<int, int[]> resourceEventIndexesByCombatant,
            Dictionary<long, int> projectedMetricSources,
            Dictionary<int, int> projectedMechanicSources,
            Dictionary<int, int> projectedResourceSources,
            IReadOnlyList<DirectedPairSnapshot> pairs,
            IReadOnlyList<CombatantSummary> combatants)
        {
            var outgoingBuilders = new Dictionary<int, PairKeyArrayBuilder>();
            var incomingBuilders = new Dictionary<int, PairKeyArrayBuilder>();
            for (var i = 0; i < pairs.Count; i++)
                AddPair(outgoingBuilders, incomingBuilders, pairs[i].Key);

            var outgoingPairsByCombatant = FreezePairs(outgoingBuilders);
            var incomingPairsByCombatant = FreezePairs(incomingBuilders);
            var combatantsById = new Dictionary<int, CombatantSummary>(combatants.Count);
            for (var i = 0; i < combatants.Count; i++)
            {
                var combatant = combatants[i];
                if (combatant.CombatantId > 0)
                    combatantsById[combatant.CombatantId] = combatant;
            }

            return new ArchivePayloadIndex(
                metricEventOrdinalsByCombatant,
                mechanicEventIndexesByCombatant,
                resourceEventIndexesByCombatant,
                projectedMetricSources,
                projectedMechanicSources,
                projectedResourceSources,
                outgoingPairsByCombatant,
                incomingPairsByCombatant,
                combatantsById);
        }

        public long[] GetMetricEventOrdinals(int combatantId)
            => _metricEventOrdinalsByCombatant.TryGetValue(combatantId, out var ordinals) ? ordinals : EmptyEventOrdinals;

        public int[] GetMechanicEventIndexes(int combatantId)
            => _mechanicEventIndexesByCombatant.TryGetValue(combatantId, out var indexes) ? indexes : EmptyEventIndexes;

        public int[] GetResourceEventIndexes(int combatantId)
            => _resourceEventIndexesByCombatant.TryGetValue(combatantId, out var indexes) ? indexes : EmptyEventIndexes;

        public int ResolveMetricSourceId(long eventOrdinal, int sourceId)
            => _projectedMetricSources.TryGetValue(eventOrdinal, out var projectedSourceId) ? projectedSourceId : sourceId;

        public int ResolveMechanicSourceId(int eventIndex, int sourceId)
            => _projectedMechanicSources.TryGetValue(eventIndex, out var projectedSourceId) ? projectedSourceId : sourceId;

        public int ResolveResourceSourceId(int eventIndex, int sourceId)
            => _projectedResourceSources.TryGetValue(eventIndex, out var projectedSourceId) ? projectedSourceId : sourceId;

        public DirectedPairKey[] GetOutgoingPairs(int combatantId)
            => _outgoingPairsByCombatant.TryGetValue(combatantId, out var pairs) ? pairs : EmptyPairs;

        public DirectedPairKey[] GetIncomingPairs(int combatantId)
            => _incomingPairsByCombatant.TryGetValue(combatantId, out var pairs) ? pairs : EmptyPairs;

        public CombatantSummary? GetCombatant(int combatantId)
            => _combatantsById.TryGetValue(combatantId, out var combatant) ? combatant : null;

        private static void AddPair(Dictionary<int, PairKeyArrayBuilder> outgoingBuilders, Dictionary<int, PairKeyArrayBuilder> incomingBuilders, DirectedPairKey key)
        {
            if (key.SourceId <= 0 || key.TargetId <= 0)
                return;

            AddPairKey(outgoingBuilders, key.SourceId, key);
            AddPairKey(incomingBuilders, key.TargetId, key);
        }

        private static void AddPairKey(Dictionary<int, PairKeyArrayBuilder> builders, int combatantId, DirectedPairKey key)
        {
            ref var builder = ref CollectionsMarshal.GetValueRefOrAddDefault(builders, combatantId, out _);
            builder.Add(key);
        }

        private static Dictionary<int, DirectedPairKey[]> FreezePairs(Dictionary<int, PairKeyArrayBuilder> builders)
        {
            var result = new Dictionary<int, DirectedPairKey[]>(builders.Count);
            foreach (var (combatantId, builder) in builders)
            {
                var pairs = builder.ToArray();
                Array.Sort(pairs, ComparePairKeys);
                result[combatantId] = pairs;
            }

            return result;
        }

        private static int ComparePairKeys(DirectedPairKey left, DirectedPairKey right)
        {
            var cmp = left.SourceId.CompareTo(right.SourceId);
            return cmp != 0 ? cmp : left.TargetId.CompareTo(right.TargetId);
        }
    }

    private struct LongArrayBuilder
    {
        private long[]? _items;
        private int _count;

        public void Add(long value)
        {
            var items = _items;
            if (items is null)
            {
                _items = [value];
                _count = 1;
                return;
            }

            if (_count == items.Length)
            {
                Array.Resize(ref items, items.Length << 1);
                _items = items;
            }

            items[_count++] = value;
        }

        public readonly long[] ToArray()
        {
            if (_count == 0)
                return [];

            var result = new long[_count];
            Array.Copy(_items!, result, _count);
            return result;
        }
    }

    private struct PairKeyArrayBuilder
    {
        private DirectedPairKey[]? _items;
        private int _count;

        public void Add(DirectedPairKey value)
        {
            var items = _items;
            if (items is null)
            {
                _items = [value];
                _count = 1;
                return;
            }

            if (_count == items.Length)
            {
                Array.Resize(ref items, items.Length << 1);
                _items = items;
            }

            items[_count++] = value;
        }

        public readonly DirectedPairKey[] ToArray()
        {
            if (_count == 0)
                return [];

            var result = new DirectedPairKey[_count];
            Array.Copy(_items!, result, _count);
            return result;
        }
    }

    private struct IntArrayBuilder
    {
        private int[]? _items;
        private int _count;

        public void Add(int value)
        {
            var items = _items;
            if (items is null)
            {
                _items = [value];
                _count = 1;
                return;
            }

            if (_count == items.Length)
            {
                Array.Resize(ref items, items.Length << 1);
                _items = items;
            }

            items[_count++] = value;
        }

        public readonly int[] ToArray()
        {
            if (_count == 0)
                return [];

            var result = new int[_count];
            Array.Copy(_items!, result, _count);
            return result;
        }
    }
}

public readonly record struct SceneArchiveEntityIdentity
{
    public int EntityId { get; init; }
    public int? NpcCode { get; init; }
    public NpcKind Kind { get; init; }
    public int? OwnerEntityId { get; init; }
    public EntityOwnerKind OwnerKind { get; init; }
    public bool NpcCombatActive { get; init; }
    public long? Value2136 { get; init; }
    public long? Sequence2136 { get; init; }
    public long? Value0140 { get; init; }
    public long? Value0240 { get; init; }
    public (byte State0, byte State1)? State4636 { get; init; }
    public (int SequenceId, int ResultCode)? Latest2C38 { get; init; }
    public long LastObservedOrdinal { get; init; }

    public static SceneArchiveEntityIdentity From(EntityRecord e) => new()
    {
        EntityId = e.EntityId,
        NpcCode = e.NpcCode,
        Kind = e.Kind,
        OwnerEntityId = e.OwnerEntityId,
        OwnerKind = e.OwnerKind,
        NpcCombatActive = e.NpcCombatActive,
        Value2136 = e.Value2136,
        Sequence2136 = e.Sequence2136,
        Value0140 = e.Value0140,
        Value0240 = e.Value0240,
        State4636 = e.State4636,
        Latest2C38 = e.Latest2C38,
        LastObservedOrdinal = e.LastObservedOrdinal
    };
}

public readonly record struct SceneArchiveEntityVital
{
    public int EntityId { get; init; }
    public long CurrentHp { get; init; }
    public long? MaxHp { get; init; }
    public long ObservedAtMilliseconds { get; init; }
    public long ObservationOrdinal { get; init; }

    public static SceneArchiveEntityVital From(in EntityVitalState state) => new()
    {
        EntityId = state.EntityId,
        CurrentHp = state.CurrentHp,
        MaxHp = state.MaxHp,
        ObservedAtMilliseconds = state.ObservedAtMilliseconds,
        ObservationOrdinal = state.ObservationOrdinal
    };
}

public readonly record struct SceneArchiveBossFocus
{
    public int InstanceId { get; init; }
    public long Hp { get; init; }
    public long MaxHp { get; init; }
    public long CumulativeLostHp { get; init; }
    public long LastObservedAtMilliseconds { get; init; }
    public bool HasHp { get; init; }
    public bool HasMaxHp { get; init; }
}
