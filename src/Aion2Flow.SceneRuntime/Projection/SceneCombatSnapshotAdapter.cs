using System.Numerics;
using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public sealed class SceneCombatSnapshotAdapter(EntityStore entities, CombatStore combat, SceneBoundaryStore boundary, BossFocusStore? bossFocus = null, Guid encounterId = default)
{
    private const int SmallSetStackCapacity = 64;

    private readonly Dictionary<int, CombatantClassEvidence> _classEvidence = [];
    private readonly Dictionary<int, int> _inferredOwnerBySummon = [];
    private readonly Dictionary<int, int> _nextInferredOwnerBySummon = [];
    private readonly Dictionary<int, SummonOwnerInferenceAccumulator> _ownerInferenceBySource = [];
    private readonly Dictionary<SkillCategory, OwnerCandidateAccumulator> _ownerCandidatesByCategory = [];
    private readonly Dictionary<SummonOwnerInferenceKey, OwnerCandidateAccumulator> _directOwnerCandidatesBySummonCategory = [];
    private readonly Dictionary<int, int> _resolvedCombatantIds = [];
    private readonly Dictionary<int, bool> _knownSummons = [];
    private readonly Dictionary<int, TargetInfo> _targetInfos = [];
    private long _ownerInferenceCombatRevision = -1;
    private long _ownerInferenceEntityRevision = -1;
    private long _ownerInferenceSkillMapRevision = -1;
    private long _ownerInferenceVersion;
    private int _ownerInferenceScannedEventCount;
    private long _classEvidenceCombatRevision = -1;
    private long _classEvidenceEntityRevision = -1;
    private long _classEvidenceOwnerVersion = -1;
    private long _classEvidenceSkillMapRevision = -1;
    private int _classEvidenceScannedEventCount;
    private long _resolveCacheEntityRevision = -1;
    private long _resolveCacheOwnerVersion = -1;
    private bool _ownerInferenceReady;
    private bool _hasProjectionBaseline;

    public SceneCombatSnapshotAdapter(EntityStore entities, CombatStore combat, SceneBoundaryStore boundary)
        : this(entities, combat, boundary, null, default)
    {
    }

    internal SceneCombatSnapshotAdapter(EntityStore entities, CombatStore combat, SceneBoundaryStore boundary, BossFocusStore? bossFocus, Guid encounterId, SceneCombatSnapshotAdapterSnapshot snapshot)
        : this(entities, combat, boundary, bossFocus, encounterId)
    {
        RestoreSnapshot(snapshot);
    }

    internal SceneCombatSnapshotAdapterSnapshot CreateProjectionSnapshot()
    {
        PrepareProjectionCaches();
        EnsureClassEvidence();
        return new SceneCombatSnapshotAdapterSnapshot(
            CreateClassEvidenceSnapshot(),
            CreateInferredOwnerSnapshot(),
            CreateOwnerInferenceSourceSnapshot(),
            CreateOwnerCandidateSnapshot(),
            CreateDirectOwnerCandidateSnapshot(),
            _ownerInferenceCombatRevision,
            _ownerInferenceEntityRevision,
            _ownerInferenceSkillMapRevision,
            _ownerInferenceVersion,
            _ownerInferenceReady,
            _classEvidenceCombatRevision,
            _classEvidenceEntityRevision,
            _classEvidenceOwnerVersion,
            _classEvidenceSkillMapRevision);
    }

    public SceneCombatSnapshot CreateSnapshot(SceneKind kind = SceneKind.Standard)
    {
        var builder = new SceneCombatSnapshotBuilder();
        builder.Reset(encounterId, kind, combat.Combatants.Count, 0);
        BuildSnapshot(builder);
        return builder.ToSnapshot(combat.Revision);
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
        builder.SetEncounterWindow(start, end, encounterTime);

        if (encounterTime > 0)
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

        builder.SetEncounter(EvaluateEncounter(trackingTargetId, encounterTime, targetObservation, now));
    }

    public IReadOnlyList<CombatDetailEvent> CreateDetailEvents(SceneCombatSnapshot snapshot, int combatantId)
    {
        if (!CanProjectDetailCombatant(snapshot, combatantId))
            return [];

        var events = new List<CombatDetailEvent>();
        WriteDetailEvents(snapshot, combatantId, new ListDetailEventWriter(events));
        return events;
    }

    internal CombatDetailWriteResult WriteDetailEvents(SceneCombatSnapshot snapshot, int combatantId, ICombatDetailEventWriter writer)
        => WriteDetailEvents(snapshot, combatantId, writer, CombatDetailProjectionScope.EncounterWindow);

    internal CombatDetailWriteResult WriteDetailEvents(SceneCombatSnapshot snapshot, int combatantId, ICombatDetailEventWriter writer, CombatDetailProjectionScope scope)
    {
        if (!CanProjectDetailCombatant(snapshot, combatantId))
            return default;

        PrepareProjectionCaches();

        var count = 0;
        var revision = 0L;
        var records = combat.EventSpan;
        foreach (ref readonly var record in records)
        {
            if (!TryCreateDetailEventCached(snapshot, combatantId, in record, scope, out var detailEvent))
                continue;

            writer.Add(in detailEvent);
            count++;
            revision = Math.Max(revision, detailEvent.Revision);
        }

        return new CombatDetailWriteResult(count, revision);
    }

    internal bool TryCreateDetailEvent(SceneCombatSnapshot snapshot, int combatantId, in CombatEventRecord record, out CombatDetailEvent detailEvent)
    {
        PrepareProjectionCaches();
        return TryCreateDetailEventCached(snapshot, combatantId, in record, CombatDetailProjectionScope.EncounterWindow, out detailEvent);
    }

    private bool TryCreateDetailEventCached(SceneCombatSnapshot snapshot, int combatantId, in CombatEventRecord record, CombatDetailProjectionScope scope, out CombatDetailEvent detailEvent)
    {
        var eventSourceId = ResolveCombatantIdCached(record.SourceId);
        if (eventSourceId != combatantId && record.TargetId != combatantId)
        {
            detailEvent = default;
            return false;
        }

        if (!ShouldIncludeDetailEvent(in record, eventSourceId, record.TargetId, snapshot, scope))
        {
            detailEvent = default;
            return false;
        }

        detailEvent = new CombatDetailEvent(record.Observation, eventSourceId, record.TargetId, ObservedAt(record), record.Revision);
        return true;
    }

    public CombatSkillBreakdownSnapshot CreateSkillBreakdown(SceneCombatSnapshot snapshot, int combatantId)
    {
        if (combatantId <= 0 || snapshot.EncounterTime <= 0 || snapshot.EncounterEndTime < snapshot.EncounterStartTime)
            return CombatSkillBreakdownSnapshot.Empty;

        PrepareProjectionCaches();
        var skills = new Dictionary<CombatActionKey, SkillMetrics>();
        ApplySkillBreakdownEvents(snapshot, combatantId, skills);

        return CombatSkillBreakdownSnapshot.From(skills);
    }

    private static bool IsSnapshotTarget(SceneCombatSnapshot snapshot, int entityId) =>
        snapshot.TargetObservation?.InstanceId == entityId || snapshot.Encounter.TrackingTargetId == entityId;

    private bool CanProjectDetailCombatant(SceneCombatSnapshot snapshot, int combatantId) =>
        combatantId > 0 &&
        snapshot.EncounterTime > 0 &&
        snapshot.EncounterEndTime >= snapshot.EncounterStartTime &&
        (snapshot.Combatants.ContainsKey(combatantId) ||
         IsSnapshotTarget(snapshot, combatantId) ||
         CombatPairProjection.GetCombatant(combat, combatantId) is not null);

    public int ResolveDetailCombatantId(int entityId)
    {
        PrepareProjectionCaches();
        return ResolveCombatantIdCached(entityId);
    }

    internal bool IsSummonDamageTarget(int sourceId, int targetId, long damage)
    {
        PrepareProjectionCaches();
        return IsSummonDamageTargetCached(sourceId, targetId, damage);
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
            if (!HasDamageActivity(pair) || IsSummonDamageTargetCached(pair.SourceId, pair.TargetId, pair.TotalDamage))
                continue;

            var targetId = pair.TargetId;
            if (targetId <= 0 || IsKnownSummonCached(targetId))
                continue;

            var firstObserved = FirstObservedAt(pair);
            ref var info = ref CollectionsMarshal.GetValueRefOrAddDefault(_targetInfos, targetId, out var exists);
            if (!exists)
                info = new TargetInfo(firstObserved, lastObserved);

            info.Add(firstObserved);
            info.Add(lastObserved);
        }

        if (_targetInfos.Count == 0)
            return new TargetDecision(0, 0, 0, latestObservedAt);

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
    }

    private void ProcessClassEvidenceEvents(ReadOnlySpan<CombatEventRecord> events)
    {
        foreach (ref readonly var record in events)
        {
            var sourceId = ResolveCombatantIdCached(record.SourceId);
            if (sourceId <= 0 || sourceId != record.SourceId || IsKnownNpcCombatant(sourceId) || IsKnownSummonCached(sourceId))
                continue;

            var observation = record.Observation;
            if (!CombatantClassEvidence.TryCreate(in observation, out var characterClass, out var score))
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

    private void ApplySkillBreakdownEvents(SceneCombatSnapshot snapshot, int combatantId, Dictionary<CombatActionKey, SkillMetrics> skills)
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

        if (relevant.Count == 0)
            return;

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

    private static void AddSkillEvent(Dictionary<CombatActionKey, SkillMetrics> skills, in CombatEventRecord e)
    {
        var observation = e.Observation;
        var actionKey = CombatActionKey.FromObservation(in observation);
        ref var metrics = ref CollectionsMarshal.GetValueRefOrAddDefault(skills, actionKey, out var exists);
        if (!exists)
        {
            metrics = new SkillMetrics(in observation);
        }

        metrics.ProcessObservation(in observation);
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

        resolved = _inferredOwnerBySummon.TryGetValue(combatantId, out var inferredOwnerId) ? inferredOwnerId : combatantId;
        return resolved;
    }

    private bool IsSummonDamageTargetCached(in CombatEventRecord e)
    {
        return IsSummonDamageTargetCached(e.SourceId, e.TargetId, e.ContributesDamage ? e.Observation.Damage : 0);
    }

    private bool IsSummonDamageTargetCached(int sourceId, int targetId, long damage)
    {
        if (targetId <= 0 || damage <= 0)
            return false;

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
        if (entities.TryGet(entityId, out var entity))
        {
            if (entity.OwnerKind == EntityOwnerKind.Summon || entity.Kind == NpcKind.Summon)
                return true;

            if (entity.Kind != NpcKind.Summon && IsExplicitNonSummon(entity))
                return false;
        }

        return _inferredOwnerBySummon.ContainsKey(entityId);
    }

    private bool IsExplicitKnownSummonCore(int entityId) =>
        entities.TryGet(entityId, out var entity) && entity.OwnerKind == EntityOwnerKind.Summon;

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

    private static bool HasDamageActivity(CombatPairRecord pair) =>
        pair.TotalDamage > 0 || pair.AttemptCount > 0 || pair.EvadeCount > 0 || pair.InvincibleCount > 0 || pair.MultiHitCount > 0;

    private static bool IsRelevantRecoveryEvent(in CombatEventRecord e, int sourceId, int targetId, ref SmallIntSet relevant)
    {
        if (e.Observation.Damage <= 0 || (!relevant.Contains(sourceId) && !relevant.Contains(targetId)))
            return false;

        return IsRecoveryEvent(in e);
    }

    private static bool IsRelevantRecoveryEvent(in CombatEventRecord e, int sourceId, int targetId, SceneCombatSnapshot snapshot)
    {
        if (e.Observation.Damage <= 0)
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
        return e.Observation.EventKind is CombatEventKind.Healing or CombatEventKind.Support
               || e.Observation.ValueKind is CombatValueKind.Healing or CombatValueKind.PeriodicHealing or CombatValueKind.DrainHealing or CombatValueKind.Shield or CombatValueKind.Support;
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

    private void ProcessOwnerInferenceEvents(ReadOnlySpan<CombatEventRecord> events)
    {
        if (CombatResourceRegistry.SkillMap.Count == 0)
            return;

        foreach (ref readonly var e in events)
        {
            var sourceId = e.SourceId;
            if (sourceId <= 0 || IsExplicitKnownSummonCore(sourceId))
                continue;

            if (entities.TryGet(sourceId, out var entity) && entity.Kind is not NpcKind.Unknown and not NpcKind.Summon)
                continue;

            var observation = e.Observation;
            if (!TryResolveSkill(in observation, out var skill))
                continue;

            if (IsSummonOwnerCandidateSkill(skill) && IsDirectSummonOwnerSupportEvidence(in e) && IsPotentialImplicitSummonTarget(sourceId, e.TargetId))
            {
                var key = new SummonOwnerInferenceKey(e.TargetId, skill.Category);
                ref var directOwners = ref CollectionsMarshal.GetValueRefOrAddDefault(_directOwnerCandidatesBySummonCategory, key, out var directOwnersExist);
                if (!directOwnersExist)
                    directOwners = default;
                directOwners.Add(sourceId);
            }

            ref var source = ref CollectionsMarshal.GetValueRefOrAddDefault(_ownerInferenceBySource, sourceId, out var sourceExists);
            if (!sourceExists)
                source = new SummonOwnerInferenceAccumulator(sourceId);

            if (IsPreexistingSummonSignatureSkill(skill))
            {
                source.AddSummonSkillCategory(skill.Category);
                continue;
            }

            if (!IsSummonOwnerCandidateSkill(skill))
                continue;

            source.HasOwnerSkillEvidence = true;
            ref var owners = ref CollectionsMarshal.GetValueRefOrAddDefault(_ownerCandidatesByCategory, skill.Category, out var ownersExist);
            if (!ownersExist)
                owners = default;
            owners.Add(sourceId);
        }
    }

    private bool RebuildInferredSummonOwners()
    {
        _nextInferredOwnerBySummon.Clear();
        if (CombatResourceRegistry.SkillMap.Count != 0)
        {
            foreach (var (_, source) in _ownerInferenceBySource)
            {
                if (source.HasOwnerSkillEvidence || !source.TryGetSingleSummonSkillCategory(out var category))
                    continue;

                if (category == SkillCategory.Unknown)
                    continue;

                var ownerId = _directOwnerCandidatesBySummonCategory.TryGetValue(new SummonOwnerInferenceKey(source.SourceId, category), out var directOwners)
                    ? directOwners.ResolveUniqueOwner(source.SourceId)
                    : 0;
                if (ownerId <= 0 && _ownerCandidatesByCategory.TryGetValue(category, out var owners))
                    ownerId = owners.ResolveUniqueOwner(source.SourceId);
                if (ownerId > 0)
                    _nextInferredOwnerBySummon[source.SourceId] = ownerId;
            }
        }

        if (DictionaryEquals(_inferredOwnerBySummon, _nextInferredOwnerBySummon))
            return false;

        _inferredOwnerBySummon.Clear();
        foreach (var (summonId, ownerId) in _nextInferredOwnerBySummon)
            _inferredOwnerBySummon[summonId] = ownerId;

        _ownerInferenceVersion++;
        return true;
    }

    private static bool DictionaryEquals(Dictionary<int, int> left, Dictionary<int, int> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || other != value)
                return false;
        }

        return true;
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

    private SceneCombatSnapshotOwnerEntry[] CreateInferredOwnerSnapshot()
    {
        if (_inferredOwnerBySummon.Count == 0)
            return [];

        var result = new SceneCombatSnapshotOwnerEntry[_inferredOwnerBySummon.Count];
        var index = 0;
        foreach (var (summonId, ownerId) in _inferredOwnerBySummon)
            result[index++] = new SceneCombatSnapshotOwnerEntry(summonId, ownerId);
        return result;
    }

    private SceneCombatSnapshotOwnerInferenceSourceEntry[] CreateOwnerInferenceSourceSnapshot()
    {
        if (_ownerInferenceBySource.Count == 0)
            return [];

        var result = new SceneCombatSnapshotOwnerInferenceSourceEntry[_ownerInferenceBySource.Count];
        var index = 0;
        foreach (var (sourceId, source) in _ownerInferenceBySource)
            result[index++] = new SceneCombatSnapshotOwnerInferenceSourceEntry(sourceId, source.HasOwnerSkillEvidence, source.SummonSkillCategoryMask);
        return result;
    }

    private SceneCombatSnapshotOwnerCandidateEntry[] CreateOwnerCandidateSnapshot()
    {
        if (_ownerCandidatesByCategory.Count == 0)
            return [];

        var result = new SceneCombatSnapshotOwnerCandidateEntry[_ownerCandidatesByCategory.Count];
        var index = 0;
        foreach (var (category, owners) in _ownerCandidatesByCategory)
            result[index++] = new SceneCombatSnapshotOwnerCandidateEntry(category, owners.ToArray());
        return result;
    }

    private SceneCombatSnapshotDirectOwnerCandidateEntry[] CreateDirectOwnerCandidateSnapshot()
    {
        if (_directOwnerCandidatesBySummonCategory.Count == 0)
            return [];

        var result = new SceneCombatSnapshotDirectOwnerCandidateEntry[_directOwnerCandidatesBySummonCategory.Count];
        var index = 0;
        foreach (var (key, owners) in _directOwnerCandidatesBySummonCategory)
            result[index++] = new SceneCombatSnapshotDirectOwnerCandidateEntry(key.SummonId, key.Category, owners.ToArray());
        return result;
    }

    private void RestoreSnapshot(SceneCombatSnapshotAdapterSnapshot snapshot)
    {
        for (var i = 0; i < snapshot.ClassEvidence.Length; i++)
        {
            var entry = snapshot.ClassEvidence[i];
            _classEvidence[entry.EntityId] = entry.Evidence;
        }

        for (var i = 0; i < snapshot.InferredOwners.Length; i++)
        {
            var entry = snapshot.InferredOwners[i];
            _inferredOwnerBySummon[entry.SummonId] = entry.OwnerId;
        }

        for (var i = 0; i < snapshot.OwnerInferenceSources.Length; i++)
        {
            var entry = snapshot.OwnerInferenceSources[i];
            _ownerInferenceBySource[entry.SourceId] = SummonOwnerInferenceAccumulator.FromSnapshot(entry.SourceId, entry.HasOwnerSkillEvidence, entry.SummonSkillCategoryMask);
        }

        for (var i = 0; i < snapshot.OwnerCandidates.Length; i++)
        {
            var entry = snapshot.OwnerCandidates[i];
            _ownerCandidatesByCategory[entry.Category] = OwnerCandidateAccumulator.FromSnapshot(entry.OwnerIds);
        }

        for (var i = 0; i < snapshot.DirectOwnerCandidates.Length; i++)
        {
            var entry = snapshot.DirectOwnerCandidates[i];
            _directOwnerCandidatesBySummonCategory[new SummonOwnerInferenceKey(entry.SummonId, entry.Category)] = OwnerCandidateAccumulator.FromSnapshot(entry.OwnerIds);
        }

        _ownerInferenceCombatRevision = snapshot.OwnerInferenceCombatRevision;
        _ownerInferenceEntityRevision = snapshot.OwnerInferenceEntityRevision;
        _ownerInferenceSkillMapRevision = snapshot.OwnerInferenceSkillMapRevision;
        _ownerInferenceVersion = snapshot.OwnerInferenceVersion;
        _ownerInferenceReady = snapshot.OwnerInferenceReady;
        _ownerInferenceScannedEventCount = 0;
        _classEvidenceCombatRevision = snapshot.ClassEvidenceCombatRevision;
        _classEvidenceEntityRevision = snapshot.ClassEvidenceEntityRevision;
        _classEvidenceOwnerVersion = snapshot.ClassEvidenceOwnerVersion;
        _classEvidenceSkillMapRevision = snapshot.ClassEvidenceSkillMapRevision;
        _classEvidenceScannedEventCount = 0;
        _hasProjectionBaseline = true;
    }

    private void ResetOwnerInferenceScan()
    {
        _ownerInferenceBySource.Clear();
        _ownerCandidatesByCategory.Clear();
        _directOwnerCandidatesBySummonCategory.Clear();
        _ownerInferenceScannedEventCount = 0;
    }

    private void PrepareProjectionCaches()
    {
        EnsureOwnerInference();
        EnsureResolveCaches();
    }

    private void EnsureClassEvidence()
    {
        var combatRevision = combat.Revision;
        var entityRevision = entities.Revision;
        var ownerVersion = _ownerInferenceVersion;
        var skillMapRevision = CombatResourceRegistry.SkillMapRevision;
        var events = combat.EventSpan;
        var rebuildFromStart = combatRevision < _classEvidenceCombatRevision ||
                               (!_hasProjectionBaseline &&
                                (_classEvidenceEntityRevision != entityRevision ||
                                 _classEvidenceOwnerVersion != ownerVersion ||
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
        _classEvidenceEntityRevision = entityRevision;
        _classEvidenceOwnerVersion = ownerVersion;
        _classEvidenceSkillMapRevision = skillMapRevision;
    }

    private void EnsureOwnerInference()
    {
        var combatRevision = combat.Revision;
        var entityRevision = entities.Revision;
        var skillMapRevision = CombatResourceRegistry.SkillMapRevision;
        if (_ownerInferenceReady &&
            _ownerInferenceCombatRevision == combatRevision &&
            (_hasProjectionBaseline || _ownerInferenceEntityRevision == entityRevision) &&
            (_hasProjectionBaseline || _ownerInferenceSkillMapRevision == skillMapRevision))
        {
            return;
        }

        var events = combat.EventSpan;
        var rebuildFromStart = combatRevision < _ownerInferenceCombatRevision ||
                               (!_hasProjectionBaseline &&
                                (!_ownerInferenceReady ||
                                 _ownerInferenceEntityRevision != entityRevision ||
                                 _ownerInferenceSkillMapRevision != skillMapRevision ||
                                 _ownerInferenceScannedEventCount > events.Length));

        if (rebuildFromStart)
            ResetOwnerInferenceScan();

        if (_ownerInferenceScannedEventCount < events.Length)
        {
            ProcessOwnerInferenceEvents(events[_ownerInferenceScannedEventCount..]);
            _ownerInferenceScannedEventCount = events.Length;
        }

        if (rebuildFromStart || _ownerInferenceCombatRevision != combatRevision)
            RebuildInferredSummonOwners();

        _ownerInferenceCombatRevision = combatRevision;
        _ownerInferenceEntityRevision = entityRevision;
        _ownerInferenceSkillMapRevision = skillMapRevision;
        _ownerInferenceReady = true;
    }

    private void EnsureResolveCaches()
    {
        if (_resolveCacheEntityRevision == entities.Revision &&
            _resolveCacheOwnerVersion == _ownerInferenceVersion)
        {
            return;
        }

        _resolvedCombatantIds.Clear();
        _knownSummons.Clear();
        _resolveCacheEntityRevision = entities.Revision;
        _resolveCacheOwnerVersion = _ownerInferenceVersion;
    }

    private struct SummonOwnerInferenceAccumulator(int sourceId)
    {
        private uint _summonSkillCategoryMask;

        public int SourceId { get; } = sourceId;
        public bool HasOwnerSkillEvidence { get; set; }
        public readonly uint SummonSkillCategoryMask => _summonSkillCategoryMask;

        public static SummonOwnerInferenceAccumulator FromSnapshot(int sourceId, bool hasOwnerSkillEvidence, uint summonSkillCategoryMask)
        {
            return new SummonOwnerInferenceAccumulator(sourceId)
            {
                HasOwnerSkillEvidence = hasOwnerSkillEvidence,
                _summonSkillCategoryMask = summonSkillCategoryMask
            };
        }

        public void AddSummonSkillCategory(SkillCategory category)
        {
            if ((uint)category < 32)
                _summonSkillCategoryMask |= 1u << (int)category;
        }

        public readonly bool TryGetSingleSummonSkillCategory(out SkillCategory category)
        {
            if (_summonSkillCategoryMask == 0 || (_summonSkillCategoryMask & (_summonSkillCategoryMask - 1)) != 0)
            {
                category = SkillCategory.Unknown;
                return false;
            }

            category = (SkillCategory)BitOperations.TrailingZeroCount(_summonSkillCategoryMask);
            return true;
        }
    }

    private struct OwnerCandidateAccumulator
    {
        private const int InlineCapacity = 4;
        private int _count;
        private int _id0;
        private int _id1;
        private int _id2;
        private int _id3;
        private HashSet<int>? _overflow;

        public void Add(int ownerId)
        {
            if (ownerId <= 0 || ContainsInline(ownerId))
                return;

            if (_overflow is { } overflow)
            {
                overflow.Add(ownerId);
                return;
            }

            if (_count < InlineCapacity)
            {
                SetInline(_count++, ownerId);
                return;
            }

            _overflow = [_id0, _id1, _id2, _id3, ownerId];
        }

        public readonly int ResolveUniqueOwner(int excludedId)
        {
            var ownerId = 0;
            if (_overflow is not null)
            {
                foreach (var candidate in _overflow)
                {
                    if (!TrySelect(candidate, excludedId, ref ownerId))
                        return 0;
                }

                return ownerId;
            }

            for (var i = 0; i < _count; i++)
            {
                var candidate = GetInline(i);
                if (!TrySelect(candidate, excludedId, ref ownerId))
                    return 0;
            }

            return ownerId;
        }

        public readonly int[] ToArray()
        {
            if (_overflow is { } overflow)
            {
                var result = overflow.ToArray();
                Array.Sort(result);
                return result;
            }

            if (_count == 0)
                return [];

            var inline = new int[_count];
            for (var i = 0; i < _count; i++)
                inline[i] = GetInline(i);
            Array.Sort(inline);
            return inline;
        }

        public static OwnerCandidateAccumulator FromSnapshot(int[] ownerIds)
        {
            var accumulator = new OwnerCandidateAccumulator();
            for (var i = 0; i < ownerIds.Length; i++)
                accumulator.Add(ownerIds[i]);
            return accumulator;
        }

        private readonly bool ContainsInline(int ownerId)
        {
            for (var i = 0; i < _count; i++)
            {
                if (GetInline(i) == ownerId)
                    return true;
            }

            return false;
        }

        private readonly int GetInline(int index)
        {
            return index switch
            {
                0 => _id0,
                1 => _id1,
                2 => _id2,
                _ => _id3
            };
        }

        private void SetInline(int index, int ownerId)
        {
            switch (index)
            {
                case 0:
                    _id0 = ownerId;
                    break;
                case 1:
                    _id1 = ownerId;
                    break;
                case 2:
                    _id2 = ownerId;
                    break;
                default:
                    _id3 = ownerId;
                    break;
            }
        }

        private static bool TrySelect(int candidate, int excludedId, ref int ownerId)
        {
            if (candidate == excludedId)
                return true;

            if (ownerId != 0)
                return false;

            ownerId = candidate;
            return true;
        }
    }

    private static bool TryResolveSkill(in CombatObservation observation, out SkillDisplayEntry skill)
    {
        if (observation.SkillCode > 0 && CombatResourceRegistry.SkillMap.TryGetValue(observation.SkillCode, out skill))
            return true;

        skill = default;
        return false;
    }

    private static bool IsSummonOwnerCandidateSkill(SkillDisplayEntry skill) =>
        skill.SourceType == SkillSourceType.PcSkill && CombatantClassEvidence.MapSkillCategoryToClass(skill.Category) is not null;

    private static bool IsPreexistingSummonSignatureSkill(SkillDisplayEntry skill) =>
        skill.Category == SkillCategory.Elementalist && skill.Name.Contains("Spirit:", StringComparison.OrdinalIgnoreCase);

    private bool IsPotentialImplicitSummonTarget(int sourceId, int targetId)
    {
        if (targetId <= 0 || targetId == sourceId)
            return false;

        if (!entities.TryGet(targetId, out var target))
            return true;

        if (target.IsPlayer || !string.IsNullOrWhiteSpace(target.Nickname))
            return false;

        return target.Kind == NpcKind.Summon || (target.Kind == NpcKind.Unknown && !target.NpcCode.HasValue);
    }

    private static bool IsDirectSummonOwnerSupportEvidence(in CombatEventRecord e) =>
        e.TargetId > 0 && e.Observation.Damage > 0 && IsRecoveryEvent(in e);

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
            hp = entity.CurrentHp;
            battleToggledOn = entity.NpcCombatActive;
        }

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

    private EncounterSummarySnapshot EvaluateEncounter(int targetId, long encounterTime, NpcRuntimeObservationSnapshot? observation, long nowMilliseconds)
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
            IsActive: encounterTime > 0 || observation?.BattleToggledOn == true || observation?.Hp.HasValue == true,
            ShouldArchive: false,
            Reason: encounterTime > 0 ? "scene-combat" : observation?.BattleToggledOn == true ? "battle-toggle" : observation?.Hp.HasValue == true ? "hp-observed" : "insufficient-signal");
    }

    private readonly record struct TargetDecision(long EncounterStartTime, long EncounterEndTime, int TrackingTargetId, long LatestObservedAt);

    private readonly record struct SummonOwnerInferenceKey(int SummonId, SkillCategory Category);

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

    private sealed class ListDetailEventWriter(List<CombatDetailEvent> events) : ICombatDetailEventWriter
    {
        public void Clear() => events.Clear();

        public void Add(in CombatDetailEvent detailEvent) => events.Add(detailEvent);
    }
}

