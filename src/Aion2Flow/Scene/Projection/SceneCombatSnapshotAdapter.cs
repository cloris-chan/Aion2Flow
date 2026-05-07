using System.Globalization;
using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Combat.NpcRuntime;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Scene.Stores;

namespace Cloris.Aion2Flow.Scene.Projection;

public sealed class SceneCombatSnapshotAdapter(EntityStore entities, CombatStore combat, MetadataStore metadata, BossFocusStore? bossFocus = null, Guid battleId = default)
{
    private readonly Dictionary<int, ClassEvidence> _classEvidence = [];
    private readonly Dictionary<int, int> _inferredOwnerBySummon = [];

    public DamageMeterSnapshot CreateSnapshot()
    {
        _classEvidence.Clear();
        _inferredOwnerBySummon.Clear();
        InferPreexistingSummonOwners();
        var snapshot = new DamageMeterSnapshot
        {
            BattleId = battleId == default ? Guid.NewGuid() : battleId,
            MapId = metadata.CurrentMapId,
            MapInstanceId = metadata.CurrentMapInstanceId
        };

        var targetDecision = DecideTarget();
        var trackingTargetId = ResolveTrackingTargetId(targetDecision.TrackingTargetId);
        var damageTargetId = targetDecision.DamageTargetId > 0 ? targetDecision.DamageTargetId : trackingTargetId;
        snapshot.TargetName = ResolveTargetName(damageTargetId);
        snapshot.TargetObservation = BuildTargetObservation(trackingTargetId);

        var (start, end) = ResolveBattleWindow(targetDecision.TargetIds);
        var battleTime = end > start ? end - start : 0;
        snapshot.BattleStartTime = start;
        snapshot.BattleEndTime = end;
        snapshot.BattleTime = battleTime;

        if (battleTime > 0)
        {
            foreach (var battleEvent in EnumerateBattleEvents(start, end, null))
                ApplyEvent(snapshot, battleEvent);
        }

        var totalDamage = 0L;
        foreach (var (id, data) in snapshot.Combatants)
        {
            data.CharacterClass = ResolveCharacterClass(id);
            if (data.CharacterClass is not null)
                totalDamage += data.DamageAmount;
        }

        foreach (var data in snapshot.Combatants.Values)
        {
            data.DamagePerSecond = battleTime > 0 ? (double)data.DamageAmount / battleTime * 1000 : 0;
            data.HealingPerSecond = battleTime > 0 ? (double)data.HealingAmount / battleTime * 1000 : 0;
            data.DamageContribution = totalDamage > 0 ? (double)data.DamageAmount / totalDamage : 0;
        }

        snapshot.Encounter = EvaluateEncounter(trackingTargetId, battleTime, snapshot.TargetObservation);
        return snapshot;
    }

    public IReadOnlyList<CombatDetailEvent> CreateDetailEvents(DamageMeterSnapshot snapshot, int combatantId, CombatPairProjection projection)
    {
        if (combatantId <= 0 || snapshot.BattleTime <= 0 || snapshot.BattleStartTime <= 0 || snapshot.BattleEndTime < snapshot.BattleStartTime || !snapshot.Combatants.ContainsKey(combatantId))
            return [];

        _inferredOwnerBySummon.Clear();
        InferPreexistingSummonOwners();
        var events = new List<CombatDetailEvent>();
        AppendDetailEvents(events, snapshot, combatantId, projection.Pairs.Values);
        events.Sort(static (a, b) => a.Revision.CompareTo(b.Revision));

        return events;
    }

    public string ResolveDetailDisplayName(int entityId) => ResolveDisplayName(entityId);

    private void ApplyEvent(DamageMeterSnapshot snapshot, BattleEvent battleEvent)
    {
        if (battleEvent.SourceId <= 0)
            return;

        var metrics = GetOrAdd(snapshot, battleEvent.SourceId);
        var packet = ToPacket(battleEvent.Record, battleEvent.SourceId, battleEvent.TargetId);

        var observation = battleEvent.Record.Observation;
        if (!IsKnownNpcCombatant(battleEvent.SourceId) && !IsKnownSummon(battleEvent.SourceId) && battleEvent.Record.SourceId == battleEvent.SourceId && TryGetClassEvidence(battleEvent.SourceId, battleEvent.TargetId, in observation, out var characterClass, out var score))
        {
            ref var evidence = ref CollectionsMarshal.GetValueRefOrAddDefault(_classEvidence, battleEvent.SourceId, out _);
            evidence.Add(characterClass, score);
        }

        metrics.ProcessCombatEvent(packet);
    }

