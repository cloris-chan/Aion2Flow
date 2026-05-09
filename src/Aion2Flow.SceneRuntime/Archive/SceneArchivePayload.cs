using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Archive;

public sealed class SceneArchivePayload
{
    private ArchivePayloadIndex? _detailIndex;

    public SceneCombatSnapshot Snapshot { get; init; } = new();
    public DateTimeOffset SceneStarted { get; init; }
    public IReadOnlyList<SceneArchiveCombatEvent> Events { get; init; } = [];
    public IReadOnlyDictionary<int, string> DisplayNames { get; init; } = new Dictionary<int, string>();
    public IReadOnlyList<DirectedPairSnapshot> Pairs { get; init; } = [];
    public IReadOnlyList<CombatantSummary> Combatants { get; init; } = [];
    public IReadOnlyList<SceneArchiveEntityIdentity> Entities { get; init; } = [];
    public IReadOnlyDictionary<int, string> NpcNamesByCode { get; init; } = new Dictionary<int, string>();
    public IReadOnlyList<SceneArchiveBossFocus> Bosses { get; init; } = [];

    internal IReadOnlyDictionary<int, int[]> EventIndicesByCombatant => DetailIndex.EventIndicesByCombatant;
    internal IReadOnlyDictionary<int, DirectedPairKey[]> OutgoingPairsByCombatant => DetailIndex.OutgoingPairsByCombatant;
    internal IReadOnlyDictionary<int, DirectedPairKey[]> IncomingPairsByCombatant => DetailIndex.IncomingPairsByCombatant;
    internal IReadOnlyDictionary<int, CombatantSummary> CombatantsById => DetailIndex.CombatantsById;

    private ArchivePayloadIndex DetailIndex
    {
        get => _detailIndex ??= ArchivePayloadIndex.Create(Events, Pairs, Combatants);
        init => _detailIndex = value;
    }

    internal static SceneArchivePayload CreateLocked(SceneCombatSnapshot snapshot, DateTimeOffset sceneStarted, EntityStore entities, MetadataStore metadata, BossFocusStore bossFocus, CombatPairProjection pairsProjection, SceneCombatSnapshotAdapter adapter)
    {
        var archivedSnapshot = snapshot.DeepClone();
        var eventsByKey = new Dictionary<EventKey, SceneArchiveCombatEvent>();
        var displayNames = new Dictionary<int, string>();
        var entityIds = new HashSet<int>();

        foreach (var combatantId in archivedSnapshot.Combatants.Keys.Order())
        {
            AddEntity(entityIds, combatantId);
            AddDisplayName(displayNames, adapter, combatantId);
            AddCombatantDetailEvents(eventsByKey, displayNames, entityIds, pairsProjection, adapter, archivedSnapshot, combatantId);
        }

        if (archivedSnapshot.TargetObservation?.InstanceId is int targetId)
        {
            AddEntity(entityIds, targetId);
            AddDisplayName(displayNames, adapter, targetId);
            AddCombatantDetailEvents(eventsByKey, displayNames, entityIds, pairsProjection, adapter, archivedSnapshot, targetId);
        }

        var eventsSnapshot = eventsByKey.Values.ToArray();
        Array.Sort(eventsSnapshot, CompareEvents);
        var identities = BuildIdentities(entities, entityIds);
        var npcNames = BuildNpcNames(metadata, identities);
        var pairs = BuildPairs(eventsSnapshot);
        var combatants = BuildCombatants(pairs);
        var bosses = BuildBosses(bossFocus, archivedSnapshot);
        var detailIndex = ArchivePayloadIndex.Create(eventsSnapshot, pairs, combatants);

        return new SceneArchivePayload
        {
            Snapshot = archivedSnapshot,
            SceneStarted = sceneStarted,
            Events = eventsSnapshot,
            DisplayNames = new Dictionary<int, string>(displayNames),
            Pairs = pairs,
            Combatants = combatants,
            Entities = identities,
            NpcNamesByCode = npcNames,
            Bosses = bosses,
            DetailIndex = detailIndex
        };
    }

