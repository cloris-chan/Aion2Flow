using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class PeriodicPoolCanonicalizer
{
    private readonly record struct Key(int TargetId, int ChainId, int PoolSkillCode);
    private readonly record struct PoolState(PeriodicPoolKind Kind, long Remaining, int CasterId, int GrantSourceId, int GrantTargetId, CombatObservation Grant, bool GrantEmitted);
    private readonly Dictionary<Key, PoolState> _pools = [];

    public CombatCanonicalizationBatch Normalize(int sourceId, int targetId, in CombatObservation observation)
    {
        var normalized = NormalizeBaseObservation(sourceId, targetId, in observation);
        if (normalized.PeriodicRelation == PeriodicEffectRelation.None || targetId <= 0 || normalized.ChainId == 0)
            return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, normalized));

        var key = new Key(targetId, normalized.ChainId, ResolvePoolSkillCode(in normalized));
        var mode = normalized.PeriodicMode;
        if (mode == 9)
            return OpenPool(sourceId, targetId, key, in normalized);

        if (mode is not (10 or 11) || !_pools.TryGetValue(key, out var state))
            return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, normalized));

        if (state.Kind == PeriodicPoolKind.Unresolved)
        {
            var kind = DetermineContinuationKind(sourceId, mode, in state);
            if (kind == PeriodicPoolKind.Unresolved)
            {
                if (mode == 10)
                    _pools.Remove(key);

                return CombatCanonicalizationBatch.Empty;
            }

            state = PromotePool(state, kind);
            _pools[key] = state;
        }

        if (state.Kind == PeriodicPoolKind.Shield)
            return ApplyShieldContinuation(targetId, key, state, mode, in normalized);

        if (state.Kind == PeriodicPoolKind.PeriodicHealing && mode == 11)
            return ApplyPeriodicHealingContinuation(sourceId, targetId, key, state, in normalized);

        if (mode == 10)
            _pools.Remove(key);

        return CombatCanonicalizationBatch.Empty;
    }

    private CombatCanonicalizationBatch OpenPool(int sourceId, int targetId, Key key, in CombatObservation observation)
    {
        if (observation.Damage > 0 && sourceId > 0 && targetId > 0)
        {
            _pools[key] = new PoolState(PeriodicPoolKind.Unresolved, observation.Damage, sourceId, sourceId, targetId, observation, false);
            return CombatCanonicalizationBatch.Empty;
        }

        return CombatCanonicalizationBatch.Empty;
    }

    private static PeriodicPoolKind DetermineContinuationKind(int sourceId, int mode, in PoolState state)
    {
        if (sourceId <= 0)
            return PeriodicPoolKind.Unresolved;

        if (sourceId == state.CasterId)
            return mode == 11 ? PeriodicPoolKind.PeriodicHealing : PeriodicPoolKind.Unresolved;

        return PeriodicPoolKind.Shield;
    }

    private static PoolState PromotePool(PoolState state, PeriodicPoolKind kind)
    {
        if (kind == PeriodicPoolKind.Shield)
        {
            var grant = state.Grant with
            {
                EventKind = CombatEventKind.Support,
                ValueKind = CombatValueKind.Shield,
                EffectTag = PacketEffectTag.ShieldGrant
            };
            return new PoolState(PeriodicPoolKind.Shield, state.Remaining, state.CasterId, state.GrantSourceId, state.GrantTargetId, grant, state.GrantEmitted);
        }

        var healingGrant = state.Grant with
        {
            Damage = 0,
            EventKind = CombatEventKind.Healing,
            ValueKind = CombatValueKind.PeriodicHealing
        };
        return new PoolState(PeriodicPoolKind.PeriodicHealing, state.Remaining, state.CasterId, state.GrantSourceId, state.GrantTargetId, healingGrant, true);
    }

    private CombatCanonicalizationBatch ApplyShieldContinuation(int targetId, Key key, PoolState state, int mode, in CombatObservation observation)
    {
        var newRemaining = mode == 10 ? 0 : Math.Max(0, observation.Damage);
        var tailValue = Math.Max(0, observation.PeriodicTailPrefixValue);
        var remainingDelta = Math.Max(0, state.Remaining - newRemaining);
        var absorbed = mode == 10
            ? Math.Min(state.Remaining, Math.Max(0, observation.Damage))
            : tailValue > 0 ? tailValue : remainingDelta;

        if (absorbed <= 0)
        {
            if (mode == 10)
                _pools.Remove(key);
            else
                _pools[key] = state with { Remaining = newRemaining };

            return CombatCanonicalizationBatch.Empty;
        }

        var absorbedObservation = observation with
        {
            Damage = absorbed,
            EventKind = CombatEventKind.Support,
            ValueKind = CombatValueKind.Shield,
            EffectTag = PacketEffectTag.ShieldAbsorbed
        };

        var absorbedResult = new CombatCanonicalizationResult(state.CasterId, targetId, absorbedObservation);
        var results = state.GrantEmitted
            ? CombatCanonicalizationBatch.One(absorbedResult)
            : CombatCanonicalizationBatch.Two(new CombatCanonicalizationResult(state.GrantSourceId, state.GrantTargetId, state.Grant), absorbedResult);
        if (mode == 10)
            _pools.Remove(key);
        else
            _pools[key] = state with { Remaining = newRemaining, GrantEmitted = true };

        return results;
    }

    private CombatCanonicalizationBatch ApplyPeriodicHealingContinuation(int sourceId, int targetId, Key key, PoolState state, in CombatObservation observation)
    {
        var currentRemaining = Math.Max(0, observation.Damage);
        var tailValue = Math.Max(0, observation.PeriodicTailPrefixValue);
        var remainingDelta = Math.Max(0, state.Remaining - currentRemaining);
        var healingAmount = tailValue > 0 ? tailValue : remainingDelta;
        if (healingAmount <= 0)
        {
            if (currentRemaining == 0)
                _pools.Remove(key);
            else
                _pools[key] = state with { Remaining = currentRemaining };

            return CombatCanonicalizationBatch.Empty;
        }

        if (currentRemaining == 0)
            _pools.Remove(key);
        else
            _pools[key] = state with { Remaining = currentRemaining };

        var tick = new CombatCanonicalizationResult(sourceId, targetId, observation with
        {
            Damage = healingAmount,
            EventKind = CombatEventKind.Healing,
            ValueKind = CombatValueKind.PeriodicHealing
        });
        return CombatCanonicalizationBatch.One(tick);
    }

    private static CombatObservation NormalizeBaseObservation(int sourceId, int targetId, in CombatObservation observation) => CombatResourceRegistry.NormalizeObservationForStorage(sourceId, targetId, in observation);

    private static int ResolvePoolSkillCode(in CombatObservation observation) => observation.SkillCode != 0 ? observation.SkillCode : observation.OriginalSkillCode;
}

internal enum PeriodicPoolKind : byte
{
    Unresolved,
    Shield,
    PeriodicHealing
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
