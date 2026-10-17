using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public sealed class MechanicStore(int eventCapacity = 0, int pairCapacity = 0, int combatantCapacity = 0) : ISnapshotChangeFeed<CombatSnapshotChange>
{
    private EventJournal<CombatMechanicEventRecord> _events = new(eventCapacity);
    private readonly Dictionary<(int Source, int Target), CombatMechanicPairRecord> _pairs = pairCapacity > 0 ? new Dictionary<(int, int), CombatMechanicPairRecord>(pairCapacity) : [];
    private readonly Dictionary<int, CombatantMechanicRecord> _combatants = combatantCapacity > 0 ? new Dictionary<int, CombatantMechanicRecord>(combatantCapacity) : [];
    private readonly CombatPairIndex _pairIndex = new(combatantCapacity);
    private readonly CombatSnapshotChangeLog _changeLog = new(ResolveChangeLogCapacity(eventCapacity));
    private readonly Dictionary<int, long> _detailRevisionByCombatant = combatantCapacity > 0 ? new(combatantCapacity) : [];
    private long _revision;

    public IReadOnlyList<CombatMechanicEventRecord> Events => _events;
    public IReadOnlyDictionary<(int Source, int Target), CombatMechanicPairRecord> Pairs => _pairs;
    public IReadOnlyDictionary<int, CombatantMechanicRecord> Combatants => _combatants;
    public long Revision => _revision;

    internal EventSegment<CombatMechanicEventRecord> FreezeEventSegment() => _events.Freeze();

    public void EnsureCapacity(int eventCapacity, int pairCapacity = 0, int combatantCapacity = 0)
    {
        if (eventCapacity > 0)
        {
            _events.EnsureCapacity(eventCapacity);
            _changeLog.EnsureCapacity(ResolveChangeLogCapacity(eventCapacity));
        }
        if (pairCapacity > 0)
            _pairs.EnsureCapacity(pairCapacity);
        if (combatantCapacity > 0)
        {
            _combatants.EnsureCapacity(combatantCapacity);
            _pairIndex.EnsureCapacity(combatantCapacity);
            _detailRevisionByCombatant.EnsureCapacity(combatantCapacity);
        }
    }

    public void Apply(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatMechanicOccurrence mechanic,
        long observedAtMilliseconds,
        long sourceObservationOrdinal,
        RawPacketReference raw)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observedAtMilliseconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceObservationOrdinal, CombatStore.UnknownSourceObservationOrdinal);
        if (!mechanic.HasFacts)
            throw new ArgumentException("A mechanic occurrence must contain at least one packet-backed fact.", nameof(mechanic));

        var revision = ++_revision;
        _events.Append(new CombatMechanicEventRecord(
            sourceId,
            targetId,
            observation,
            mechanic,
            CombatEventKey.FromObservation(in observation),
            observedAtMilliseconds,
            sourceObservationOrdinal,
            raw,
            revision));

        var pairKey = (sourceId, targetId);
        ref var pair = ref CollectionsMarshal.GetValueRefOrAddDefault(_pairs, pairKey, out var pairExists);
        if (!pairExists)
        {
            pair = new CombatMechanicPairRecord(sourceId, targetId, observedAtMilliseconds);
            _pairIndex.Add(pairKey);
        }
        pair!.Apply(in mechanic, observation.SkillCode, observedAtMilliseconds, revision);
        _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.PairUpdated, sourceId, pairKey, revision));
        MarkDetailRevision(sourceId, revision);
        MarkDetailRevision(targetId, revision);

        if (sourceId > 0)
        {
            GetOrAddCombatant(sourceId, observedAtMilliseconds).ApplyOutgoing(in mechanic, observedAtMilliseconds, revision);
            _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.CombatantUpdated, sourceId, default, revision));
        }
        if (targetId > 0)
        {
            GetOrAddCombatant(targetId, observedAtMilliseconds).ApplyIncoming(in mechanic, observedAtMilliseconds, revision);
            _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.CombatantUpdated, targetId, default, revision));
        }
    }

    public bool TryGetPair(int sourceId, int targetId, out CombatMechanicPairRecord? pair) => _pairs.TryGetValue((sourceId, targetId), out pair);

    public bool TryGetCombatant(int combatantId, out CombatantMechanicRecord? combatant) => _combatants.TryGetValue(combatantId, out combatant);

    public IReadOnlyCollection<(int Source, int Target)> GetOutgoingPairs(int sourceId) =>
        _pairIndex.GetOutgoing(sourceId) ?? [];

    public IReadOnlyCollection<(int Source, int Target)> GetIncomingPairs(int targetId) =>
        _pairIndex.GetIncoming(targetId) ?? [];

    public bool TryGetLastCombatActivityObservedAt(int combatantId, out long observedAtMilliseconds)
        => TryGetLastCombatActivityObservedAt(combatantId, static _ => true, out observedAtMilliseconds);

    public bool TryGetLastCombatActivityObservedAt(int combatantId, Func<int, bool> includeCounterpart, out long observedAtMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(includeCounterpart);
        observedAtMilliseconds = 0;
        if (combatantId <= 0)
            return false;

        var hasActivity = TryApplyLastCombatActivityObservedAt(_pairIndex.GetOutgoing(combatantId), includeCounterpart, outgoing: true, ref observedAtMilliseconds);
        hasActivity |= TryApplyLastCombatActivityObservedAt(_pairIndex.GetIncoming(combatantId), includeCounterpart, outgoing: false, ref observedAtMilliseconds);
        return hasActivity;
    }

    public bool TryGetEventByRevision(long revision, out CombatMechanicEventRecord record)
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
        _events = new EventJournal<CombatMechanicEventRecord>();
        _pairs.Clear();
        _combatants.Clear();
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

    private static int ResolveChangeLogCapacity(int eventCapacity) =>
        eventCapacity > int.MaxValue / 3 ? int.MaxValue : eventCapacity * 3;

    private void MarkDetailRevision(int combatantId, long revision)
    {
        if (combatantId <= 0)
            return;

        ref var current = ref CollectionsMarshal.GetValueRefOrAddDefault(_detailRevisionByCombatant, combatantId, out var exists);
        current = exists ? Math.Max(current, revision) : revision;
    }

    private bool TryApplyLastCombatActivityObservedAt(
        HashSet<(int Source, int Target)>? pairs,
        Func<int, bool> includeCounterpart,
        bool outgoing,
        ref long observedAtMilliseconds)
    {
        if (pairs is null)
            return false;

        var hasActivity = false;
        foreach (var key in pairs)
        {
            var counterpartId = outgoing ? key.Target : key.Source;
            if (includeCounterpart(counterpartId) && _pairs.TryGetValue(key, out var pair) && HasCombatActivity(pair))
            {
                observedAtMilliseconds = Math.Max(observedAtMilliseconds, pair.LastObserved);
                hasActivity = true;
            }
        }

        return hasActivity;
    }

    private static bool HasCombatActivity(CombatMechanicPairRecord pair) =>
        pair.Modifiers != DamageModifiers.None ||
        pair.HitCount > 0 ||
        pair.AttemptCount > 0 ||
        pair.EvadeCount > 0 ||
        pair.InvincibleCount > 0 ||
        pair.MultiHitCount > 0 ||
        pair.MultiHitSubCount > 0;

    private CombatantMechanicRecord GetOrAddCombatant(int combatantId, long observedAtMilliseconds)
    {
        ref var combatant = ref CollectionsMarshal.GetValueRefOrAddDefault(_combatants, combatantId, out var exists);
        if (!exists)
            combatant = new CombatantMechanicRecord(combatantId, observedAtMilliseconds);
        return combatant!;
    }
}