    public SceneArchivePayload DeepClone()
    {
        var events = Events.Select(static e => e.DeepClone()).ToArray();
        var pairs = Pairs.Select(ClonePair).ToArray();
        var combatants = Combatants.Select(CloneCombatant).ToArray();

        return new SceneArchivePayload
        {
            Snapshot = Snapshot.DeepClone(),
            SceneStarted = SceneStarted,
            Events = events,
            DisplayNames = new Dictionary<int, string>(DisplayNames),
            Pairs = pairs,
            Combatants = combatants,
            Entities = Entities.Select(static e => e.DeepClone()).ToArray(),
            NpcNamesByCode = new Dictionary<int, string>(NpcNamesByCode),
            Bosses = Bosses.Select(static b => b.DeepClone()).ToArray(),
            DetailIndex = ArchivePayloadIndex.Create(events, pairs, combatants)
        };
    }

    public CombatDetailDelta CreateDetailDelta(int combatantId)
    {
        if (combatantId <= 0)
        {
            return new CombatDetailDelta
            {
                CombatantId = combatantId,
                DisplayNames = DisplayNames
            };
        }

        var detailIndex = DetailIndex;
        var eventIndices = detailIndex.GetEventIndices(combatantId);
        var combatant = detailIndex.GetCombatant(combatantId);
        if (eventIndices.Length == 0 && combatant is null && !Snapshot.Combatants.ContainsKey(combatantId))
        {
            return new CombatDetailDelta
            {
                CombatantId = combatantId,
                DisplayNames = DisplayNames
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
            DisplayNames = DisplayNames,
            Combatant = combatant is not null ? CloneCombatant(combatant) : null
        };
    }

    private static void AddEntity(HashSet<int> entityIds, int entityId)
    {
        if (entityId > 0)
            entityIds.Add(entityId);
    }

    private static void AddDisplayName(Dictionary<int, string> displayNames, SceneCombatSnapshotAdapter adapter, int entityId)
    {
        if (entityId <= 0)
            return;

        var displayName = adapter.ResolveDetailDisplayName(entityId);
        if (!string.IsNullOrWhiteSpace(displayName))
            displayNames[entityId] = displayName;
    }

    private static void AddCombatantDetailEvents(Dictionary<EventKey, SceneArchiveCombatEvent> eventsByKey, Dictionary<int, string> displayNames, HashSet<int> entityIds, CombatPairProjection pairsProjection, SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot archivedSnapshot, int combatantId)
    {
        var events = pairsProjection.GetDetailEvents(adapter, archivedSnapshot, combatantId);
        for (var i = 0; i < events.Count; i++)
        {
            var detailEvent = events[i];
            AddEntity(entityIds, detailEvent.SourceId);
            AddEntity(entityIds, detailEvent.TargetId);
            AddDisplayName(displayNames, adapter, detailEvent.SourceId);
            AddDisplayName(displayNames, adapter, detailEvent.TargetId);
            var archiveEvent = SceneArchiveCombatEvent.From(in detailEvent);
            eventsByKey.TryAdd(CreateKey(in detailEvent), archiveEvent);
        }
    }

    private static SceneArchiveEntityIdentity[] BuildIdentities(EntityStore entities, HashSet<int> entityIds)
    {
        var result = new List<SceneArchiveEntityIdentity>(entityIds.Count);
        foreach (var entityId in entityIds.Order())
        {
            if (!entities.TryGet(entityId, out var entity))
                continue;

            result.Add(SceneArchiveEntityIdentity.From(entity));
        }

        return [.. result];
    }

    private static Dictionary<int, string> BuildNpcNames(MetadataStore metadata, IReadOnlyList<SceneArchiveEntityIdentity> identities)
    {
        var result = new Dictionary<int, string>();
        for (var i = 0; i < identities.Count; i++)
        {
            if (identities[i].NpcCode is not int npcCode || !metadata.TryGetNpcName(npcCode, out var npcName) || string.IsNullOrWhiteSpace(npcName))
                continue;

            result[npcCode] = npcName;
        }

        return result;
    }

    private static EventKey CreateKey(in CombatDetailEvent e) => new(e.Revision, e.SourceId, e.TargetId, e.Packet.SkillCode, e.Packet.Timestamp, e.Packet.Damage, e.Packet.EventKind, e.Packet.ValueKind);

    private static int CompareEvents(SceneArchiveCombatEvent left, SceneArchiveCombatEvent right)
    {
        var cmp = left.Revision.CompareTo(right.Revision);
        if (cmp != 0)
            return cmp;
        cmp = left.Packet.Timestamp.CompareTo(right.Packet.Timestamp);
        if (cmp != 0)
            return cmp;
        cmp = left.SourceId.CompareTo(right.SourceId);
        if (cmp != 0)
            return cmp;
        return left.TargetId.CompareTo(right.TargetId);
    }

    private static int CompareEventIndices(int left, int right, IReadOnlyList<SceneArchiveCombatEvent> events)
        => CompareEvents(events[left], events[right]);

    private static DirectedPairSnapshot[] BuildPairs(IReadOnlyList<SceneArchiveCombatEvent> events)
    {
        var pairs = new Dictionary<DirectedPairKey, PairAccumulator>();
        for (var i = 0; i < events.Count; i++)
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

        return pairs.Values
            .Select(static p => p.ToSnapshot())
            .OrderBy(static p => p.Key.SourceId)
            .ThenBy(static p => p.Key.TargetId)
            .ToArray();
    }

    private static DirectedPairSnapshot ClonePair(DirectedPairSnapshot pair) => new()
    {
        Key = pair.Key,
        TotalDamage = pair.TotalDamage,
        TotalHealing = pair.TotalHealing,
        TotalShield = pair.TotalShield,
        TotalShieldAbsorbed = pair.TotalShieldAbsorbed,
        ShieldCount = pair.ShieldCount,
        ShieldAbsorbedCount = pair.ShieldAbsorbedCount,
        HitCount = pair.HitCount,
        AttemptCount = pair.AttemptCount,
        EvadeCount = pair.EvadeCount,
        InvincibleCount = pair.InvincibleCount,
        MultiHitCount = pair.MultiHitCount,
        LastSkillCode = pair.LastSkillCode,
        FirstObserved = pair.FirstObserved,
        LastObserved = pair.LastObserved,
        Revision = pair.Revision
    };

    private static CombatantSummary[] BuildCombatants(IReadOnlyList<DirectedPairSnapshot> pairs)
    {
        var combatants = new Dictionary<int, CombatantAccumulator>();
        for (var i = 0; i < pairs.Count; i++)
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

        return combatants.Values
            .Select(static c => c.ToSummary())
            .OrderBy(static c => c.CombatantId)
            .ToArray();
    }

    private static SceneArchiveBossFocus[] BuildBosses(BossFocusStore bossFocus, SceneCombatSnapshot snapshot)
    {
        var targetIds = new HashSet<int>();
        if (snapshot.TargetObservation?.InstanceId is int targetId && targetId > 0)
            targetIds.Add(targetId);
        if (snapshot.Encounter.TrackingTargetId > 0)
            targetIds.Add(snapshot.Encounter.TrackingTargetId);

        if (targetIds.Count == 0)
            return [];

        return bossFocus.GetObservedBosses(0, long.MaxValue)
            .Where(b => targetIds.Contains(b.InstanceId))
            .Select(static b => new SceneArchiveBossFocus
            {
                InstanceId = b.InstanceId,
                Hp = b.Hp,
                MaxHp = b.MaxHp,
                LastObservedAtMilliseconds = b.LastObservedAtMilliseconds,
                HasHp = b.HasHp
            })
            .OrderBy(static b => b.InstanceId)
            .ToArray();
    }

    private static CombatantSummary CloneCombatant(CombatantSummary combatant) => new()
    {
        CombatantId = combatant.CombatantId,
        OutgoingDamage = combatant.OutgoingDamage,
        OutgoingHits = combatant.OutgoingHits,
        OutgoingAttempts = combatant.OutgoingAttempts,
        OutgoingEvades = combatant.OutgoingEvades,
        OutgoingInvincibles = combatant.OutgoingInvincibles,
        OutgoingMultiHits = combatant.OutgoingMultiHits,
        IncomingDamage = combatant.IncomingDamage,
        IncomingHits = combatant.IncomingHits,
        IncomingAttempts = combatant.IncomingAttempts,
        IncomingEvades = combatant.IncomingEvades,
        IncomingInvincibles = combatant.IncomingInvincibles,
        IncomingMultiHits = combatant.IncomingMultiHits,
        OutgoingHealing = combatant.OutgoingHealing,
        IncomingHealing = combatant.IncomingHealing,
        OutgoingShield = combatant.OutgoingShield,
        IncomingShield = combatant.IncomingShield,
        OutgoingShieldAbsorbed = combatant.OutgoingShieldAbsorbed,
        IncomingShieldAbsorbed = combatant.IncomingShieldAbsorbed,
        OutgoingShieldCount = combatant.OutgoingShieldCount,
        IncomingShieldCount = combatant.IncomingShieldCount,
        OutgoingShieldAbsorbedCount = combatant.OutgoingShieldAbsorbedCount,
        IncomingShieldAbsorbedCount = combatant.IncomingShieldAbsorbedCount,
        FirstObserved = combatant.FirstObserved,
        LastObserved = combatant.LastObserved,
        Revision = combatant.Revision
    };

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
        private long _revision;

        public void Apply(SceneArchiveCombatEvent e)
        {
            var packet = e.Packet;
            var contribution = CombatContributionClassifier.Evaluate(packet);

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
            _lastSkillCode = packet.SkillCode;
            _revision = Math.Max(_revision, e.Revision);
            var observedAt = packet.Timestamp > 0 ? packet.Timestamp : e.Revision;
            _firstObserved = _firstObserved > 0 ? Math.Min(_firstObserved, observedAt) : observedAt;
            _lastObserved = Math.Max(_lastObserved, observedAt);
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
            if (pair.FirstObserved > 0)
                _firstObserved = _firstObserved > 0 ? Math.Min(_firstObserved, pair.FirstObserved) : pair.FirstObserved;
            _lastObserved = Math.Max(_lastObserved, pair.LastObserved);
            _revision = Math.Max(_revision, pair.Revision);
        }
    }