    private void AppendDetailEvents(List<CombatDetailEvent> events, DamageMeterSnapshot snapshot, int combatantId, IEnumerable<DirectedPairSnapshot> pairs)
    {
        foreach (var pair in pairs)
        {
            var sourceId = ResolveCombatantId(pair.Key.SourceId);
            if (sourceId != combatantId && pair.Key.TargetId != combatantId)
                continue;

            foreach (var record in combat.GetPairEvents(pair.Key.SourceId, pair.Key.TargetId))
            {
                var eventSourceId = ResolveCombatantId(record.SourceId);
                if (!ShouldIncludeDetailEvent(record, eventSourceId, record.TargetId, snapshot))
                    continue;

                events.Add(new CombatDetailEvent(ToPacket(record, eventSourceId, record.TargetId), eventSourceId, record.TargetId, record.Revision));
            }
        }
    }

    private bool ShouldIncludeDetailEvent(CombatEventRecord e, int sourceId, int targetId, DamageMeterSnapshot snapshot)
    {
        if (IsWithinBattleWindow(e, snapshot.BattleStartTime, snapshot.BattleEndTime))
            return !IsSummonDamageTarget(e);

        var relevant = new HashSet<int>(snapshot.Combatants.Keys);
        if (snapshot.TargetObservation?.InstanceId is int targetInstanceId && targetInstanceId > 0)
            relevant.Add(targetInstanceId);

        return IsRelevantRecoveryEvent(e, sourceId, targetId, relevant);
    }

    private CombatantMetrics GetOrAdd(DamageMeterSnapshot snapshot, int combatantId)
    {
        if (!snapshot.Combatants.TryGetValue(combatantId, out var metrics))
        {
            metrics = new CombatantMetrics(ResolveDisplayName(combatantId));
            snapshot.Combatants[combatantId] = metrics;
        }

        return metrics;
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
        foreach (var e in combat.Events)
        {
            if (!e.ContributesDamage || IsSummonDamageTarget(e))
                continue;

            var targetId = e.TargetId;
            if (targetId <= 0 || IsKnownSummon(targetId))
                continue;

            ref var info = ref CollectionsMarshal.GetValueRefOrAddDefault(targets, targetId, out var exists);
            if (!exists)
                info = new TargetInfo(targetId, 0, ObservedAt(e), ObservedAt(e));

            info.Add(e.Observation.Damage, ObservedAt(e));
        }

        return targets;
    }

    private (long Start, long End) ResolveBattleWindow(HashSet<int> targetIds)
    {
        if (targetIds.Count == 0)
            return (0, 0);

        var found = false;
        var start = long.MaxValue;
        var end = long.MinValue;

        foreach (var e in combat.Events)
        {
            if (!targetIds.Contains(e.TargetId) || !e.ContributesDamage || IsSummonDamageTarget(e))
                continue;

            found = true;
            var observedAt = ObservedAt(e);
            start = Math.Min(start, observedAt);
            end = Math.Max(end, observedAt);
        }

        if (!found)
            return (0, 0);

        if (start == end)
            ExpandSinglePointBattleWindowFromRelevantRecovery(targetIds, ref start, ref end);

        return (start, end);
    }

    private void ExpandSinglePointBattleWindowFromRelevantRecovery(HashSet<int> targetIds, ref long start, ref long end)
    {
        var relevant = new HashSet<int>();
        foreach (var e in combat.Events)
        {
            if (!targetIds.Contains(e.TargetId) || !e.ContributesDamage || IsSummonDamageTarget(e))
                continue;

            relevant.Add(ResolveCombatantId(e.SourceId));
            if (e.TargetId > 0)
                relevant.Add(e.TargetId);
        }

        if (relevant.Count == 0)
            return;

        foreach (var e in combat.Events)
        {
            if (IsWithinBattleWindow(e, start, end) || IsSummonDamageTarget(e))
                continue;

            var sourceId = ResolveCombatantId(e.SourceId);
            if (!IsRelevantRecoveryEvent(e, sourceId, e.TargetId, relevant))
                continue;

            var observedAt = ObservedAt(e);
            start = Math.Min(start, observedAt);
            end = Math.Max(end, observedAt);
        }
    }

