using System.Numerics;
using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public sealed class SceneCombatSnapshotAdapter(EntityStore entities, CombatStore combat, SceneBoundaryStore boundary, BossFocusStore? bossFocus = null, Guid encounterId = default)
{
    private const int SmallSetStackCapacity = 64;

    private readonly Dictionary<int, ClassEvidence> _classEvidence = [];
    private readonly Dictionary<int, int> _inferredOwnerBySummon = [];
    private readonly Dictionary<int, SummonOwnerInferenceAccumulator> _ownerInferenceBySource = [];
    private readonly Dictionary<SkillCategory, OwnerCandidateAccumulator> _ownerCandidatesByCategory = [];
    private bool _ownerInferenceReady;

    public SceneCombatSnapshotAdapter(EntityStore entities, CombatStore combat, SceneBoundaryStore boundary)
        : this(entities, combat, boundary, null, default)
    {
    }

    public SceneCombatSnapshot CreateSnapshot()
    {
        var builder = new SceneCombatSnapshotBuilder();
        builder.Reset(encounterId, combat.Combatants.Count, 0);
        BuildSnapshot(builder);
        return builder.ToSnapshot(combat.Revision);
    }

    internal void BuildSnapshot(SceneCombatSnapshotBuilder builder)
    {
        _classEvidence.Clear();
        ResetOwnerInference();
        EnsureOwnerInference();
        builder.SetMap(boundary.CurrentMapId, boundary.CurrentMapInstanceId);

        var targetDecision = DecideTarget();
        var now = ResolveSnapshotNow();
        var trackingTargetId = ResolveTrackingTargetId(targetDecision.TrackingTargetId, now);
        var targetObservation = BuildTargetObservation(trackingTargetId);
        builder.SetTarget(targetObservation);

        var (start, end) = ResolveEncounterWindow(targetDecision.TargetIds);
        var encounterTime = end > start ? end - start : 0;
        builder.SetEncounterWindow(start, end, encounterTime);

        if (encounterTime > 0)
        {
            ApplyEncounterEvents(builder, start, end);
        }

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
        if (combatantId <= 0 || snapshot.EncounterTime <= 0 || snapshot.EncounterStartTime <= 0 || snapshot.EncounterEndTime < snapshot.EncounterStartTime || !snapshot.Combatants.ContainsKey(combatantId) && !IsSnapshotTarget(snapshot, combatantId))
            return [];

        var events = new List<CombatDetailEvent>();
        WriteDetailEvents(snapshot, combatantId, new ListDetailEventWriter(events));
        return events;
    }

    internal CombatDetailWriteResult WriteDetailEvents(SceneCombatSnapshot snapshot, int combatantId, ICombatDetailEventWriter writer)
    {
        if (combatantId <= 0 || snapshot.EncounterTime <= 0 || snapshot.EncounterStartTime <= 0 || snapshot.EncounterEndTime < snapshot.EncounterStartTime || !snapshot.Combatants.ContainsKey(combatantId) && !IsSnapshotTarget(snapshot, combatantId))
            return default;

        ResetOwnerInference();
        EnsureOwnerInference();

        var count = 0;
        var revision = 0L;
        var records = combat.EventSpan;
        foreach (ref readonly var record in records)
        {
            if (!TryCreateDetailEvent(snapshot, combatantId, in record, out var detailEvent))
                continue;

            writer.Add(in detailEvent);
            count++;
            revision = Math.Max(revision, detailEvent.Revision);
        }

        return new CombatDetailWriteResult(count, revision);
    }

    internal bool TryCreateDetailEvent(SceneCombatSnapshot snapshot, int combatantId, in CombatEventRecord record, out CombatDetailEvent detailEvent)
    {
        EnsureOwnerInference();

        var eventSourceId = ResolveCombatantId(record.SourceId);
        if (eventSourceId != combatantId && record.TargetId != combatantId)
        {
            detailEvent = default;
            return false;
        }

        if (!ShouldIncludeDetailEvent(in record, eventSourceId, record.TargetId, snapshot))
        {
            detailEvent = default;
            return false;
        }

        detailEvent = new CombatDetailEvent(record.Observation, eventSourceId, record.TargetId, ObservedAt(record), record.Revision);
        return true;
    }

    public CombatSkillBreakdownSnapshot CreateSkillBreakdown(SceneCombatSnapshot snapshot, int combatantId)
    {
        if (combatantId <= 0 || snapshot.EncounterTime <= 0 || snapshot.EncounterStartTime <= 0 || snapshot.EncounterEndTime < snapshot.EncounterStartTime)
            return CombatSkillBreakdownSnapshot.Empty;

        ResetOwnerInference();
        EnsureOwnerInference();
        var skills = new Dictionary<int, SkillMetrics>();
        ApplySkillBreakdownEvents(snapshot, combatantId, skills);

        return CombatSkillBreakdownSnapshot.From(skills);
    }

    private static bool IsSnapshotTarget(SceneCombatSnapshot snapshot, int entityId) =>
        snapshot.TargetObservation?.InstanceId == entityId || snapshot.Encounter.TrackingTargetId == entityId;

    public int ResolveDetailCombatantId(int entityId)
    {
        EnsureOwnerInference();
        return ResolveCombatantId(entityId);
    }

    private void ApplyEvent(SceneCombatSnapshotBuilder builder, in CombatEventRecord record, int sourceId, int targetId)
    {
        if (sourceId <= 0)
            return;

        ref var metrics = ref builder.GetOrAddCombatant(sourceId);
        var observation = record.Observation;
        if (!IsKnownNpcCombatant(sourceId) && !IsKnownSummon(sourceId) && record.SourceId == sourceId && TryGetClassEvidence(sourceId, targetId, in observation, out var characterClass, out var score))
        {
            ref var evidence = ref CollectionsMarshal.GetValueRefOrAddDefault(_classEvidence, sourceId, out _);
            evidence.Add(characterClass, score);
        }

        metrics.ProcessCombatObservation(in observation);
    }

    private bool ShouldIncludeDetailEvent(in CombatEventRecord e, int sourceId, int targetId, SceneCombatSnapshot snapshot)
    {
        if (IsWithinEncounterWindow(in e, snapshot.EncounterStartTime, snapshot.EncounterEndTime))
            return !IsSummonDamageTarget(in e);

        return IsRelevantRecoveryEvent(in e, sourceId, targetId, snapshot);
    }

    private TargetDecision DecideTarget()
    {
        var targets = BuildTargetInfos();
        if (targets.Count == 0)
            return new TargetDecision([], 0, 0);

        var targetIds = new HashSet<int>(targets.Count);
        var damageTargetId = 0;
        var damage = long.MinValue;
        var trackingTargetId = 0;
        var lastObserved = long.MinValue;

        foreach (var (targetId, info) in targets)
        {
            targetIds.Add(targetId);
            if (info.Damage > damage)
            {
                damage = info.Damage;
                damageTargetId = targetId;
            }

            if (info.LastDamageAt > lastObserved)
            {
                lastObserved = info.LastDamageAt;
                trackingTargetId = targetId;
            }
        }

        return new TargetDecision(targetIds, damageTargetId, trackingTargetId);
    }

    private Dictionary<int, TargetInfo> BuildTargetInfos()
    {
        var targets = new Dictionary<int, TargetInfo>();
        var events = combat.EventSpan;
        foreach (ref readonly var e in events)
        {
            if (!e.ContributesDamage || IsSummonDamageTarget(in e))
                continue;

            var targetId = e.TargetId;
            if (targetId <= 0 || IsKnownSummon(targetId))
                continue;

            ref var info = ref CollectionsMarshal.GetValueRefOrAddDefault(targets, targetId, out var exists);
            if (!exists)
                info = new TargetInfo(targetId, 0, ObservedAt(in e), ObservedAt(in e));

            info.Add(e.Observation.Damage, ObservedAt(in e));
        }

        return targets;
    }

    private (long Start, long End) ResolveEncounterWindow(HashSet<int> targetIds)
    {
        if (targetIds.Count == 0)
            return (0, 0);

        var found = false;
        var start = long.MaxValue;
        var end = long.MinValue;

        var events = combat.EventSpan;
        foreach (ref readonly var e in events)
        {
            if (!targetIds.Contains(e.TargetId) || !e.ContributesDamage || IsSummonDamageTarget(in e))
                continue;

            found = true;
            var observedAt = ObservedAt(in e);
            start = Math.Min(start, observedAt);
            end = Math.Max(end, observedAt);
        }

        if (!found)
            return (0, 0);

        if (start == end)
            ExpandSinglePointEncounterWindowFromRelevantRecovery(targetIds, ref start, ref end);

        return (start, end);
    }

    private void ExpandSinglePointEncounterWindowFromRelevantRecovery(HashSet<int> targetIds, ref long start, ref long end)
    {
        Span<int> relevantBuffer = stackalloc int[SmallSetStackCapacity];
        var relevant = new SmallIntSet(relevantBuffer);
        var events = combat.EventSpan;
        foreach (ref readonly var e in events)
        {
            if (!targetIds.Contains(e.TargetId) || !e.ContributesDamage || IsSummonDamageTarget(in e))
                continue;

            relevant.Add(ResolveCombatantId(e.SourceId));
            if (e.TargetId > 0)
                relevant.Add(e.TargetId);
        }

        if (relevant.Count == 0)
            return;

        var events2 = combat.EventSpan;
        foreach (ref readonly var e in events2)
        {
            if (IsWithinEncounterWindow(in e, start, end) || IsSummonDamageTarget(in e))
                continue;

            var sourceId = ResolveCombatantId(e.SourceId);
            if (!IsRelevantRecoveryEvent(in e, sourceId, e.TargetId, ref relevant))
                continue;

            var observedAt = ObservedAt(in e);
            start = Math.Min(start, observedAt);
            end = Math.Max(end, observedAt);
        }
    }

    private void ApplyEncounterEvents(SceneCombatSnapshotBuilder builder, long start, long end)
    {
        if (start <= 0 || end < start)
            return;

        Span<int> relevantBuffer = stackalloc int[SmallSetStackCapacity];
        var relevant = new SmallIntSet(relevantBuffer);
        var events = combat.EventSpan;
        foreach (ref readonly var e in events)
        {
            if (!IsWithinEncounterWindow(in e, start, end) || IsSummonDamageTarget(in e))
                continue;

            var sourceId = ResolveCombatantId(e.SourceId);
            relevant.Add(sourceId);
            if (e.TargetId > 0)
                relevant.Add(e.TargetId);

            ApplyEvent(builder, in e, sourceId, e.TargetId);
        }

        if (relevant.Count == 0)
            return;

        var events2 = combat.EventSpan;
        foreach (ref readonly var e in events2)
        {
            if (IsWithinEncounterWindow(in e, start, end) || IsSummonDamageTarget(in e))
                continue;

            var sourceId = ResolveCombatantId(e.SourceId);
            if (!IsRelevantRecoveryEvent(in e, sourceId, e.TargetId, ref relevant))
                continue;

            ApplyEvent(builder, in e, sourceId, e.TargetId);
        }
    }

    private void ApplySkillBreakdownEvents(SceneCombatSnapshot snapshot, int combatantId, Dictionary<int, SkillMetrics> skills)
    {
        Span<int> relevantBuffer = stackalloc int[SmallSetStackCapacity];
        var relevant = new SmallIntSet(relevantBuffer);
        var events = combat.EventSpan;
        foreach (ref readonly var e in events)
        {
            if (!IsWithinEncounterWindow(in e, snapshot.EncounterStartTime, snapshot.EncounterEndTime) || IsSummonDamageTarget(in e))
                continue;

            var sourceId = ResolveCombatantId(e.SourceId);
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
            if (IsWithinEncounterWindow(in e, snapshot.EncounterStartTime, snapshot.EncounterEndTime) || IsSummonDamageTarget(in e))
                continue;

            var sourceId = ResolveCombatantId(e.SourceId);
            if (sourceId != combatantId || !IsRelevantRecoveryEvent(in e, sourceId, e.TargetId, ref relevant))
                continue;

            AddSkillEvent(skills, in e);
        }
    }

    private static void AddSkillEvent(Dictionary<int, SkillMetrics> skills, in CombatEventRecord e)
    {
        var observation = e.Observation;
        ref var metrics = ref CollectionsMarshal.GetValueRefOrAddDefault(skills, observation.SkillCode, out var exists);
        if (!exists)
        {
            metrics = new SkillMetrics(in observation);
        }

        metrics.ProcessObservation(in observation);
    }

    private int ResolveCombatantId(int combatantId)
    {
        if (combatantId <= 0)
            return combatantId;

        if (entities.TryGet(combatantId, out var entity) && entity.OwnerEntityId is int ownerId)
            return ownerId;

        return _inferredOwnerBySummon.TryGetValue(combatantId, out var inferredOwnerId) ? inferredOwnerId : combatantId;
    }

    private bool IsSummonDamageTarget(in CombatEventRecord e)
    {
        if (e.TargetId <= 0 || !e.ContributesDamage)
            return false;

        if (IsKnownSummon(e.TargetId))
            return true;

        return ResolveCombatantId(e.SourceId) == ResolveCombatantId(e.TargetId);
    }

    private bool IsKnownSummon(int entityId) =>
        _inferredOwnerBySummon.ContainsKey(entityId) || entities.TryGet(entityId, out var entity) && (entity.OwnerEntityId.HasValue || entity.Kind == NpcKind.Summon);

    private bool IsKnownNpcCombatant(int entityId) =>
        entities.TryGet(entityId, out var entity) && (entity.NpcCode.HasValue || entity.Kind is NpcKind.Monster or NpcKind.Boss or NpcKind.Friendly or NpcKind.Summon);

    private bool ShouldDisplayCombatant(int entityId)
    {
        if (!entities.TryGet(entityId, out var entity))
            return true;

        if (entity.NpcCode.HasValue)
            return false;

        return entity.Kind is not (NpcKind.Monster or NpcKind.Boss or NpcKind.Friendly or NpcKind.Summon);
    }

    private static bool IsWithinEncounterWindow(in CombatEventRecord e, long start, long end) =>
        ObservedAt(e) >= start && ObservedAt(e) <= end;

    private static long ObservedAt(in CombatEventRecord e) =>
        e.ObservedAtMilliseconds > 0 ? e.ObservedAtMilliseconds : e.Revision;

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

        return _classEvidence.TryGetValue(entityId, out var evidence) ? evidence.Resolve() : null;
    }

    private void InferPreexistingSummonOwners()
    {
        if (CombatResourceRegistry.SkillMap.Count == 0)
            return;

        _ownerInferenceBySource.Clear();
        _ownerCandidatesByCategory.Clear();

        var events = combat.EventSpan;
        foreach (ref readonly var e in events)
        {
            var sourceId = e.SourceId;
            if (sourceId <= 0 || IsKnownSummon(sourceId))
                continue;

            if (entities.TryGet(sourceId, out var entity) && entity.Kind is not NpcKind.Unknown and not NpcKind.Summon)
                continue;

            var observation = e.Observation;
            if (!TryResolveSkill(in observation, out var skill))
                continue;

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

        foreach (var (_, source) in _ownerInferenceBySource)
        {
            if (source.HasOwnerSkillEvidence || !source.TryGetSingleSummonSkillCategory(out var category))
                continue;

            if (category == SkillCategory.Unknown || _inferredOwnerBySummon.ContainsKey(source.SourceId) || !_ownerCandidatesByCategory.TryGetValue(category, out var owners))
                continue;

            var ownerId = owners.ResolveUniqueOwner(source.SourceId);
            if (ownerId > 0)
                _inferredOwnerBySummon[source.SourceId] = ownerId;
        }
    }

    private struct SummonOwnerInferenceAccumulator(int sourceId)
    {
        private uint _summonSkillCategoryMask;

        public int SourceId { get; } = sourceId;
        public bool HasOwnerSkillEvidence { get; set; }

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

    private void ResetOwnerInference()
    {
        _inferredOwnerBySummon.Clear();
        _ownerInferenceReady = false;
    }

    private void EnsureOwnerInference()
    {
        if (_ownerInferenceReady)
            return;

        InferPreexistingSummonOwners();
        _ownerInferenceReady = true;
    }

    private static bool TryResolveSkill(in CombatObservation observation, out Skill skill)
    {
        if (observation.SkillCode > 0 && CombatResourceRegistry.SkillMap.TryGetValue(observation.SkillCode, out skill))
            return true;

        var originalSkillCode = observation.OriginalSkillCode != 0 ? observation.OriginalSkillCode : observation.SkillCode;
        if (CombatResourceRegistry.InferOriginalSkillCode(originalSkillCode) is { } inferredSkillCode && CombatResourceRegistry.SkillMap.TryGetValue(inferredSkillCode, out skill))
            return true;

        skill = default;
        return false;
    }

    private static bool IsSummonOwnerCandidateSkill(Skill skill) =>
        skill.SourceType == SkillSourceType.PcSkill && MapSkillCategoryToClass(skill.Category) is not null;

    private static bool IsPreexistingSummonSignatureSkill(Skill skill) =>
        skill.Category == SkillCategory.Elementalist && skill.Name.Contains("Spirit:", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetClassEvidence(int sourceId, int targetId, in CombatObservation observation, out CharacterClass characterClass, out int score)
    {
        characterClass = default;
        score = 0;

        if (!CombatResourceRegistry.SkillMap.TryGetValue(observation.SkillCode, out var skill))
            return false;

        var mappedClass = MapSkillCategoryToClass(skill.Category);
        if (mappedClass is null || skill.SourceType != SkillSourceType.PcSkill || observation.PeriodicRelation != PeriodicEffectRelation.None || observation.EffectTag == PacketEffectTag.RegenerationHealing)
            return false;

        score = observation.EventKind == CombatEventKind.Damage
            ? 6
            : observation.ValueKind == CombatValueKind.Shield
                ? 4
                : observation.EventKind == CombatEventKind.Healing
                    ? 3
                    : observation.EventKind == CombatEventKind.Support
                        ? 2
                        : 0;

        if (score <= 0)
            return false;

        characterClass = mappedClass.Value;
        return true;
    }

    private static CharacterClass? MapSkillCategoryToClass(SkillCategory category) =>
        category switch
        {
            SkillCategory.Gladiator => CharacterClass.Gladiator,
            SkillCategory.Templar => CharacterClass.Templar,
            SkillCategory.Ranger => CharacterClass.Ranger,
            SkillCategory.Assassin => CharacterClass.Assassin,
            SkillCategory.Sorcerer => CharacterClass.Sorcerer,
            SkillCategory.Cleric => CharacterClass.Cleric,
            SkillCategory.Elementalist => CharacterClass.Elementalist,
            SkillCategory.Chanter => CharacterClass.Chanter,
            _ => null,
        };

    private NpcRuntimeObservationSnapshot? BuildTargetObservation(int targetId)
    {
        if (targetId <= 0)
            return null;

        uint? value2136 = null;
        uint? sequence2136 = null;
        uint? value0140 = null;
        uint? value0240 = null;
        byte? state4636Value0 = null;
        byte? state4636Value1 = null;
        int? sequence2C38 = null;
        int? result2C38 = null;
        int? hp = null;
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

    private long ResolveSnapshotNow()
    {
        var now = 0L;
        var events = combat.EventSpan;
        foreach (ref readonly var e in events)
            now = Math.Max(now, ObservedAt(in e));

        if (now <= 0 && bossFocus is not null)
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

    private readonly record struct TargetDecision(HashSet<int> TargetIds, int DamageTargetId, int TrackingTargetId);

    private struct TargetInfo(int targetId, long damage, long firstDamageAt, long lastDamageAt)
    {
        public int TargetId { get; } = targetId;
        public long Damage { get; private set; } = damage;
        public long FirstDamageAt { get; private set; } = firstDamageAt;
        public long LastDamageAt { get; private set; } = lastDamageAt;

        public void Add(long damage, long observedAt)
        {
            Damage += damage;
            FirstDamageAt = FirstDamageAt > 0 ? Math.Min(FirstDamageAt, observedAt) : observedAt;
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

    private struct ClassEvidence
    {
        private int _gladiator;
        private int _templar;
        private int _assassin;
        private int _ranger;
        private int _sorcerer;
        private int _elementalist;
        private int _cleric;
        private int _chanter;

        public void Add(CharacterClass characterClass, int score)
        {
            if (score <= 0)
                return;

            switch (characterClass)
            {
                case CharacterClass.Gladiator:
                    _gladiator += score;
                    break;
                case CharacterClass.Templar:
                    _templar += score;
                    break;
                case CharacterClass.Assassin:
                    _assassin += score;
                    break;
                case CharacterClass.Ranger:
                    _ranger += score;
                    break;
                case CharacterClass.Sorcerer:
                    _sorcerer += score;
                    break;
                case CharacterClass.Elementalist:
                    _elementalist += score;
                    break;
                case CharacterClass.Cleric:
                    _cleric += score;
                    break;
                case CharacterClass.Chanter:
                    _chanter += score;
                    break;
            }
        }

        public readonly CharacterClass? Resolve()
        {
            CharacterClass? topClass = null;
            var topScore = 0;
            var secondScore = 0;

            Consider(CharacterClass.Gladiator, _gladiator, ref topClass, ref topScore, ref secondScore);
            Consider(CharacterClass.Templar, _templar, ref topClass, ref topScore, ref secondScore);
            Consider(CharacterClass.Assassin, _assassin, ref topClass, ref topScore, ref secondScore);
            Consider(CharacterClass.Ranger, _ranger, ref topClass, ref topScore, ref secondScore);
            Consider(CharacterClass.Sorcerer, _sorcerer, ref topClass, ref topScore, ref secondScore);
            Consider(CharacterClass.Elementalist, _elementalist, ref topClass, ref topScore, ref secondScore);
            Consider(CharacterClass.Cleric, _cleric, ref topClass, ref topScore, ref secondScore);
            Consider(CharacterClass.Chanter, _chanter, ref topClass, ref topScore, ref secondScore);

            if (topClass is null || topScore < 4)
                return null;

            return topScore - secondScore >= 2 ? topClass.Value : null;

            static void Consider(CharacterClass candidateClass, int candidateScore, ref CharacterClass? topClass, ref int topScore, ref int secondScore)
            {
                if (candidateScore <= 0)
                    return;

                if (topClass is null || candidateScore > topScore || (candidateScore == topScore && candidateClass < topClass.Value))
                {
                    secondScore = topScore;
                    topClass = candidateClass;
                    topScore = candidateScore;
                    return;
                }

                if (candidateScore > secondScore)
                    secondScore = candidateScore;
            }
        }
    }

    private sealed class ListDetailEventWriter(List<CombatDetailEvent> events) : ICombatDetailEventWriter
    {
        public void Clear() => events.Clear();

        public void Add(in CombatDetailEvent detailEvent) => events.Add(detailEvent);
    }
}