    private readonly record struct EventKey(long Revision, int SourceId, int TargetId, int SkillCode, long Timestamp, int Damage, CombatEventKind EventKind, CombatValueKind ValueKind);

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
            var eventBuilders = new Dictionary<int, List<int>>();
            for (var i = 0; i < events.Count; i++)
            {
                var e = events[i];
                AddEventIndex(eventBuilders, e.SourceId, i);
                if (e.TargetId != e.SourceId)
                    AddEventIndex(eventBuilders, e.TargetId, i);
            }

            var eventIndicesByCombatant = new Dictionary<int, int[]>(eventBuilders.Count);
            foreach (var (combatantId, indices) in eventBuilders)
            {
                indices.Sort((left, right) => CompareEventIndices(left, right, events));
                eventIndicesByCombatant[combatantId] = [.. indices];
            }

            var outgoingBuilders = new Dictionary<int, List<DirectedPairKey>>();
            var incomingBuilders = new Dictionary<int, List<DirectedPairKey>>();
            if (pairs.Count > 0)
            {
                for (var i = 0; i < pairs.Count; i++)
                    AddPair(outgoingBuilders, incomingBuilders, pairs[i].Key);
            }
            else
            {
                var pairKeys = new HashSet<DirectedPairKey>();
                for (var i = 0; i < events.Count; i++)
                {
                    var e = events[i];
                    if (e.SourceId <= 0 || e.TargetId <= 0)
                        continue;

                    pairKeys.Add(new DirectedPairKey(e.SourceId, e.TargetId));
                }

                foreach (var key in pairKeys.OrderBy(static p => p.SourceId).ThenBy(static p => p.TargetId))
                    AddPair(outgoingBuilders, incomingBuilders, key);
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

        private static void AddEventIndex(Dictionary<int, List<int>> builders, int combatantId, int eventIndex)
        {
            if (combatantId <= 0)
                return;

            if (!builders.TryGetValue(combatantId, out var indices))
            {
                indices = [];
                builders[combatantId] = indices;
            }

            indices.Add(eventIndex);
        }

        private static void AddPair(Dictionary<int, List<DirectedPairKey>> outgoingBuilders, Dictionary<int, List<DirectedPairKey>> incomingBuilders, DirectedPairKey key)
        {
            if (key.SourceId <= 0 || key.TargetId <= 0)
                return;

            AddPairKey(outgoingBuilders, key.SourceId, key);
            AddPairKey(incomingBuilders, key.TargetId, key);
        }

        private static void AddPairKey(Dictionary<int, List<DirectedPairKey>> builders, int combatantId, DirectedPairKey key)
        {
            if (!builders.TryGetValue(combatantId, out var pairs))
            {
                pairs = [];
                builders[combatantId] = pairs;
            }

            pairs.Add(key);
        }

        private static Dictionary<int, DirectedPairKey[]> FreezePairs(Dictionary<int, List<DirectedPairKey>> builders)
        {
            var result = new Dictionary<int, DirectedPairKey[]>(builders.Count);
            foreach (var (combatantId, pairs) in builders)
            {
                pairs.Sort(static (left, right) =>
                {
                    var cmp = left.SourceId.CompareTo(right.SourceId);
                    return cmp != 0 ? cmp : left.TargetId.CompareTo(right.TargetId);
                });
                result[combatantId] = [.. pairs];
            }

            return result;
        }
    }
}

