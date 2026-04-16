using System.Collections.Concurrent;
using System.Globalization;
using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Combat;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Combat.NpcRuntime;
using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Battle.Runtime;

public sealed class CombatMetricsEngine(CombatMetricsStore store)
{
    private readonly Lock _stateLock = new();

    internal readonly record struct BattlePacketContext(ParsedCombatPacket Packet, int SourceId, int TargetId);

    public CombatMetricsEngine() : this(new CombatMetricsStore())
    {
    }

    private sealed class CharacterClassInferenceState
    {
        private readonly Dictionary<CharacterClass, int> _scores = [];

        public void Add(CharacterClass characterClass, int score)
        {
            if (score <= 0)
            {
                return;
            }

            _scores[characterClass] = _scores.TryGetValue(characterClass, out var current)
                ? current + score
                : score;
        }

        public CharacterClass? Resolve()
        {
            if (_scores.Count == 0)
            {
                return null;
            }

            CharacterClass? topClass = null;
            var topScore = 0;
            var secondScore = 0;

            foreach (var (candidateClass, candidateScore) in _scores)
            {
                if (topClass is null ||
                    candidateScore > topScore ||
                    (candidateScore == topScore && candidateClass < topClass.Value))
                {
                    secondScore = topScore;
                    topClass = candidateClass;
                    topScore = candidateScore;
                    continue;
                }

                if (candidateScore > secondScore)
                {
                    secondScore = candidateScore;
                }
            }

            if (topClass is null)
            {
                return null;
            }

            if (topScore < 4)
            {
                return null;
            }

            return topScore - secondScore >= 2
                ? topClass.Value
                : null;
        }
    }

    public CombatMetricsStore Store { get; } = store;

    private readonly Dictionary<int, EncounterTargetInfo> _targetInfoMap = [];

    private int _currentTarget;
    private Guid _currentBattleId = Guid.NewGuid();

    private sealed record TargetDecision(HashSet<int> TargetIds, string TargetName, int TrackingTargetId);

    private static SkillCollection _skillMap = [];

    public static SkillCollection SkillMap
    {
        get => _skillMap;
        set
        {
            _skillMap = value ?? [];
            SkillDisplayMap = _skillMap;
            SkillCodes = [.. _skillMap.Select(x => x.Id).OrderBy(x => x)];
            ResolvedSkillCodeCache.Clear();
            CombatEventClassifier.ClearCaches();
        }
    }

    public static SkillCollection SkillDisplayMap { get; private set; } = [];
    public static int[] SkillCodes { get; private set; } = [];
    public static IReadOnlyDictionary<int, NpcCatalogEntry> NpcCatalog { get; private set; } =
        new Dictionary<int, NpcCatalogEntry>();
    private static readonly ConcurrentDictionary<int, int> ResolvedSkillCodeCache = [];

    public static void EnsureCombatResources()
    {
        if (SkillMap.Count != 0)
        {
            return;
        }

        SkillMap = ResourceDatabase.LoadCombatSkills();
    }

    public static void LoadSkillMap(string lang)
    {
        SkillMap = ResourceDatabase.LoadCombatSkills();
        UpdateDisplayResources(
            ResourceDatabase.LoadSkills(lang),
            ResourceDatabase.LoadNpcCatalog(lang));
    }

    public static void SetGameResources(SkillCollection skillMap, IReadOnlyDictionary<int, NpcCatalogEntry> npcCatalog)
    {
        SkillMap = skillMap;
        SkillDisplayMap = skillMap;
        NpcCatalog = npcCatalog;
    }

    public static void UpdateDisplayResources(SkillCollection skillMap, IReadOnlyDictionary<int, NpcCatalogEntry> npcCatalog)
    {
        EnsureCombatResources();
        SkillDisplayMap = skillMap;
        NpcCatalog = npcCatalog;
    }

    public static bool TryResolveNpcCatalogEntry(int npcCode, out NpcCatalogEntry entry)
    {
        if (NpcCatalog.TryGetValue(npcCode, out entry))
        {
            return true;
        }

        entry = default;
        return false;
    }

