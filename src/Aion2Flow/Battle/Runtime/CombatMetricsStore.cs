using System.Collections.Concurrent;
using System.Text;
using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Battle.Runtime;

public sealed class CombatMetricsStore
{
    private const long SelfPeriodicHealingPoolWindowMilliseconds = 5000;
    private const int MaxTrackedMultiHitCandidates = 64;
    private const int MaxPendingDodgeSignals = 16;
    private const int MaxPendingAvoidancePackets = 32;
    private const int MaxResolvedPeriodicLinks = 128;

    private readonly record struct SelfPeriodicHealingPoolKey(int SourceId, int TargetId, int OriginalSkillCode);
    private readonly record struct SelfPeriodicHealingPoolState(int LastRawDamage, long LastTimestamp);
    private readonly record struct MultiHitDamageCandidate(
        ParsedCombatPacket Packet,
        int SourceId,
        int TargetId,
        int SkillCode,
        int Marker,
        int Unknown,
        int Flag);
    private readonly record struct PendingDodgeSignal(
        int OriginalSkillCode,
        int TrackedSkillCode,
        int Marker,
        long ObservationOrdinal);
    private readonly record struct PendingCompactAvoidance(
        int SourceId,
        int TargetId,
        int OriginalSkillCode,
        int Marker,
        long Timestamp,
        long FrameOrdinal,
        long BatchOrdinal);
    private readonly record struct AvoidedSignature(int SourceId, int TargetId, int Marker);
    private readonly record struct PeriodicLinkSignature(int TargetId, int LinkId, int SequenceId, int TailRaw, long BatchOrdinal);
    private sealed record NpcInstanceState
    {
        public int? NpcCode { get; init; }
        public int? Hp { get; init; }
        public bool? BattleToggledOn { get; init; }
        public NpcKind? Kind { get; init; }
        public uint? Value2136 { get; init; }
        public uint? Sequence2136 { get; init; }
        public uint? Value0140 { get; init; }
        public uint? Value0240 { get; init; }
        public (byte State0, byte State1)? State4636 { get; init; }
        public (int SequenceId, int ResultCode)? Latest2C38 { get; init; }
        public ConcurrentDictionary<int, int>? Results2C38BySequence { get; init; }
    }

    public readonly record struct NpcInstanceStateSnapshot(
        int? NpcCode,
        int? Hp,
        bool? BattleToggledOn,
        NpcKind? Kind,
        uint? Value2136,
        uint? Sequence2136,
        uint? Value0140,
        uint? Value0240,
        (byte State0, byte State1)? State4636,
        (int SequenceId, int ResultCode)? Latest2C38);

    private readonly ConcurrentDictionary<int, ConcurrentQueue<ParsedCombatPacket>> _packetsByTarget = new();
    private readonly ConcurrentDictionary<int, ConcurrentQueue<ParsedCombatPacket>> _packetsBySource = new();
    private readonly ConcurrentDictionary<EntityPairKey, ConcurrentQueue<ParsedCombatPacket>> _packetsByPair = new();
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<EntityPairKey, byte>> _outgoingPairKeysBySource = new();
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<EntityPairKey, byte>> _incomingPairKeysByTarget = new();
    private readonly ConcurrentDictionary<int, string> _nicknameStorage = new();
    private readonly ConcurrentDictionary<int, int> _summonOwnerByInstance = new();
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, byte>> _summonInstancesByOwner = new();
    private readonly ConcurrentDictionary<int, string> _npcNameByCode = new();
    private readonly ConcurrentDictionary<int, NpcInstanceState> _npcStateByInstance = new();
    private readonly ConcurrentDictionary<int, long> _detailRevisionByCombatant = new();
    private readonly Lock _selfPeriodicHealingPoolLock = new();
    private readonly Dictionary<SelfPeriodicHealingPoolKey, SelfPeriodicHealingPoolState> _selfPeriodicHealingPools = [];
    private readonly Lock _multiHitLock = new();
    private readonly List<MultiHitDamageCandidate> _recentDamageCandidates = [];
    private readonly Lock _compactOutcomeLock = new();
    private readonly Dictionary<int, PendingDodgeSignal> _pendingDodgeSignalsBySource = [];
    private readonly List<PendingCompactAvoidance> _pendingCompactAvoidances = [];
    private readonly List<ParsedCombatPacket> _pendingDirectAvoidancePackets = [];
    private readonly HashSet<PeriodicLinkSignature> _resolvedPeriodicLinks = [];
    private readonly Queue<PeriodicLinkSignature> _resolvedPeriodicLinkOrder = [];
    private readonly HashSet<int> _currentBatchDodgeTargets = [];
    private readonly HashSet<AvoidedSignature> _resolvedAvoidanceSignatures = [];
    private readonly List<PendingCompactAvoidance> _pendingCompactDamageEntries = [];
    private int _lastObservedNpcSource;
    private long _observationOrdinal;
    private long _currentAvoidanceBatchOrdinal;
    private long _detailRevisionSequence;

    public int CurrentTarget { get; set; }

