using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class PeriodicPoolCanonicalizer
{
    private readonly record struct Key(int TargetId, int ChainId, int EffectCode);
    private readonly record struct RemainingValueState(long Remaining, int CasterId, int GrantSourceId, int GrantTargetId, CombatObservation Grant, bool ShieldGrantEmitted);

    private readonly Dictionary<Key, RemainingValueState> _states = [];
    private readonly Mode10TailSidecarGate _mode10TailGate;

    public PeriodicPoolCanonicalizer()
        : this(static _ => false)
    {
    }

    public PeriodicPoolCanonicalizer(EntityStore entities)
        : this(instanceId => entities.TryGet(instanceId, out var entity) && entity.OwnerEntityId.HasValue)
    {
    }

    private PeriodicPoolCanonicalizer(Func<int, bool> hasSummonOwner)
    {
        _mode10TailGate = new Mode10TailSidecarGate(hasSummonOwner);
    }

    public void ObserveCompactControl0638(int sourceId, int skillCode, int flag, in TimelineStamp stamp)
    {
        _mode10TailGate.ObserveCompactControl0638(sourceId, skillCode, flag, in stamp);
    }

    public CombatCanonicalizationBatch Normalize(int sourceId, int targetId, in CombatObservation observation)
    {
        return Normalize(sourceId, targetId, in observation, default);
    }

    public CombatCanonicalizationBatch Normalize(int sourceId, int targetId, in CombatObservation observation, in TimelineStamp stamp)
    {
        var normalized = NormalizeBaseObservation(sourceId, targetId, in observation);
        if (normalized.PeriodicRelation == PeriodicEffectRelation.None || targetId <= 0 || normalized.ChainId == 0)
            return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, normalized));

        var key = ResolveStateKey(targetId, normalized.ChainId, in normalized);
        return normalized.PeriodicMode switch
        {
            9 => OpenState(sourceId, targetId, key, in normalized),
            10 when _mode10TailGate.TryAcceptDamageTick(sourceId, targetId, in observation, in stamp) => CloseStateWithDamageTick(sourceId, targetId, key, in normalized),
            10 => CloseState(key),
            11 => ApplyContinuation(sourceId, targetId, key, in normalized),
            _ => CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, normalized))
        };
    }

    private CombatCanonicalizationBatch OpenState(int sourceId, int targetId, Key key, in CombatObservation observation)
    {
        if (observation.Damage > 0 && sourceId > 0 && targetId > 0)
            _states[key] = new RemainingValueState(Math.Max(0, observation.Damage), sourceId, sourceId, targetId, observation, false);

        return CombatCanonicalizationBatch.Empty;
    }

    private CombatCanonicalizationBatch CloseState(Key key)
    {
        _states.Remove(key);
        return CombatCanonicalizationBatch.Empty;
    }

    private CombatCanonicalizationBatch CloseStateWithDamageTick(int sourceId, int targetId, Key key, in CombatObservation observation)
    {
        _states.Remove(key);
        return CombatCanonicalizationBatch.One(
            new CombatCanonicalizationResult(sourceId, targetId, NormalizeMode10DamageTick(sourceId, targetId, in observation)));
    }

    private CombatCanonicalizationBatch ApplyContinuation(int sourceId, int targetId, Key key, in CombatObservation observation)
    {
        var emittedValue = Math.Max(0, observation.PeriodicTailPrefixValue);
        if (sourceId <= 0)
            return CombatCanonicalizationBatch.Empty;

        if (!_states.TryGetValue(key, out var state))
            return EmitStandaloneContinuation(sourceId, targetId, in observation, emittedValue);

        _states[key] = state with { Remaining = Math.Max(0, observation.Damage) };
        if (emittedValue <= 0)
            return CombatCanonicalizationBatch.Empty;

        if (sourceId == state.CasterId)
            return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, observation with
            {
                Damage = emittedValue,
                EventKind = CombatEventKind.Healing,
                ValueKind = CombatValueKind.PeriodicHealing
            }));

        return ApplyShieldAbsorb(targetId, key, state, observation with { Damage = emittedValue });
    }

    private static CombatCanonicalizationBatch EmitStandaloneContinuation(int sourceId, int targetId, in CombatObservation observation, long emittedValue)
    {
        if (emittedValue <= 0)
            return CombatCanonicalizationBatch.Empty;

        return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, observation with { Damage = emittedValue }));
    }

    private CombatCanonicalizationBatch ApplyShieldAbsorb(int targetId, Key key, RemainingValueState state, in CombatObservation observation)
    {
        var absorbedResult = new CombatCanonicalizationResult(state.CasterId, targetId, observation with
        {
            EventKind = CombatEventKind.Support,
            ValueKind = CombatValueKind.Shield,
            EffectTag = PacketEffectTag.ShieldAbsorbed
        });

        if (state.ShieldGrantEmitted)
            return CombatCanonicalizationBatch.One(absorbedResult);

        _states[key] = state with { ShieldGrantEmitted = true };
        var grant = state.Grant with
        {
            EventKind = CombatEventKind.Support,
            ValueKind = CombatValueKind.Shield,
            EffectTag = PacketEffectTag.ShieldGrant
        };
        return CombatCanonicalizationBatch.Two(new CombatCanonicalizationResult(state.GrantSourceId, state.GrantTargetId, grant), absorbedResult);
    }

    private static CombatObservation NormalizeBaseObservation(int sourceId, int targetId, in CombatObservation observation) => CombatResourceRegistry.NormalizeObservationForStorage(sourceId, targetId, in observation);

    private static Key ResolveStateKey(int targetId, int chainId, in CombatObservation observation) =>
        new(targetId, chainId, ResolvePeriodicEffectCode(in observation));

    private static int ResolvePeriodicEffectCode(in CombatObservation observation)
    {
        if (observation.PeriodicTailSkillCodeRaw > 0)
            return observation.PeriodicTailSkillCodeRaw;

        if (observation.SkillCode > 0)
            return observation.SkillCode;

        return 0;
    }

    private static CombatObservation NormalizeMode10DamageTick(int sourceId, int targetId, in CombatObservation observation)
    {
        var tailSkillCode = observation.PeriodicTailSkillCodeRaw;
        var reassigned = observation with
        {
            SkillCode = tailSkillCode,
            OriginalSkillCode = tailSkillCode,
            BaseSkillCode = 0,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.PeriodicDamage
        };
        return NormalizeBaseObservation(sourceId, targetId, in reassigned);
    }

    private sealed class Mode10TailSidecarGate(Func<int, bool> hasSummonOwner)
    {
        private const int MaxRecentTailControls = 128;
        private const int MaxTriggeredTailGates = 256;
        private const int MaxAcceptedDamageChains = 256;
        private const long MaxTailControlObservationDistance = 64;
        private const long MaxTailControlBatchDistance = 16;
        private const int CompactControlContinuationFlag = 0;
        private const int CompactControlTriggeredFlag = 12;

        private readonly record struct TailKey(int SourceId, int TailSkillCode);
        private readonly record struct TailControl(TailKey Key, int Flag, TimelineStamp Stamp);
        private readonly record struct DamageChainKey(int SourceId, int TargetId, int ChainId, int TailSkillCode);

        private readonly HashSet<TailKey> _triggeredTailGates = [];
        private readonly Queue<TailKey> _triggeredTailGateOrder = [];
        private readonly List<TailControl> _recentTailControls = new(MaxRecentTailControls);
        private readonly HashSet<DamageChainKey> _acceptedDamageChains = [];
        private readonly Queue<DamageChainKey> _acceptedDamageChainOrder = [];

        public void ObserveCompactControl0638(int sourceId, int tailSkillCode, int flag, in TimelineStamp stamp)
        {
            if (sourceId <= 0 || tailSkillCode <= 0)
                return;

            var key = new TailKey(sourceId, tailSkillCode);
            if (flag == CompactControlTriggeredFlag || hasSummonOwner(sourceId))
                RememberTriggeredTailGate(key);

            _recentTailControls.Add(new TailControl(key, flag, stamp));
            TrimRecentTailControls();
        }

        public bool TryAcceptDamageTick(int sourceId, int targetId, in CombatObservation observation, in TimelineStamp stamp)
        {
            if (!IsDamageTickShape(in observation))
                return false;

            var chainKey = new DamageChainKey(sourceId, targetId, observation.ChainId, observation.PeriodicTailSkillCodeRaw);
            if (_acceptedDamageChains.Contains(chainKey))
                return true;

            if (!HasAssociatedTailControl(sourceId, observation.PeriodicTailSkillCodeRaw, in stamp))
                return false;

            RememberAcceptedDamageChain(chainKey);
            return true;
        }

        private static bool IsDamageTickShape(in CombatObservation observation) =>
            observation.PeriodicRelation == PeriodicEffectRelation.Target &&
            observation.PeriodicTailLength == 4 &&
            observation.PeriodicTailSkillCodeRaw > 0 &&
            observation.PeriodicTailSkillCodeRaw != ResolveBodySkillCode(in observation) &&
            observation.PeriodicTailPrefixValue == 0 &&
            observation.Damage > 0;

        private static int ResolveBodySkillCode(in CombatObservation observation) =>
            observation.PeriodicBodySkillCode > 0 ? observation.PeriodicBodySkillCode : observation.SkillCode;

        private bool HasAssociatedTailControl(int sourceId, int tailSkillCode, in TimelineStamp stamp)
        {
            var key = new TailKey(sourceId, tailSkillCode);
            TrimRecentTailControls();
            for (var i = _recentTailControls.Count - 1; i >= 0; i--)
            {
                var control = _recentTailControls[i];
                if (control.Key != key)
                    continue;

                var controlStamp = control.Stamp;
                if (!IsRecentTailControl(in controlStamp, in stamp))
                    continue;

                if (control.Flag == CompactControlTriggeredFlag)
                    return true;

                if (control.Flag == CompactControlContinuationFlag && (_triggeredTailGates.Contains(key) || hasSummonOwner(sourceId)))
                    return true;
            }

            return false;
        }

        private void RememberTriggeredTailGate(TailKey key)
        {
            if (!_triggeredTailGates.Add(key))
                return;

            _triggeredTailGateOrder.Enqueue(key);
            while (_triggeredTailGates.Count > MaxTriggeredTailGates)
                _triggeredTailGates.Remove(_triggeredTailGateOrder.Dequeue());
        }

        private void RememberAcceptedDamageChain(DamageChainKey key)
        {
            if (!_acceptedDamageChains.Add(key))
                return;

            _acceptedDamageChainOrder.Enqueue(key);
            while (_acceptedDamageChains.Count > MaxAcceptedDamageChains)
                _acceptedDamageChains.Remove(_acceptedDamageChainOrder.Dequeue());
        }

        private void TrimRecentTailControls()
        {
            while (_recentTailControls.Count > MaxRecentTailControls)
                _recentTailControls.RemoveAt(0);
        }

        private static bool IsRecentTailControl(in TimelineStamp controlStamp, in TimelineStamp tickStamp)
        {
            if (!HasTimeline(in controlStamp) || !HasTimeline(in tickStamp))
                return false;

            if (controlStamp.ObservationOrdinal >= 0 && tickStamp.ObservationOrdinal >= controlStamp.ObservationOrdinal)
            {
                var observationDistance = tickStamp.ObservationOrdinal - controlStamp.ObservationOrdinal;
                if (observationDistance <= MaxTailControlObservationDistance)
                    return true;
            }

            if (controlStamp.BatchOrdinal > 0 && tickStamp.BatchOrdinal >= controlStamp.BatchOrdinal)
            {
                var batchDistance = tickStamp.BatchOrdinal - controlStamp.BatchOrdinal;
                if (batchDistance <= MaxTailControlBatchDistance)
                    return true;
            }

            return false;
        }

        private static bool HasTimeline(in TimelineStamp stamp) =>
            stamp.OffsetTicks != 0 ||
            stamp.ObservationOrdinal != 0 ||
            stamp.FrameOrdinal != 0 ||
            stamp.BatchOrdinal != 0;
    }
}

