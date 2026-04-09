using System.Collections.Concurrent;
using System.Text;
using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Battle.Runtime;

public sealed class CombatMetricsStore
{
    private const int RestoreHpSkillCode = 1010000;
    private const long DrainFollowupWindowMilliseconds = 250;
    private const long SelfPeriodicHealingPoolWindowMilliseconds = 5000;
    private const int MaxTrackedMultiHitCandidates = 64;
    private const int MaxPendingDodgeSignals = 16;
    private const int MaxPendingWrappedMultiHitOwners = 8;
    private const int MultiHitSidecarSkillCode = 13350000;
    private const ushort WrappedMultiHitInnerOpcode = 0x3642;

    private static readonly HashSet<int> DodgeSkillCodes =
    [
        11000100,
        12000100,
        13000100,
        14000100,
        15000100,
        16000100,
        17000100,
        18000100
    ];

    private readonly record struct SelfPeriodicHealingPoolKey(int SourceId, int TargetId, int OriginalSkillCode);
    private readonly record struct SelfPeriodicHealingPoolState(int LastRawDamage, long LastTimestamp);
    private readonly record struct MultiHitDamageCandidate(
        ParsedCombatPacket Packet,
        int SourceId,
        int TargetId,
        int SkillCode,
        int Marker,
        int Unknown,
        int Flag,
        long FrameOrdinal,
        long ObservationOrdinal);
    private readonly record struct PendingDodgeSignal(
        int ActorId,
        int OriginalSkillCode,
        int TrackedSkillCode,
        int Marker,
        long FrameOrdinal,
        long ObservationOrdinal);
    private readonly record struct PendingWrappedMultiHitOwner(
        Guid PacketId,
        ulong Stamp,
        long ObservationOrdinal);

    private readonly ConcurrentDictionary<int, ConcurrentQueue<ParsedCombatPacket>> _packetsByTarget = new();
    private readonly ConcurrentDictionary<int, ConcurrentQueue<ParsedCombatPacket>> _packetsBySource = new();
    private readonly ConcurrentDictionary<int, string> _nicknameStorage = new();
    private readonly ConcurrentDictionary<int, int> _summonOwnerByInstance = new();
    private readonly ConcurrentDictionary<int, string> _npcNameByCode = new();
    private readonly ConcurrentDictionary<int, int> _npcCodeByInstance = new();
    private readonly ConcurrentDictionary<int, int> _npcHpByInstance = new();
    private readonly ConcurrentDictionary<int, bool> _npcBattleStateByInstance = new();
    private readonly ConcurrentDictionary<int, NpcKind> _npcKindByInstance = new();
    private readonly ConcurrentDictionary<int, uint> _npc2136ValueByInstance = new();
    private readonly ConcurrentDictionary<int, uint> _npc2136SequenceByInstance = new();
    private readonly ConcurrentDictionary<int, uint> _npc0140ValueByInstance = new();
    private readonly ConcurrentDictionary<int, uint> _npc0240ValueByInstance = new();
    private readonly ConcurrentDictionary<int, (byte State0, byte State1)> _npc4636StateByInstance = new();
    private readonly ConcurrentDictionary<int, (int SequenceId, int ResultCode)> _npc2C38StateByInstance = new();
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, int>> _npc2C38StateByInstanceAndSequence = new();
    private readonly ConcurrentDictionary<int, DrainFollowupCandidate> _recentDrainDamageByVictim = new();
    private readonly Lock _selfPeriodicHealingPoolLock = new();
    private readonly Dictionary<SelfPeriodicHealingPoolKey, SelfPeriodicHealingPoolState> _selfPeriodicHealingPools = [];
    private readonly Lock _multiHitLock = new();
    private readonly List<MultiHitDamageCandidate> _recentDamageCandidates = [];
    private readonly List<PendingWrappedMultiHitOwner> _pendingWrappedMultiHitOwners = [];
    private readonly Lock _compactOutcomeLock = new();
    private readonly Dictionary<int, PendingDodgeSignal> _pendingDodgeSignalsByActor = [];
    private int _lastObservedNpcSource;
    private long _lastObservedNpcSourceTimestamp;
    private int _localActorId;
    private long _observationOrdinal;

    private readonly record struct DrainFollowupCandidate(
        int SourceActorId,
        int VictimActorId,
        int SkillCode,
        int OriginalSkillCode,
        long Timestamp);

    public int CurrentTarget { get; set; }

    public void AppendCombatPacket(ParsedCombatPacket packet)
    {
        if (packet.FrameOrdinal <= 0)
        {
            packet.FrameOrdinal = NextObservationOrdinal();
        }

        PreparePacketForStorage(packet);
        StorePacket(packet);
    }

    public void FlushPendingOutcomeSidecars(long timestamp)
    {
        lock (_compactOutcomeLock)
        {
            TrimPendingDodgeSignals_NoLock();
        }
    }