    public void AppendCombatPacket(ParsedCombatPacket packet)
    {
        if (packet.FrameOrdinal <= 0)
        {
            packet.FrameOrdinal = NextObservationOrdinal();
        }

        if (packet.BatchOrdinal <= 0)
        {
            packet.BatchOrdinal = packet.FrameOrdinal;
        }

        var shouldTrackAvoidance = !packet.IsNormalized;

        PreparePacketForStorage(packet);
        StorePacket(packet);

        if (shouldTrackAvoidance)
        {
            TrackDirectAvoidanceCandidate(packet);
        }
    }

    public void FlushPendingOutcomeSidecars()
    {
        lock (_compactOutcomeLock)
        {
            FinalizeAvoidanceBatch_NoLock();
            TrimPendingDodgeSignals_NoLock();
        }
    }

    public int FlushOrphanCompactHits()
    {
        lock (_compactOutcomeLock)
        {
            var storedKeys = new HashSet<(long Batch, int Source, int Target, int Marker)>();
            foreach (var queue in _packetsBySource.Values)
            {
                foreach (var p in queue)
                {
                    if (p.Marker <= 0 || p.SourceId <= 0 || p.TargetId <= 0 || p.SourceId == p.TargetId)
                        continue;
                    if (p.EventKind != CombatEventKind.Damage)
                        continue;
                    storedKeys.Add((p.BatchOrdinal, p.SourceId, p.TargetId, p.Marker));
                    storedKeys.Add((p.BatchOrdinal - 1, p.SourceId, p.TargetId, p.Marker));
                    storedKeys.Add((p.BatchOrdinal + 1, p.SourceId, p.TargetId, p.Marker));
                }
            }

            var totalOrphans = 0;
            foreach (var entry in _pendingCompactDamageEntries)
            {
                if (entry.Marker <= 0)
                    continue;

                var key = (entry.BatchOrdinal, entry.SourceId, entry.TargetId, entry.Marker);
                if (storedKeys.Contains(key))
                    continue;

                var resolvedSkill = CombatMetricsEngine.InferOriginalSkillCode(entry.OriginalSkillCode);
                if (resolvedSkill is null)
                    continue;

                if (!CombatMetricsEngine.SkillMap.TryGetValue(resolvedSkill.Value, out var skill))
                    continue;

                if (skill.SourceType != SkillSourceType.ItemSkill)
                    continue;

                if ((skill.Semantics & SkillSemantics.PeriodicDamage) != 0)
                    continue;

                totalOrphans++;
                var packet = new ParsedCombatPacket
                {
                    SourceId = entry.SourceId,
                    TargetId = entry.TargetId,
                    OriginalSkillCode = entry.OriginalSkillCode,
                    SkillCode = entry.OriginalSkillCode,
                    Marker = entry.Marker,
                    Timestamp = entry.Timestamp,
                    FrameOrdinal = entry.FrameOrdinal,
                    BatchOrdinal = entry.BatchOrdinal,
                    Damage = 0,
                    HitContribution = 1,
                    AttemptContribution = 1,
                    EventKind = CombatEventKind.Damage,
                    ValueKind = CombatValueKind.Damage
                };

                PreparePacketForStorage(packet);
                StorePacket(packet);
            }

            _pendingCompactDamageEntries.Clear();
            return totalOrphans;
        }
    }