public readonly record struct CombatCanonicalizationResult(int SourceId, int TargetId, CombatObservation Observation);

public readonly struct CombatCanonicalizationBatch
{
    private readonly CombatCanonicalizationResult _first;
    private readonly CombatCanonicalizationResult _second;

    private CombatCanonicalizationBatch(int count, in CombatCanonicalizationResult first, in CombatCanonicalizationResult second)
    {
        Count = count;
        _first = first;
        _second = second;
    }

    public static CombatCanonicalizationBatch Empty => default;

    public int Count { get; }

    public static CombatCanonicalizationBatch One(in CombatCanonicalizationResult result) => new(1, in result, default);

    public static CombatCanonicalizationBatch Two(in CombatCanonicalizationResult first, in CombatCanonicalizationResult second) => new(2, in first, in second);

    public CombatCanonicalizationResult this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return index == 0 ? _first : _second;
        }
    }

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator
    {
        private readonly CombatCanonicalizationBatch _batch;
        private int _index;

        internal Enumerator(CombatCanonicalizationBatch batch)
        {
            _batch = batch;
            _index = -1;
            Current = default;
        }

        public CombatCanonicalizationResult Current { get; private set; }

        public bool MoveNext()
        {
            var next = _index + 1;
            if ((uint)next >= (uint)_batch.Count)
                return false;

            _index = next;
            Current = _batch[next];
            return true;
        }
    }
}