    public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int type, long timestamp) =>
        RegisterCompactValue0438(targetId, sourceId, skillCodeRaw, marker, 0, type, timestamp, NextObservationOrdinal());

    public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, long timestamp) =>
        RegisterCompactValue0438(targetId, sourceId, skillCodeRaw, marker, layoutTag, type, timestamp, NextObservationOrdinal());

    public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, long timestamp, long frameOrdinal)
    {
        RememberCompactObservationSource(sourceId, timestamp);

        if (!IsCompactEvadeSignal(targetId, sourceId, layoutTag, type) || marker <= 0)
        {
            return;
        }

        var trackedSkillCode = ResolveTrackedSkillCode(skillCodeRaw);
        if (trackedSkillCode <= 0 ||
            sourceId <= 0 ||
            targetId <= 0 ||
            sourceId == targetId)
        {
            return;
        }

        StoreCompactEvade(sourceId, targetId, skillCodeRaw, marker, timestamp, frameOrdinal);
    }

    public void RegisterWrapped8456Sidecar(ushort innerOpcode, uint innerValue, ulong stamp, long timestamp, long frameOrdinal)
    {
        if (innerOpcode != WrappedMultiHitInnerOpcode || innerValue == 0)
        {
            return;
        }

        lock (_multiHitLock)
        {
            TrimExpiredMultiHitCandidates_NoLock();
            if (!TryFindWrapped8456MultiHitOwner_NoLock(stamp, out var ownerPacketId))
            {
                return;
            }

            if (!TryFindDamageCandidateByPacketId_NoLock(ownerPacketId, out var candidate))
            {
                return;
            }

            if (candidate.Packet.HasAuthoritativeMultiHitCount)
            {
                return;
            }

            candidate.Packet.MultiHitCount = Math.Max(candidate.Packet.MultiHitCount, (int)innerValue);
            if (candidate.Packet.MultiHitCount > 0)
            {
                candidate.Packet.Modifiers |= DamageModifiers.MultiHit;
            }
        }
    }

    public void RegisterWrapped8456Sidecar(ushort innerOpcode, uint innerValue, long timestamp) =>
        RegisterWrapped8456Sidecar(innerOpcode, innerValue, 0, timestamp, NextObservationOrdinal());

    public void RegisterCompactControl0638(int sourceId, int skillCodeRaw, int marker, long timestamp) =>
        RegisterCompactControl0638(sourceId, skillCodeRaw, marker, 0, timestamp, NextObservationOrdinal());

    public void RegisterCompactControl0638(int sourceId, int skillCodeRaw, int marker, int flag, long timestamp) =>
        RegisterCompactControl0638(sourceId, skillCodeRaw, marker, flag, timestamp, NextObservationOrdinal());

    public void RegisterCompactControl0638(int sourceId, int skillCodeRaw, int marker, int flag, long timestamp, long frameOrdinal)
    {
        var observationOrdinal = NextObservationOrdinal();
        RememberCompactObservationSource(sourceId, timestamp);

        if (sourceId <= 0 || marker <= 0)
        {
            return;
        }

        var trackedSkillCode = ResolveTrackedSkillCode(skillCodeRaw);
        if (trackedSkillCode <= 0)
        {
            return;
        }

        if (!IsDodgeSkill(trackedSkillCode))
        {
            return;
        }

        lock (_compactOutcomeLock)
        {
            _pendingDodgeSignalsByActor[sourceId] = new PendingDodgeSignal(
                sourceId,
                skillCodeRaw,
                trackedSkillCode,
                marker,
                frameOrdinal,
                observationOrdinal);
            TrimPendingDodgeSignals_NoLock();
        }
    }

    private void PreparePacketForStorage(ParsedCombatPacket packet)
    {
        ApplyDrainFollowupAttribution(packet);
        CombatMetricsEngine.NormalizePacketForStorage(packet);
        NormalizeSelfPeriodicHealingPool(packet);
        ApplyMultiHitAttribution(packet);
    }

    private void StorePacket(ParsedCombatPacket packet)
    {
        var bySource = _packetsBySource.GetOrAdd(packet.SourceId, _ => new ConcurrentQueue<ParsedCombatPacket>());
        bySource.Enqueue(packet);

        var byTarget = _packetsByTarget.GetOrAdd(packet.TargetId, _ => new ConcurrentQueue<ParsedCombatPacket>());
        byTarget.Enqueue(packet);
    }

    private void NormalizeSelfPeriodicHealingPool(ParsedCombatPacket packet)
    {
        if (!TryGetSelfPeriodicHealingPoolKey(packet, out var key, out var isSeed))
        {
            return;
        }

        var rawDamage = packet.Damage;
        if (rawDamage <= 0)
        {
            return;
        }

        lock (_selfPeriodicHealingPoolLock)
        {
            if (isSeed)
            {
                packet.Damage = 0;
                _selfPeriodicHealingPools[key] = new SelfPeriodicHealingPoolState(rawDamage, packet.Timestamp);
                return;
            }

            if (!_selfPeriodicHealingPools.TryGetValue(key, out var state))
            {
                return;
            }

            var delta = packet.Timestamp - state.LastTimestamp;
            if (delta < 0 ||
                delta > SelfPeriodicHealingPoolWindowMilliseconds ||
                rawDamage >= state.LastRawDamage)
            {
                _selfPeriodicHealingPools.Remove(key);
                return;
            }

            packet.Damage = state.LastRawDamage - rawDamage;
            if (packet.Damage <= 0)
            {
                _selfPeriodicHealingPools.Remove(key);
                return;
            }

            _selfPeriodicHealingPools[key] = new SelfPeriodicHealingPoolState(rawDamage, packet.Timestamp);
        }
    }

    private void RememberCompactObservationSource(int sourceId, long timestamp)
    {
        if (sourceId <= 0)
        {
            return;
        }

        RememberNpcObservationSource(sourceId, timestamp);
    }

    public void RegisterMultiHitSidecar(int sourceId, int skillCodeRaw, int marker, long timestamp, long frameOrdinal)
    {
        if (sourceId <= 0 || marker <= 0)
        {
            return;
        }

        if (ResolveTrackedSkillCode(skillCodeRaw) != MultiHitSidecarSkillCode)
        {
            return;
        }

        lock (_multiHitLock)
        {
            TrimExpiredMultiHitCandidates_NoLock();

            for (var i = _recentDamageCandidates.Count - 1; i >= 0; i--)
            {
                var candidate = _recentDamageCandidates[i];
                if (candidate.SourceId != sourceId)
                {
                    continue;
                }

                var expectedMarker = candidate.Marker + candidate.Packet.MultiHitCount + 1;
                if (marker != expectedMarker)
                {
                    continue;
                }

                if (candidate.Packet.HasAuthoritativeMultiHitCount)
                {
                    return;
                }

                candidate.Packet.MultiHitCount++;
                candidate.Packet.Modifiers |= DamageModifiers.MultiHit;
                RegisterPendingWrappedMultiHitOwner_NoLock(candidate.Packet.Id, 0, NextObservationOrdinal());
                return;
            }
        }
    }

    public void RegisterMultiHitSidecar(int sourceId, int skillCodeRaw, int marker, long timestamp) =>
        RegisterMultiHitSidecar(sourceId, skillCodeRaw, marker, timestamp, NextObservationOrdinal());

    public void Register3538Sidecar(int targetId, int actorId, long timestamp, long frameOrdinal)
    {
        if (targetId <= 0 || actorId <= 0 || actorId == targetId)
        {
            return;
        }

        lock (_multiHitLock)
        {
            TrimExpiredMultiHitCandidates_NoLock();
            if (!TryFindLatestMultiHitCandidate_NoLock(actorId, targetId, out var candidate))
            {
                return;
            }

            if (candidate.Packet.HasAuthoritativeMultiHitCount || candidate.Packet.MultiHitCount > 0)
            {
                return;
            }

            candidate.Packet.MultiHitCount = 1;
            candidate.Packet.Modifiers |= DamageModifiers.MultiHit;
            RegisterPendingWrappedMultiHitOwner_NoLock(candidate.Packet.Id, 0, NextObservationOrdinal());
        }
    }

    public void Register3538Sidecar(int targetId, int actorId, long timestamp) =>
        Register3538Sidecar(targetId, actorId, timestamp, NextObservationOrdinal());

    private static bool TryGetSelfPeriodicHealingPoolKey(
        ParsedCombatPacket packet,
        out SelfPeriodicHealingPoolKey key,
        out bool isSeed)
    {
        key = default;
        isSeed = false;

        if (packet.SourceId <= 0 ||
            packet.TargetId <= 0 ||
            packet.SourceId != packet.TargetId)
        {
            return false;
        }

        if (!string.Equals(packet.EffectFamily, "periodic-self-mode-9", StringComparison.Ordinal) &&
            !string.Equals(packet.EffectFamily, "periodic-self-mode-11", StringComparison.Ordinal))
        {
            return false;
        }

        var originalSkillCode = packet.OriginalSkillCode != 0
            ? packet.OriginalSkillCode
            : packet.SkillCode;
        if (originalSkillCode <= 0)
        {
            return false;
        }

        var resolvedSkillKind = packet.SkillKind != SkillKind.Unknown
            ? packet.SkillKind
            : CombatEventClassifier.ResolveSkillKind(CombatMetricsEngine.InferOriginalSkillCode(originalSkillCode) ?? packet.SkillCode);
        if (resolvedSkillKind != SkillKind.PeriodicHealing)
        {
            return false;
        }

        key = new SelfPeriodicHealingPoolKey(packet.SourceId, packet.TargetId, originalSkillCode);
        isSeed = string.Equals(packet.EffectFamily, "periodic-self-mode-9", StringComparison.Ordinal);
        return true;
    }

    public void AppendNpcCode(int instanceId, int npcCode) => _npcCodeByInstance[instanceId] = npcCode;
    public void AppendNpcName(int npcCode, string name) => _npcNameByCode[npcCode] = name;
    public void AppendNpcKind(int instanceId, NpcKind kind) => _npcKindByInstance[instanceId] = kind;
    public void AppendNpcHp(int instanceId, int hp) => _npcHpByInstance[instanceId] = hp;
    public void AppendNpc2136State(int instanceId, uint sequence, uint value0)
    {
        _npc2136SequenceByInstance[instanceId] = sequence;
        _npc2136ValueByInstance[instanceId] = value0;
    }
    public void AppendNpc0140Value(int instanceId, uint value0) => _npc0140ValueByInstance[instanceId] = value0;
    public void AppendNpc0240Value(int instanceId, uint value0) => _npc0240ValueByInstance[instanceId] = value0;
    public void AppendNpc4636State(int instanceId, byte state0, byte state1) => _npc4636StateByInstance[instanceId] = (state0, state1);
    public void AppendNpc2C38State(int instanceId, int sequenceId, int resultCode)
    {
        _npc2C38StateByInstance[instanceId] = (sequenceId, resultCode);
        var bySequence = _npc2C38StateByInstanceAndSequence.GetOrAdd(instanceId, static _ => new ConcurrentDictionary<int, int>());
        bySequence[sequenceId] = resultCode;
    }

    public void RegisterObservation2C38(int instanceId, int mode, int sequenceId, int resultCode, long timestamp, long frameOrdinal)
    {
        AppendNpc2C38State(instanceId, sequenceId, resultCode);
        RememberNpcObservationSource(instanceId, timestamp);

        if (instanceId <= 0 || resultCode != 7)
        {
            return;
        }

        PendingDodgeSignal pendingDodge;
        lock (_compactOutcomeLock)
        {
            if (!_pendingDodgeSignalsByActor.TryGetValue(instanceId, out pendingDodge))
            {
                return;
            }

            _pendingDodgeSignalsByActor.Remove(instanceId);
        }

        if (mode != 2 || !IsInvincibleDodgeSignal(pendingDodge.OriginalSkillCode, pendingDodge.TrackedSkillCode))
        {
            return;
        }

        StoreInvincible(instanceId, pendingDodge.Marker, timestamp, frameOrdinal);
    }

    public void RegisterObservation2C38(int instanceId, int sequenceId, int resultCode, long timestamp) =>
        RegisterObservation2C38(instanceId, 1, sequenceId, resultCode, timestamp, NextObservationOrdinal());

    public void RegisterObservation2A38(
        int sourceId,
        int mode,
        int groupCode,
        int sequenceId,
        uint buffCodeRaw,
        ushort headValue,
        int stackValue,
        long timestamp,
        long frameOrdinal)
    {
        RememberCompactObservationSource(sourceId, timestamp);
    }

    public void RegisterObservation2A38(
        int sourceId,
        int mode,
        int groupCode,
        int sequenceId,
        uint buffCodeRaw,
        ushort headValue,
        int stackValue,
        long timestamp) =>
        RegisterObservation2A38(sourceId, mode, groupCode, sequenceId, buffCodeRaw, headValue, stackValue, timestamp, NextObservationOrdinal());

    public bool TryGetNpc2C38State(int instanceId, int? preferredSequenceId, out int sequenceId, out int resultCode)
    {
        sequenceId = 0;
        resultCode = 0;

        if (preferredSequenceId.HasValue)
        {
            if (_npc2C38StateByInstanceAndSequence.TryGetValue(instanceId, out var bySequence) &&
                bySequence.TryGetValue(preferredSequenceId.Value, out resultCode))
            {
                sequenceId = preferredSequenceId.Value;
                return true;
            }

            return false;
        }

        if (_npc2C38StateByInstance.TryGetValue(instanceId, out var state))
        {
            sequenceId = state.SequenceId;
            resultCode = state.ResultCode;
            return true;
        }

        return false;
    }

    public void RememberNpcObservationSource(int instanceId)
    {
        RememberNpcObservationSource(instanceId, 0);
    }

    private void RememberNpcObservationSource(int instanceId, long timestamp)
    {
        if (instanceId > 0)
        {
            _lastObservedNpcSource = instanceId;
            _lastObservedNpcSourceTimestamp = timestamp;
        }
    }

    public void RememberLocalActor(int actorId)
    {
        if (actorId > 0)
        {
            _localActorId = actorId;
        }
    }

    public int ResolveNpcObservationSource()
    {
        return CurrentTarget > 0 ? CurrentTarget : _lastObservedNpcSource;
    }

    public int LocalActorId => _localActorId;

    public void AppendSummon(int ownerId, int summonInstanceId)
    {
        _summonOwnerByInstance[summonInstanceId] = ownerId;
        _npcKindByInstance[summonInstanceId] = NpcKind.Summon;
    }
    public void ToggleNpcBattle(int instanceId)
    {
        _npcBattleStateByInstance.AddOrUpdate(instanceId, true, static (_, current) => !current);
    }

    public void AppendNickname(int uid, string nickname)
    {
        if (_nicknameStorage.TryGetValue(uid, out var existing) && existing == nickname)
            return;

        if (_nicknameStorage.TryGetValue(uid, out var existingName))
        {
            var nicknameByteCount = Encoding.UTF8.GetByteCount(nickname);
            if (nicknameByteCount == 2 && nicknameByteCount < Encoding.UTF8.GetByteCount(existingName))
            {
                return;
            }
        }

        _nicknameStorage[uid] = nickname;
    }

    public void ResetCombatStorage()
    {
        _packetsBySource.Clear();
        _packetsByTarget.Clear();
        _summonOwnerByInstance.Clear();
        _npcHpByInstance.Clear();
        _npcBattleStateByInstance.Clear();
        _npc2136ValueByInstance.Clear();
        _npc2136SequenceByInstance.Clear();
        _npc0140ValueByInstance.Clear();
        _npc0240ValueByInstance.Clear();
        _npc4636StateByInstance.Clear();
        _npc2C38StateByInstance.Clear();
        _npc2C38StateByInstanceAndSequence.Clear();
        _recentDrainDamageByVictim.Clear();
        lock (_selfPeriodicHealingPoolLock)
        {
            _selfPeriodicHealingPools.Clear();
        }
        lock (_multiHitLock)
        {
            _recentDamageCandidates.Clear();
            _pendingWrappedMultiHitOwners.Clear();
        }
        lock (_compactOutcomeLock)
        {
            _pendingDodgeSignalsByActor.Clear();
        }
        _lastObservedNpcSource = 0;
        _lastObservedNpcSourceTimestamp = 0;
        CurrentTarget = 0;
        _observationOrdinal = 0;
    }

    public ConcurrentDictionary<int, ConcurrentQueue<ParsedCombatPacket>> CombatPacketsByTarget => _packetsByTarget;
    public ConcurrentDictionary<int, ConcurrentQueue<ParsedCombatPacket>> CombatPacketsBySource => _packetsBySource;
    public ConcurrentDictionary<int, string> Nicknames => _nicknameStorage;
    public ConcurrentDictionary<int, int> SummonOwnerByInstance => _summonOwnerByInstance;
    public ConcurrentDictionary<int, string> NpcNameByCode => _npcNameByCode;
    public ConcurrentDictionary<int, int> NpcCodeByInstance => _npcCodeByInstance;
    public ConcurrentDictionary<int, int> NpcHpByInstance => _npcHpByInstance;
    public ConcurrentDictionary<int, bool> NpcBattleStateByInstance => _npcBattleStateByInstance;
    public ConcurrentDictionary<int, NpcKind> NpcKindByInstance => _npcKindByInstance;
    public ConcurrentDictionary<int, uint> Npc2136ValueByInstance => _npc2136ValueByInstance;
    public ConcurrentDictionary<int, uint> Npc2136SequenceByInstance => _npc2136SequenceByInstance;
    public ConcurrentDictionary<int, uint> Npc0140ValueByInstance => _npc0140ValueByInstance;
    public ConcurrentDictionary<int, uint> Npc0240ValueByInstance => _npc0240ValueByInstance;
    public ConcurrentDictionary<int, (byte State0, byte State1)> Npc4636StateByInstance => _npc4636StateByInstance;
    public ConcurrentDictionary<int, (int SequenceId, int ResultCode)> Npc2C38StateByInstance => _npc2C38StateByInstance;

    public CombatMetricsStore DeepClone()
    {
        var clone = new CombatMetricsStore
        {
            CurrentTarget = CurrentTarget,
            _lastObservedNpcSource = _lastObservedNpcSource,
            _lastObservedNpcSourceTimestamp = _lastObservedNpcSourceTimestamp,
            _localActorId = _localActorId
        };

        CloneQueues(_packetsByTarget, clone._packetsByTarget);
        CloneQueues(_packetsBySource, clone._packetsBySource);
        CloneValues(_nicknameStorage, clone._nicknameStorage);
        CloneValues(_summonOwnerByInstance, clone._summonOwnerByInstance);
        CloneValues(_npcNameByCode, clone._npcNameByCode);
        CloneValues(_npcCodeByInstance, clone._npcCodeByInstance);
        CloneValues(_npcHpByInstance, clone._npcHpByInstance);
        CloneValues(_npcBattleStateByInstance, clone._npcBattleStateByInstance);
        CloneValues(_npcKindByInstance, clone._npcKindByInstance);
        CloneValues(_npc2136ValueByInstance, clone._npc2136ValueByInstance);
        CloneValues(_npc2136SequenceByInstance, clone._npc2136SequenceByInstance);
        CloneValues(_npc0140ValueByInstance, clone._npc0140ValueByInstance);
        CloneValues(_npc0240ValueByInstance, clone._npc0240ValueByInstance);
        CloneValues(_npc4636StateByInstance, clone._npc4636StateByInstance);
        CloneValues(_npc2C38StateByInstance, clone._npc2C38StateByInstance);
        lock (_compactOutcomeLock)
        {
            foreach (var (actorId, pendingSignal) in _pendingDodgeSignalsByActor)
            {
                clone._pendingDodgeSignalsByActor[actorId] = pendingSignal;
            }
        }
        lock (_multiHitLock)
        {
            clone._pendingWrappedMultiHitOwners.AddRange(_pendingWrappedMultiHitOwners);
        }

        foreach (var (instanceId, statesBySequence) in _npc2C38StateByInstanceAndSequence)
        {
            var clonedBySequence = clone._npc2C38StateByInstanceAndSequence.GetOrAdd(instanceId, static _ => new ConcurrentDictionary<int, int>());
            CloneValues(statesBySequence, clonedBySequence);
        }

        return clone;
    }

    public CombatMetricsStore CreateArchiveSlice(DamageMeterSnapshot snapshot)
    {
        var clone = new CombatMetricsStore();
        var relevantActorIds = new HashSet<int>(snapshot.Combatants.Keys);
        var relevantNpcInstanceIds = new HashSet<int>();
        var battleStart = snapshot.BattleStartTime;
        var battleEnd = snapshot.BattleEndTime;
        var hasBattleWindow = battleStart > 0 && battleEnd >= battleStart;

        if (snapshot.TargetObservation?.InstanceId is int targetInstanceId && targetInstanceId > 0)
        {
            relevantActorIds.Add(targetInstanceId);
            relevantNpcInstanceIds.Add(targetInstanceId);
        }

        foreach (var queue in _packetsBySource.Values)
        {
            foreach (var packet in queue)
            {
                var sourceActorId = CombatMetricsEngine.ResolveCombatantActorId(this, packet.SourceId);
                var targetActorId = CombatMetricsEngine.ResolveCombatantActorId(this, packet.TargetId);
                if (hasBattleWindow &&
                    (packet.Timestamp < battleStart || packet.Timestamp > battleEnd) &&
                    !IsRelevantRecoveryPacket(packet, sourceActorId, targetActorId, relevantActorIds))
                {
                    continue;
                }

                var packetClone = packet.DeepClone();
                clone.AppendCombatPacket(packetClone);
                relevantActorIds.Add(packetClone.SourceId);
                relevantActorIds.Add(packetClone.TargetId);
            }
        }

        ExpandRelevantActorIdsWithSummonOwners(relevantActorIds);

        foreach (var actorId in relevantActorIds)
        {
            if (_nicknameStorage.TryGetValue(actorId, out var nickname))
            {
                clone._nicknameStorage[actorId] = nickname;
            }

            if (_summonOwnerByInstance.TryGetValue(actorId, out var ownerId))
            {
                clone._summonOwnerByInstance[actorId] = ownerId;
            }

            if (_npcCodeByInstance.TryGetValue(actorId, out var npcCode))
            {
                clone._npcCodeByInstance[actorId] = npcCode;
                relevantNpcInstanceIds.Add(actorId);
                if (_npcNameByCode.TryGetValue(npcCode, out var npcName))
                {
                    clone._npcNameByCode[npcCode] = npcName;
                }
            }

            if (_npcKindByInstance.TryGetValue(actorId, out var npcKind))
            {
                clone._npcKindByInstance[actorId] = npcKind;
            }
        }

        foreach (var instanceId in relevantNpcInstanceIds)
        {
            if (_npcHpByInstance.TryGetValue(instanceId, out var hp))
            {
                clone._npcHpByInstance[instanceId] = hp;
            }

            if (_npcBattleStateByInstance.TryGetValue(instanceId, out var battleState))
            {
                clone._npcBattleStateByInstance[instanceId] = battleState;
            }

            if (_npc2136ValueByInstance.TryGetValue(instanceId, out var value2136))
            {
                clone._npc2136ValueByInstance[instanceId] = value2136;
            }

            if (_npc2136SequenceByInstance.TryGetValue(instanceId, out var sequence2136))
            {
                clone._npc2136SequenceByInstance[instanceId] = sequence2136;
            }

            if (_npc0140ValueByInstance.TryGetValue(instanceId, out var value0140))
            {
                clone._npc0140ValueByInstance[instanceId] = value0140;
            }

            if (_npc0240ValueByInstance.TryGetValue(instanceId, out var value0240))
            {
                clone._npc0240ValueByInstance[instanceId] = value0240;
            }

            if (_npc4636StateByInstance.TryGetValue(instanceId, out var state4636))
            {
                clone._npc4636StateByInstance[instanceId] = state4636;
            }

            if (_npc2C38StateByInstance.TryGetValue(instanceId, out var state2C38))
            {
                clone._npc2C38StateByInstance[instanceId] = state2C38;
            }

            if (_npc2C38StateByInstanceAndSequence.TryGetValue(instanceId, out var bySequence))
            {
                var clonedBySequence = clone._npc2C38StateByInstanceAndSequence.GetOrAdd(instanceId, static _ => new ConcurrentDictionary<int, int>());
                CloneValues(bySequence, clonedBySequence);
            }
        }

        clone.CurrentTarget = CurrentTarget > 0 && relevantActorIds.Contains(CurrentTarget)
            ? CurrentTarget
            : snapshot.TargetObservation?.InstanceId ?? 0;
        clone._lastObservedNpcSource = relevantNpcInstanceIds.Contains(_lastObservedNpcSource)
            ? _lastObservedNpcSource
            : 0;
        clone._lastObservedNpcSourceTimestamp = clone._lastObservedNpcSource != 0
            ? _lastObservedNpcSourceTimestamp
            : 0;

        return clone;
    }

    private static bool IsRelevantRecoveryPacket(
        ParsedCombatPacket packet,
        int sourceActorId,
        int targetActorId,
        HashSet<int> relevantActorIds)
    {
        return CombatMetricsEngine.IsRelevantRecoveryPacket(packet, sourceActorId, targetActorId, relevantActorIds);
    }

    private static void CloneQueues(
        ConcurrentDictionary<int, ConcurrentQueue<ParsedCombatPacket>> source,
        ConcurrentDictionary<int, ConcurrentQueue<ParsedCombatPacket>> destination)
    {
        foreach (var (key, queue) in source)
        {
            var clonedQueue = destination.GetOrAdd(key, static _ => new ConcurrentQueue<ParsedCombatPacket>());
            foreach (var packet in queue)
            {
                clonedQueue.Enqueue(packet.DeepClone());
            }
        }
    }

    private void ExpandRelevantActorIdsWithSummonOwners(HashSet<int> relevantActorIds)
    {
        var pendingActorIds = new Queue<int>(relevantActorIds);
        while (pendingActorIds.Count > 0)
        {
            var actorId = pendingActorIds.Dequeue();
            if (!_summonOwnerByInstance.TryGetValue(actorId, out var ownerId) ||
                !relevantActorIds.Add(ownerId))
            {
                continue;
            }

            pendingActorIds.Enqueue(ownerId);
        }
    }

    private static void CloneValues<TKey, TValue>(
        ConcurrentDictionary<TKey, TValue> source,
        ConcurrentDictionary<TKey, TValue> destination)
        where TKey : notnull
    {
        foreach (var (key, value) in source)
        {
            destination[key] = value;
        }
    }

    private void ApplyDrainFollowupAttribution(ParsedCombatPacket packet)
    {
        if (TryResolveDrainHealingFollowup(packet, out var followup))
        {
            packet.SourceId = followup.SourceActorId;
            packet.TargetId = followup.SourceActorId;
            packet.OriginalSkillCode = followup.OriginalSkillCode;
            packet.SkillCode = followup.SkillCode;
        }

        if (TryBuildDrainFollowupCandidate(packet, out var candidate))
        {
            _recentDrainDamageByVictim[candidate.VictimActorId] = candidate;
        }
    }

    private bool TryResolveDrainHealingFollowup(ParsedCombatPacket packet, out DrainFollowupCandidate candidate)
    {
        candidate = default;

        if (packet.Damage <= 0 ||
            packet.SourceId <= 0 ||
            packet.TargetId <= 0 ||
            packet.SourceId != packet.TargetId)
        {
            return false;
        }

        if (ResolveTrackedSkillCode(packet) != RestoreHpSkillCode)
        {
            return false;
        }

        var victimActorId = packet.SourceId;
        if (!_recentDrainDamageByVictim.TryGetValue(victimActorId, out candidate))
        {
            return false;
        }

        var delta = packet.Timestamp - candidate.Timestamp;
        if (delta < 0 || delta > DrainFollowupWindowMilliseconds)
        {
            _recentDrainDamageByVictim.TryRemove(victimActorId, out _);
            return false;
        }

        if (candidate.SourceActorId <= 0 || candidate.SourceActorId == victimActorId)
        {
            return false;
        }

        _recentDrainDamageByVictim.TryRemove(victimActorId, out _);
        return true;
    }

    private static bool TryBuildDrainFollowupCandidate(ParsedCombatPacket packet, out DrainFollowupCandidate candidate)
    {
        candidate = default;

        if (packet.Damage <= 0 ||
            packet.SourceId <= 0 ||
            packet.TargetId <= 0 ||
            packet.SourceId == packet.TargetId)
        {
            return false;
        }

        var trackedSkillCode = ResolveTrackedSkillCode(packet);
        if (trackedSkillCode <= 0)
        {
            return false;
        }

        var semantics = CombatEventClassifier.ResolveSkillSemantics(trackedSkillCode);
        var kind = CombatEventClassifier.ResolveSkillKind(trackedSkillCode);
        if ((semantics & SkillSemantics.DrainOrAbsorb) == 0 && kind != SkillKind.DrainOrAbsorb)
        {
            return false;
        }

        candidate = new DrainFollowupCandidate(
            packet.SourceId,
            packet.TargetId,
            trackedSkillCode,
            packet.OriginalSkillCode != 0 ? packet.OriginalSkillCode : trackedSkillCode,
            packet.Timestamp);
        return true;
    }

    private static int ResolveTrackedSkillCode(ParsedCombatPacket packet)
    {
        var originalSkillCode = packet.OriginalSkillCode != 0 ? packet.OriginalSkillCode : packet.SkillCode;
        return ResolveTrackedSkillCode(originalSkillCode);
    }

    private static int ResolveTrackedSkillCode(int skillCode)
    {
        var originalSkillCode = skillCode;
        if (originalSkillCode <= 0)
        {
            return 0;
        }

        var variant = CombatMetricsEngine.ParseSkillVariant(originalSkillCode);
        return CombatMetricsEngine.InferOriginalSkillCode(originalSkillCode) ?? variant.NormalizedSkillCode;
    }

    private long NextObservationOrdinal() => Interlocked.Increment(ref _observationOrdinal);

    private static bool IsInvincibleDodgeSignal(int originalSkillCode, int trackedSkillCode)
    {
        if (!IsDodgeSkill(trackedSkillCode) || originalSkillCode <= 0)
        {
            return false;
        }

        return CombatMetricsEngine.ParseSkillVariant(originalSkillCode).ChargeStage > 0;
    }

    private static bool IsCompactEvadeSignal(int targetId, int sourceId, int layoutTag, int type)
    {
        if (targetId <= 0 || sourceId <= 0 || targetId == sourceId)
        {
            return false;
        }

        return type == 1 && (layoutTag == 0 || layoutTag == 2);
    }

    private static bool IsDodgeSkill(int trackedSkillCode) => DodgeSkillCodes.Contains(trackedSkillCode);

    private void ApplyMultiHitAttribution(ParsedCombatPacket packet)
    {
        if (!IsDirectDamageCandidate(packet) || packet.HitContribution == 0)
        {
            return;
        }

        var trackedSkillCode = ResolveTrackedSkillCode(packet);
        if (trackedSkillCode <= 0 || packet.Marker <= 0)
        {
            return;
        }

        lock (_multiHitLock)
        {
            TrimExpiredMultiHitCandidates_NoLock();
            var observationOrdinal = NextObservationOrdinal();

            if (TryResolveEmbeddedMultiHitFollowup(packet, trackedSkillCode, out var candidate))
            {
                packet.HitContribution = 0;
                packet.AttemptContribution = 0;
                candidate.Packet.MultiHitCount++;
                candidate.Packet.Modifiers |= DamageModifiers.MultiHit;
                RegisterPendingWrappedMultiHitOwner_NoLock(candidate.Packet.Id, 0, observationOrdinal);
                return;
            }

            _recentDamageCandidates.Add(new MultiHitDamageCandidate(
                packet,
                packet.SourceId,
                packet.TargetId,
                trackedSkillCode,
                packet.Marker,
                packet.Unknown,
                packet.Flag,
                packet.FrameOrdinal,
                observationOrdinal));
            TrimExpiredMultiHitCandidates_NoLock();

            if (packet.HasAuthoritativeMultiHitCount || packet.MultiHitCount > 0)
            {
                RegisterPendingWrappedMultiHitOwner_NoLock(packet.Id, 0, observationOrdinal);
            }
        }
    }

    private bool TryResolveEmbeddedMultiHitFollowup(
        ParsedCombatPacket packet,
        int trackedSkillCode,
        out MultiHitDamageCandidate candidate)
    {
        candidate = default;

        if (packet.Flag != 0)
        {
            return false;
        }

        for (var i = _recentDamageCandidates.Count - 1; i >= 0; i--)
        {
            var current = _recentDamageCandidates[i];
            if (current.SourceId != packet.SourceId ||
                current.TargetId != packet.TargetId ||
                current.SkillCode != trackedSkillCode ||
                current.Marker != packet.Marker ||
                current.Unknown != packet.Unknown ||
                current.Flag == 0 ||
                current.Packet.HasAuthoritativeMultiHitCount)
            {
                continue;
            }

            candidate = current;
            return true;
        }

        return false;
    }

    private bool TryFindLatestMultiHitCandidate_NoLock(int sourceId, int? targetId, out MultiHitDamageCandidate candidate)
    {
        candidate = default;
        var hasLatest = false;
        var latest = default(MultiHitDamageCandidate);
        var hasOwned = false;
        var owned = default(MultiHitDamageCandidate);

        for (var i = _recentDamageCandidates.Count - 1; i >= 0; i--)
        {
            var current = _recentDamageCandidates[i];
            if (current.SourceId != sourceId)
            {
                continue;
            }

            if (targetId is int resolvedTargetId && current.TargetId != resolvedTargetId)
            {
                continue;
            }

            if (!hasLatest)
            {
                latest = current;
                hasLatest = true;
            }

            if (!hasOwned &&
                (current.Packet.HasAuthoritativeMultiHitCount || current.Packet.MultiHitCount > 0))
            {
                owned = current;
                hasOwned = true;
            }
        }

        if (hasOwned)
        {
            candidate = owned;
            return true;
        }

        if (hasLatest)
        {
            candidate = latest;
            return true;
        }

        return false;
    }

    private bool TryFindDamageCandidateByPacketId_NoLock(Guid packetId, out MultiHitDamageCandidate candidate)
    {
        for (var i = _recentDamageCandidates.Count - 1; i >= 0; i--)
        {
            if (_recentDamageCandidates[i].Packet.Id != packetId)
            {
                continue;
            }

            candidate = _recentDamageCandidates[i];
            return true;
        }

        candidate = default;
        return false;
    }

    private void RegisterPendingWrappedMultiHitOwner_NoLock(Guid packetId, ulong stamp, long observationOrdinal)
    {
        for (var i = _pendingWrappedMultiHitOwners.Count - 1; i >= 0; i--)
        {
            if (_pendingWrappedMultiHitOwners[i].PacketId != packetId)
            {
                continue;
            }

            _pendingWrappedMultiHitOwners[i] = new PendingWrappedMultiHitOwner(
                packetId,
                stamp != 0 ? stamp : _pendingWrappedMultiHitOwners[i].Stamp,
                observationOrdinal);
            TrimPendingWrappedMultiHitOwners_NoLock();
            return;
        }

        _pendingWrappedMultiHitOwners.Add(new PendingWrappedMultiHitOwner(packetId, stamp, observationOrdinal));
        TrimPendingWrappedMultiHitOwners_NoLock();
    }

    private bool TryFindWrapped8456MultiHitOwner_NoLock(ulong stamp, out Guid packetId)
    {
        for (var i = _pendingWrappedMultiHitOwners.Count - 1; i >= 0; i--)
        {
            var current = _pendingWrappedMultiHitOwners[i];
            if (current.Stamp != stamp)
            {
                continue;
            }

            packetId = current.PacketId;
            return true;
        }

        PendingWrappedMultiHitOwner candidate = default;
        var hasCandidate = false;
        for (var i = _pendingWrappedMultiHitOwners.Count - 1; i >= 0; i--)
        {
            var current = _pendingWrappedMultiHitOwners[i];
            if (current.Stamp != 0)
            {
                continue;
            }

            if (hasCandidate)
            {
                packetId = Guid.Empty;
                return false;
            }

            candidate = current;
            hasCandidate = true;
        }

        if (!hasCandidate)
        {
            packetId = Guid.Empty;
            return false;
        }

        for (var i = 0; i < _pendingWrappedMultiHitOwners.Count; i++)
        {
            if (_pendingWrappedMultiHitOwners[i].PacketId != candidate.PacketId)
            {
                continue;
            }

            _pendingWrappedMultiHitOwners[i] = new PendingWrappedMultiHitOwner(candidate.PacketId, stamp, candidate.ObservationOrdinal);
            break;
        }

        packetId = candidate.PacketId;
        return true;
    }

    private void TrimExpiredMultiHitCandidates_NoLock()
    {
        while (_recentDamageCandidates.Count > MaxTrackedMultiHitCandidates)
        {
            _recentDamageCandidates.RemoveAt(0);
        }

        for (var i = _pendingWrappedMultiHitOwners.Count - 1; i >= 0; i--)
        {
            if (TryFindDamageCandidateByPacketId_NoLock(_pendingWrappedMultiHitOwners[i].PacketId, out _))
            {
                continue;
            }

            _pendingWrappedMultiHitOwners.RemoveAt(i);
        }
    }

    private void TrimPendingWrappedMultiHitOwners_NoLock()
    {
        while (_pendingWrappedMultiHitOwners.Count > MaxPendingWrappedMultiHitOwners)
        {
            _pendingWrappedMultiHitOwners.RemoveAt(0);
        }
    }

    private void TrimPendingDodgeSignals_NoLock()
    {
        while (_pendingDodgeSignalsByActor.Count > MaxPendingDodgeSignals)
        {
            var oldest = _pendingDodgeSignalsByActor.OrderBy(static pair => pair.Value.ObservationOrdinal).First();
            _pendingDodgeSignalsByActor.Remove(oldest.Key);
        }
    }

    private void StoreCompactEvade(int sourceId, int targetId, int originalSkillCode, int marker, long timestamp, long frameOrdinal)
    {
        var packet = new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            OriginalSkillCode = originalSkillCode,
            SkillCode = originalSkillCode,
            Marker = marker,
            Timestamp = timestamp,
            FrameOrdinal = frameOrdinal,
            Damage = 0,
            HitContribution = 0,
            AttemptContribution = 1,
            Modifiers = DamageModifiers.Evade,
            EffectFamily = "compact-evade",
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };

        PreparePacketForStorage(packet);
        StorePacket(packet);
    }

    private void StoreInvincible(int evaderId, int marker, long timestamp, long frameOrdinal)
    {
        if (evaderId <= 0)
        {
            return;
        }

        var packet = new ParsedCombatPacket
        {
            SourceId = 0,
            TargetId = evaderId,
            OriginalSkillCode = SyntheticCombatSkillCodes.UnresolvedInvincible,
            SkillCode = SyntheticCombatSkillCodes.UnresolvedInvincible,
            Marker = marker,
            Timestamp = timestamp,
            FrameOrdinal = frameOrdinal,
            Damage = 0,
            HitContribution = 0,
            AttemptContribution = 1,
            Modifiers = DamageModifiers.Invincible,
            EffectFamily = "dodge-invincible",
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };

        PreparePacketForStorage(packet);
        StorePacket(packet);
    }

    private static bool IsDirectDamageCandidate(ParsedCombatPacket packet)
    {
        if (packet.Damage <= 0 ||
            packet.SourceId <= 0 ||
            packet.TargetId <= 0 ||
            packet.SourceId == packet.TargetId)
        {
            return false;
        }

        return packet.ValueKind is CombatValueKind.Damage or CombatValueKind.DrainDamage or CombatValueKind.Unknown
               || packet.EventKind == CombatEventKind.Damage;
    }
}
