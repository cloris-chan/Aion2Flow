using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Archive;

public sealed class SceneArchivePayload
{
    private ArchivePayloadIndex? _detailIndex;

    public SceneKind Kind { get; init; }
    public DateTimeOffset SceneStarted { get; init; }
    public SceneJournalSegment TimelineSegment { get; init; }
    public IReadOnlyList<SceneArchiveCombatEvent> Events { get; init; } = [];
    public SceneIdentityScope IdentityScope { get; init; } = SceneIdentityScope.Empty;
    public IReadOnlyList<DirectedPairSnapshot> Pairs { get; init; } = [];
    public IReadOnlyList<CombatantSummary> Combatants { get; init; } = [];
    public IReadOnlyList<SceneArchiveEntityIdentity> Entities { get; init; } = [];
    public IReadOnlyList<SceneArchiveBossFocus> Bosses { get; init; } = [];
    public IReadOnlyList<int> BossNpcCodes { get; init; } = [];

    internal IReadOnlyDictionary<int, int[]> EventIndicesByCombatant => DetailIndex.EventIndicesByCombatant;
    internal IReadOnlyDictionary<int, DirectedPairKey[]> OutgoingPairsByCombatant => DetailIndex.OutgoingPairsByCombatant;
    internal IReadOnlyDictionary<int, DirectedPairKey[]> IncomingPairsByCombatant => DetailIndex.IncomingPairsByCombatant;
    internal IReadOnlyDictionary<int, CombatantSummary> CombatantsById => DetailIndex.CombatantsById;

    private ArchivePayloadIndex DetailIndex
    {
        get => _detailIndex ??= ArchivePayloadIndex.Create(Events, Pairs, Combatants);
        init => _detailIndex = value;
    }

    internal static SceneArchivePayload CreateLocked(SceneCombatSnapshot snapshot, DateTimeOffset sceneStarted, EntityStore entities, SceneBoundaryStore boundary, RuntimeMetadataRegistry metadataRegistry, BossFocusStore bossFocus, SceneCombatSnapshotAdapter adapter, SceneJournalSegment timelineSegment)
    {
        var archivedSnapshot = snapshot;
        var eventsByKey = new Dictionary<EventKey, SceneArchiveCombatEvent>();
        var entityIds = new HashSet<int>();

        var snapshotCombatants = archivedSnapshot.Combatants.AsSpan();
        foreach (ref readonly var combatant in snapshotCombatants)
        {
            var combatantId = combatant.Id;
            AddEntity(entityIds, combatantId);
            AddCombatantDetailEvents(eventsByKey, entityIds, adapter, archivedSnapshot, combatantId);
        }

        if (archivedSnapshot.TargetObservation?.InstanceId is int targetId)
        {
            AddEntity(entityIds, targetId);
            AddCombatantDetailEvents(eventsByKey, entityIds, adapter, archivedSnapshot, targetId);
        }

        AddKnownEntities(entityIds, entities);

        var eventsSnapshot = eventsByKey.Values.ToArray();
        Array.Sort(eventsSnapshot, CompareEvents);
        var identities = BuildIdentities(entities, entityIds);
        var identityScope = BuildIdentityScope(entityIds, identities, boundary, metadataRegistry);
        var pairs = BuildPairs(eventsSnapshot);
        var combatants = BuildCombatants(pairs);
        var bosses = BuildBosses(bossFocus);
        var detailIndex = ArchivePayloadIndex.Create(eventsSnapshot, pairs, combatants);

        return new SceneArchivePayload
        {
            Kind = archivedSnapshot.Kind,
            SceneStarted = sceneStarted,
            TimelineSegment = timelineSegment,
            Events = eventsSnapshot,
            IdentityScope = identityScope,
            Pairs = pairs,
            Combatants = combatants,
            Entities = identities,
            Bosses = bosses,
            BossNpcCodes = archivedSnapshot.BossNpcCodes.AsSpan().ToArray(),
            DetailIndex = detailIndex
        };
    }