    private IEnumerable<BattleEvent> EnumerateBattleEvents(long start, long end, HashSet<int>? filterCombatantIds)
    {
        if (start <= 0 || end < start)
            yield break;

        var relevant = new HashSet<int>();
        foreach (var e in combat.Events)
        {
            if (!IsWithinBattleWindow(e, start, end) || IsSummonDamageTarget(e))
                continue;

            var sourceId = ResolveCombatantId(e.SourceId);
            relevant.Add(sourceId);
            if (e.TargetId > 0)
                relevant.Add(e.TargetId);

            if (filterCombatantIds is null || filterCombatantIds.Contains(sourceId) || filterCombatantIds.Contains(e.TargetId))
                yield return new BattleEvent(e, sourceId, e.TargetId);
        }

        if (relevant.Count == 0)
            yield break;

        foreach (var e in combat.Events)
        {
            if (IsWithinBattleWindow(e, start, end) || IsSummonDamageTarget(e))
                continue;

            var sourceId = ResolveCombatantId(e.SourceId);
            if (!IsRelevantRecoveryEvent(e, sourceId, e.TargetId, relevant))
                continue;

            if (filterCombatantIds is null || filterCombatantIds.Contains(sourceId) || filterCombatantIds.Contains(e.TargetId))
                yield return new BattleEvent(e, sourceId, e.TargetId);
        }
    }

    private int ResolveCombatantId(int combatantId)
    {
        if (combatantId <= 0)
            return combatantId;

        if (entities.TryGet(combatantId, out var entity) && entity.OwnerEntityId is int ownerId)
            return ownerId;

        return _inferredOwnerBySummon.TryGetValue(combatantId, out var inferredOwnerId) ? inferredOwnerId : combatantId;
    }

    private bool IsSummonDamageTarget(CombatEventRecord e)
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

    private static bool IsWithinBattleWindow(CombatEventRecord e, long start, long end) =>
        ObservedAt(e) >= start && ObservedAt(e) <= end;

    private static long ObservedAt(CombatEventRecord e) =>
        e.ObservedAtMilliseconds > 0 ? e.ObservedAtMilliseconds : e.Revision;

    private static bool IsRelevantRecoveryEvent(CombatEventRecord e, int sourceId, int targetId, HashSet<int> relevant)
    {
        if (e.Observation.Damage <= 0 || (!relevant.Contains(sourceId) && !relevant.Contains(targetId)))
            return false;

        return e.Observation.EventKind is CombatEventKind.Healing or CombatEventKind.Support
               || e.Observation.ValueKind is CombatValueKind.Healing or CombatValueKind.PeriodicHealing or CombatValueKind.DrainHealing or CombatValueKind.Shield or CombatValueKind.Support;
    }

    private string ResolveTargetName(int targetId)
    {
        if (targetId <= 0 || !entities.TryGet(targetId, out var entity) || entity.NpcCode is not int npcCode)
            return string.Empty;

        if (CombatMetricsEngine.TryResolveNpcCatalogEntry(npcCode, out var catalogEntry) && !string.IsNullOrWhiteSpace(catalogEntry.Name))
            return catalogEntry.Name;

        return metadata.TryGetNpcName(npcCode, out var npcName) && !string.IsNullOrWhiteSpace(npcName) ? npcName : string.Empty;
    }

    private string ResolveDisplayName(int entityId)
    {
        if (metadata.TryGetDisplayName(entityId, out var displayName) && !string.IsNullOrWhiteSpace(displayName))
            return displayName;

        if (entities.TryGet(entityId, out var entity))
        {
            if (!string.IsNullOrWhiteSpace(entity.Nickname))
                return entity.Nickname;

            if (entity.NpcCode is int npcCode)
            {
                if (CombatMetricsEngine.TryResolveNpcCatalogEntry(npcCode, out var catalogEntry) && !string.IsNullOrWhiteSpace(catalogEntry.Name))
                    return catalogEntry.Name;

                if (metadata.TryGetNpcName(npcCode, out var npcName) && !string.IsNullOrWhiteSpace(npcName))
                    return npcName;

                return $"NPC-{npcCode}";
            }
        }

        return entityId.ToString(CultureInfo.InvariantCulture);
    }

