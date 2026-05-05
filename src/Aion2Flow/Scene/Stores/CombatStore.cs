using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.Scene.Stores;

public sealed class CombatStore
{
    private readonly Dictionary<(int Source, int Target), CombatPairRecord> _pairs = [];
    private readonly Dictionary<int, CombatantRecord> _combatants = [];
    private readonly Dictionary<int, HashSet<(int, int)>> _outgoingBySource = [];
    private readonly Dictionary<int, HashSet<(int, int)>> _incomingByTarget = [];
    private long _revision;

    public IReadOnlyDictionary<(int Source, int Target), CombatPairRecord> Pairs => _pairs;
    public IReadOnlyDictionary<int, CombatantRecord> Combatants => _combatants;
    public long Revision => _revision;

    public void ApplyCombat(int sourceId, int targetId, long damage, int hitCount, int attemptCount, int skillCode)
    {
        _revision++;

        var pairKey = (sourceId, targetId);
        if (!_pairs.TryGetValue(pairKey, out var pair))
        {
            pair = new CombatPairRecord { SourceId = sourceId, TargetId = targetId };
            _pairs[pairKey] = pair;
        }
        pair.TotalDamage += damage;
        pair.HitCount += hitCount;
        pair.AttemptCount += attemptCount;
        pair.LastSkillCode = skillCode;
        pair.Revision = _revision;

        var source = GetOrAddCombatant(sourceId);
        source.OutgoingDamage += damage;
        source.OutgoingHits += hitCount;
        source.OutgoingAttempts += attemptCount;
        source.Revision = _revision;

        if (targetId > 0)
        {
            var target = GetOrAddCombatant(targetId);
            target.IncomingDamage += damage;
            target.IncomingHits += hitCount;
            target.IncomingAttempts += attemptCount;
            target.Revision = _revision;
        }

        if (!_outgoingBySource.TryGetValue(sourceId, out var outgoing))
        {
            outgoing = [];
            _outgoingBySource[sourceId] = outgoing;
        }
        outgoing.Add(pairKey);

        if (targetId > 0)
        {
            if (!_incomingByTarget.TryGetValue(targetId, out var incoming))
            {
                incoming = [];
                _incomingByTarget[targetId] = incoming;
            }
            incoming.Add(pairKey);
        }
    }

    public bool TryGetPair(int sourceId, int targetId, out CombatPairRecord? pair) =>
        _pairs.TryGetValue((sourceId, targetId), out pair);

    public bool TryGetCombatant(int combatantId, out CombatantRecord? combatant) =>
        _combatants.TryGetValue(combatantId, out combatant);

    public IReadOnlyCollection<(int, int)> GetOutgoingPairs(int sourceId) =>
        _outgoingBySource.TryGetValue(sourceId, out var pairs) ? pairs : [];

    public IReadOnlyCollection<(int, int)> GetIncomingPairs(int targetId) =>
        _incomingByTarget.TryGetValue(targetId, out var pairs) ? pairs : [];

    private CombatantRecord GetOrAddCombatant(int combatantId)
    {
        ref var record = ref CollectionsMarshal.GetValueRefOrAddDefault(_combatants, combatantId, out var exists);

        if (!exists)
        {
            record = new CombatantRecord { CombatantId = combatantId };
        }

        return record!;
    }

    public void Clear()
    {
        _pairs.Clear();
        _combatants.Clear();
        _outgoingBySource.Clear();
        _incomingByTarget.Clear();
        _revision = 0;
    }
}

public sealed class CombatPairRecord
{
    public int SourceId { get; init; }
    public int TargetId { get; init; }
    public long TotalDamage { get; set; }
    public int HitCount { get; set; }
    public int AttemptCount { get; set; }
    public int LastSkillCode { get; set; }
    public long Revision { get; set; }
}

public sealed class CombatantRecord
{
    public int CombatantId { get; init; }
    public long OutgoingDamage { get; set; }
    public int OutgoingHits { get; set; }
    public int OutgoingAttempts { get; set; }
    public long IncomingDamage { get; set; }
    public int IncomingHits { get; set; }
    public int IncomingAttempts { get; set; }
    public long Revision { get; set; }
}
