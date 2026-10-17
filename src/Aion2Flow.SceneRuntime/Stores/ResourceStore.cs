using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public sealed class ResourceStore(int eventCapacity = 0, int pairCapacity = 0, int combatantCapacity = 0) : ISnapshotChangeFeed<CombatSnapshotChange>
{
    private EventJournal<CombatResourceEventRecord> _events = new(eventCapacity);
    private readonly Dictionary<(int Source, int Target), CombatResourcePairRecord> _pairs = pairCapacity > 0 ? new Dictionary<(int, int), CombatResourcePairRecord>(pairCapacity) : [];
    private readonly CombatPairIndex _pairIndex = new(combatantCapacity);
    private readonly CombatSnapshotChangeLog _changeLog = new(eventCapacity);
    private readonly Dictionary<int, long> _detailRevisionByCombatant = combatantCapacity > 0 ? new(combatantCapacity) : [];
    private long _revision;

    public IReadOnlyList<CombatResourceEventRecord> Events => _events;
    public IReadOnlyDictionary<(int Source, int Target), CombatResourcePairRecord> Pairs => _pairs;
    public long Revision => _revision;

    internal EventSegment<CombatResourceEventRecord> FreezeEventSegment() => _events.Freeze();

    public void EnsureCapacity(int eventCapacity, int pairCapacity = 0, int combatantCapacity = 0)
    {
        if (eventCapacity > 0)
        {
            _events.EnsureCapacity(eventCapacity);
            _changeLog.EnsureCapacity(eventCapacity);
        }
        if (pairCapacity > 0)
            _pairs.EnsureCapacity(pairCapacity);
        if (combatantCapacity > 0)
        {
            _pairIndex.EnsureCapacity(combatantCapacity);
            _detailRevisionByCombatant.EnsureCapacity(combatantCapacity);
        }
    }

    public void Apply(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatResourceOccurrence resource,
        long observedAtMilliseconds,
        long sourceObservationOrdinal,
        RawPacketReference raw)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observedAtMilliseconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceObservationOrdinal, CombatStore.UnknownSourceObservationOrdinal);
        if (resource.Resource == CombatResourceKind.Unknown)
            throw new ArgumentException("A resource occurrence must identify a packet-backed resource kind.", nameof(resource));

        var revision = ++_revision;
        _events.Append(new CombatResourceEventRecord(
            sourceId,
            targetId,
            observation,
            resource,
            CombatEventKey.FromObservation(in observation),
            observedAtMilliseconds,
            sourceObservationOrdinal,
            raw,
            revision));

        var pairKey = (sourceId, targetId);
        ref var pair = ref CollectionsMarshal.GetValueRefOrAddDefault(_pairs, pairKey, out var exists);
        if (!exists)
        {
            pair = new CombatResourcePairRecord(sourceId, targetId, observedAtMilliseconds);
            _pairIndex.Add(pairKey);
        }
        pair!.Apply(in resource, observation.SkillCode, observedAtMilliseconds, revision);
        _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.PairUpdated, sourceId, pairKey, revision));
        MarkDetailRevision(sourceId, revision);
        MarkDetailRevision(targetId, revision);
    }

    public bool TryGetPair(int sourceId, int targetId, out CombatResourcePairRecord? pair) => _pairs.TryGetValue((sourceId, targetId), out pair);

    public IReadOnlyCollection<(int Source, int Target)> GetOutgoingPairs(int sourceId) =>
        _pairIndex.GetOutgoing(sourceId) ?? [];

    public IReadOnlyCollection<(int Source, int Target)> GetIncomingPairs(int targetId) =>
        _pairIndex.GetIncoming(targetId) ?? [];

    public bool TryGetEventByRevision(long revision, out CombatResourceEventRecord record)
    {
        var index = revision - 1;
        if ((ulong)index < (uint)_events.Count)
        {
            var candidate = _events[(int)index];
            if (candidate.Revision == revision)
            {
                record = candidate;
                return true;
            }
        }

        record = default;
        return false;
    }

    public long GetCombatantDetailRevision(int combatantId) =>
        combatantId > 0 && _detailRevisionByCombatant.TryGetValue(combatantId, out var revision) ? revision : 0;

    public void Clear()
    {
        _events = new EventJournal<CombatResourceEventRecord>();
        _pairs.Clear();
        _pairIndex.Clear();
        _changeLog.Clear();
        _detailRevisionByCombatant.Clear();
        _revision = 0;
    }

    public SnapshotChangeCursor CreateCursor(long afterRevision) => new(afterRevision, 0);

    public SnapshotChangeBatch<CombatSnapshotChange> ReadChanges(SnapshotChangeCursor cursor, int maxChanges)
        => _changeLog.ReadChanges(cursor, maxChanges);

    public SnapshotChangeCopyResult CopyChanges(SnapshotChangeCursor cursor, Span<CombatSnapshotChange> destination)
        => _changeLog.CopyChanges(cursor, destination);

    private void MarkDetailRevision(int combatantId, long revision)
    {
        if (combatantId <= 0)
            return;

        ref var current = ref CollectionsMarshal.GetValueRefOrAddDefault(_detailRevisionByCombatant, combatantId, out var exists);
        current = exists ? Math.Max(current, revision) : revision;
    }

}

public readonly record struct CombatResourceEventRecord(
    int SourceId,
    int TargetId,
    CombatWireObservation Observation,
    CombatResourceOccurrence Resource,
    CombatEventKey EventKey,
    long ObservedAtMilliseconds,
    long SourceObservationOrdinal,
    RawPacketReference Raw,
    long Revision);

public sealed class CombatResourcePairRecord(int sourceId, int targetId, long observedAtMilliseconds)
{
    public int SourceId { get; } = sourceId;
    public int TargetId { get; } = targetId;
    public long HealthRestored { get; private set; }
    public long HealthUnknown { get; private set; }
    public long ManaRestored { get; private set; }
    public long ManaSpent { get; private set; }
    public long ManaUnknown { get; private set; }
    public int LastSkillCode { get; private set; }
    public long FirstObserved { get; private set; } = observedAtMilliseconds;
    public long LastObserved { get; private set; } = observedAtMilliseconds;
    public long Revision { get; private set; }

    internal void Apply(in CombatResourceOccurrence resource, int skillCode, long observedAtMilliseconds, long revision)
    {
        switch (resource.Resource, resource.Flow)
        {
            case (CombatResourceKind.Health, CombatResourceFlowKind.Restore):
                HealthRestored += resource.Amount;
                break;
            case (CombatResourceKind.Health, _):
                HealthUnknown += resource.Amount;
                break;
            case (CombatResourceKind.Mana, CombatResourceFlowKind.Restore):
                ManaRestored += resource.Amount;
                break;
            case (CombatResourceKind.Mana, CombatResourceFlowKind.Spend):
                ManaSpent += resource.Amount;
                break;
            case (CombatResourceKind.Mana, _):
                ManaUnknown += resource.Amount;
                break;
        }
        LastSkillCode = skillCode;
        FirstObserved = Math.Min(FirstObserved, observedAtMilliseconds);
        LastObserved = Math.Max(LastObserved, observedAtMilliseconds);
        Revision = revision;
    }

}