    private int ResolveTrackingTargetId(int trackingTargetId)
    {
        if (trackingTargetId > 0)
            return trackingTargetId;

        return bossFocus is not null && bossFocus.TryGetObservedBoss(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 10_000, out var boss) ? boss.InstanceId : 0;
    }

    private CharacterClass? ResolveCharacterClass(int entityId)
    {
        if (IsKnownNpcCombatant(entityId))
            return null;

        return _classEvidence.TryGetValue(entityId, out var evidence) ? evidence.Resolve() : null;
    }

    private void InferPreexistingSummonOwners()
    {
        if (CombatMetricsEngine.SkillMap.Count == 0)
            return;

        var ownerCandidatesByCategory = new Dictionary<SkillCategory, HashSet<int>>();
        var summonCandidates = new Dictionary<int, SkillCategory>();

        foreach (var group in combat.Events.GroupBy(static e => e.SourceId))
        {
            var sourceId = group.Key;
            if (sourceId <= 0 || IsKnownSummon(sourceId))
                continue;

            if (entities.TryGet(sourceId, out var entity) && entity.Kind is not NpcKind.Unknown and not NpcKind.Summon)
                continue;

            var summonSkillCategories = new HashSet<SkillCategory>();
            var hasOwnerSkillEvidence = false;

            foreach (var e in group)
            {
                var observation = e.Observation;
                if (!TryResolveSkill(in observation, out var skill))
                    continue;

                if (IsPreexistingSummonSignatureSkill(skill))
                {
                    summonSkillCategories.Add(skill.Category);
                    continue;
                }

                if (!IsSummonOwnerCandidateSkill(skill))
                    continue;

                hasOwnerSkillEvidence = true;
                if (!ownerCandidatesByCategory.TryGetValue(skill.Category, out var owners))
                {
                    owners = [];
                    ownerCandidatesByCategory[skill.Category] = owners;
                }

                owners.Add(sourceId);
            }

            if (!hasOwnerSkillEvidence && summonSkillCategories.Count == 1 && summonSkillCategories.First() != SkillCategory.Unknown)
                summonCandidates[sourceId] = summonSkillCategories.First();
        }

        foreach (var (summonId, category) in summonCandidates)
        {
            if (_inferredOwnerBySummon.ContainsKey(summonId) || !ownerCandidatesByCategory.TryGetValue(category, out var owners))
                continue;

            var ownerId = 0;
            foreach (var candidateOwnerId in owners)
            {
                if (candidateOwnerId == summonId)
                    continue;

                if (ownerId != 0)
                {
                    ownerId = 0;
                    break;
                }

                ownerId = candidateOwnerId;
            }

            if (ownerId > 0)
                _inferredOwnerBySummon[summonId] = ownerId;
        }
    }