public readonly record struct CombatMechanicEventRecord(
    int SourceId,
    int TargetId,
    CombatWireObservation Observation,
    CombatMechanicOccurrence Mechanic,
    CombatEventKey EventKey,
    long ObservedAtMilliseconds,
    long SourceObservationOrdinal,
    RawPacketReference Raw,
    long Revision);

public sealed class CombatMechanicPairRecord(int sourceId, int targetId, long observedAtMilliseconds)
{
    public int SourceId { get; } = sourceId;
    public int TargetId { get; } = targetId;
    public DamageModifiers Modifiers { get; private set; }
    public int HitCount { get; private set; }
    public int AttemptCount { get; private set; }
    public int EvadeCount { get; private set; }
    public int InvincibleCount { get; private set; }
    public int MultiHitCount { get; private set; }
    public int MultiHitSubCount { get; private set; }
    public int LastSkillCode { get; private set; }
    public long FirstObserved { get; private set; } = observedAtMilliseconds;
    public long LastObserved { get; private set; } = observedAtMilliseconds;
    public long Revision { get; private set; }

    internal void Apply(in CombatMechanicOccurrence mechanic, int skillCode, long observedAtMilliseconds, long revision)
    {
        Modifiers |= mechanic.Modifiers;
        HitCount += mechanic.HitCount;
        AttemptCount += mechanic.AttemptCount;
        EvadeCount += mechanic.EvadeCount;
        InvincibleCount += mechanic.InvincibleCount;
        MultiHitCount += mechanic.MultiHitCount;
        MultiHitSubCount += mechanic.MultiHitSubCount;
        LastSkillCode = skillCode;
        FirstObserved = Math.Min(FirstObserved, observedAtMilliseconds);
        LastObserved = Math.Max(LastObserved, observedAtMilliseconds);
        Revision = revision;
    }

}