public sealed class SceneArchiveCombatEvent
{
    public ParsedCombatPacket Packet { get; init; } = new();
    public int SourceId { get; init; }
    public int TargetId { get; init; }
    public long Revision { get; init; }

    public static SceneArchiveCombatEvent From(in CombatDetailEvent e) => new()
    {
        Packet = e.Packet.DeepClone(),
        SourceId = e.SourceId,
        TargetId = e.TargetId,
        Revision = e.Revision
    };

    public SceneArchiveCombatEvent DeepClone() => new()
    {
        Packet = Packet.DeepClone(),
        SourceId = SourceId,
        TargetId = TargetId,
        Revision = Revision
    };

    public CombatDetailEvent ToDetailEvent() => new(Packet.DeepClone(), SourceId, TargetId, Revision);
}

public sealed class SceneArchiveEntityIdentity
{
    public int EntityId { get; init; }
    public int? NpcCode { get; init; }
    public NpcKind Kind { get; init; }
    public string Nickname { get; init; } = string.Empty;
    public bool IsPlayer { get; init; }
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
        Nickname = e.Nickname ?? string.Empty,
        IsPlayer = e.IsPlayer,
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
        Nickname = Nickname,
        IsPlayer = IsPlayer,
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
    public long LastObservedAtMilliseconds { get; init; }
    public bool HasHp { get; init; }

    public SceneArchiveBossFocus DeepClone() => new()
    {
        InstanceId = InstanceId,
        Hp = Hp,
        MaxHp = MaxHp,
        LastObservedAtMilliseconds = LastObservedAtMilliseconds,
        HasHp = HasHp
    };
}