    private static bool TryResolveSkill(in CombatObservation observation, out Skill skill)
    {
        if (observation.SkillCode > 0 && CombatMetricsEngine.SkillMap.TryGetValue(observation.SkillCode, out skill))
            return true;

        var originalSkillCode = observation.OriginalSkillCode != 0 ? observation.OriginalSkillCode : observation.SkillCode;
        if (CombatMetricsEngine.InferOriginalSkillCode(originalSkillCode) is { } inferredSkillCode && CombatMetricsEngine.SkillMap.TryGetValue(inferredSkillCode, out skill))
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

        if (!CombatMetricsEngine.SkillMap.TryGetValue(observation.SkillCode, out var skill))
            return false;

        var mappedClass = MapSkillCategoryToClass(skill.Category);
        if (mappedClass is null || skill.SourceType != SkillSourceType.PcSkill || observation.PeriodicRelation != PeriodicEffectRelation.None || observation.EventKind == CombatEventKind.Support && targetId == sourceId)
            return false;

        score = observation.EventKind == CombatEventKind.Damage
            ? 6
            : observation.ValueKind == CombatValueKind.Shield
                ? 4
                : observation.EventKind == CombatEventKind.Healing && observation.ValueKind == CombatValueKind.Healing
                    ? 3
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

    private NpcRuntimeObservation? BuildTargetObservation(int targetId)
    {
        if (targetId <= 0 || !entities.TryGet(targetId, out var entity))
            return null;

        var observation = new NpcRuntimeObservation
        {
            InstanceId = targetId,
            Value2136 = entity.Value2136,
            Sequence2136 = entity.Sequence2136,
            Value0140 = entity.Value0140,
            Value0240 = entity.Value0240,
            State4636Value0 = entity.State4636?.State0,
            State4636Value1 = entity.State4636?.State1,
            Sequence2C38 = entity.Latest2C38?.SequenceId,
            Result2C38 = entity.Latest2C38?.ResultCode,
            Hp = entity.CurrentHp,
            BattleToggledOn = entity.BattleActive
        };
        observation.PhaseHint = NpcRuntimeObservationInterpreter.InferPhaseHint(observation);
        return observation;
    }

    private EncounterSummary EvaluateEncounter(int targetId, long battleTime, NpcRuntimeObservation? observation)
    {
        if (targetId <= 0 && bossFocus is not null && bossFocus.TryGetObservedBoss(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 10_000, out var boss))
        {
            targetId = boss.InstanceId;
            observation = BuildTargetObservation(targetId);
        }

        if (targetId <= 0)
        {
            return new EncounterSummary
            {
                TrackingTargetId = 0,
                PhaseHint = NpcRuntimePhaseHint.Unknown,
                IsActive = false,
                ShouldArchive = false,
                Reason = "no-target"
            };
        }

        return new EncounterSummary
        {
            TrackingTargetId = targetId,
            PhaseHint = observation?.PhaseHint ?? NpcRuntimePhaseHint.Unknown,
            IsActive = battleTime > 0 || observation?.BattleToggledOn == true || observation?.Hp.HasValue == true,
            ShouldArchive = false,
            Reason = battleTime > 0 ? "scene-combat" : observation?.BattleToggledOn == true ? "battle-toggle" : observation?.Hp.HasValue == true ? "hp-observed" : "insufficient-signal"
        };
    }

    private static ParsedCombatPacket ToPacket(CombatEventRecord e, int sourceId, int targetId)
    {
        var observation = e.Observation;
        var packet = new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = observation.SkillCode,
            OriginalSkillCode = observation.OriginalSkillCode,
            BaseSkillCode = observation.BaseSkillCode,
            Damage = checked((int)observation.Damage),
            HitContribution = observation.HitCount,
            AttemptContribution = observation.AttemptCount,
            DetailRaw = observation.DetailRaw,
            Marker = observation.Marker,
            Type = observation.Type,
            Flag = observation.Flag,
            LayoutTag = observation.LayoutTag,
            Loop = observation.Loop,
            MultiHitCount = observation.MultiHitCount,
            DrainHealAmount = observation.DrainHealAmount,
            RegenerationAmount = observation.RegenerationAmount,
            Modifiers = observation.Modifiers,
            ResourceKind = observation.ResourceKind,
            EventKind = observation.EventKind,
            ValueKind = observation.ValueKind,
            Timestamp = ObservedAt(e)
        };

        if (observation.PeriodicRelation != PeriodicEffectRelation.None)
            packet.SetPeriodicEffect(observation.PeriodicRelation, observation.PeriodicMode);

        if (observation.EffectTag != PacketEffectTag.None)
            packet.SetEffectTag(observation.EffectTag);

        return packet;
    }

    private readonly record struct TargetDecision(HashSet<int> TargetIds, int DamageTargetId, int TrackingTargetId);

    private readonly record struct BattleEvent(CombatEventRecord Record, int SourceId, int TargetId);

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

    private struct ClassEvidence
    {
        private Dictionary<CharacterClass, int>? _scores;

        public void Add(CharacterClass characterClass, int score)
        {
            if (score <= 0)
                return;

            _scores ??= [];
            _scores[characterClass] = _scores.TryGetValue(characterClass, out var current) ? current + score : score;
        }

        public CharacterClass? Resolve()
        {
            if (_scores is null || _scores.Count == 0)
                return null;

            CharacterClass? topClass = null;
            var topScore = 0;
            var secondScore = 0;
            foreach (var (candidateClass, candidateScore) in _scores)
            {
                if (topClass is null || candidateScore > topScore || (candidateScore == topScore && candidateClass < topClass.Value))
                {
                    secondScore = topScore;
                    topClass = candidateClass;
                    topScore = candidateScore;
                    continue;
                }

                if (candidateScore > secondScore)
                    secondScore = candidateScore;
            }

            if (topClass is null || topScore < 4)
                return null;

            return topScore - secondScore >= 2 ? topClass.Value : null;
        }
    }
}