internal sealed record SceneCombatSnapshotAdapterSnapshot(
    SceneCombatSnapshotClassEvidenceEntry[] ClassEvidence,
    SceneCombatSnapshotOwnerEntry[] InferredOwners,
    SceneCombatSnapshotOwnerInferenceSourceEntry[] OwnerInferenceSources,
    SceneCombatSnapshotOwnerCandidateEntry[] OwnerCandidates,
    SceneCombatSnapshotDirectOwnerCandidateEntry[] DirectOwnerCandidates,
    long OwnerInferenceCombatRevision,
    long OwnerInferenceEntityRevision,
    long OwnerInferenceSkillMapRevision,
    long OwnerInferenceVersion,
    bool OwnerInferenceReady,
    long ClassEvidenceCombatRevision,
    long ClassEvidenceEntityRevision,
    long ClassEvidenceOwnerVersion,
    long ClassEvidenceSkillMapRevision);

internal readonly record struct SceneCombatSnapshotClassEvidenceEntry(int EntityId, CombatantClassEvidence Evidence);

internal readonly record struct SceneCombatSnapshotOwnerEntry(int SummonId, int OwnerId);

internal readonly record struct SceneCombatSnapshotOwnerInferenceSourceEntry(int SourceId, bool HasOwnerSkillEvidence, uint SummonSkillCategoryMask);

internal readonly record struct SceneCombatSnapshotOwnerCandidateEntry(SkillCategory Category, int[] OwnerIds);

internal readonly record struct SceneCombatSnapshotDirectOwnerCandidateEntry(int SummonId, SkillCategory Category, int[] OwnerIds);