    public static NpcKind ResolveNpcKind(NpcCatalogKind kind)
    {
        return kind switch
        {
            NpcCatalogKind.Monster => NpcKind.Monster,
            NpcCatalogKind.Boss => NpcKind.Boss,
            NpcCatalogKind.Summon => NpcKind.Summon,
            NpcCatalogKind.Friendly => NpcKind.Friendly,
            _ => NpcKind.Unknown
        };
    }

    public Guid CurrentBattleId
    {
        get
        {
            lock (_stateLock)
            {
                return _currentBattleId;
            }
        }
    }

    public DamageMeterSnapshot CreateBattleSnapshot()
    {
        lock (_stateLock)
        {
            Store.FlushPendingOutcomeSidecars();
            var packetMap = Store.CombatPacketsByTarget;
            var nicknameData = Store.Nicknames;
            var characterClassEvidenceByCombatant = new Dictionary<int, CharacterClassInferenceState>();

            foreach (var (target, data) in packetMap)
            {
                var flag = false;
                if (!_targetInfoMap.TryGetValue(target, out var targetInfo))
                {
                    flag = true;
                }

                foreach (var packet in data)
                {
                    NormalizePacketForStorage(packet);
                    if (flag)
                    {
                        flag = false;
                        targetInfo = new EncounterTargetInfo(target, 0, packet.Timestamp, packet.Timestamp);
                        _targetInfoMap[target] = targetInfo;
                    }
                    targetInfo?.ProcessPacket(packet);
                }
            }

            var dataSnapshot = new DamageMeterSnapshot();
            var targetDecision = DecideTarget();
            dataSnapshot.BattleId = _currentBattleId;
            dataSnapshot.TargetName = targetDecision.TargetName;

            _currentTarget = targetDecision.TrackingTargetId;
            Store.CurrentTarget = _currentTarget;
            dataSnapshot.TargetObservation = BuildTargetObservation(_currentTarget);

            var (battleStart, battleEnd) = ResolveBattleWindow(targetDecision.TargetIds);
            var battleTime = battleEnd - battleStart;

            long totalDamage = 0;

            dataSnapshot.BattleStartTime = battleStart;
            dataSnapshot.BattleEndTime = battleEnd;

            if (battleTime == 0)
            {
                return dataSnapshot;
            }

            foreach (var battlePacket in EnumerateBattlePackets(Store, battleStart, battleEnd))
            {
                var packet = battlePacket.Packet;
                var uid = battlePacket.SourceId;
                if (uid <= 0)
                {
                    continue;
                }

                var nickname = nicknameData.TryGetValue(uid, out var name)
                    ? name
                    : nicknameData.TryGetValue(Store.SummonOwnerByInstance.TryGetValue(uid, out var parent) ? parent : uid, out var alt)
                        ? alt
                        : uid.ToString(CultureInfo.InvariantCulture);

                if (!dataSnapshot.Combatants.TryGetValue(uid, out var personal))
                {
                    personal = new CombatantMetrics(nickname);

                    dataSnapshot.Combatants[uid] = personal;
                }

                if (TryGetClassEvidence(packet, out var inferredClass, out var evidenceScore))
                {
                    if (!characterClassEvidenceByCombatant.TryGetValue(uid, out var inferenceState))
                    {
                        inferenceState = new CharacterClassInferenceState();
                        characterClassEvidenceByCombatant[uid] = inferenceState;
                    }

                    inferenceState.Add(inferredClass, evidenceScore);
                    personal.CharacterClass ??= inferenceState.Resolve();
                }

                if (personal.ProcessCombatEvent(packet) && personal.CharacterClass is not null)
                    totalDamage += packet.Damage;
            }

            foreach (var data in dataSnapshot.Combatants.Values)
            {
                data.DamagePerSecond = (double)data.DamageAmount / battleTime * 1000;
                data.HealingPerSecond = (double)data.HealingAmount / battleTime * 1000;
                data.DamageContribution = totalDamage > 0
                    ? (double)data.DamageAmount / totalDamage * 100
                    : 0;
            }

            dataSnapshot.BattleTime = battleTime;
            dataSnapshot.Encounter = EncounterHeuristicEvaluator.Evaluate(_currentTarget, battleTime, dataSnapshot.TargetObservation);
            return dataSnapshot;
        }
    }

