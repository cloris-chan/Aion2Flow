using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class PeriodicPoolCanonicalizer
{
    private readonly record struct Key(int TargetId, int ChainId, int SkillIdentityCode);
    private readonly record struct RemainingValueState(long Remaining, int CasterId, int GrantSourceId, int GrantTargetId, CombatWireObservation Grant, bool ShieldGrantEmitted);

    private readonly Dictionary<Key, RemainingValueState> _states = [];

    public CombatCanonicalizationBatch Normalize(int sourceId, int targetId, in CombatWireObservation observation)
    {
        if (observation.PeriodicRelation != PeriodicEffectRelation.None && targetId > 0 && observation.ChainId != 0 && observation.PeriodicMode == 10)
        {
            return CloseStateOrEmitStandaloneMode10Target(sourceId, targetId, ResolveStateKey(targetId, observation.ChainId, in observation), in observation);
        }

        if (observation.PeriodicRelation == PeriodicEffectRelation.None || targetId <= 0 || observation.ChainId == 0)
            return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, observation));

        var key = ResolveStateKey(targetId, observation.ChainId, in observation);
        return observation.PeriodicMode switch
        {
            9 => OpenState(sourceId, targetId, key, in observation),
            11 => ApplyContinuation(sourceId, targetId, key, in observation),
            _ => CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, observation))
        };
    }

    internal PeriodicPoolCanonicalizerSnapshot CreateSnapshot()
    {
        if (_states.Count == 0)
            return new PeriodicPoolCanonicalizerSnapshot([]);

        var states = new PeriodicPoolCanonicalizerStateSnapshot[_states.Count];
        var index = 0;
        foreach (var (key, state) in _states)
            states[index++] = new PeriodicPoolCanonicalizerStateSnapshot(key.TargetId, key.ChainId, key.SkillIdentityCode, state.Remaining, state.CasterId, state.GrantSourceId, state.GrantTargetId, state.Grant, state.ShieldGrantEmitted);
        return new PeriodicPoolCanonicalizerSnapshot(states);
    }

    internal static PeriodicPoolCanonicalizer FromSnapshot(PeriodicPoolCanonicalizerSnapshot snapshot)
    {
        var canonicalizer = new PeriodicPoolCanonicalizer();
        for (var i = 0; i < snapshot.States.Length; i++)
        {
            var state = snapshot.States[i];
            var key = new Key(state.TargetId, state.ChainId, state.SkillIdentityCode);
            canonicalizer._states[key] = new RemainingValueState(state.Remaining, state.CasterId, state.GrantSourceId, state.GrantTargetId, state.Grant, state.ShieldGrantEmitted);
        }

        return canonicalizer;
    }

    private CombatCanonicalizationBatch OpenState(int sourceId, int targetId, Key key, in CombatWireObservation observation)
    {
        if (observation.Damage <= 0 || sourceId <= 0 || targetId <= 0)
            return CombatCanonicalizationBatch.Empty;

        var grant = observation;
        _states[key] = new RemainingValueState(
            Math.Max(0, observation.Damage),
            sourceId,
            sourceId,
            targetId,
            grant,
            ShieldGrantEmitted: false);

        return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(
            sourceId,
            targetId,
            grant,
            CombatPacketRule.None,
            CombatMaterializationKind.PeriodicPoolGrant,
            suppression: CombatSuppressionReason.PeriodicPoolSemanticCandidate));
    }

    internal void AcknowledgeEmittedGrant(int sourceId, int targetId, in CombatWireObservation observation)
    {
        var key = ResolveStateKey(targetId, observation.ChainId, in observation);
        if (!_states.TryGetValue(key, out var state) ||
            state.ShieldGrantEmitted ||
            state.GrantSourceId != sourceId ||
            state.GrantTargetId != targetId)
        {
            return;
        }

        _states[key] = state with { ShieldGrantEmitted = true };
    }

    private CombatCanonicalizationBatch CloseStateOrEmitStandaloneMode10Target(int sourceId, int targetId, Key key, in CombatWireObservation observation)
    {
        if (_states.Remove(key) || !IsStandaloneMode10DamagePacket(sourceId, targetId, in observation))
        {
            return CombatCanonicalizationBatch.Empty;
        }

        var normalized = observation with
        {
            SkillCode = observation.PeriodicTailSkillCodeRaw,
            HitCount = 0,
            AttemptCount = 0
        };
        return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, normalized, CombatPacketRule.PeriodicFallbackDamage));
    }

    private static bool IsStandaloneMode10DamagePacket(int sourceId, int targetId, in CombatWireObservation observation) =>
        sourceId > 0 &&
        targetId > 0 &&
        observation.PeriodicRelation == PeriodicEffectRelation.Target &&
        observation.PeriodicMode == 10 &&
        observation.ChainId != 0 &&
        observation.Damage > 0 &&
        observation.PeriodicTailLength == 4 &&
        observation.PeriodicTailPrefixValue == 0 &&
        observation.PeriodicTailSkillCodeRaw > 0;

    private CombatCanonicalizationBatch ApplyContinuation(int sourceId, int targetId, Key key, in CombatWireObservation observation)
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
            return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(
                sourceId,
                targetId,
                observation with { Damage = emittedValue },
                CombatPacketRule.PeriodicRecovery,
                CombatMaterializationKind.PeriodicRecovery));

        return ApplyShieldAbsorb(targetId, key, state, observation with { Damage = emittedValue });
    }

    private static CombatCanonicalizationBatch EmitStandaloneContinuation(int sourceId, int targetId, in CombatWireObservation observation, long emittedValue)
    {
        if (emittedValue <= 0)
            return CombatCanonicalizationBatch.Empty;

        return CombatCanonicalizationBatch.One(new CombatCanonicalizationResult(sourceId, targetId, observation with { Damage = emittedValue }));
    }

    private CombatCanonicalizationBatch ApplyShieldAbsorb(int targetId, Key key, RemainingValueState state, in CombatWireObservation observation)
    {
        var absorbedResult = new CombatCanonicalizationResult(
            state.CasterId,
            targetId,
            observation,
            CombatPacketRule.PeriodicShieldAbsorbed,
            CombatMaterializationKind.PeriodicPoolAbsorb);

        if (state.ShieldGrantEmitted)
            return CombatCanonicalizationBatch.One(absorbedResult);

        _states[key] = state with { ShieldGrantEmitted = true };
        return CombatCanonicalizationBatch.Two(
            new CombatCanonicalizationResult(state.GrantSourceId, state.GrantTargetId, state.Grant, CombatPacketRule.PeriodicShieldGrant, CombatMaterializationKind.PeriodicPoolGrant),
            absorbedResult);
    }

    private static Key ResolveStateKey(int targetId, int chainId, in CombatWireObservation observation) => new(targetId, chainId, ResolvePeriodicSkillIdentityCode(in observation));

    private static int ResolvePeriodicSkillIdentityCode(in CombatWireObservation observation) => Math.Max(0, observation.PeriodicTailSkillCodeRaw);
}

