using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.Scene.Stores;

public sealed class CombatStore : ISnapshotChangeFeed<CombatSnapshotChange>
{
    private readonly Dictionary<(int Source, int Target), CombatPairRecord> _pairs = [];
    private readonly Dictionary<int, CombatantRecord> _combatants = [];
    private readonly Dictionary<int, HashSet<(int, int)>> _outgoingBySource = [];
    private readonly Dictionary<int, HashSet<(int, int)>> _incomingByTarget = [];
    private readonly List<CombatSnapshotChange> _changeLog = [];
    private readonly Dictionary<int, long> _detailRevisionByCombatant = [];
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
        _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.PairUpdated, sourceId, pairKey, _revision));
        MarkDetailRevision(sourceId, _revision);
        MarkDetailRevision(targetId, _revision);

        var source = GetOrAddCombatant(sourceId);
        source.OutgoingDamage += damage;
        source.OutgoingHits += hitCount;
        source.OutgoingAttempts += attemptCount;
        source.Revision = _revision;
        _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.CombatantUpdated, sourceId, default, _revision));

        if (targetId > 0)
        {
            var target = GetOrAddCombatant(targetId);
            target.IncomingDamage += damage;
            target.IncomingHits += hitCount;
            target.IncomingAttempts += attemptCount;
            target.Revision = _revision;
            _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.CombatantUpdated, targetId, default, _revision));
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

    public long GetCombatantDetailRevision(int combatantId) =>
        combatantId > 0 && _detailRevisionByCombatant.TryGetValue(combatantId, out var revision) ? revision : 0;

    private CombatantRecord GetOrAddCombatant(int combatantId)
    {
        ref var record = ref CollectionsMarshal.GetValueRefOrAddDefault(_combatants, combatantId, out var exists);

        if (!exists)
        {
            record = new CombatantRecord { CombatantId = combatantId };
        }

        return record!;
    }

    private void MarkDetailRevision(int combatantId, long revision)
    {
        if (combatantId <= 0)
            return;

        _detailRevisionByCombatant[combatantId] = _detailRevisionByCombatant.TryGetValue(combatantId, out var current)
            ? Math.Max(current, revision)
            : revision;
    }

    public void Clear()
    {
        _pairs.Clear();
        _combatants.Clear();
        _outgoingBySource.Clear();
        _incomingByTarget.Clear();
        _changeLog.Clear();
        _detailRevisionByCombatant.Clear();
        _revision = 0;
    }

    public SnapshotChangeCursor CreateCursor(long afterRevision) =>
        new(afterRevision, 0);

    public SnapshotChangeBatch<CombatSnapshotChange> ReadChanges(SnapshotChangeCursor cursor, int maxChanges)
    {
        maxChanges = Math.Max(1, maxChanges);

        int lo = 0, hi = _changeLog.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_changeLog[mid].Revision <= cursor.Revision)
                lo = mid + 1;
            else
                hi = mid;
        }

        int start = lo + cursor.Offset;
        if (start >= _changeLog.Count)
            return new SnapshotChangeBatch<CombatSnapshotChange>(cursor.Revision, _revision, [], false);

        int count = Math.Min(maxChanges, _changeLog.Count - start);
        if (count < _changeLog.Count - start)
        {
            var lastRevision = _changeLog[start + count - 1].Revision;
            if (_changeLog[start + count].Revision == lastRevision)
            {
                while (count > 0 && _changeLog[start + count - 1].Revision == lastRevision)
                    count--;
                if (count == 0)
                {
                    count = 1;
                    while (start + count < _changeLog.Count && _changeLog[start + count].Revision == lastRevision)
                        count++;
                }
            }
        }
        var changes = _changeLog.GetRange(start, count);
        return new SnapshotChangeBatch<CombatSnapshotChange>(
            cursor.Revision,
            changes[^1].Revision,
            changes,
            start + count < _changeLog.Count);
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