    public DamageMeterSnapshot CreateSnapshot() => CreateBattleSnapshot();

    public void Reset()
    {
        lock (_stateLock)
        {
            Store.ResetCombatStorage();
            _targetInfoMap.Clear();
            _currentTarget = 0;
            _currentBattleId = Guid.NewGuid();
        }
    }

    internal static int? InferOriginalSkillCode(int skillCode)
    {
        foreach (var candidate in EnumerateSkillCodeCandidates(skillCode))
        {
            if (Array.BinarySearch(SkillCodes, candidate) >= 0)
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<int> EnumerateSkillCodeCandidates(int skillCode)
    {
        if (skillCode <= 0)
        {
            yield break;
        }

        var seen = new HashSet<int>();
        static bool Push(HashSet<int> set, int value) => value > 0 && set.Add(value);

        if (Push(seen, skillCode))
        {
            yield return skillCode;
        }

        var directChargeBase = skillCode - (skillCode % 10);
        if (Push(seen, directChargeBase))
        {
            yield return directChargeBase;
        }

        var specializationBase = skillCode - (skillCode % 10000);
        if (Push(seen, specializationBase))
        {
            yield return specializationBase;
        }

        var specializationWithChargeBase = specializationBase + (skillCode % 10);
        if (Push(seen, specializationWithChargeBase))
        {
            yield return specializationWithChargeBase;
        }

        var byHundred = skillCode / 100;
        if (Push(seen, byHundred))
        {
            yield return byHundred;
        }

        var byThousand = skillCode - (skillCode % 1000);
        if (Push(seen, byThousand))
        {
            yield return byThousand;
        }

    }

    internal static void NormalizePacketForStorage(ParsedCombatPacket packet)
    {
        if (packet.IsNormalized)
        {
            return;
        }

        var originalSkillCode = packet.OriginalSkillCode != 0 ? packet.OriginalSkillCode : packet.SkillCode;
        var variant = ParseSkillVariant(originalSkillCode);
        packet.OriginalSkillCode = variant.OriginalSkillCode;
        packet.BaseSkillCode = variant.BaseSkillCode;
        packet.ChargeStage = variant.ChargeStage;
        packet.SpecializationMask = variant.SpecializationMask;
        if (packet.Type == 3)
        {
            packet.Modifiers |= DamageModifiers.Critical;
        }
        packet.SkillCode = ResolveSkillCode(packet.SkillCode, originalSkillCode, variant);
        packet.SkillKind = CombatEventClassifier.ResolveSkillKind(packet.SkillCode);
        packet.SkillSemantics = CombatEventClassifier.ResolveSkillSemantics(packet.SkillCode);
        packet.ValueKind = CombatEventClassifier.ClassifyValueKind(packet);
        packet.EventKind = CombatEventClassifier.Classify(packet);

        if (packet.ValueKind is CombatValueKind.PeriodicDamage or CombatValueKind.PeriodicHealing
            && (packet.Modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) == 0)
        {
            packet.HitContribution = 0;
            packet.AttemptContribution = 0;
        }

        packet.IsNormalized = true;
    }

    private static int ResolveSkillCode(int packetSkillCode, int originalSkillCode, SkillVariantInfo variant)
    {
        if (SkillMap.Count == 0)
        {
            if (packetSkillCode > 0)
            {
                return packetSkillCode;
            }

            if (originalSkillCode > 0)
            {
                return originalSkillCode;
            }

            return variant.NormalizedSkillCode;
        }

        if (originalSkillCode <= 0)
        {
            return variant.NormalizedSkillCode;
        }

        if (ResolvedSkillCodeCache.TryGetValue(originalSkillCode, out var cached))
        {
            return cached;
        }

        var inferredSkillCode = InferOriginalSkillCode(originalSkillCode) ?? variant.NormalizedSkillCode;
        var resolvedSkillCode = ResolveTriggeredSiblingSkillCode(originalSkillCode, inferredSkillCode);
        ResolvedSkillCodeCache[originalSkillCode] = resolvedSkillCode;
        return resolvedSkillCode;
    }

    private static int ResolveTriggeredSiblingSkillCode(int originalSkillCode, int inferredSkillCode)
    {
        if (originalSkillCode <= 0 ||
            inferredSkillCode <= 0 ||
            SkillMap.Count == 0 ||
            Array.BinarySearch(SkillCodes, originalSkillCode) >= 0 ||
            !SkillMap.TryGetValue(inferredSkillCode, out var inferredSkill))
        {
            return inferredSkillCode;
        }

        var variantSuffix = inferredSkillCode % 10000;
        if (variantSuffix == 0)
        {
            return inferredSkillCode;
        }

        foreach (var triggeredSkillId in inferredSkill.EnumerateTriggeredSkillIds())
        {
            if (triggeredSkillId <= 0)
            {
                continue;
            }

            if (!SkillMap.TryGetValue(triggeredSkillId, out var candidate))
            {
                continue;
            }

            if (candidate.Id == inferredSkillCode ||
                candidate.Id % 10000 != variantSuffix ||
                candidate.Category != inferredSkill.Category ||
                candidate.SourceType != inferredSkill.SourceType)
            {
                continue;
            }

            return candidate.Id;
        }

        return inferredSkillCode;
    }

    private static bool TryGetClassEvidence(ParsedCombatPacket packet, out CharacterClass characterClass, out int score)
    {
        characterClass = default;
        score = 0;

        if (!SkillMap.TryGetValue(packet.SkillCode, out var skill))
        {
            return false;
        }

        var mappedClass = MapSkillCategoryToClass(skill.Category);
        if (mappedClass is null)
        {
            return false;
        }

        if (skill.SourceType != SkillSourceType.PcSkill)
        {
            return false;
        }

        var semantics = packet.SkillSemantics != SkillSemantics.None
            ? packet.SkillSemantics
            : skill.Semantics;
        var isPeriodicSelf = packet.IsPeriodicEffect &&
            packet.EffectFamily.StartsWith("periodic-self", StringComparison.Ordinal);
        var hasOffensiveSignal = (semantics & (SkillSemantics.Damage | SkillSemantics.PeriodicDamage | SkillSemantics.DrainOrAbsorb)) != 0;
        var hasShieldSignal = (semantics & SkillSemantics.ShieldOrBarrier) != 0;
        var hasHealingSignal = (semantics & SkillSemantics.Healing) != 0;
        var hasPeriodicHealingSignal = (semantics & SkillSemantics.PeriodicHealing) != 0;
        var hasSupportSignal = (semantics & SkillSemantics.Support) != 0;

        if (isPeriodicSelf)
        {
            return false;
        }

        if (hasSupportSignal && !hasOffensiveSignal && !hasShieldSignal && packet.TargetId == packet.SourceId)
        {
            return false;
        }

        score = hasOffensiveSignal
            ? 6
            : hasShieldSignal && !packet.IsPeriodicEffect
                ? 4
                : hasHealingSignal && !hasPeriodicHealingSignal && !packet.IsPeriodicEffect
                    ? 3
                    : 0;

        if (score <= 0)
        {
            return false;
        }

        characterClass = mappedClass.Value;
        return true;
    }

    private static CharacterClass? MapSkillCategoryToClass(SkillCategory category)
    {
        return category switch
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
    }

    internal static SkillVariantInfo ParseSkillVariant(int originalSkillCode)
    {
        if (originalSkillCode <= 0)
        {
            return new SkillVariantInfo(0, 0, 0, 0, 0);
        }

        var chargeStage = originalSkillCode % 10;
        var specializationDigits = (originalSkillCode / 10) % 1000;
        var specializationMask = 0;
        var specializationAccumulator = specializationDigits;

        while (specializationAccumulator > 0)
        {
            var digit = specializationAccumulator % 10;
            specializationAccumulator /= 10;
            if (digit is >= 1 and <= 5)
            {
                specializationMask |= 1 << (digit - 1);
            }
        }

        var baseSkillCode = originalSkillCode - (originalSkillCode % 10000);
        var normalizedSkillCode = baseSkillCode + chargeStage;
        return new SkillVariantInfo(originalSkillCode, normalizedSkillCode, baseSkillCode, chargeStage, specializationMask);
    }

    private TargetDecision DecideTarget()
    {
        if (_targetInfoMap.Count == 0)
        {
            return new TargetDecision([], string.Empty, 0);
        }

        var targetIds = new HashSet<int>(_targetInfoMap.Count);
        var mostDamageTarget = 0;
        var mostDamageAmount = double.MinValue;
        var mostRecentTarget = 0;
        var mostRecentTime = long.MinValue;

        foreach (var (targetId, info) in _targetInfoMap)
        {
            targetIds.Add(targetId);

            if (info.DamageAmount > mostDamageAmount)
            {
                mostDamageAmount = info.DamageAmount;
                mostDamageTarget = targetId;
            }

            if (info.LastDamageTime > mostRecentTime)
            {
                mostRecentTime = info.LastDamageTime;
                mostRecentTarget = targetId;
            }
        }

        return new TargetDecision(targetIds, ResolveTargetName(mostDamageTarget), mostRecentTarget);
    }

    private string ResolveTargetName(int target)
    {
        if (!Store.TryGetNpcRuntimeState(target, out var state) ||
            state.NpcCode is not int npcCode)
        {
            return string.Empty;
        }

        if (NpcCatalog.TryGetValue(npcCode, out var entry) && !string.IsNullOrWhiteSpace(entry.Name))
        {
            return entry.Name;
        }

        return Store.NpcNameByCode.TryGetValue(npcCode, out var name) ? name : string.Empty;
    }

    internal static IEnumerable<BattlePacketContext> EnumerateBattlePackets(
        CombatMetricsStore store,
        long battleStart,
        long battleEnd)
    {
        if (battleStart <= 0 || battleEnd < battleStart)
        {
            yield break;
        }

        var relevantCombatantIds = new HashSet<int>();

        foreach (var queue in store.CombatPacketsBySource.Values)
        {
            foreach (var packet in queue)
            {
                if (!IsWithinBattleWindow(packet, battleStart, battleEnd))
                {
                    continue;
                }

                var sourceId = ResolveCombatantId(store, packet.SourceId);
                var targetId = ResolveCombatantId(store, packet.TargetId);
                relevantCombatantIds.Add(sourceId);
                relevantCombatantIds.Add(targetId);
                yield return new BattlePacketContext(packet, sourceId, targetId);
            }
        }

        if (relevantCombatantIds.Count == 0)
        {
            yield break;
        }

        foreach (var queue in store.CombatPacketsBySource.Values)
        {
            foreach (var packet in queue)
            {
                if (IsWithinBattleWindow(packet, battleStart, battleEnd))
                {
                    continue;
                }

                var sourceId = ResolveCombatantId(store, packet.SourceId);
                var targetId = ResolveCombatantId(store, packet.TargetId);
                if (!IsRelevantRecoveryPacket(packet, sourceId, targetId, relevantCombatantIds))
                {
                    continue;
                }

                yield return new BattlePacketContext(packet, sourceId, targetId);
            }
        }
    }

    private static bool IsWithinBattleWindow(ParsedCombatPacket packet, long battleStart, long battleEnd)
        => packet.Timestamp >= battleStart && packet.Timestamp <= battleEnd;

    private (long Start, long End) ResolveBattleWindow(HashSet<int> targetIds)
    {
        var found = false;
        var start = long.MaxValue;
        var end = long.MinValue;

        foreach (var targetId in targetIds)
        {
            if (!_targetInfoMap.TryGetValue(targetId, out var info))
            {
                continue;
            }

            found = true;
            if (info.FirstDamageTime < start)
            {
                start = info.FirstDamageTime;
            }

            if (info.LastDamageTime > end)
            {
                end = info.LastDamageTime;
            }
        }

        return found ? (start, end) : (0, 0);
    }

    internal static int ResolveCombatantId(CombatMetricsStore store, int combatantId)
    {
        return store.SummonOwnerByInstance.TryGetValue(combatantId, out var ownerId)
            ? ownerId
            : combatantId;
    }

    internal static string ResolveCombatantDisplayName(CombatMetricsStore store, DamageMeterSnapshot snapshot, int combatantId)
    {
        if (store.TryGetNpcRuntimeState(combatantId, out var state) &&
            state.NpcCode is int npcCode)
        {
            if (NpcCatalog.TryGetValue(npcCode, out var entry) && !string.IsNullOrWhiteSpace(entry.Name))
            {
                return entry.Name;
            }

            if (store.NpcNameByCode.TryGetValue(npcCode, out var npcName) && !string.IsNullOrWhiteSpace(npcName))
            {
                return npcName;
            }
        }

        if (snapshot.Combatants.TryGetValue(combatantId, out var combatant) && !string.IsNullOrWhiteSpace(combatant.Nickname))
        {
            return combatant.Nickname;
        }

        if (store.Nicknames.TryGetValue(combatantId, out var nickname) && !string.IsNullOrWhiteSpace(nickname))
        {
            return nickname;
        }

        return combatantId.ToString(CultureInfo.InvariantCulture);
    }

    internal static bool IsRelevantRecoveryPacket(
        ParsedCombatPacket packet,
        int sourceId,
        int targetId,
        HashSet<int> relevantCombatantIds)
    {
        if (packet.Damage <= 0)
        {
            return false;
        }

        if (!relevantCombatantIds.Contains(sourceId) && !relevantCombatantIds.Contains(targetId))
        {
            return false;
        }

        return packet.EventKind is CombatEventKind.Healing or CombatEventKind.Support
               || packet.ValueKind is CombatValueKind.Healing
                   or CombatValueKind.PeriodicHealing
                   or CombatValueKind.DrainHealing
                   or CombatValueKind.Shield
                   or CombatValueKind.Support;
    }

    private NpcRuntimeObservation? BuildTargetObservation(int targetId)
    {
        if (targetId <= 0)
        {
            return null;
        }

        var observation = new NpcRuntimeObservation
        {
            InstanceId = targetId
        };

        if (Store.TryGetNpcRuntimeState(targetId, out var state))
        {
            if (state.Value2136 is uint value2136)
            {
                observation.Value2136 = value2136;
            }

            if (state.Sequence2136 is uint seq2136)
            {
                observation.Sequence2136 = seq2136;
            }

            if (state.Value0140 is uint value0140)
            {
                observation.Value0140 = value0140;
            }

            if (state.Value0240 is uint value0240)
            {
                observation.Value0240 = value0240;
            }

            if (state.State4636 is { } state4636)
            {
                observation.State4636Value0 = state4636.State0;
                observation.State4636Value1 = state4636.State1;
            }

            if (state.Hp is int hp)
            {
                observation.Hp = hp;
            }

            if (state.BattleToggledOn is bool battle)
            {
                observation.BattleToggledOn = battle;
            }
        }

        int? preferred2C38Sequence = observation.Sequence2136.HasValue
            ? checked((int)observation.Sequence2136.Value)
            : null;

        if (Store.TryGetNpc2C38State(targetId, preferred2C38Sequence, out var sequence2C38, out var result2C38))
        {
            observation.Sequence2C38 = sequence2C38;
            observation.Result2C38 = result2C38;
        }

        observation.PhaseHint = NpcRuntimeObservationInterpreter.InferPhaseHint(observation);
        return observation;
    }

}
