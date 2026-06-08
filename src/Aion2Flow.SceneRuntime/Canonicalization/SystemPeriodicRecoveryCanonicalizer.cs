using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class SystemPeriodicRecoveryCanonicalizer
{
    private readonly record struct Key(int SourceId, int TargetId, int ChainId, int TailSkillCodeRaw);
    private readonly record struct State(long Damage, long FrameOrdinal, long BatchOrdinal);
    private readonly Dictionary<Key, State> _seeds = [];

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

    internal SystemPeriodicRecoveryCanonicalizerSnapshot CreateSnapshot()
    {
        if (_seeds.Count == 0)
            return new SystemPeriodicRecoveryCanonicalizerSnapshot([]);

        var seeds = new SystemPeriodicRecoverySeedSnapshot[_seeds.Count];
        var index = 0;
        foreach (var (key, state) in _seeds)
            seeds[index++] = new SystemPeriodicRecoverySeedSnapshot(key.SourceId, key.TargetId, key.ChainId, key.TailSkillCodeRaw, state.Damage, state.FrameOrdinal, state.BatchOrdinal);
        return new SystemPeriodicRecoveryCanonicalizerSnapshot(seeds);
    }

    internal static SystemPeriodicRecoveryCanonicalizer FromSnapshot(SystemPeriodicRecoveryCanonicalizerSnapshot snapshot)
    {
        var canonicalizer = new SystemPeriodicRecoveryCanonicalizer();
        for (var i = 0; i < snapshot.Seeds.Length; i++)
        {
            var seed = snapshot.Seeds[i];
            canonicalizer._seeds[new Key(seed.SourceId, seed.TargetId, seed.ChainId, seed.TailSkillCodeRaw)] = new State(seed.Damage, seed.FrameOrdinal, seed.BatchOrdinal);
        }

        return canonicalizer;
    }

    private static bool TryGetKey(int sourceId, int targetId, in CombatObservation observation, out Key key, out bool isSeed)
    {
        key = default;
        isSeed = false;

        if (sourceId <= 0 ||
            targetId <= 0 ||
            sourceId != targetId ||
            observation.Damage <= 0 ||
            observation.PeriodicRelation != PeriodicEffectRelation.Self ||
            observation.PeriodicMode is not (1 or 2) ||
            observation.ChainId == 0)
            return false;

        key = new Key(sourceId, targetId, observation.ChainId, observation.PeriodicTailSkillCodeRaw);
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
}

internal sealed record SystemPeriodicRecoveryCanonicalizerSnapshot(SystemPeriodicRecoverySeedSnapshot[] Seeds);

internal readonly record struct SystemPeriodicRecoverySeedSnapshot(int SourceId, int TargetId, int ChainId, int TailSkillCodeRaw, long Damage, long FrameOrdinal, long BatchOrdinal);