public sealed class CombatantMechanicRecord(int combatantId, long observedAtMilliseconds)
{
    public int CombatantId { get; } = combatantId;
    public int OutgoingHits { get; private set; }
    public int OutgoingAttempts { get; private set; }
    public int OutgoingEvades { get; private set; }
    public int OutgoingInvincibles { get; private set; }
    public int OutgoingMultiHits { get; private set; }
    public int IncomingHits { get; private set; }
    public int IncomingAttempts { get; private set; }
    public int IncomingEvades { get; private set; }
    public int IncomingInvincibles { get; private set; }
    public int IncomingMultiHits { get; private set; }
    public long FirstObserved { get; private set; } = observedAtMilliseconds;
    public long LastObserved { get; private set; } = observedAtMilliseconds;
    public long Revision { get; private set; }

    internal void ApplyOutgoing(in CombatMechanicOccurrence mechanic, long observedAtMilliseconds, long revision)
    {
        OutgoingHits += mechanic.HitCount;
        OutgoingAttempts += mechanic.AttemptCount;
        OutgoingEvades += mechanic.EvadeCount;
        OutgoingInvincibles += mechanic.InvincibleCount;
        OutgoingMultiHits += mechanic.MultiHitCount;
        ApplyObserved(observedAtMilliseconds, revision);
    }

    internal void ApplyIncoming(in CombatMechanicOccurrence mechanic, long observedAtMilliseconds, long revision)
    {
        IncomingHits += mechanic.HitCount;
        IncomingAttempts += mechanic.AttemptCount;
        IncomingEvades += mechanic.EvadeCount;
        IncomingInvincibles += mechanic.InvincibleCount;
        IncomingMultiHits += mechanic.MultiHitCount;
        ApplyObserved(observedAtMilliseconds, revision);
    }

    private void ApplyObserved(long observedAtMilliseconds, long revision)
    {
        FirstObserved = Math.Min(FirstObserved, observedAtMilliseconds);
        LastObserved = Math.Max(LastObserved, observedAtMilliseconds);
        Revision = revision;
    }
}
