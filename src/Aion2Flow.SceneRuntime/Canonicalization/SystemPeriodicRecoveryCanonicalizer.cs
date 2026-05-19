using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class SystemPeriodicRecoveryCanonicalizer
{
    private const int PeriodicSelfRecoveryBaseSkillCode = 190000000;
    private readonly record struct Key(int SourceId, int TargetId, int OriginalSkillCode);
    private readonly record struct State(long Damage, long FrameOrdinal, long BatchOrdinal);
    private readonly Dictionary<Key, State> _seeds = [];

    internal StateSnapshot CreateStateSnapshot()
    {
        var seeds = new SeedStateSnapshot[_seeds.Count];
        var index = 0;
        foreach (var pair in _seeds)
            seeds[index++] = new SeedStateSnapshot(pair.Key.SourceId, pair.Key.TargetId, pair.Key.OriginalSkillCode, pair.Value.Damage, pair.Value.FrameOrdinal, pair.Value.BatchOrdinal);
        Array.Sort(seeds, static (left, right) =>
        {
            var cmp = left.SourceId.CompareTo(right.SourceId);
            if (cmp != 0) return cmp;
            cmp = left.TargetId.CompareTo(right.TargetId);
            return cmp != 0 ? cmp : left.OriginalSkillCode.CompareTo(right.OriginalSkillCode);
        });
        return new StateSnapshot(seeds);
    }

    internal void RestoreState(StateSnapshot snapshot)
    {
        _seeds.Clear();
        _seeds.EnsureCapacity(snapshot.Seeds.Length);
        foreach (ref readonly var seed in snapshot.Seeds.AsSpan())
            _seeds.Add(new Key(seed.SourceId, seed.TargetId, seed.OriginalSkillCode), new State(seed.Damage, seed.FrameOrdinal, seed.BatchOrdinal));
    }

    public CombatCanonicalizationResult Normalize(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation)
    {
        if (!TryGetKey(sourceId, targetId, in observation, out var key, out var isSeed))
            return new CombatCanonicalizationResult(sourceId, targetId, observation);

        if (isSeed)
        {
            _seeds[key] = new State(observation.Damage, stamp.FrameOrdinal, stamp.BatchOrdinal);
            return new CombatCanonicalizationResult(sourceId, targetId, observation with
            {
                EventKind = CombatEventKind.Support,
                ValueKind = CombatValueKind.Support
            });
        }

        if (!_seeds.TryGetValue(key, out var state))
            return new CombatCanonicalizationResult(sourceId, targetId, observation);

        _seeds.Remove(key);
        if (!IsContinuationAfterSeed(in stamp, state) || observation.Damage != state.Damage)
            return new CombatCanonicalizationResult(sourceId, targetId, observation);

        return new CombatCanonicalizationResult(sourceId, targetId, observation with
        {
            EventKind = CombatEventKind.Healing,
            ValueKind = CombatValueKind.PeriodicHealing
        });
    }

    private static bool TryGetKey(int sourceId, int targetId, in CombatObservation observation, out Key key, out bool isSeed)
    {
        key = default;
        isSeed = false;

        if (sourceId <= 0 || targetId <= 0 || sourceId != targetId || observation.Damage <= 0 || observation.PeriodicRelation != PeriodicEffectRelation.Self || observation.PeriodicMode is not (1 or 2))
            return false;

        var originalSkillCode = ResolveOriginalSkillCode(in observation);
        if (originalSkillCode <= 0)
            return false;

        var baseSkillCode = observation.BaseSkillCode > 0 ? observation.BaseSkillCode : CombatResourceRegistry.ParseSkillVariant(originalSkillCode).BaseSkillCode;
        if (baseSkillCode != PeriodicSelfRecoveryBaseSkillCode)
            return false;

        key = new Key(sourceId, targetId, originalSkillCode);
        isSeed = observation.PeriodicMode == 1;
        return true;
    }

    private static bool IsContinuationAfterSeed(in TimelineStamp stamp, State state)
    {
        if (stamp.BatchOrdinal > 0 && state.BatchOrdinal > 0)
            return stamp.BatchOrdinal >= state.BatchOrdinal;

        if (stamp.FrameOrdinal > 0 && state.FrameOrdinal > 0)
            return stamp.FrameOrdinal >= state.FrameOrdinal;

        return true;
    }

    private static int ResolveOriginalSkillCode(in CombatObservation observation) =>
        observation.OriginalSkillCode != 0 ? observation.OriginalSkillCode : observation.SkillCode;

    internal sealed class StateSnapshot(SeedStateSnapshot[] seeds)
    {
        public SeedStateSnapshot[] Seeds { get; } = seeds;

        public StateSnapshot DeepClone()
        {
            var seeds = new SeedStateSnapshot[Seeds.Length];
            Array.Copy(Seeds, seeds, seeds.Length);
            return new StateSnapshot(seeds);
        }
    }

    internal readonly record struct SeedStateSnapshot(int SourceId, int TargetId, int OriginalSkillCode, long Damage, long FrameOrdinal, long BatchOrdinal);
}