internal sealed record PeriodicPoolCanonicalizerSnapshot(PeriodicPoolCanonicalizerStateSnapshot[] States);

internal readonly record struct PeriodicPoolCanonicalizerStateSnapshot(int TargetId, int ChainId, int SkillIdentityCode, long Remaining, int CasterId, int GrantSourceId, int GrantTargetId, CombatWireObservation Grant, bool ShieldGrantEmitted);

public readonly record struct CombatCanonicalizationResult(int SourceId, int TargetId, CombatWireObservation Observation, CombatOccurrenceResolution Resolution)
{
    public CombatCanonicalizationResult(int sourceId, int targetId, CombatWireObservation observation)
        : this(sourceId, targetId, observation, CombatOccurrenceResolution.Primary)
    {
    }

    public CombatCanonicalizationResult(
        int sourceId,
        int targetId,
        CombatWireObservation observation,
        CombatPacketRule packetRule,
        CombatMaterializationKind materialization = CombatMaterializationKind.Primary,
        CombatAssociationKind association = CombatAssociationKind.None,
        CombatSuppressionReason suppression = CombatSuppressionReason.None)
        : this(sourceId, targetId, observation, new CombatOccurrenceResolution(packetRule, materialization, association, suppression))
    {
    }

    public CombatCanonicalizationResult Inherit(in CombatOccurrenceResolution previous) => this with { Resolution = Resolution.Inherit(in previous) };
}

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