    public SceneArchivePayload DeepClone()
    {
        var events = new SceneArchiveCombatEvent[Events.Count];
        for (var i = 0; i < events.Length; i++)
            events[i] = Events[i];

        var pairs = new DirectedPairSnapshot[Pairs.Count];
        for (var i = 0; i < pairs.Length; i++)
            pairs[i] = Pairs[i];

        var combatants = new CombatantSummary[Combatants.Count];
        for (var i = 0; i < combatants.Length; i++)
            combatants[i] = Combatants[i];

        var entities = new SceneArchiveEntityIdentity[Entities.Count];
        for (var i = 0; i < entities.Length; i++)
            entities[i] = Entities[i].DeepClone();

        var bosses = new SceneArchiveBossFocus[Bosses.Count];
        for (var i = 0; i < bosses.Length; i++)
            bosses[i] = Bosses[i].DeepClone();
        var bossNpcCodes = new int[BossNpcCodes.Count];
        for (var i = 0; i < bossNpcCodes.Length; i++)
            bossNpcCodes[i] = BossNpcCodes[i];

        return new SceneArchivePayload
        {
            Kind = Kind,
            SceneStarted = SceneStarted,
            TimelineSegment = TimelineSegment,
            Events = events,
            IdentityScope = IdentityScope.DeepClone(),
            Pairs = pairs,
            Combatants = combatants,
            Entities = entities,
            Bosses = bosses,
            BossNpcCodes = bossNpcCodes,
            DetailIndex = ArchivePayloadIndex.Create(events, pairs, combatants)
        };
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
        var eventIndices = detailIndex.GetEventIndices(combatantId);
        var combatant = detailIndex.GetCombatant(combatantId);
        if (eventIndices.Length == 0 && combatant is null)
        {
            return new CombatDetailDelta
            {
                CombatantId = combatantId
            };
        }

        var events = new CombatDetailEvent[eventIndices.Length];
        var revision = 0L;
        for (var i = 0; i < eventIndices.Length; i++)
        {
            var archiveEvent = Events[eventIndices[i]];
            events[i] = archiveEvent.ToDetailEvent();
            revision = Math.Max(revision, archiveEvent.Revision);
        }

        return new CombatDetailDelta
        {
            CombatantId = combatantId,
            Revision = revision,
            OutgoingPairs = detailIndex.GetOutgoingPairs(combatantId),
            IncomingPairs = detailIndex.GetIncomingPairs(combatantId),
            Events = events,
            Combatant = combatant
        };
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

    private static void AddCombatantDetailEvents(Dictionary<EventKey, SceneArchiveCombatEvent> eventsByKey, HashSet<int> entityIds, SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot archivedSnapshot, int combatantId)
    {
        var events = adapter.CreateDetailEvents(archivedSnapshot, combatantId);
        for (var i = 0; i < events.Count; i++)
        {
            var detailEvent = events[i];
            AddEntity(entityIds, detailEvent.SourceId);
            AddEntity(entityIds, detailEvent.TargetId);
            var archiveEvent = SceneArchiveCombatEvent.From(in detailEvent);
            eventsByKey.TryAdd(CreateKey(in detailEvent), archiveEvent);
        }
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

    private static EventKey CreateKey(in CombatDetailEvent e) => new(e.Revision, e.SourceId, e.TargetId, e.SkillCode, e.ObservedAt, e.Amount, e.EventKind, e.ValueKind);

    private static int CompareEvents(SceneArchiveCombatEvent left, SceneArchiveCombatEvent right)
    {
        var cmp = left.Revision.CompareTo(right.Revision);
        if (cmp != 0)
            return cmp;
        cmp = left.ObservedAt.CompareTo(right.ObservedAt);
        if (cmp != 0)
            return cmp;
        cmp = left.SourceId.CompareTo(right.SourceId);
        if (cmp != 0)
            return cmp;
        return left.TargetId.CompareTo(right.TargetId);
    }

    private static int CompareEventIndices(int left, int right, IReadOnlyList<SceneArchiveCombatEvent> events)
        => CompareEvents(events[left], events[right]);

    private static DirectedPairSnapshot[] BuildPairs(SceneArchiveCombatEvent[] events)
    {
        var pairs = new Dictionary<DirectedPairKey, PairAccumulator>();
        for (var i = 0; i < events.Length; i++)
        {
            var e = events[i];
            if (e.SourceId <= 0 || e.TargetId <= 0)
                continue;

            var key = new DirectedPairKey(e.SourceId, e.TargetId);
            if (!pairs.TryGetValue(key, out var pair))
            {
                pair = new PairAccumulator(key);
                pairs[key] = pair;
            }

            pair.Apply(e);
        }

        if (pairs.Count == 0)
            return [];

        var result = new DirectedPairSnapshot[pairs.Count];
        var index = 0;
        foreach (var pair in pairs.Values)
            result[index++] = pair.ToSnapshot();

        Array.Sort(result, static (left, right) =>
        {
            var cmp = left.Key.SourceId.CompareTo(right.Key.SourceId);
            return cmp != 0 ? cmp : left.Key.TargetId.CompareTo(right.Key.TargetId);
        });
        return result;
    }

    private static CombatantSummary[] BuildCombatants(DirectedPairSnapshot[] pairs)
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
                HasHp = boss.HasHp
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
        private int _lastSkillCode;
        private long _firstObserved;
        private long _lastObserved;
        private bool _hasObserved;
        private long _revision;

        public void Apply(SceneArchiveCombatEvent e)
        {
            var observation = e.Observation;
            var contribution = CombatContributionClassifier.Evaluate(in observation);

            _totalDamage += contribution.DamageAmount;
            _totalHealing += contribution.HealingAmount;
            _totalShield += contribution.ShieldGrantAmount;
            _totalShieldAbsorbed += contribution.ShieldAbsorbedAmount;
            _shieldCount += contribution.ShieldGrantCount;
            _shieldAbsorbedCount += contribution.ShieldAbsorbedCount;
            _hitCount += contribution.HitCount;
            _attemptCount += contribution.AttemptCount;
            _evadeCount += contribution.EvadeCount;
            _invincibleCount += contribution.InvincibleCount;
            _multiHitCount += contribution.MultiHitCount;
            _lastSkillCode = observation.SkillCode;
            _revision = Math.Max(_revision, e.Revision);
            var observedAt = e.ObservedAt;
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

        private void ApplyObserved(DirectedPairSnapshot pair)
        {
            if (_hasObserved)
            {
                _firstObserved = Math.Min(_firstObserved, pair.FirstObserved);
                _lastObserved = Math.Max(_lastObserved, pair.LastObserved);
            }
            else
            {
                _firstObserved = pair.FirstObserved;
                _lastObserved = pair.LastObserved;
                _hasObserved = true;
            }
            _revision = Math.Max(_revision, pair.Revision);
        }
    }

    private readonly record struct EventKey(long Revision, int SourceId, int TargetId, int SkillCode, long Timestamp, long Damage, CombatEventKind EventKind, CombatValueKind ValueKind);

    private sealed class ArchivePayloadIndex
    {
        private static readonly int[] EmptyEventIndices = [];
        private static readonly DirectedPairKey[] EmptyPairs = [];

        private readonly Dictionary<int, int[]> _eventIndicesByCombatant;
        private readonly Dictionary<int, DirectedPairKey[]> _outgoingPairsByCombatant;
        private readonly Dictionary<int, DirectedPairKey[]> _incomingPairsByCombatant;
        private readonly Dictionary<int, CombatantSummary> _combatantsById;

        private ArchivePayloadIndex(Dictionary<int, int[]> eventIndicesByCombatant, Dictionary<int, DirectedPairKey[]> outgoingPairsByCombatant, Dictionary<int, DirectedPairKey[]> incomingPairsByCombatant, Dictionary<int, CombatantSummary> combatantsById)
        {
            _eventIndicesByCombatant = eventIndicesByCombatant;
            _outgoingPairsByCombatant = outgoingPairsByCombatant;
            _incomingPairsByCombatant = incomingPairsByCombatant;
            _combatantsById = combatantsById;
        }

        public IReadOnlyDictionary<int, int[]> EventIndicesByCombatant => _eventIndicesByCombatant;
        public IReadOnlyDictionary<int, DirectedPairKey[]> OutgoingPairsByCombatant => _outgoingPairsByCombatant;
        public IReadOnlyDictionary<int, DirectedPairKey[]> IncomingPairsByCombatant => _incomingPairsByCombatant;
        public IReadOnlyDictionary<int, CombatantSummary> CombatantsById => _combatantsById;

        public static ArchivePayloadIndex Create(IReadOnlyList<SceneArchiveCombatEvent> events, IReadOnlyList<DirectedPairSnapshot> pairs, IReadOnlyList<CombatantSummary> combatants)
        {
            var eventBuilders = new Dictionary<int, IntArrayBuilder>();
            for (var i = 0; i < events.Count; i++)
            {
                var e = events[i];
                AddEventIndex(eventBuilders, e.SourceId, i);
                if (e.TargetId != e.SourceId)
                    AddEventIndex(eventBuilders, e.TargetId, i);
            }

            var eventIndicesByCombatant = new Dictionary<int, int[]>(eventBuilders.Count);
            foreach (var (combatantId, builder) in eventBuilders)
            {
                var indices = builder.ToArray();
                Array.Sort(indices, (left, right) => CompareEventIndices(left, right, events));
                eventIndicesByCombatant[combatantId] = indices;
            }

            var outgoingBuilders = new Dictionary<int, PairKeyArrayBuilder>();
            var incomingBuilders = new Dictionary<int, PairKeyArrayBuilder>();
            if (pairs.Count > 0)
            {
                for (var i = 0; i < pairs.Count; i++)
                    AddPair(outgoingBuilders, incomingBuilders, pairs[i].Key);
            }
            else
            {
                var pairKeys = new Dictionary<DirectedPairKey, byte>();
                for (var i = 0; i < events.Count; i++)
                {
                    var e = events[i];
                    if (e.SourceId <= 0 || e.TargetId <= 0)
                        continue;

                    pairKeys.TryAdd(new DirectedPairKey(e.SourceId, e.TargetId), 0);
                }

                if (pairKeys.Count > 0)
                {
                    var keys = new DirectedPairKey[pairKeys.Count];
                    var index = 0;
                    foreach (var key in pairKeys.Keys)
                        keys[index++] = key;
                    Array.Sort(keys, ComparePairKeys);
                    for (var i = 0; i < keys.Length; i++)
                        AddPair(outgoingBuilders, incomingBuilders, keys[i]);
                }
            }

            var outgoingPairsByCombatant = FreezePairs(outgoingBuilders);
            var incomingPairsByCombatant = FreezePairs(incomingBuilders);
            var combatantsById = new Dictionary<int, CombatantSummary>(combatants.Count);
            for (var i = 0; i < combatants.Count; i++)
            {
                var combatant = combatants[i];
                if (combatant.CombatantId > 0)
                    combatantsById[combatant.CombatantId] = combatant;
            }

            return new ArchivePayloadIndex(eventIndicesByCombatant, outgoingPairsByCombatant, incomingPairsByCombatant, combatantsById);
        }

        public int[] GetEventIndices(int combatantId)
            => _eventIndicesByCombatant.TryGetValue(combatantId, out var indices) ? indices : EmptyEventIndices;

        public DirectedPairKey[] GetOutgoingPairs(int combatantId)
            => _outgoingPairsByCombatant.TryGetValue(combatantId, out var pairs) ? pairs : EmptyPairs;

        public DirectedPairKey[] GetIncomingPairs(int combatantId)
            => _incomingPairsByCombatant.TryGetValue(combatantId, out var pairs) ? pairs : EmptyPairs;

        public CombatantSummary? GetCombatant(int combatantId)
            => _combatantsById.TryGetValue(combatantId, out var combatant) ? combatant : null;

        private static void AddEventIndex(Dictionary<int, IntArrayBuilder> builders, int combatantId, int eventIndex)
        {
            if (combatantId <= 0)
                return;

            ref var builder = ref CollectionsMarshal.GetValueRefOrAddDefault(builders, combatantId, out _);
            builder.Add(eventIndex);
        }

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
}

public readonly record struct SceneArchiveCombatEvent
{
    public int SourceId { get; init; }
    public int TargetId { get; init; }
    public CombatObservation Observation { get; init; }
    public long ObservedAtMilliseconds { get; init; }
    public long Revision { get; init; }
    public long ObservedAt => ObservedAtMilliseconds;

    public static SceneArchiveCombatEvent From(in CombatDetailEvent e) => new()
    {
        SourceId = e.SourceId,
        TargetId = e.TargetId,
        Observation = e.Observation,
        ObservedAtMilliseconds = e.ObservedAtMilliseconds,
        Revision = e.Revision
    };

    public CombatDetailEvent ToDetailEvent() => new(Observation, SourceId, TargetId, ObservedAtMilliseconds, Revision);
}

public sealed class SceneArchiveEntityIdentity
{
    public int EntityId { get; init; }
    public int? NpcCode { get; init; }
    public NpcKind Kind { get; init; }
    public int? OwnerEntityId { get; init; }
    public int? CurrentHp { get; init; }
    public int? MaxHp { get; init; }
    public bool NpcCombatActive { get; init; }
    public uint? Value2136 { get; init; }
    public uint? Sequence2136 { get; init; }
    public uint? Value0140 { get; init; }
    public uint? Value0240 { get; init; }
    public (byte State0, byte State1)? State4636 { get; init; }
    public (int SequenceId, int ResultCode)? Latest2C38 { get; init; }
    public long LastObservedOrdinal { get; init; }

    public static SceneArchiveEntityIdentity From(EntityRecord e) => new()
    {
        EntityId = e.EntityId,
        NpcCode = e.NpcCode,
        Kind = e.Kind,
        OwnerEntityId = e.OwnerEntityId,
        CurrentHp = e.CurrentHp,
        MaxHp = e.MaxHp,
        NpcCombatActive = e.NpcCombatActive,
        Value2136 = e.Value2136,
        Sequence2136 = e.Sequence2136,
        Value0140 = e.Value0140,
        Value0240 = e.Value0240,
        State4636 = e.State4636,
        Latest2C38 = e.Latest2C38,
        LastObservedOrdinal = e.LastObservedOrdinal
    };

    public SceneArchiveEntityIdentity DeepClone() => new()
    {
        EntityId = EntityId,
        NpcCode = NpcCode,
        Kind = Kind,
        OwnerEntityId = OwnerEntityId,
        CurrentHp = CurrentHp,
        MaxHp = MaxHp,
        NpcCombatActive = NpcCombatActive,
        Value2136 = Value2136,
        Sequence2136 = Sequence2136,
        Value0140 = Value0140,
        Value0240 = Value0240,
        State4636 = State4636,
        Latest2C38 = Latest2C38,
        LastObservedOrdinal = LastObservedOrdinal
    };
}

public sealed class SceneArchiveBossFocus
{
    public int InstanceId { get; init; }
    public int Hp { get; init; }
    public int MaxHp { get; init; }
    public long CumulativeLostHp { get; init; }
    public long LastObservedAtMilliseconds { get; init; }
    public bool HasHp { get; init; }

    public SceneArchiveBossFocus DeepClone() => new()
    {
        InstanceId = InstanceId,
        Hp = Hp,
        MaxHp = MaxHp,
        CumulativeLostHp = CumulativeLostHp,
        LastObservedAtMilliseconds = LastObservedAtMilliseconds,
        HasHp = HasHp
    };
}
