using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class PeriodicPoolCanonicalizer
{
    private readonly record struct Key(int TargetId, int ChainId, int PoolSkillCode);
    private readonly record struct RemainingValueState(long Remaining, int CasterId, int GrantSourceId, int GrantTargetId, CombatObservation Grant, bool ShieldGrantEmitted);
    private readonly Dictionary<Key, RemainingValueState> _states = [];

    public CombatCanonicalizationBatch Normalize(int sourceId, int targetId, in CombatObservation observation)
    {
        var normalized = NormalizeBaseObservation(sourceId, targetId, in observation);
        if (normalized.PeriodicRelation == PeriodicEffectRelation.None || targetId <= 0 || normalized.ChainId == 0)
            return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, normalized));

        var key = new Key(targetId, normalized.ChainId, ResolvePoolSkillCode(in normalized));
        return normalized.PeriodicMode switch
        {
            9 => OpenState(sourceId, targetId, key, in normalized),
            10 when IsMode10DamageTick(in normalized) => CloseStateWithDamageTick(sourceId, targetId, key, in normalized),
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

    private static int ResolvePoolSkillCode(in CombatObservation observation) => observation.SkillCode != 0 ? observation.SkillCode : observation.OriginalSkillCode;

    private static bool IsMode10DamageTick(in CombatObservation observation) =>
        observation.PeriodicRelation == PeriodicEffectRelation.Target &&
        observation.PeriodicTailSkillCodeRaw > 0 &&
        observation.ValueKind == CombatValueKind.PeriodicDamage &&
        observation.Damage > 0 &&
        ResolveMode10TickSkillFamily(in observation) is var skillFamily &&
        skillFamily > 0 &&
        IsMode10TickTailSkill(skillFamily, observation.PeriodicTailSkillCodeRaw) &&
        observation.OriginalSkillCode == (long)skillFamily * 100 + 11;

    private static bool IsMode10TickTailSkill(int skillFamily, int tailSkillCode)
    {
        var suffix = tailSkillCode - skillFamily;
        return suffix is 3 or 30;
    }

    private static int ResolveMode10TickSkillFamily(in CombatObservation observation)
    {
        return observation.OriginalSkillCode > 0 ? observation.OriginalSkillCode / 100 : 0;
    }

    private static CombatObservation NormalizeMode10DamageTick(int sourceId, int targetId, in CombatObservation observation)
    {
        var tailSkillCode = observation.PeriodicTailSkillCodeRaw;
        var reassigned = observation with
        {
            SkillCode = tailSkillCode,
            OriginalSkillCode = tailSkillCode,
            BaseSkillCode = 0
        };
        return NormalizeBaseObservation(sourceId, targetId, in reassigned);
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