    public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int type, long timestamp) =>
        RegisterCompactValue0438(targetId, sourceId, skillCodeRaw, marker, 0, type, timestamp, NextObservationOrdinal(), NextObservationOrdinal());

    public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, long timestamp) =>
        RegisterCompactValue0438(targetId, sourceId, skillCodeRaw, marker, layoutTag, type, timestamp, NextObservationOrdinal(), NextObservationOrdinal());

    public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        RememberNpcObservationSource(sourceId);

        if (type == 2 && sourceId > 0 && targetId > 0 && sourceId != targetId)
        {
            lock (_compactOutcomeLock)
            {
                _pendingCompactDamageEntries.Add(new PendingCompactAvoidance(
                    sourceId, targetId, skillCodeRaw, marker, timestamp, frameOrdinal, batchOrdinal));
            }
        }

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

        lock (_compactOutcomeLock)
        {
            EnsureAvoidanceBatch_NoLock(batchOrdinal);

            var signature = new AvoidedSignature(sourceId, targetId, marker);
            if (_resolvedAvoidanceSignatures.Contains(signature))
            {
                return;
            }

            _pendingCompactAvoidances.Add(new PendingCompactAvoidance(
                sourceId,
                targetId,
                skillCodeRaw,
                marker,
                timestamp,
                frameOrdinal,
                batchOrdinal));
            TrimPendingAvoidances_NoLock();
        }
    }

    public void RegisterCompactControl0238(int sourceId, int skillCodeRaw, int marker) =>
        RegisterCompactControl0238(sourceId, skillCodeRaw, marker, 0);

    public void RegisterCompactControl0238(int sourceId, int skillCodeRaw, int marker, long batchOrdinal) =>
        RegisterCompactAvoidanceSignal(sourceId, skillCodeRaw, marker, batchOrdinal);

    public void RegisterCompactControl0638(int sourceId, int skillCodeRaw, int marker) =>
        RegisterCompactControl0638(sourceId, skillCodeRaw, marker, 0);

    public void RegisterCompactControl0638(int sourceId, int skillCodeRaw, int marker, long batchOrdinal)
    {
        RegisterCompactAvoidanceSignal(sourceId, skillCodeRaw, marker, batchOrdinal);
    }

    public void RegisterPeriodicLink0538(int targetId, int sourceId, int linkId, int sequenceId, int tailRaw, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        RememberNpcObservationSource(targetId);

        if (targetId <= 0 || sourceId <= 0 || targetId != sourceId || linkId <= 0 || sequenceId <= 0)
        {
            return;
        }

        lock (_compactOutcomeLock)
        {
            EnsureAvoidanceBatch_NoLock(batchOrdinal);

            var signature = new PeriodicLinkSignature(targetId, linkId, sequenceId, tailRaw, batchOrdinal);
            if (!_resolvedPeriodicLinks.Add(signature))
            {
                return;
            }

            _resolvedPeriodicLinkOrder.Enqueue(signature);
            TrimResolvedPeriodicLinks_NoLock();

            StoreInvincible(
                linkId,
                targetId,
                tailRaw > 0 ? tailRaw : SyntheticCombatSkillCodes.UnresolvedInvincible,
                sequenceId,
                timestamp,
                frameOrdinal,
                batchOrdinal,
                PacketEffectTag.PeriodicLinkInvincible);
        }
    }

    private void RegisterCompactAvoidanceSignal(int sourceId, int skillCodeRaw, int marker, long batchOrdinal)
    {
        var observationOrdinal = NextObservationOrdinal();
        RememberNpcObservationSource(sourceId);

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
            if (batchOrdinal > 0)
            {
                EnsureAvoidanceBatch_NoLock(batchOrdinal);
            }

            _pendingDodgeSignalsBySource[sourceId] = new PendingDodgeSignal(
                skillCodeRaw,
                trackedSkillCode,
                marker,
                observationOrdinal);
            _currentBatchDodgeTargets.Add(sourceId);
            TrimPendingDodgeSignals_NoLock();
        }
    }

    public void RegisterObservation2A38(int sourceId, int mode, int groupCode, int sequenceId, long timestamp, long frameOrdinal, long batchOrdinal)
        => RegisterObservation2A38(sourceId, mode, groupCode, sequenceId, 0, 0, timestamp, frameOrdinal, batchOrdinal);

    public void RegisterObservation2A38(int sourceId, int mode, int groupCode, int sequenceId, ushort headValue, uint buffCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        RememberNpcObservationSource(sourceId);

        if (sourceId <= 0)
        {
            return;
        }

        lock (_compactOutcomeLock)
        {
            EnsureAvoidanceBatch_NoLock(batchOrdinal);

            if (mode != 1 || groupCode != 17)
            {
                return;
            }
        }
    }

    private void PreparePacketForStorage(ParsedCombatPacket packet)
    {
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

        IndexPacketByPair(packet);
        MarkPacketDetailRevision(packet);
    }

    private void IndexPacketByPair(ParsedCombatPacket packet)
    {
        var pairKey = new EntityPairKey(packet.SourceId, packet.TargetId);
        var byPair = _packetsByPair.GetOrAdd(pairKey, static _ => new ConcurrentQueue<ParsedCombatPacket>());
        byPair.Enqueue(packet);

        if (packet.SourceId > 0)
        {
            var outgoingPairs = _outgoingPairKeysBySource.GetOrAdd(
                packet.SourceId,
                static _ => new ConcurrentDictionary<EntityPairKey, byte>());
            outgoingPairs[pairKey] = 0;
        }

        if (packet.TargetId > 0)
        {
            var incomingPairs = _incomingPairKeysByTarget.GetOrAdd(
                packet.TargetId,
                static _ => new ConcurrentDictionary<EntityPairKey, byte>());
            incomingPairs[pairKey] = 0;
        }
    }

    private void MarkPacketDetailRevision(ParsedCombatPacket packet)
    {
        var revision = NextDetailRevision();
        MarkCombatantDetailRevision(CombatMetricsEngine.ResolveCombatantId(this, packet.SourceId), revision);
        MarkCombatantDetailRevision(packet.TargetId, revision);
    }

    private long NextDetailRevision() => Interlocked.Increment(ref _detailRevisionSequence);

    private void MarkCombatantDetailRevision(int combatantId, long revision)
    {
        if (combatantId <= 0)
        {
            return;
        }

        _detailRevisionByCombatant.AddOrUpdate(
            combatantId,
            revision,
            (_, currentRevision) => Math.Max(currentRevision, revision));
    }

    private void MarkCombatantAndAdjacentDetailsDirty(int combatantId)
    {
        if (combatantId <= 0)
        {
            return;
        }

        var revision = NextDetailRevision();
        MarkCombatantDetailRevision(combatantId, revision);

        if (_outgoingPairKeysBySource.TryGetValue(combatantId, out var outgoingPairs))
        {
            foreach (var pairKey in outgoingPairs.Keys)
            {
                MarkCombatantDetailRevision(pairKey.TargetId, revision);
            }
        }

        if (_incomingPairKeysByTarget.TryGetValue(combatantId, out var incomingPairs))
        {
            foreach (var pairKey in incomingPairs.Keys)
            {
                MarkCombatantDetailRevision(CombatMetricsEngine.ResolveCombatantId(this, pairKey.SourceId), revision);
            }
        }
    }

    private void MarkSummonOwnerAttributionDirty(int ownerId, int summonInstanceId)
    {
        if (ownerId <= 0 || summonInstanceId <= 0)
        {
            return;
        }

        var revision = NextDetailRevision();
        MarkCombatantDetailRevision(ownerId, revision);

        if (_outgoingPairKeysBySource.TryGetValue(summonInstanceId, out var outgoingPairs))
        {
            foreach (var pairKey in outgoingPairs.Keys)
            {
                MarkCombatantDetailRevision(pairKey.TargetId, revision);
            }
        }
    }

    private void RebuildPairIndexes()
    {
        _packetsByPair.Clear();
        _outgoingPairKeysBySource.Clear();
        _incomingPairKeysByTarget.Clear();
        _summonInstancesByOwner.Clear();

        foreach (var (summonInstanceId, ownerId) in _summonOwnerByInstance)
        {
            var summons = _summonInstancesByOwner.GetOrAdd(ownerId, static _ => new ConcurrentDictionary<int, byte>());
            summons[summonInstanceId] = 0;
        }

        foreach (var queue in _packetsBySource.Values)
        {
            foreach (var packet in queue)
            {
                IndexPacketByPair(packet);
            }
        }
    }

    private void NormalizeSelfPeriodicHealingPool(ParsedCombatPacket packet)
    {
        if (!TryGetSelfPeriodicHealingPoolKey(packet, out var key, out var isSeed))
        {
            return;
        }

        var rawDamage = packet.Damage;
        if (rawDamage <= 0 && isSeed)
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

            if (rawDamage == 0)
            {
                _selfPeriodicHealingPools.Remove(key);
            }
            else
            {
                _selfPeriodicHealingPools[key] = new SelfPeriodicHealingPoolState(rawDamage, packet.Timestamp);
            }
        }
    }

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

        if (!packet.IsPeriodicSelfMode(9) && !packet.IsPeriodicSelfMode(11))
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
        if (resolvedSkillKind != SkillKind.PeriodicHealing && resolvedSkillKind != SkillKind.ShieldOrBarrier)
        {
            return false;
        }

        key = new SelfPeriodicHealingPoolKey(packet.SourceId, packet.TargetId, originalSkillCode);
        isSeed = packet.IsPeriodicSelfMode(9);
        return true;
    }

    public void AppendNpcCode(int instanceId, int npcCode)
    {
        UpdateNpcState(instanceId, state => state with { NpcCode = npcCode });
        MarkCombatantAndAdjacentDetailsDirty(instanceId);
    }

    public void AppendNpcName(int npcCode, string name) => _npcNameByCode[npcCode] = name;

    public void AppendNpcKind(int instanceId, NpcKind kind) =>
        UpdateNpcState(instanceId, state => state with { Kind = kind });

    public void AppendNpcHp(int instanceId, int hp) =>
        UpdateNpcState(instanceId, state => state with { Hp = hp });

    public void AppendNpc2136State(int instanceId, uint sequence, uint value0)
    {
        UpdateNpcState(instanceId, state => state with
        {
            Sequence2136 = sequence,
            Value2136 = value0
        });
    }

    public void AppendNpc0140Value(int instanceId, uint value0) =>
        UpdateNpcState(instanceId, state => state with { Value0140 = value0 });

    public void AppendNpc0240Value(int instanceId, uint value0) =>
        UpdateNpcState(instanceId, state => state with { Value0240 = value0 });

    public void AppendNpc4636State(int instanceId, byte state0, byte state1) =>
        UpdateNpcState(instanceId, state => state with { State4636 = (state0, state1) });

    public void AppendNpc2C38State(int instanceId, int sequenceId, int resultCode)
    {
        if (instanceId <= 0)
        {
            return;
        }

        _npcStateByInstance.AddOrUpdate(
            instanceId,
            _ =>
            {
                var bySequence = new ConcurrentDictionary<int, int>();
                bySequence[sequenceId] = resultCode;
                return new NpcInstanceState
                {
                    Latest2C38 = (sequenceId, resultCode),
                    Results2C38BySequence = bySequence
                };
            },
            (_, current) =>
            {
                var bySequence = current.Results2C38BySequence ?? new ConcurrentDictionary<int, int>();
                bySequence[sequenceId] = resultCode;
                return current with
                {
                    Latest2C38 = (sequenceId, resultCode),
                    Results2C38BySequence = bySequence
                };
            });
    }

    public void RegisterObservation2C38(int instanceId, int mode, int sequenceId, int resultCode, long timestamp, long frameOrdinal)
        => RegisterObservation2C38(instanceId, mode, sequenceId, resultCode, timestamp, frameOrdinal, frameOrdinal);

    public void RegisterObservation2C38(int instanceId, int mode, int sequenceId, int resultCode, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        AppendNpc2C38State(instanceId, sequenceId, resultCode);
        RememberNpcObservationSource(instanceId);

        if (instanceId <= 0)
        {
            return;
        }

        lock (_compactOutcomeLock)
        {
            EnsureAvoidanceBatch_NoLock(batchOrdinal);
        }
    }

    public void RegisterObservation2C38(int instanceId, int sequenceId, int resultCode, long timestamp) =>
        RegisterObservation2C38(instanceId, 1, sequenceId, resultCode, timestamp, NextObservationOrdinal(), NextObservationOrdinal());

    public bool TryGetNpc2C38State(int instanceId, int? preferredSequenceId, out int sequenceId, out int resultCode)
    {
        sequenceId = 0;
        resultCode = 0;

        if (!_npcStateByInstance.TryGetValue(instanceId, out var state))
        {
            return false;
        }

        if (preferredSequenceId.HasValue)
        {
            if (state.Results2C38BySequence is not null &&
                state.Results2C38BySequence.TryGetValue(preferredSequenceId.Value, out resultCode))
            {
                sequenceId = preferredSequenceId.Value;
                return true;
            }

            return false;
        }

        if (state.Latest2C38 is { } latest2C38)
        {
            sequenceId = latest2C38.SequenceId;
            resultCode = latest2C38.ResultCode;
            return true;
        }

        return false;
    }

    public bool TryGetNpcRuntimeState(int instanceId, out NpcInstanceStateSnapshot state)
    {
        if (_npcStateByInstance.TryGetValue(instanceId, out var current))
        {
            state = new NpcInstanceStateSnapshot(
                current.NpcCode,
                current.Hp,
                current.BattleToggledOn,
                current.Kind,
                current.Value2136,
                current.Sequence2136,
                current.Value0140,
                current.Value0240,
                current.State4636,
                current.Latest2C38);
            return true;
        }

        state = default;
        return false;
    }

    public void RememberNpcObservationSource(int instanceId)
    {
        if (instanceId > 0)
        {
            _lastObservedNpcSource = instanceId;
        }
    }

    public int ResolveNpcObservationSource()
    {
        return CurrentTarget > 0 ? CurrentTarget : _lastObservedNpcSource;
    }

    public void AppendSummon(int ownerId, int summonInstanceId)
    {
        _summonOwnerByInstance[summonInstanceId] = ownerId;
        var summons = _summonInstancesByOwner.GetOrAdd(ownerId, static _ => new ConcurrentDictionary<int, byte>());
        summons[summonInstanceId] = 0;
        AppendNpcKind(summonInstanceId, NpcKind.Summon);
        MarkSummonOwnerAttributionDirty(ownerId, summonInstanceId);
    }

    public void ToggleNpcBattle(int instanceId)
    {
        UpdateNpcState(
            instanceId,
            state => state with
            {
                BattleToggledOn = !(state.BattleToggledOn ?? false)
            });
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
        MarkCombatantAndAdjacentDetailsDirty(uid);
    }

    public void ResetCombatStorage()
    {
        _packetsBySource.Clear();
        _packetsByTarget.Clear();
        _packetsByPair.Clear();
        _outgoingPairKeysBySource.Clear();
        _incomingPairKeysByTarget.Clear();
        _summonOwnerByInstance.Clear();
        _summonInstancesByOwner.Clear();
        _detailRevisionByCombatant.Clear();
        lock (_selfPeriodicHealingPoolLock)
        {
            _selfPeriodicHealingPools.Clear();
        }
        lock (_multiHitLock)
        {
            _recentDamageCandidates.Clear();
        }
        lock (_compactOutcomeLock)
        {
            _pendingDodgeSignalsBySource.Clear();
            _pendingCompactAvoidances.Clear();
            _pendingDirectAvoidancePackets.Clear();
            _currentBatchDodgeTargets.Clear();
            _resolvedAvoidanceSignatures.Clear();
            _resolvedPeriodicLinks.Clear();
            _resolvedPeriodicLinkOrder.Clear();
            _currentAvoidanceBatchOrdinal = 0;
        }
        _lastObservedNpcSource = 0;
        CurrentTarget = 0;
        _observationOrdinal = 0;
        _detailRevisionSequence = 0;
    }

    public ConcurrentDictionary<int, ConcurrentQueue<ParsedCombatPacket>> CombatPacketsByTarget => _packetsByTarget;
    public ConcurrentDictionary<int, ConcurrentQueue<ParsedCombatPacket>> CombatPacketsBySource => _packetsBySource;
    public ConcurrentDictionary<int, string> Nicknames => _nicknameStorage;
    public ConcurrentDictionary<int, int> SummonOwnerByInstance => _summonOwnerByInstance;
    public ConcurrentDictionary<int, string> NpcNameByCode => _npcNameByCode;

    internal long GetCombatantDetailRevision(int combatantId)
    {
        return combatantId > 0 && _detailRevisionByCombatant.TryGetValue(combatantId, out var revision)
            ? revision
            : 0;
    }

    internal IEnumerable<CombatMetricsEngine.BattlePacketContext> EnumerateCombatantDetailPackets(
        DamageMeterSnapshot snapshot,
        int combatantId)
    {
        if (combatantId <= 0 ||
            snapshot.BattleTime <= 0 ||
            snapshot.BattleStartTime <= 0 ||
            snapshot.BattleEndTime < snapshot.BattleStartTime)
        {
            yield break;
        }

        var relevantCombatantIds = new HashSet<int> { combatantId };
        var yieldedPairs = new HashSet<EntityPairKey>();

        foreach (var pairKey in EnumerateOutgoingPairKeys(combatantId))
        {
            if (!yieldedPairs.Add(pairKey))
            {
                continue;
            }

            foreach (var context in EnumeratePairPackets(pairKey, snapshot.BattleStartTime, snapshot.BattleEndTime, relevantCombatantIds))
            {
                yield return context;
            }
        }

        foreach (var pairKey in EnumerateIncomingPairKeys(combatantId))
        {
            if (!yieldedPairs.Add(pairKey))
            {
                continue;
            }

            foreach (var context in EnumeratePairPackets(pairKey, snapshot.BattleStartTime, snapshot.BattleEndTime, relevantCombatantIds))
            {
                yield return context;
            }
        }
    }

    private IEnumerable<EntityPairKey> EnumerateOutgoingPairKeys(int combatantId)
    {
        if (_outgoingPairKeysBySource.TryGetValue(combatantId, out var directPairs))
        {
            foreach (var pairKey in directPairs.Keys)
            {
                yield return pairKey;
            }
        }

        if (!_summonInstancesByOwner.TryGetValue(combatantId, out var summonInstances))
        {
            yield break;
        }

        foreach (var summonInstanceId in summonInstances.Keys)
        {
            if (!_outgoingPairKeysBySource.TryGetValue(summonInstanceId, out var summonPairs))
            {
                continue;
            }

            foreach (var pairKey in summonPairs.Keys)
            {
                yield return pairKey;
            }
        }
    }

    private IEnumerable<EntityPairKey> EnumerateIncomingPairKeys(int combatantId)
    {
        if (!_incomingPairKeysByTarget.TryGetValue(combatantId, out var incomingPairs))
        {
            yield break;
        }

        foreach (var pairKey in incomingPairs.Keys)
        {
            yield return pairKey;
        }
    }

    private IEnumerable<CombatMetricsEngine.BattlePacketContext> EnumeratePairPackets(
        EntityPairKey pairKey,
        long battleStart,
        long battleEnd,
        HashSet<int> relevantCombatantIds)
    {
        if (!_packetsByPair.TryGetValue(pairKey, out var queue))
        {
            yield break;
        }

        foreach (var packet in queue)
        {
            var sourceId = CombatMetricsEngine.ResolveCombatantId(this, packet.SourceId);
            var targetId = packet.TargetId;
            if (packet.Timestamp >= battleStart && packet.Timestamp <= battleEnd)
            {
                yield return new CombatMetricsEngine.BattlePacketContext(packet, sourceId, targetId);
                continue;
            }

            if (!CombatMetricsEngine.IsRelevantRecoveryPacket(packet, sourceId, targetId, relevantCombatantIds))
            {
                continue;
            }

            yield return new CombatMetricsEngine.BattlePacketContext(packet, sourceId, targetId);
        }
    }

    public CombatMetricsStore DeepClone()
    {
        var clone = new CombatMetricsStore
        {
            CurrentTarget = CurrentTarget,
            _lastObservedNpcSource = _lastObservedNpcSource
        };

        CloneQueues(_packetsByTarget, clone._packetsByTarget);
        CloneQueues(_packetsBySource, clone._packetsBySource);
        CloneValues(_nicknameStorage, clone._nicknameStorage);
        CloneValues(_summonOwnerByInstance, clone._summonOwnerByInstance);
        CloneValues(_npcNameByCode, clone._npcNameByCode);
        CloneNpcStates(_npcStateByInstance, clone._npcStateByInstance);
        CloneValues(_detailRevisionByCombatant, clone._detailRevisionByCombatant);
        clone._detailRevisionSequence = _detailRevisionSequence;
        lock (_compactOutcomeLock)
        {
            foreach (var (sourceId, pendingSignal) in _pendingDodgeSignalsBySource)
            {
                clone._pendingDodgeSignalsBySource[sourceId] = pendingSignal;
            }
        }

        clone.RebuildPairIndexes();
        return clone;
    }

    public CombatMetricsStore CreateArchiveSlice(DamageMeterSnapshot snapshot)
    {
        var clone = new CombatMetricsStore();
        var relevantCombatantIds = new HashSet<int>(snapshot.Combatants.Keys);
        var relevantNpcInstanceIds = new HashSet<int>();
        var battleStart = snapshot.BattleStartTime;
        var battleEnd = snapshot.BattleEndTime;
        var hasBattleWindow = battleStart > 0 && battleEnd >= battleStart;

        if (snapshot.TargetObservation?.InstanceId is int targetInstanceId && targetInstanceId > 0)
        {
            relevantCombatantIds.Add(targetInstanceId);
            relevantNpcInstanceIds.Add(targetInstanceId);
        }

        foreach (var queue in _packetsBySource.Values)
        {
            foreach (var packet in queue)
            {
                var sourceId = CombatMetricsEngine.ResolveCombatantId(this, packet.SourceId);
                var targetId = CombatMetricsEngine.ResolveCombatantId(this, packet.TargetId);
                if (hasBattleWindow &&
                    (packet.Timestamp < battleStart || packet.Timestamp > battleEnd) &&
                    !IsRelevantRecoveryPacket(packet, sourceId, targetId, relevantCombatantIds))
                {
                    continue;
                }

                var packetClone = packet.DeepClone();
                clone.AppendCombatPacket(packetClone);
                relevantCombatantIds.Add(packetClone.SourceId);
                relevantCombatantIds.Add(packetClone.TargetId);
            }
        }

        ExpandRelevantCombatantIdsWithSummonOwners(relevantCombatantIds);

        foreach (var combatantId in relevantCombatantIds)
        {
            if (_nicknameStorage.TryGetValue(combatantId, out var nickname))
            {
                clone._nicknameStorage[combatantId] = nickname;
            }

            if (_summonOwnerByInstance.TryGetValue(combatantId, out var ownerId))
            {
                clone._summonOwnerByInstance[combatantId] = ownerId;
            }

            if (_npcStateByInstance.TryGetValue(combatantId, out var npcState))
            {
                clone._npcStateByInstance[combatantId] = CloneNpcState(npcState);
                relevantNpcInstanceIds.Add(combatantId);
                if (npcState.NpcCode is int npcCode &&
                    _npcNameByCode.TryGetValue(npcCode, out var npcName))
                {
                    clone._npcNameByCode[npcCode] = npcName;
                }
            }
        }

        clone.CurrentTarget = CurrentTarget > 0 && relevantCombatantIds.Contains(CurrentTarget)
            ? CurrentTarget
            : snapshot.TargetObservation?.InstanceId ?? 0;
        clone._lastObservedNpcSource = relevantNpcInstanceIds.Contains(_lastObservedNpcSource)
            ? _lastObservedNpcSource
            : 0;
        clone.RebuildPairIndexes();

        return clone;
    }

    private static bool IsRelevantRecoveryPacket(
        ParsedCombatPacket packet,
        int sourceId,
        int targetId,
        HashSet<int> relevantCombatantIds)
    {
        return CombatMetricsEngine.IsRelevantRecoveryPacket(packet, sourceId, targetId, relevantCombatantIds);
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

    private static void CloneNpcStates(
        ConcurrentDictionary<int, NpcInstanceState> source,
        ConcurrentDictionary<int, NpcInstanceState> destination)
    {
        foreach (var (key, state) in source)
        {
            destination[key] = CloneNpcState(state);
        }
    }

    private static NpcInstanceState CloneNpcState(NpcInstanceState state)
    {
        return state with
        {
            Results2C38BySequence = state.Results2C38BySequence is null
                ? null
                : new ConcurrentDictionary<int, int>(state.Results2C38BySequence)
        };
    }

    private void ExpandRelevantCombatantIdsWithSummonOwners(HashSet<int> relevantCombatantIds)
    {
        var pendingCombatantIds = new Queue<int>(relevantCombatantIds);
        while (pendingCombatantIds.Count > 0)
        {
            var combatantId = pendingCombatantIds.Dequeue();
            if (!_summonOwnerByInstance.TryGetValue(combatantId, out var ownerId) ||
                !relevantCombatantIds.Add(ownerId))
            {
                continue;
            }

            pendingCombatantIds.Enqueue(ownerId);
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

    private void UpdateNpcState(int instanceId, Func<NpcInstanceState, NpcInstanceState> update)
    {
        if (instanceId <= 0)
        {
            return;
        }

        _npcStateByInstance.AddOrUpdate(
            instanceId,
            _ => update(new NpcInstanceState()),
            (_, current) => update(current));
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

    private void TrackDirectAvoidanceCandidate(ParsedCombatPacket packet)
    {
        if (!IsDirectBlockedDamageCandidate(packet))
        {
            return;
        }

        lock (_compactOutcomeLock)
        {
            EnsureAvoidanceBatch_NoLock(packet.BatchOrdinal);

            var signature = new AvoidedSignature(packet.SourceId, packet.TargetId, packet.Marker);
            if (_resolvedAvoidanceSignatures.Contains(signature))
            {
                return;
            }

            _pendingDirectAvoidancePackets.Add(packet);
            TrimPendingAvoidances_NoLock();
        }
    }

    private void EnsureAvoidanceBatch_NoLock(long batchOrdinal)
    {
        var resolvedBatchOrdinal = batchOrdinal > 0 ? batchOrdinal : NextObservationOrdinal();
        if (_currentAvoidanceBatchOrdinal == 0)
        {
            _currentAvoidanceBatchOrdinal = resolvedBatchOrdinal;
            return;
        }

        if (resolvedBatchOrdinal == _currentAvoidanceBatchOrdinal)
        {
            return;
        }

        FinalizeAvoidanceBatch_NoLock();
        _currentAvoidanceBatchOrdinal = resolvedBatchOrdinal;
    }

    private void FinalizeAvoidanceBatch_NoLock()
    {
        if (_currentAvoidanceBatchOrdinal == 0)
        {
            return;
        }

        foreach (var packet in _pendingDirectAvoidancePackets)
        {
            var signature = new AvoidedSignature(packet.SourceId, packet.TargetId, packet.Marker);
            if (_resolvedAvoidanceSignatures.Contains(signature))
            {
                continue;
            }

            if (_currentBatchDodgeTargets.Contains(packet.TargetId))
            {
                _resolvedAvoidanceSignatures.Add(signature);
                ApplyAvoidedModifier(packet, DamageModifiers.Evade, PacketEffectTag.ActiveDodgeEvade);
            }
        }

        foreach (var pending in _pendingCompactAvoidances)
        {
            var signature = new AvoidedSignature(pending.SourceId, pending.TargetId, pending.Marker);
            if (_resolvedAvoidanceSignatures.Contains(signature))
            {
                continue;
            }

            _resolvedAvoidanceSignatures.Add(signature);
            StoreCompactEvade(
                pending.SourceId,
                pending.TargetId,
                pending.OriginalSkillCode,
                pending.Marker,
                pending.Timestamp,
                pending.FrameOrdinal,
                pending.BatchOrdinal);
        }

        _pendingCompactAvoidances.Clear();
        _pendingDirectAvoidancePackets.Clear();
        _currentBatchDodgeTargets.Clear();
        _resolvedAvoidanceSignatures.Clear();
        _currentAvoidanceBatchOrdinal = 0;
    }

    private static bool IsDirectBlockedDamageCandidate(ParsedCombatPacket packet)
    {
        if (packet.Damage != 1 ||
            packet.SourceId <= 0 ||
            packet.TargetId <= 0 ||
            packet.SourceId == packet.TargetId)
        {
            return false;
        }

        return packet.ValueKind is CombatValueKind.Damage or CombatValueKind.DrainDamage or CombatValueKind.Unknown
               || packet.EventKind == CombatEventKind.Damage;
    }

    private static void ApplyAvoidedModifier(ParsedCombatPacket packet, DamageModifiers modifier, PacketEffectTag effectTag)
    {
        packet.Damage = 0;
        packet.HitContribution = 0;
        packet.AttemptContribution = Math.Max(packet.AttemptContribution, 1);
        packet.Modifiers &= ~(DamageModifiers.Evade | DamageModifiers.Invincible | DamageModifiers.Critical);
        packet.Modifiers |= modifier;
        packet.SetEffectTag(effectTag);
        packet.IsNormalized = false;
        CombatMetricsEngine.NormalizePacketForStorage(packet);
    }

    private static bool IsCompactEvadeSignal(int targetId, int sourceId, int layoutTag, int type)
    {
        if (targetId <= 0 || sourceId <= 0 || targetId == sourceId)
        {
            return false;
        }

        return type == 1 && (layoutTag == 0 || layoutTag == 2);
    }

    private static bool IsDodgeSkill(int trackedSkillCode)
    {
        var suffix = trackedSkillCode % 1000000;
        if (suffix != 100)
            return false;
        var classPrefix = trackedSkillCode / 1000000;
        return classPrefix is >= 11 and <= 18;
    }

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

            _recentDamageCandidates.Add(new MultiHitDamageCandidate(
                packet,
                packet.SourceId,
                packet.TargetId,
                trackedSkillCode,
                packet.Marker,
                packet.Unknown,
                packet.Flag));
            TrimExpiredMultiHitCandidates_NoLock();
        }
    }

    private void TrimExpiredMultiHitCandidates_NoLock()
    {
        while (_recentDamageCandidates.Count > MaxTrackedMultiHitCandidates)
        {
            _recentDamageCandidates.RemoveAt(0);
        }
    }

    private void TrimPendingDodgeSignals_NoLock()
    {
        while (_pendingDodgeSignalsBySource.Count > MaxPendingDodgeSignals)
        {
            var oldest = _pendingDodgeSignalsBySource.OrderBy(static pair => pair.Value.ObservationOrdinal).First();
            _pendingDodgeSignalsBySource.Remove(oldest.Key);
        }
    }

    private void TrimPendingAvoidances_NoLock()
    {
        while (_pendingCompactAvoidances.Count > MaxPendingAvoidancePackets)
        {
            _pendingCompactAvoidances.RemoveAt(0);
        }

        while (_pendingDirectAvoidancePackets.Count > MaxPendingAvoidancePackets)
        {
            _pendingDirectAvoidancePackets.RemoveAt(0);
        }
    }

    private ParsedCombatPacket StoreCompactEvade(int sourceId, int targetId, int originalSkillCode, int marker, long timestamp, long frameOrdinal, long batchOrdinal)
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
            BatchOrdinal = batchOrdinal,
            Damage = 0,
            HitContribution = 0,
            AttemptContribution = 1,
            Modifiers = DamageModifiers.Evade,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };
        packet.SetEffectTag(PacketEffectTag.CompactEvade);

        PreparePacketForStorage(packet);
        StorePacket(packet);
        return packet;
    }

    private void StoreInvincible(int sourceId, int targetId, int originalSkillCode, int marker, long timestamp, long frameOrdinal, long batchOrdinal, PacketEffectTag effectTag)
    {
        if (targetId <= 0)
        {
            return;
        }

        var resolvedSkillCode = originalSkillCode > 0
            ? originalSkillCode
            : SyntheticCombatSkillCodes.UnresolvedInvincible;

        var packet = new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            OriginalSkillCode = resolvedSkillCode,
            SkillCode = resolvedSkillCode,
            Marker = marker,
            Timestamp = timestamp,
            FrameOrdinal = frameOrdinal,
            BatchOrdinal = batchOrdinal,
            Damage = 0,
            HitContribution = 0,
            AttemptContribution = 1,
            Modifiers = DamageModifiers.Invincible,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };
        packet.SetEffectTag(effectTag);

        PreparePacketForStorage(packet);
        StorePacket(packet);
    }

    private void TrimResolvedPeriodicLinks_NoLock()
    {
        while (_resolvedPeriodicLinkOrder.Count > MaxResolvedPeriodicLinks)
        {
            var signature = _resolvedPeriodicLinkOrder.Dequeue();
            _resolvedPeriodicLinks.Remove(signature);
        }
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
