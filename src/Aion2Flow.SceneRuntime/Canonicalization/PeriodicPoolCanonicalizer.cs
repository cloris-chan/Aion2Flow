using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class PeriodicPoolCanonicalizer
{
    private readonly record struct Key(int TargetId, int ChainId, int OriginalSkillCode);
    private readonly record struct PoolState(CombatValueKind Kind, long Remaining, int CasterId, int GrantSourceId, int GrantTargetId, CombatObservation Grant, bool GrantEmitted);
    private readonly Dictionary<Key, PoolState> _pools = [];

    public CombatCanonicalizationBatch Normalize(int sourceId, int targetId, in CombatObservation observation)
    {
        var normalized = NormalizeBaseObservation(sourceId, targetId, in observation);
        if (normalized.PeriodicRelation == PeriodicEffectRelation.None || targetId <= 0 || normalized.ChainId == 0)
            return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, normalized));

        var key = new Key(targetId, normalized.ChainId, ResolveOriginalSkillCode(in normalized));
        var mode = normalized.PeriodicMode;
        if (mode == 9)
            return OpenPool(sourceId, targetId, key, in normalized);

        if (mode is not (10 or 11) || !_pools.TryGetValue(key, out var state))
            return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, normalized));

        if (state.Kind == CombatValueKind.Unknown)
        {
            if (mode != 11)
            {
                _pools.Remove(key);
                return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, normalized));
            }

            state = PromoteAmbiguousPool(sourceId, targetId, state);
            _pools[key] = state;
        }

        if (state.Kind == CombatValueKind.Shield)
            return ApplyShieldContinuation(targetId, key, state, mode, in normalized);

        if (state.Kind == CombatValueKind.PeriodicHealing && mode == 11)
            return ApplyPeriodicHealingContinuation(sourceId, targetId, key, state, in normalized);

        return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, normalized));
    }

    private CombatCanonicalizationBatch OpenPool(int sourceId, int targetId, Key key, in CombatObservation observation)
    {
        if (observation.ValueKind == CombatValueKind.PeriodicHealing && IsPeriodicHealingPoolPacket(sourceId, targetId, in observation))
        {
            var opened = observation with { Damage = 0 };
            _pools[key] = new PoolState(CombatValueKind.PeriodicHealing, observation.Damage, sourceId, sourceId, targetId, opened, true);
            return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, opened));
        }

        if (observation.Damage > 0 && sourceId > 0)
        {
            _pools[key] = new PoolState(CombatValueKind.Unknown, observation.Damage, sourceId, sourceId, targetId, observation, false);
            return CombatCanonicalizationBatch.Empty;
        }

        return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, observation));
    }

    private static PoolState PromoteAmbiguousPool(int sourceId, int targetId, PoolState state)
    {
        if (sourceId != targetId)
        {
            var grant = state.Grant with
            {
                EventKind = CombatEventKind.Support,
                ValueKind = CombatValueKind.Shield,
                EffectTag = PacketEffectTag.ShieldGrant
            };
            return new PoolState(CombatValueKind.Shield, state.Remaining, state.CasterId, state.GrantSourceId, state.GrantTargetId, grant, state.GrantEmitted);
        }

        var healingGrant = state.Grant with
        {
            Damage = 0,
            EventKind = CombatEventKind.Healing,
            ValueKind = CombatValueKind.PeriodicHealing
        };
        return new PoolState(CombatValueKind.PeriodicHealing, state.Remaining, state.CasterId, state.GrantSourceId, state.GrantTargetId, healingGrant, state.GrantEmitted);
    }

    private CombatCanonicalizationBatch ApplyShieldContinuation(int targetId, Key key, PoolState state, int mode, in CombatObservation observation)
    {
        var newRemaining = Math.Max(0, observation.Damage);
        var absorbed = Math.Max(0, state.Remaining - newRemaining);
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
            _pools[key] = new PoolState(CombatValueKind.Shield, newRemaining, state.CasterId, state.GrantSourceId, state.GrantTargetId, state.Grant, true);

        return results;
    }

    private CombatCanonicalizationBatch ApplyPeriodicHealingContinuation(int sourceId, int targetId, Key key, PoolState state, in CombatObservation observation)
    {
        var rawDamage = observation.Damage;
        if (rawDamage >= state.Remaining)
        {
            _pools.Remove(key);
            return AppendGrantIfNeeded(state, new CombatCanonicalizationResult(sourceId, targetId, observation));
        }

        var healingAmount = state.Remaining - rawDamage;
        if (healingAmount <= 0)
        {
            _pools.Remove(key);
            return AppendGrantIfNeeded(state, new CombatCanonicalizationResult(sourceId, targetId, observation));
        }

        if (rawDamage == 0)
            _pools.Remove(key);
        else
            _pools[key] = new PoolState(CombatValueKind.PeriodicHealing, rawDamage, state.CasterId, state.GrantSourceId, state.GrantTargetId, state.Grant, true);

        var tick = new CombatCanonicalizationResult(sourceId, targetId, observation with
        {
            Damage = healingAmount,
            EventKind = CombatEventKind.Healing,
            ValueKind = CombatValueKind.PeriodicHealing
        });
        return AppendGrantIfNeeded(state, tick);
    }

    private static CombatCanonicalizationBatch AppendGrantIfNeeded(PoolState state, in CombatCanonicalizationResult tail)
    {
        if (state.GrantEmitted)
            return CombatCanonicalizationBatch.One(tail);

        return CombatCanonicalizationBatch.Two(new CombatCanonicalizationResult(state.GrantSourceId, state.GrantTargetId, state.Grant), tail);
    }

    private static CombatObservation NormalizeBaseObservation(int sourceId, int targetId, in CombatObservation observation)
        => CombatResourceRegistry.NormalizeObservationForStorage(sourceId, targetId, in observation);

    private static bool IsPeriodicHealingPoolPacket(int sourceId, int targetId, in CombatObservation observation)
    {
        if (sourceId <= 0 || targetId <= 0)
            return false;

        var isSelfPool = sourceId == targetId && observation.PeriodicRelation == PeriodicEffectRelation.Self && observation.PeriodicMode is 9 or 11;
        var isTargetPool = observation.PeriodicRelation == PeriodicEffectRelation.Target && observation.PeriodicMode is 9 or 11 && IsEnhanceSpiritBenediction(in observation);
        return isSelfPool || isTargetPool;
    }

    private static bool IsEnhanceSpiritBenediction(in CombatObservation observation)
    {
        const int skillCode = 16190000;
        return MatchesBase(in observation, skillCode) ||
               MatchesExact(in observation, skillCode, 16190010, 16190020, 16190030) ||
               MatchesByHundred(observation.SkillCode, skillCode) ||
               MatchesByHundred(observation.OriginalSkillCode, skillCode);
    }

    private static bool MatchesExact(in CombatObservation observation, int a, int b, int c, int d) =>
        MatchesSkillCode(in observation, a) ||
        MatchesSkillCode(in observation, b) ||
        MatchesSkillCode(in observation, c) ||
        MatchesSkillCode(in observation, d);

    private static bool MatchesSkillCode(in CombatObservation observation, int skillCode) =>
        skillCode > 0 &&
        (observation.SkillCode == skillCode ||
         observation.OriginalSkillCode == skillCode ||
         CombatResourceRegistry.InferOriginalSkillCode(observation.OriginalSkillCode) == skillCode ||
         CombatResourceRegistry.InferOriginalSkillCode(observation.SkillCode) == skillCode);

    private static bool MatchesBase(in CombatObservation observation, int baseSkillCode) =>
        baseSkillCode > 0 &&
        (observation.BaseSkillCode == baseSkillCode ||
         CombatResourceRegistry.ParseSkillVariant(observation.OriginalSkillCode).BaseSkillCode == baseSkillCode ||
         CombatResourceRegistry.ParseSkillVariant(observation.SkillCode).BaseSkillCode == baseSkillCode);

    private static bool MatchesByHundred(int candidateSkillCode, int skillCode) =>
        candidateSkillCode > 0 && skillCode > 0 && candidateSkillCode / 100 == skillCode;

    private static int ResolveOriginalSkillCode(in CombatObservation observation) =>
        observation.OriginalSkillCode != 0 ? observation.OriginalSkillCode : observation.SkillCode;
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

    public static CombatCanonicalizationBatch One(in CombatCanonicalizationResult result) =>
        new(1, in result, default);

    public static CombatCanonicalizationBatch Two(in CombatCanonicalizationResult first, in CombatCanonicalizationResult second) =>
        new(2, in first, in second);

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
