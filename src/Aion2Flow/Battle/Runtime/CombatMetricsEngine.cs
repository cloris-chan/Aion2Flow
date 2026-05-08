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
    private readonly Dictionary<int, CharacterClassInferenceState> _characterClassEvidenceByCombatant = [];
    private readonly Dictionary<int, CharacterClass> _resolvedCharacterClassByCombatant = [];

    private int _currentTarget;
    private Guid _currentBattleId = Guid.NewGuid();

    private readonly record struct TargetDecision(HashSet<int> TargetIds, string TargetName, int TrackingTargetId);

    public static SkillCollection SkillMap
    {
        get => CombatResourceRegistry.SkillMap;
        set => CombatResourceRegistry.SkillMap = value;
    }

    public static SkillCollection SkillDisplayMap => CombatResourceRegistry.SkillDisplayMap;
    public static int[] SkillCodes => CombatResourceRegistry.SkillCodes;
    public static IReadOnlyDictionary<int, NpcCatalogEntry> NpcCatalog => CombatResourceRegistry.NpcCatalog;

    public static void EnsureCombatResources() => CombatResourceRegistry.EnsureCombatResources();

    public static void LoadSkillMap(string lang) => CombatResourceRegistry.LoadSkillMap(lang);

    public static void SetGameResources(SkillCollection skillMap, IReadOnlyDictionary<int, NpcCatalogEntry> npcCatalog) => CombatResourceRegistry.SetGameResources(skillMap, npcCatalog);

    public static void UpdateDisplayResources(SkillCollection skillMap, IReadOnlyDictionary<int, NpcCatalogEntry> npcCatalog) => CombatResourceRegistry.UpdateDisplayResources(skillMap, npcCatalog);

    public static bool TryResolveNpcCatalogEntry(int npcCode, out NpcCatalogEntry entry) => CombatResourceRegistry.TryResolveNpcCatalogEntry(npcCode, out entry);

    public static NpcKind ResolveNpcKind(NpcCatalogKind kind) => CombatResourceRegistry.ResolveNpcKind(kind);

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

            InferPreexistingSummonOwners();

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
                    if (!CombatEventClassifier.CountsTowardsDamage(packet) ||
                        IsSummonDamageTarget(Store, packet))
                    {
                        continue;
                    }

                    if (flag)
                    {
                        flag = false;
                        targetInfo = new EncounterTargetInfo(target, 0, packet.Timestamp, packet.Timestamp);
                        _targetInfoMap[target] = targetInfo;
                    }
                    targetInfo?.ProcessPacket(packet);
                }
            }

            PruneSummonDamageTargets();

            var dataSnapshot = new DamageMeterSnapshot();
            var targetDecision = DecideTarget();
            dataSnapshot.BattleId = _currentBattleId;
            dataSnapshot.TargetName = targetDecision.TargetName;
            dataSnapshot.MapId = Store.CurrentMapId;
            dataSnapshot.MapInstanceId = Store.CurrentMapInstanceId;

            _currentTarget = targetDecision.TrackingTargetId;
            Store.CurrentTarget = _currentTarget;
            dataSnapshot.TargetObservation = BuildTargetObservation(_currentTarget);

            var (battleStart, battleEnd) = ResolveBattleWindow(targetDecision.TargetIds);
            var battleTime = battleEnd - battleStart;

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

                if (IsKnownNpcCombatant(uid))
                {
                    _resolvedCharacterClassByCombatant.Remove(uid);
                    _characterClassEvidenceByCombatant.Remove(uid);
                }
                else if (_resolvedCharacterClassByCombatant.TryGetValue(uid, out var resolvedCharacterClass))
                {
                    personal.CharacterClass = resolvedCharacterClass;
                }
                else if (!Store.SummonOwnerByInstance.ContainsKey(uid) &&
                         packet.SourceId == uid &&
                         (nicknameData.ContainsKey(uid) || !Store.TryGetNpcRuntimeState(uid, out var npcCheck) || !npcCheck.NpcCode.HasValue) &&
                         TryGetClassEvidence(packet, out var inferredClass, out var evidenceScore))
                {
                    if (!_characterClassEvidenceByCombatant.TryGetValue(uid, out var inferenceState))
                    {
                        inferenceState = new CharacterClassInferenceState();
                        _characterClassEvidenceByCombatant[uid] = inferenceState;
                    }

                    inferenceState.Add(inferredClass, evidenceScore);
                    if (inferenceState.Resolve() is { } resolvedClass)
                    {
                        _resolvedCharacterClassByCombatant[uid] = resolvedClass;
                        personal.CharacterClass = resolvedClass;
                    }
                }

                personal.ProcessCombatEvent(packet);
            }

            var totalDamage = 0L;
            foreach (var combatant in dataSnapshot.Combatants.Values)
            {
                if (combatant.CharacterClass is not null)
                {
                    totalDamage += combatant.DamageAmount;
                }
            }

            foreach (var data in dataSnapshot.Combatants.Values)
            {
                data.DamagePerSecond = (double)data.DamageAmount / battleTime * 1000;
                data.HealingPerSecond = (double)data.HealingAmount / battleTime * 1000;
                data.DamageContribution = totalDamage > 0
                    ? (double)data.DamageAmount / totalDamage
                    : 0;
            }

            dataSnapshot.BattleTime = battleTime;
            dataSnapshot.Encounter = BossFocusHeuristicEvaluator.Evaluate(_currentTarget, battleTime, dataSnapshot.TargetObservation);
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
            _characterClassEvidenceByCombatant.Clear();
            _resolvedCharacterClassByCombatant.Clear();
            _currentTarget = 0;
            _currentBattleId = Guid.NewGuid();
        }
    }

    internal static int? InferOriginalSkillCode(int skillCode) => CombatResourceRegistry.InferOriginalSkillCode(skillCode);

    internal static void NormalizePacketForStorage(ParsedCombatPacket packet) => CombatResourceRegistry.NormalizePacketForStorage(packet);

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

        if (packet.IsPeriodicEffect)
        {
            return false;
        }

        if (packet.EventKind == CombatEventKind.Support && packet.TargetId == packet.SourceId)
        {
            return false;
        }

        score = packet.EventKind == CombatEventKind.Damage
            ? 6
            : packet.ValueKind == CombatValueKind.Shield
                ? 4
                : packet.EventKind == CombatEventKind.Healing
                  && packet.ValueKind == CombatValueKind.Healing
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

    private bool IsKnownNpcCombatant(int combatantId)
    {
        if (combatantId <= 0)
        {
            return false;
        }

        if (!Store.TryGetNpcRuntimeState(combatantId, out var state))
        {
            return false;
        }

        if (state.NpcCode.HasValue)
        {
            return true;
        }

        return state.Kind is NpcKind.Monster or NpcKind.Boss or NpcKind.Friendly or NpcKind.Summon;
    }

    private void InferPreexistingSummonOwners()
    {
        if (SkillMap.Count == 0)
        {
            return;
        }

        var ownerCandidatesByCategory = new Dictionary<SkillCategory, HashSet<int>>();
        var summonCandidates = new Dictionary<int, SkillCategory>();

        foreach (var (sourceId, packets) in Store.CombatPacketsBySource)
        {
            if (sourceId <= 0 || Store.SummonOwnerByInstance.ContainsKey(sourceId))
            {
                continue;
            }

            if (Store.TryGetNpcRuntimeState(sourceId, out var npcState) &&
                npcState.Kind is { } npcKind &&
                npcKind is not NpcKind.Unknown and not NpcKind.Summon)
            {
                continue;
            }

            var summonSkillCategories = new HashSet<SkillCategory>();
            var hasOwnerSkillEvidence = false;

            foreach (var packet in packets)
            {
                if (!TryResolveSkill(packet, out var skill))
                {
                    continue;
                }

                if (IsPreexistingSummonSignatureSkill(skill))
                {
                    summonSkillCategories.Add(skill.Category);
                    continue;
                }

                if (!IsSummonOwnerCandidateSkill(skill))
                {
                    continue;
                }

                hasOwnerSkillEvidence = true;
                var owners = ownerCandidatesByCategory.GetValueOrDefault(skill.Category);
                if (owners is null)
                {
                    owners = [];
                    ownerCandidatesByCategory[skill.Category] = owners;
                }

                owners.Add(sourceId);
            }

            if (!hasOwnerSkillEvidence &&
                summonSkillCategories.Count == 1 &&
                summonSkillCategories.First() != SkillCategory.Unknown)
            {
                summonCandidates[sourceId] = summonSkillCategories.First();
            }
        }

        foreach (var (summonId, category) in summonCandidates)
        {
            if (Store.SummonOwnerByInstance.ContainsKey(summonId) ||
                !ownerCandidatesByCategory.TryGetValue(category, out var owners))
            {
                continue;
            }

            var ownerId = 0;
            foreach (var candidateOwnerId in owners)
            {
                if (candidateOwnerId == summonId)
                {
                    continue;
                }

                if (ownerId != 0)
                {
                    ownerId = 0;
                    break;
                }

                ownerId = candidateOwnerId;
            }

            if (ownerId > 0)
            {
                Store.AppendSummon(ownerId, summonId);
            }
        }
    }

    private static bool TryResolveSkill(ParsedCombatPacket packet, out Skill skill)
    {
        if (packet.SkillCode > 0 && SkillMap.TryGetValue(packet.SkillCode, out skill))
        {
            return true;
        }

        var originalSkillCode = packet.OriginalSkillCode != 0 ? packet.OriginalSkillCode : packet.SkillCode;
        if (InferOriginalSkillCode(originalSkillCode) is { } inferredSkillCode &&
            SkillMap.TryGetValue(inferredSkillCode, out skill))
        {
            return true;
        }

        skill = default;
        return false;
    }

    private static bool IsSummonOwnerCandidateSkill(Skill skill)
        => skill.SourceType == SkillSourceType.PcSkill &&
           MapSkillCategoryToClass(skill.Category) is not null;

    private static bool IsPreexistingSummonSignatureSkill(Skill skill)
    {
        if (skill.Category != SkillCategory.Elementalist)
        {
            return false;
        }

        return skill.Name.Contains("Spirit:", StringComparison.OrdinalIgnoreCase);
    }

    internal static SkillVariantInfo ParseSkillVariant(int originalSkillCode) => CombatResourceRegistry.ParseSkillVariant(originalSkillCode);

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

    private void PruneSummonDamageTargets()
    {
        if (_targetInfoMap.Count == 0)
        {
            return;
        }

        List<int>? summonTargets = null;
        foreach (var targetId in _targetInfoMap.Keys)
        {
            if (IsKnownSummonInstance(Store, targetId))
            {
                (summonTargets ??= []).Add(targetId);
            }
        }

        if (summonTargets is null)
        {
            return;
        }

        foreach (var targetId in summonTargets)
        {
            _targetInfoMap.Remove(targetId);
        }
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

                if (IsSummonDamageTarget(store, packet))
                {
                    continue;
                }

                var sourceId = ResolveCombatantId(store, packet.SourceId);
                var targetId = packet.TargetId;
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

                if (IsSummonDamageTarget(store, packet))
                {
                    continue;
                }

                var sourceId = ResolveCombatantId(store, packet.SourceId);
                var targetId = packet.TargetId;
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

    private static bool IsSummonDamageTarget(CombatMetricsStore store, ParsedCombatPacket packet)
    {
        if (packet.TargetId <= 0 || !CombatEventClassifier.CountsTowardsDamage(packet))
        {
            return false;
        }

        if (store.SummonOwnerByInstance.ContainsKey(packet.TargetId))
        {
            return true;
        }

        if (ResolveCombatantId(store, packet.SourceId) == ResolveCombatantId(store, packet.TargetId))
        {
            return true;
        }

        return store.TryGetNpcRuntimeState(packet.TargetId, out var state) &&
               state.Kind == NpcKind.Summon;
    }

    private static bool IsKnownSummonInstance(CombatMetricsStore store, int instanceId)
    {
        if (instanceId <= 0)
        {
            return false;
        }

        if (store.SummonOwnerByInstance.ContainsKey(instanceId))
        {
            return true;
        }

        return store.TryGetNpcRuntimeState(instanceId, out var state) &&
               state.Kind == NpcKind.Summon;
    }

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

        if (!found)
        {
            return (0, 0);
        }

        if (start == end)
        {
            ExpandSinglePointBattleWindowFromRelevantRecovery(targetIds, ref start, ref end);
        }

        return (start, end);
    }

    private void ExpandSinglePointBattleWindowFromRelevantRecovery(HashSet<int> targetIds, ref long start, ref long end)
    {
        var relevantCombatantIds = new HashSet<int>();

        foreach (var targetId in targetIds)
        {
            if (!Store.CombatPacketsByTarget.TryGetValue(targetId, out var packets))
            {
                continue;
            }

            foreach (var packet in packets)
            {
                if (!CombatEventClassifier.CountsTowardsDamage(packet) ||
                    IsSummonDamageTarget(Store, packet))
                {
                    continue;
                }

                relevantCombatantIds.Add(ResolveCombatantId(Store, packet.SourceId));
                relevantCombatantIds.Add(packet.TargetId);
            }
        }

        if (relevantCombatantIds.Count == 0)
        {
            return;
        }

        foreach (var queue in Store.CombatPacketsBySource.Values)
        {
            foreach (var packet in queue)
            {
                if (IsWithinBattleWindow(packet, start, end) ||
                    IsSummonDamageTarget(Store, packet))
                {
                    continue;
                }

                var sourceId = ResolveCombatantId(Store, packet.SourceId);
                var targetId = packet.TargetId;
                if (!IsRelevantRecoveryPacket(packet, sourceId, targetId, relevantCombatantIds))
                {
                    continue;
                }

                if (packet.Timestamp < start)
                {
                    start = packet.Timestamp;
                }

                if (packet.Timestamp > end)
                {
                    end = packet.Timestamp;
                }
            }
        }
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
