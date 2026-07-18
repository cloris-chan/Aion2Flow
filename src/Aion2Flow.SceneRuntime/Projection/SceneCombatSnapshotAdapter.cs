using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public sealed class SceneCombatSnapshotAdapter(EntityStore entities, EntityVitalStore entityVitals, CombatStore combat, MechanicStore mechanics, ResourceStore resources, SceneBoundaryStore boundary, BossFocusStore? bossFocus = null, Guid encounterId = default)
{
    private const int SmallSetStackCapacity = 64;

    private readonly Dictionary<int, CombatantClassEvidence> _classEvidence = [];
    private readonly Dictionary<int, int> _resolvedCombatantIds = [];
    private readonly Dictionary<int, bool> _knownSummons = [];
    private readonly Dictionary<int, TargetInfo> _targetInfos = [];
    private long _classEvidenceCombatRevision = -1;
    private long _classEvidenceIdentityRevision = -1;
    private long _classEvidenceSkillMapRevision = -1;
    private int _classEvidenceScannedEventCount;
    private long _resolveCacheIdentityRevision = -1;
    private bool _hasProjectionBaseline;

    public long ReadModelRevision => SaturatingAdd(combat.Revision, mechanics.Revision, resources.Revision);

    public SceneCombatSnapshotAdapter(EntityStore entities, EntityVitalStore entityVitals, CombatStore combat, MechanicStore mechanics, ResourceStore resources, SceneBoundaryStore boundary)
        : this(entities, entityVitals, combat, mechanics, resources, boundary, null, default)
    {
    }

    internal SceneCombatSnapshotAdapter(EntityStore entities, EntityVitalStore entityVitals, CombatStore combat, MechanicStore mechanics, ResourceStore resources, SceneBoundaryStore boundary, BossFocusStore? bossFocus, Guid encounterId, SceneCombatSnapshotAdapterSnapshot snapshot)
        : this(entities, entityVitals, combat, mechanics, resources, boundary, bossFocus, encounterId)
    {
        RestoreSnapshot(snapshot);
    }

    internal SceneCombatSnapshotAdapterSnapshot CreateProjectionSnapshot()
    {
        PrepareProjectionCaches();
        EnsureClassEvidence();
        return new SceneCombatSnapshotAdapterSnapshot(
            CreateClassEvidenceSnapshot(),
            _classEvidenceCombatRevision,
            _classEvidenceIdentityRevision,
            _classEvidenceSkillMapRevision);
    }

    public SceneCombatSnapshot CreateSnapshot(SceneKind kind = SceneKind.Standard)
    {
        var builder = new SceneCombatSnapshotBuilder();
        builder.Reset(encounterId, kind, combat.Combatants.Count + mechanics.Combatants.Count + (resources.Pairs.Count * 2), 0);
        BuildSnapshot(builder);
        return builder.ToSnapshot(ReadModelRevision);
    }

    internal void BuildSnapshot(SceneCombatSnapshotBuilder builder)
    {
        PrepareProjectionCaches();
        EnsureClassEvidence();
        builder.SetMap(boundary.CurrentMapId, boundary.CurrentMapInstanceId, boundary.SceneTransitionRevision);

        var targetDecision = BuildTargetDecisionFromCombatState();
        var now = ResolveSnapshotNow(targetDecision.LatestObservedAt);
        var trackingTargetId = ResolveTrackingTargetId(targetDecision.TrackingTargetId, now);
        var targetObservation = BuildTargetObservation(trackingTargetId);
        builder.SetTarget(targetObservation);

        var start = targetDecision.EncounterStartTime;
        var end = targetDecision.EncounterEndTime;
        if (start == end && start >= 0 && combat.EventSpan.Length > 0)
            ExpandSinglePointEncounterWindowFromRelevantRecovery(ref start, ref end);
        var encounterTime = end > start ? end - start : 0;
        var hasMaterializedActivity = combat.EventSpan.Length > 0 || mechanics.Events.Count > 0 || resources.Events.Count > 0;
        builder.SetEncounterWindow(start, end, encounterTime);

        if (hasMaterializedActivity)
            ApplyCombatState(builder);
        var totalDamage = 0L;
        foreach (var id in builder.CombatantIds)
        {
            ref var metrics = ref builder.GetExistingCombatant(id);
            metrics.CharacterClass = ResolveCharacterClass(id);
            metrics.IsVisiblePlayerCombatant = ShouldDisplayCombatant(id);
            if (metrics.CharacterClass is not null)
                totalDamage += metrics.DamageAmount;
        }

        foreach (var id in builder.CombatantIds)
        {
            ref var metrics = ref builder.GetExistingCombatant(id);
            metrics.DamagePerSecond = encounterTime > 0 ? (double)metrics.DamageAmount / encounterTime * 1000 : 0;
            metrics.HealingPerSecond = encounterTime > 0 ? (double)metrics.HealingAmount / encounterTime * 1000 : 0;
            metrics.DamageContribution = totalDamage > 0 ? (double)metrics.DamageAmount / totalDamage : 0;
        }

        builder.SetEncounter(EvaluateEncounter(trackingTargetId, hasMaterializedActivity, targetObservation, now));
    }

    public CombatDetailEventSet CreateDetailEvents(SceneCombatSnapshot snapshot, int combatantId)
    {
        if (!CanProjectDetailCombatant(snapshot, combatantId))
            return CombatDetailEventSet.Empty;

        var writer = new ListDetailEventWriter();
        WriteDetailEvents(snapshot, combatantId, writer);
        return writer.ToEventSet();
    }

    internal CombatDetailWriteResult WriteDetailEvents(SceneCombatSnapshot snapshot, int combatantId, ICombatDetailEventWriter writer)
        => WriteDetailEvents(snapshot, combatantId, writer, CombatDetailProjectionScope.EncounterWindow);

    internal CombatDetailWriteResult WriteDetailEvents(SceneCombatSnapshot snapshot, int combatantId, ICombatDetailEventWriter writer, CombatDetailProjectionScope scope)
    {
        if (!CanProjectDetailCombatant(snapshot, combatantId))
            return default;

        PrepareProjectionCaches();

        var metricEventCount = 0;
        var mechanicEventCount = 0;
        var resourceEventCount = 0;
        var revision = 0L;
        var records = combat.EventSpan;
        foreach (ref readonly var record in records)
        {
            if (!TryCreateMetricDetailEventCached(snapshot, combatantId, in record, scope, out var detailEvent))
                continue;

            writer.AddMetric(in detailEvent);
            metricEventCount++;
            revision = Math.Max(revision, detailEvent.Revision);
        }

        var mechanicEvents = mechanics.Events;
        for (var i = 0; i < mechanicEvents.Count; i++)
        {
            var record = mechanicEvents[i];
            if (!TryCreateMechanicDetailEventCached(snapshot, combatantId, in record, scope, out var detailEvent))
                continue;

            writer.AddMechanic(in detailEvent);
            mechanicEventCount++;
            revision = Math.Max(revision, detailEvent.Revision);
        }

        var resourceEvents = resources.Events;
        for (var i = 0; i < resourceEvents.Count; i++)
        {
            var record = resourceEvents[i];
            if (!TryCreateResourceDetailEventCached(snapshot, combatantId, in record, scope, out var detailEvent))
                continue;

            writer.AddResource(in detailEvent);
            resourceEventCount++;
            revision = Math.Max(revision, detailEvent.Revision);
        }

        return new CombatDetailWriteResult(metricEventCount, mechanicEventCount, resourceEventCount, revision);
    }

    internal bool TryCreateMetricDetailEvent(SceneCombatSnapshot snapshot, int combatantId, in CombatEventRecord record, out CombatMetricDetailEvent detailEvent)
    {
        PrepareProjectionCaches();
        return TryCreateMetricDetailEventCached(snapshot, combatantId, in record, CombatDetailProjectionScope.EncounterWindow, out detailEvent);
    }

    internal bool TryResolveMetricDetailEventSource(SceneCombatSnapshot snapshot, in CombatEventRecord record, out int sourceId)
    {
        PrepareProjectionCaches();
        return TryResolveMetricDetailEventSourceCached(snapshot, in record, CombatDetailProjectionScope.EncounterWindow, out sourceId);
    }

    private bool TryCreateMetricDetailEventCached(SceneCombatSnapshot snapshot, int combatantId, in CombatEventRecord record, CombatDetailProjectionScope scope, out CombatMetricDetailEvent detailEvent)
    {
        var eventSourceId = ResolveCombatantIdCached(record.SourceId);
        if (eventSourceId != combatantId && record.TargetId != combatantId)
        {
            detailEvent = default;
            return false;
        }

        return TryCreateMetricDetailEventCached(snapshot, in record, eventSourceId, scope, out detailEvent);
    }

    private bool TryCreateMetricDetailEventCached(SceneCombatSnapshot snapshot, in CombatEventRecord record, int eventSourceId, CombatDetailProjectionScope scope, out CombatMetricDetailEvent detailEvent)
    {
        if (!ShouldIncludeDetailEvent(in record, eventSourceId, record.TargetId, snapshot, scope))
        {
            detailEvent = default;
            return false;
        }

        detailEvent = CreateMetricDetailEvent(in record, eventSourceId);
        return true;
    }

    private bool TryResolveMetricDetailEventSourceCached(SceneCombatSnapshot snapshot, in CombatEventRecord record, CombatDetailProjectionScope scope, out int sourceId)
    {
        sourceId = ResolveCombatantIdCached(record.SourceId);
        if (ShouldIncludeDetailEvent(in record, sourceId, record.TargetId, snapshot, scope))
            return true;

        sourceId = 0;
        return false;
    }

    private static CombatMetricDetailEvent CreateMetricDetailEvent(in CombatEventRecord record, int sourceId)
        => new(
            CombatDetailFact.Create(
                record.Observation,
                sourceId,
                record.TargetId,
                ObservedAt(record),
                record.SourceObservationOrdinal,
                record.Revision,
                record.EventKey,
                record.Raw),
            record.Contribution);

    private bool TryCreateMechanicDetailEventCached(
        SceneCombatSnapshot snapshot,
        int combatantId,
        in CombatMechanicEventRecord record,
        CombatDetailProjectionScope scope,
        out CombatMechanicDetailEvent detailEvent)
    {
        var sourceId = ResolveCombatantIdCached(record.SourceId);
        if (sourceId != combatantId && record.TargetId != combatantId ||
            scope == CombatDetailProjectionScope.EncounterWindow &&
            !IsWithinEncounterWindow(record.ObservedAtMilliseconds, snapshot.EncounterStartTime, snapshot.EncounterEndTime) ||
            IsSummonMechanicTargetCached(record.SourceId, record.TargetId, record.Mechanic))
        {
            detailEvent = default;
            return false;
        }

        detailEvent = new CombatMechanicDetailEvent(
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
        return true;
    }

    private bool TryCreateResourceDetailEventCached(
        SceneCombatSnapshot snapshot,
        int combatantId,
        in CombatResourceEventRecord record,
        CombatDetailProjectionScope scope,
        out CombatResourceDetailEvent detailEvent)
    {
        var sourceId = ResolveCombatantIdCached(record.SourceId);
        if (sourceId != combatantId && record.TargetId != combatantId ||
            scope == CombatDetailProjectionScope.EncounterWindow &&
            !IsWithinEncounterWindow(record.ObservedAtMilliseconds, snapshot.EncounterStartTime, snapshot.EncounterEndTime))
        {
            detailEvent = default;
            return false;
        }

        detailEvent = new CombatResourceDetailEvent(
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
        return true;
    }

    public CombatSkillBreakdownSnapshot CreateSkillBreakdown(SceneCombatSnapshot snapshot, int combatantId)
    {
        if (combatantId <= 0 || snapshot.EncounterEndTime < snapshot.EncounterStartTime)
            return CombatSkillBreakdownSnapshot.Empty;

        PrepareProjectionCaches();
        var skills = new Dictionary<CombatEventKey, SkillMetrics>();
        ApplySkillBreakdownEvents(snapshot, combatantId, skills);

        return CombatSkillBreakdownSnapshot.From(skills);
    }

    private static bool IsSnapshotTarget(SceneCombatSnapshot snapshot, int entityId) =>
        snapshot.TargetObservation?.InstanceId == entityId || snapshot.Encounter.TrackingTargetId == entityId;

    private static long SaturatingAdd(long first, long second, long third)
    {
        var sum = first > long.MaxValue - second ? long.MaxValue : first + second;
        return sum > long.MaxValue - third ? long.MaxValue : sum + third;
    }

    private bool CanProjectDetailCombatant(SceneCombatSnapshot snapshot, int combatantId) =>
        combatantId > 0 &&
        snapshot.EncounterEndTime >= snapshot.EncounterStartTime &&
        (snapshot.Combatants.ContainsKey(combatantId) ||
         IsSnapshotTarget(snapshot, combatantId) ||
         CombatPairProjection.GetCombatant(combat, mechanics, resources, combatantId) is not null);

    public int ResolveDetailCombatantId(int entityId)
    {
        PrepareProjectionCaches();
        return ResolveCombatantIdCached(entityId);
    }

    internal CombatDetailProjectionVersion PrepareCurrentFrameEventProjection()
    {
        PrepareProjectionCaches();
        return new CombatDetailProjectionVersion(
            entities.IdentityRevision);
    }

    internal bool TryResolveCurrentFrameEventSourcePrepared(in CombatEventRecord record, out int sourceId)
    {
        sourceId = ResolveCombatantIdCached(record.SourceId);
        if (!IsSummonDamageTargetCached(in record))
            return true;

        sourceId = 0;
        return false;
    }

    internal bool TryResolveCurrentFrameEventSourcePrepared(in CombatMechanicEventRecord record, out int sourceId)
    {
        sourceId = ResolveCombatantIdCached(record.SourceId);
        if (!IsSummonMechanicTargetCached(record.SourceId, record.TargetId, record.Mechanic))
            return true;

        sourceId = 0;
        return false;
    }

    internal int ResolveCurrentFrameEventSourcePrepared(in CombatResourceEventRecord record) =>
        ResolveCombatantIdCached(record.SourceId);

    internal bool IsSummonDamageTarget(int sourceId, int targetId, long damage)
    {
        PrepareProjectionCaches();
        return IsSummonDamageTargetCached(sourceId, targetId, damage);
    }

    internal bool IsSummonMechanicTarget(int sourceId, int targetId, CombatMechanicOccurrence mechanic)
    {
        PrepareProjectionCaches();
        return IsSummonMechanicTargetCached(sourceId, targetId, mechanic);
    }

    private bool ShouldIncludeDetailEvent(in CombatEventRecord e, int sourceId, int targetId, SceneCombatSnapshot snapshot, CombatDetailProjectionScope scope)
    {
        if (scope == CombatDetailProjectionScope.CurrentFrame)
            return !IsSummonDamageTargetCached(in e);

        if (IsWithinEncounterWindow(in e, snapshot.EncounterStartTime, snapshot.EncounterEndTime))
            return !IsSummonDamageTargetCached(in e);

        return IsRelevantRecoveryEvent(in e, sourceId, targetId, snapshot);
    }

    private TargetDecision BuildTargetDecisionFromCombatState()
    {
        _targetInfos.Clear();
        var latestObservedAt = 0L;
        foreach (var pair in combat.Pairs.Values)
        {
            var lastObserved = ObservedAt(pair);
            latestObservedAt = Math.Max(latestObservedAt, lastObserved);
            mechanics.TryGetPair(pair.SourceId, pair.TargetId, out var mechanicPair);
            if (!HasDamageActivity(pair.TotalDamage, mechanicPair))
                continue;

            var firstObserved = mechanicPair is null ? pair.FirstObserved : Math.Min(pair.FirstObserved, mechanicPair.FirstObserved);
            AddTargetInfo(pair.SourceId, pair.TargetId, pair.TotalDamage, firstObserved, Math.Max(lastObserved, mechanicPair?.LastObserved ?? 0));
        }

        foreach (var mechanicPair in mechanics.Pairs.Values)
        {
            latestObservedAt = Math.Max(latestObservedAt, mechanicPair.LastObserved);
            if (combat.Pairs.ContainsKey((mechanicPair.SourceId, mechanicPair.TargetId)) || !HasDamageActivity(0, mechanicPair))
                continue;

            AddTargetInfo(mechanicPair.SourceId, mechanicPair.TargetId, 0, mechanicPair.FirstObserved, mechanicPair.LastObserved);
        }

        foreach (var resourcePair in resources.Pairs.Values)
            latestObservedAt = Math.Max(latestObservedAt, resourcePair.LastObserved);

        if (_targetInfos.Count == 0)
            return BuildResourceOnlyTargetDecision(latestObservedAt);

        var start = long.MaxValue;
        var end = long.MinValue;
        var trackingTargetId = 0;
        var lastTargetObserved = long.MinValue;
        foreach (var (targetId, info) in _targetInfos)
        {
            start = Math.Min(start, info.FirstDamageAt);
            end = Math.Max(end, info.LastDamageAt);
            if (info.LastDamageAt > lastTargetObserved)
            {
                lastTargetObserved = info.LastDamageAt;
                trackingTargetId = targetId;
            }
        }

        return new TargetDecision(start, end, trackingTargetId, latestObservedAt);
    }

    private TargetDecision BuildResourceOnlyTargetDecision(long latestObservedAt)
    {
        var start = long.MaxValue;
        var end = long.MinValue;
        var trackingTargetId = 0;
        var lastTargetObserved = long.MinValue;
        foreach (var pair in resources.Pairs.Values)
        {
            start = Math.Min(start, pair.FirstObserved);
            end = Math.Max(end, pair.LastObserved);
            var targetId = pair.TargetId > 0 ? pair.TargetId : ResolveCombatantIdCached(pair.SourceId);
            if (targetId > 0 && pair.LastObserved > lastTargetObserved)
            {
                trackingTargetId = targetId;
                lastTargetObserved = pair.LastObserved;
            }
        }

        return start == long.MaxValue
            ? new TargetDecision(0, 0, 0, latestObservedAt)
            : new TargetDecision(start, end, trackingTargetId, latestObservedAt);
    }

    private void AddTargetInfo(int sourceId, int targetId, long damage, long firstObserved, long lastObserved)
    {
        if (targetId <= 0 || IsKnownSummonCached(targetId) || IsSummonDamageTargetCached(sourceId, targetId, damage))
            return;

        ref var info = ref CollectionsMarshal.GetValueRefOrAddDefault(_targetInfos, targetId, out var exists);
        if (!exists)
            info = new TargetInfo(firstObserved, lastObserved);
        info.Add(firstObserved);
        info.Add(lastObserved);
    }

    private void ApplyCombatState(SceneCombatSnapshotBuilder builder)
    {
        foreach (var pair in combat.Pairs.Values)
        {
            var sourceId = ResolveCombatantIdCached(pair.SourceId);
            if (sourceId <= 0)
                continue;

            ref var metrics = ref builder.GetOrAddCombatant(sourceId);
            metrics.ApplyCombatTotals(
                IsSummonDamageTargetCached(pair.SourceId, pair.TargetId, pair.TotalDamage) ? 0 : pair.TotalDamage,
                pair.TotalHealing,
                pair.TotalPeriodicHealing,
                pair.TotalDrainDamage,
                pair.TotalDrainHealing,
                pair.TotalRegenerationHealing,
                pair.TotalShield,
                pair.ShieldCount,
                pair.TotalShieldAbsorbed,
                pair.ShieldAbsorbedCount);
        }

        foreach (var pair in mechanics.Pairs.Values)
        {
            var sourceId = ResolveCombatantIdCached(pair.SourceId);
            if (sourceId > 0)
                _ = builder.GetOrAddCombatant(sourceId);
        }

        foreach (var pair in resources.Pairs.Values)
        {
            var sourceId = ResolveCombatantIdCached(pair.SourceId);
            if (sourceId > 0)
                _ = builder.GetOrAddCombatant(sourceId);
            if (pair.TargetId > 0 && pair.TargetId != sourceId)
                _ = builder.GetOrAddCombatant(pair.TargetId);
        }
    }

    private void ProcessClassEvidenceEvents(CombatEventRange events)
    {
        foreach (ref readonly var record in events)
        {
            var sourceId = ResolveCombatantIdCached(record.SourceId);
            if (sourceId <= 0 || sourceId != record.SourceId || IsKnownNpcCombatant(sourceId) || IsKnownSummonCached(sourceId))
                continue;

            var observation = record.Observation;
            var contribution = record.Contribution;
            if (!CombatantClassEvidence.TryCreate(in observation, in contribution, out var characterClass, out var score))
                continue;

            ref var evidence = ref CollectionsMarshal.GetValueRefOrAddDefault(_classEvidence, sourceId, out _);
            evidence.Add(characterClass, score);
        }
    }

    private void ExpandSinglePointEncounterWindowFromRelevantRecovery(ref long start, ref long end)
    {
        Span<int> relevantBuffer = stackalloc int[SmallSetStackCapacity];
        var relevant = new SmallIntSet(relevantBuffer);
        var events = combat.EventSpan;
        foreach (ref readonly var e in events)
        {
            if (!_targetInfos.ContainsKey(e.TargetId) || !e.ContributesDamage || IsSummonDamageTargetCached(in e))
                continue;

            relevant.Add(ResolveCombatantIdCached(e.SourceId));
            if (e.TargetId > 0)
                relevant.Add(e.TargetId);
        }

        if (relevant.Count == 0)
            return;

        var events2 = combat.EventSpan;
        foreach (ref readonly var e in events2)
        {
            var observedAt = ObservedAt(in e);
            if (IsWithinEncounterWindow(observedAt, start, end) || IsSummonDamageTargetCached(in e))
                continue;

            var sourceId = ResolveCombatantIdCached(e.SourceId);
            if (!IsRelevantRecoveryEvent(in e, sourceId, e.TargetId, ref relevant))
                continue;

            start = Math.Min(start, observedAt);
            end = Math.Max(end, observedAt);
        }
    }

    private void ApplySkillBreakdownEvents(SceneCombatSnapshot snapshot, int combatantId, Dictionary<CombatEventKey, SkillMetrics> skills)
    {
        Span<int> relevantBuffer = stackalloc int[SmallSetStackCapacity];
        var relevant = new SmallIntSet(relevantBuffer);
        var events = combat.EventSpan;
        foreach (ref readonly var e in events)
        {
            var observedAt = ObservedAt(in e);
            if (!IsWithinEncounterWindow(observedAt, snapshot.EncounterStartTime, snapshot.EncounterEndTime) || IsSummonDamageTargetCached(in e))
                continue;

            var sourceId = ResolveCombatantIdCached(e.SourceId);
            relevant.Add(sourceId);
            if (e.TargetId > 0)
                relevant.Add(e.TargetId);

            if (sourceId == combatantId)
                AddSkillEvent(skills, in e);
        }

        if (relevant.Count > 0)
        {
            var events2 = combat.EventSpan;
            foreach (ref readonly var e in events2)
            {
                var observedAt = ObservedAt(in e);
                if (IsWithinEncounterWindow(observedAt, snapshot.EncounterStartTime, snapshot.EncounterEndTime) || IsSummonDamageTargetCached(in e))
                    continue;

                var sourceId = ResolveCombatantIdCached(e.SourceId);
                if (sourceId != combatantId || !IsRelevantRecoveryEvent(in e, sourceId, e.TargetId, ref relevant))
                    continue;

                AddSkillEvent(skills, in e);
            }
        }

        var mechanicEvents = mechanics.Events;
        for (var i = 0; i < mechanicEvents.Count; i++)
        {
            var mechanicEvent = mechanicEvents[i];
            if (!IsWithinEncounterWindow(mechanicEvent.ObservedAtMilliseconds, snapshot.EncounterStartTime, snapshot.EncounterEndTime) ||
                IsSummonMechanicTargetCached(mechanicEvent.SourceId, mechanicEvent.TargetId, mechanicEvent.Mechanic) ||
                ResolveCombatantIdCached(mechanicEvent.SourceId) != combatantId)
            {
                continue;
            }

            AddSkillMechanicEvent(skills, in mechanicEvent);
        }
    }

    private static void AddSkillEvent(Dictionary<CombatEventKey, SkillMetrics> skills, in CombatEventRecord e)
    {
        var eventKey = e.EventKey;
        ref var metrics = ref CollectionsMarshal.GetValueRefOrAddDefault(skills, eventKey, out var exists);
        if (!exists)
        {
            metrics = new SkillMetrics(eventKey);
        }

        var contribution = e.Contribution;
        metrics.ProcessContribution(in contribution);
    }

    private static void AddSkillMechanicEvent(Dictionary<CombatEventKey, SkillMetrics> skills, in CombatMechanicEventRecord e)
    {
        var eventKey = e.EventKey;
        ref var metrics = ref CollectionsMarshal.GetValueRefOrAddDefault(skills, eventKey, out var exists);
        if (!exists)
            metrics = new SkillMetrics(eventKey);

        var mechanic = e.Mechanic;
        metrics.ProcessMechanic(in mechanic);
    }

    private int ResolveCombatantIdCached(int combatantId)
    {
        if (combatantId <= 0)
            return combatantId;

        ref var resolved = ref CollectionsMarshal.GetValueRefOrAddDefault(_resolvedCombatantIds, combatantId, out var exists);
        if (exists)
            return resolved;

        if (entities.TryGet(combatantId, out var entity))
        {
            if (entity.OwnerEntityId is int ownerId)
            {
                resolved = ownerId;
                return resolved;
            }

            if (entity.Kind != NpcKind.Summon && IsExplicitNonSummon(entity))
            {
                resolved = combatantId;
                return resolved;
            }
        }

        resolved = combatantId;
        return resolved;
    }

    private bool IsSummonDamageTargetCached(in CombatEventRecord e)
    {
        return IsSummonDamageTargetCached(e.SourceId, e.TargetId, e.ContributesDamage ? e.Contribution.Amount : 0);
    }

    private bool IsSummonDamageTargetCached(int sourceId, int targetId, long damage)
    {
        if (targetId <= 0 || damage <= 0)
            return false;

        return IsSummonCombatTargetCached(sourceId, targetId);
    }

    private bool IsSummonMechanicTargetCached(
        int sourceId,
        int targetId,
        CombatMechanicOccurrence mechanic)
    {
        if (targetId <= 0 || !mechanic.HasFacts)
            return false;

        return IsSummonCombatTargetCached(sourceId, targetId);
    }

    private bool IsSummonCombatTargetCached(int sourceId, int targetId)
    {
        var sourceIsSummon = IsKnownSummonCached(sourceId);
        var targetIsSummon = IsKnownSummonCached(targetId);
        if (targetIsSummon)
            return true;

        return (sourceIsSummon || targetIsSummon) && ResolveCombatantIdCached(sourceId) == ResolveCombatantIdCached(targetId);
    }

    private bool IsKnownSummonCached(int entityId)
    {
        if (entityId <= 0)
            return false;

        ref var known = ref CollectionsMarshal.GetValueRefOrAddDefault(_knownSummons, entityId, out var exists);
        if (exists)
            return known;

        known = IsKnownSummonCore(entityId);
        return known;
    }

    private bool IsKnownSummonCore(int entityId)
    {
        return entities.TryGet(entityId, out var entity) &&
               (entity.OwnerKind == EntityOwnerKind.Summon || entity.Kind == NpcKind.Summon);
    }

    private static bool IsExplicitNonSummon(EntityRecord entity) =>
        entity.IsPlayer ||
        entity.NpcCode.HasValue ||
        entity.Kind is NpcKind.Monster or NpcKind.Boss or NpcKind.Friendly or NpcKind.TrainingDummy;

    private bool IsKnownNpcCombatant(int entityId) =>
        entities.TryGet(entityId, out var entity) && (entity.NpcCode.HasValue || entity.Kind is NpcKind.Monster or NpcKind.Boss or NpcKind.Friendly or NpcKind.Summon or NpcKind.TrainingDummy);

    private bool ShouldDisplayCombatant(int entityId)
    {
        if (!entities.TryGet(entityId, out var entity))
            return true;

        if (entity.NpcCode.HasValue)
            return false;

        return entity.Kind is not (NpcKind.Monster or NpcKind.Boss or NpcKind.Friendly or NpcKind.Summon or NpcKind.TrainingDummy);
    }

    private static bool IsWithinEncounterWindow(in CombatEventRecord e, long start, long end) =>
        IsWithinEncounterWindow(ObservedAt(e), start, end);

    private static bool IsWithinEncounterWindow(long observedAt, long start, long end) =>
        observedAt >= start && observedAt <= end;

    private static long ObservedAt(in CombatEventRecord e) =>
        e.ObservedAtMilliseconds;

    private static long FirstObservedAt(CombatPairRecord pair) =>
        pair.FirstObserved;

    private static long ObservedAt(CombatPairRecord pair) =>
        pair.LastObserved;

    private static bool HasDamageActivity(long damage, CombatMechanicPairRecord? mechanic) =>
        damage > 0 ||
        mechanic is not null &&
        (mechanic.HitCount > 0 ||
         mechanic.AttemptCount > 0 ||
         mechanic.EvadeCount > 0 ||
         mechanic.InvincibleCount > 0 ||
         mechanic.MultiHitCount > 0);

    private static bool IsRelevantRecoveryEvent(in CombatEventRecord e, int sourceId, int targetId, ref SmallIntSet relevant)
    {
        if (e.Contribution.Amount <= 0 || (!relevant.Contains(sourceId) && !relevant.Contains(targetId)))
            return false;

        return IsRecoveryEvent(in e);
    }

    private static bool IsRelevantRecoveryEvent(in CombatEventRecord e, int sourceId, int targetId, SceneCombatSnapshot snapshot)
    {
        if (e.Contribution.Amount <= 0)
            return false;

        var targetObservationId = snapshot.TargetObservation?.InstanceId;
        if (!snapshot.Combatants.ContainsKey(sourceId) &&
            !snapshot.Combatants.ContainsKey(targetId) &&
            targetObservationId != sourceId &&
            targetObservationId != targetId)
        {
            return false;
        }

        return IsRecoveryEvent(in e);
    }

    private static bool IsRecoveryEvent(in CombatEventRecord e)
    {
        return e.Contribution.Metric is CombatMetricKind.Healing or CombatMetricKind.ShieldGranted or CombatMetricKind.ShieldAbsorbed;
    }

    private int ResolveTrackingTargetId(int trackingTargetId, long nowMilliseconds)
    {
        if (trackingTargetId > 0)
            return trackingTargetId;

        return bossFocus is not null && bossFocus.TryGetObservedBoss(nowMilliseconds, 10_000, out var boss) ? boss.InstanceId : 0;
    }

    private CharacterClass? ResolveCharacterClass(int entityId)
    {
        if (IsKnownNpcCombatant(entityId))
            return null;

        if (entities.TryGet(entityId, out var entity) && entity.CharacterClass is { } characterClass)
            return characterClass;

        return _classEvidence.TryGetValue(entityId, out var evidence) ? evidence.Resolve() : null;
    }

    private SceneCombatSnapshotClassEvidenceEntry[] CreateClassEvidenceSnapshot()
    {
        if (_classEvidence.Count == 0)
            return [];

        var result = new SceneCombatSnapshotClassEvidenceEntry[_classEvidence.Count];
        var index = 0;
        foreach (var (entityId, evidence) in _classEvidence)
            result[index++] = new SceneCombatSnapshotClassEvidenceEntry(entityId, evidence);
        return result;
    }

    private void RestoreSnapshot(SceneCombatSnapshotAdapterSnapshot snapshot)
    {
        for (var i = 0; i < snapshot.ClassEvidence.Length; i++)
        {
            var entry = snapshot.ClassEvidence[i];
            _classEvidence[entry.EntityId] = entry.Evidence;
        }

        _classEvidenceCombatRevision = snapshot.ClassEvidenceCombatRevision;
        _classEvidenceIdentityRevision = snapshot.ClassEvidenceIdentityRevision;
        _classEvidenceSkillMapRevision = snapshot.ClassEvidenceSkillMapRevision;
        _classEvidenceScannedEventCount = 0;
        _hasProjectionBaseline = true;
    }

    private void PrepareProjectionCaches()
    {
        EnsureResolveCaches();
    }

    private void EnsureClassEvidence()
    {
        var combatRevision = combat.Revision;
        var identityRevision = entities.IdentityRevision;
        var skillMapRevision = CombatResourceRegistry.SkillMapRevision;
        var events = combat.EventSpan;
        var rebuildFromStart = combatRevision < _classEvidenceCombatRevision ||
                               (!_hasProjectionBaseline &&
                                (_classEvidenceIdentityRevision != identityRevision ||
                                  _classEvidenceSkillMapRevision != skillMapRevision ||
                                 _classEvidenceScannedEventCount > events.Length));

        if (rebuildFromStart)
        {
            _classEvidence.Clear();
            _classEvidenceScannedEventCount = 0;
        }

        if (_classEvidenceScannedEventCount < events.Length)
        {
            ProcessClassEvidenceEvents(events[_classEvidenceScannedEventCount..]);
            _classEvidenceScannedEventCount = events.Length;
        }

        _classEvidenceCombatRevision = combatRevision;
        _classEvidenceIdentityRevision = identityRevision;
        _classEvidenceSkillMapRevision = skillMapRevision;
    }

    private void EnsureResolveCaches()
    {
        if (_resolveCacheIdentityRevision == entities.IdentityRevision)
            return;

        _resolvedCombatantIds.Clear();
        _knownSummons.Clear();
        _resolveCacheIdentityRevision = entities.IdentityRevision;
    }

    private NpcRuntimeObservationSnapshot? BuildTargetObservation(int targetId)
    {
        if (targetId <= 0)
            return null;

        long? value2136 = null;
        long? sequence2136 = null;
        long? value0140 = null;
        long? value0240 = null;
        byte? state4636Value0 = null;
        byte? state4636Value1 = null;
        int? sequence2C38 = null;
        int? result2C38 = null;
        long? hp = null;
        bool? battleToggledOn = null;

        if (entities.TryGet(targetId, out var entity))
        {
            value2136 = entity.Value2136;
            sequence2136 = entity.Sequence2136;
            value0140 = entity.Value0140;
            value0240 = entity.Value0240;
            state4636Value0 = entity.State4636?.State0;
            state4636Value1 = entity.State4636?.State1;
            sequence2C38 = entity.Latest2C38?.SequenceId;
            result2C38 = entity.Latest2C38?.ResultCode;
            battleToggledOn = entity.NpcCombatActive;
        }

        if (entityVitals.TryGet(targetId, out var vital))
            hp = vital.CurrentHp;

        var mutableObservation = new NpcRuntimeObservation
        {
            InstanceId = targetId,
            Value2136 = value2136,
            Sequence2136 = sequence2136,
            Value0140 = value0140,
            Value0240 = value0240,
            State4636Value0 = state4636Value0,
            State4636Value1 = state4636Value1,
            Sequence2C38 = sequence2C38,
            Result2C38 = result2C38,
            Hp = hp,
            BattleToggledOn = battleToggledOn
        };
        var phaseHint = NpcRuntimeObservationInterpreter.InferPhaseHint(mutableObservation);

        return new NpcRuntimeObservationSnapshot(
            targetId,
            value2136,
            sequence2136,
            value0140,
            value0240,
            state4636Value0,
            state4636Value1,
            sequence2C38,
            result2C38,
            hp,
            battleToggledOn,
            phaseHint);
    }

    private long ResolveSnapshotNow(long latestObservedAt)
    {
        var now = latestObservedAt;

        if (combat.Pairs.Count == 0 && bossFocus is not null)
        {
            var snapshots = bossFocus.GetObservedBosses(0, long.MaxValue);
            for (var i = 0; i < snapshots.Count; i++)
                now = Math.Max(now, snapshots[i].LastObservedAtMilliseconds);
        }

        return now;
    }

    private EncounterSummarySnapshot EvaluateEncounter(int targetId, bool hasMaterializedActivity, NpcRuntimeObservationSnapshot? observation, long nowMilliseconds)
    {
        if (targetId <= 0 && bossFocus is not null && bossFocus.TryGetObservedBoss(nowMilliseconds, 10_000, out var boss))
        {
            targetId = boss.InstanceId;
            observation = BuildTargetObservation(targetId);
        }

        if (targetId <= 0)
        {
            return new EncounterSummarySnapshot(
                TrackingTargetId: 0,
                PhaseHint: NpcRuntimePhaseHint.Unknown,
                IsActive: false,
                ShouldArchive: false,
                Reason: "no-target");
        }

        return new EncounterSummarySnapshot(
            TrackingTargetId: targetId,
            PhaseHint: observation?.PhaseHint ?? NpcRuntimePhaseHint.Unknown,
            IsActive: hasMaterializedActivity || observation?.BattleToggledOn == true || observation?.Hp.HasValue == true,
            ShouldArchive: false,
            Reason: hasMaterializedActivity ? "scene-combat" : observation?.BattleToggledOn == true ? "battle-toggle" : observation?.Hp.HasValue == true ? "hp-observed" : "insufficient-signal");
    }

    private readonly record struct TargetDecision(long EncounterStartTime, long EncounterEndTime, int TrackingTargetId, long LatestObservedAt);

    private struct TargetInfo(long firstDamageAt, long lastDamageAt)
    {
        public long FirstDamageAt { get; private set; } = firstDamageAt;
        public long LastDamageAt { get; private set; } = lastDamageAt;

        public void Add(long observedAt)
        {
            FirstDamageAt = Math.Min(FirstDamageAt, observedAt);
            LastDamageAt = Math.Max(LastDamageAt, observedAt);
        }
    }

    private ref struct SmallIntSet
    {
        private Span<int> _buffer;
        private HashSet<int>? _overflow;
        private int _count;

        public SmallIntSet(Span<int> buffer)
        {
            _buffer = buffer;
            _overflow = null;
            _count = 0;
        }

        public readonly int Count => _overflow?.Count ?? _count;

        public bool Add(int value)
        {
            if (value <= 0)
                return false;

            if (_overflow is { } overflow)
                return overflow.Add(value);

            for (var i = 0; i < _count; i++)
            {
                if (_buffer[i] == value)
                    return false;
            }

            if (_count < _buffer.Length)
            {
                _buffer[_count++] = value;
                return true;
            }

            _overflow = new(_buffer.Length * 2);
            for (var i = 0; i < _count; i++)
            {
                _overflow.Add(_buffer[i]);
            }

            return _overflow.Add(value);
        }

        public readonly bool Contains(int value)
        {
            if (value <= 0)
                return false;

            if (_overflow is { } overflow)
                return overflow.Contains(value);

            for (var i = 0; i < _count; i++)
            {
                if (_buffer[i] == value)
                    return true;
            }

            return false;
        }
    }

    private sealed class ListDetailEventWriter : ICombatDetailEventWriter
    {
        private readonly List<CombatMetricDetailEvent> _metricEvents = [];
        private readonly List<CombatMechanicDetailEvent> _mechanicEvents = [];
        private readonly List<CombatResourceDetailEvent> _resourceEvents = [];

        public void Clear()
        {
            _metricEvents.Clear();
            _mechanicEvents.Clear();
            _resourceEvents.Clear();
        }

        public void AddMetric(in CombatMetricDetailEvent detailEvent) => _metricEvents.Add(detailEvent);

        public void AddMechanic(in CombatMechanicDetailEvent detailEvent) => _mechanicEvents.Add(detailEvent);

        public void AddResource(in CombatResourceDetailEvent detailEvent) => _resourceEvents.Add(detailEvent);

        public CombatDetailEventSet ToEventSet() => new(_metricEvents, _mechanicEvents, _resourceEvents);
    }
}

internal sealed record SceneCombatSnapshotAdapterSnapshot(
    SceneCombatSnapshotClassEvidenceEntry[] ClassEvidence,
    long ClassEvidenceCombatRevision,
    long ClassEvidenceIdentityRevision,
    long ClassEvidenceSkillMapRevision);

internal readonly record struct SceneCombatSnapshotClassEvidenceEntry(int EntityId, CombatantClassEvidence Evidence);
