using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public sealed class CombatStore(int eventCapacity = 0, int combatantCapacity = 0, int pairCapacity = 0) : ISnapshotChangeFeed<CombatSnapshotChange>
{
    public const long UnknownSourceObservationOrdinal = -1;

    private readonly Dictionary<(int Source, int Target), CombatPairRecord> _pairs = pairCapacity > 0 ? new(pairCapacity) : [];
    private readonly Dictionary<int, CombatantRecord> _combatants = combatantCapacity > 0 ? new(combatantCapacity) : [];
    private readonly CombatPairIndex _pairIndex = new(combatantCapacity);
    private EventJournal<CombatEventRecord> _events = new(eventCapacity);
    private readonly CombatSnapshotChangeLog _changeLog = new(ResolveChangeCapacity(eventCapacity));
    private readonly Dictionary<int, long> _detailRevisionByCombatant = combatantCapacity > 0 ? new(combatantCapacity) : [];
    private long _revision;

    public IReadOnlyDictionary<(int Source, int Target), CombatPairRecord> Pairs => _pairs;
    public IReadOnlyDictionary<int, CombatantRecord> Combatants => _combatants;
    public IReadOnlyList<CombatEventRecord> Events => _events;
    public EventRange<CombatEventRecord> EventSpan => _events.AsRange();
    public long Revision => _revision;

    public void EnsureCapacity(int eventCapacity, int combatantCapacity = 0, int pairCapacity = 0)
    {
        if (eventCapacity > 0)
        {
            _events.EnsureCapacity(eventCapacity);
            _changeLog.EnsureCapacity(ResolveChangeCapacity(eventCapacity));
        }

        if (combatantCapacity > 0)
        {
            _combatants.EnsureCapacity(combatantCapacity);
            _pairIndex.EnsureCapacity(combatantCapacity);
            _detailRevisionByCombatant.EnsureCapacity(combatantCapacity);
        }

        if (pairCapacity > 0)
        {
            _pairs.EnsureCapacity(pairCapacity);
        }
    }

    public void ApplyCombat(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatContribution contribution,
        long observedAtMilliseconds,
        long sourceObservationOrdinal = UnknownSourceObservationOrdinal,
        RawPacketReference raw = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observedAtMilliseconds);
        ValidateSourceObservationOrdinal(sourceObservationOrdinal);
        if (contribution.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(contribution), contribution.Amount, "Combat contribution amount must be positive.");
        AppendCombatEvent(sourceId, targetId, observedAtMilliseconds, sourceObservationOrdinal, raw, in observation, in contribution);
    }

    private static void ValidateSourceObservationOrdinal(long sourceObservationOrdinal)
    {
        if (sourceObservationOrdinal < UnknownSourceObservationOrdinal)
            throw new ArgumentOutOfRangeException(nameof(sourceObservationOrdinal), sourceObservationOrdinal, "Source observation ordinal must be -1 or greater.");
    }

    private void AppendCombatEvent(int sourceId, int targetId, long observedAtMilliseconds, long sourceObservationOrdinal, RawPacketReference raw, in CombatWireObservation observation, in CombatContribution contribution)
    {
        _revision++;
        var eventKey = CombatEventKey.FromObservation(in observation);
        var eventRecord = new CombatEventRecord
        {
            SourceId = sourceId,
            TargetId = targetId,
            Observation = observation,
            Raw = raw,
            ObservedAtMilliseconds = observedAtMilliseconds,
            SourceObservationOrdinal = sourceObservationOrdinal,
            Revision = _revision,
            EventKey = eventKey,
            Contribution = contribution
        };
        _events.Append(in eventRecord);
        var damage = contribution.Metric == CombatMetricKind.Damage ? contribution.Amount : 0;
        var totalHealing = contribution.Metric == CombatMetricKind.Healing ? contribution.Amount : 0;
        var shieldGranted = contribution.Metric == CombatMetricKind.ShieldGranted ? contribution.Amount : 0;
        var shieldAbsorbed = contribution.Metric == CombatMetricKind.ShieldAbsorbed ? contribution.Amount : 0;
        var shieldGrantCount = contribution.Metric == CombatMetricKind.ShieldGranted ? 1 : 0;
        var shieldAbsorbedCount = contribution.Metric == CombatMetricKind.ShieldAbsorbed ? 1 : 0;
        var periodicHealing = contribution.Metric == CombatMetricKind.Healing && contribution.Delivery == CombatDeliveryKind.Periodic ? contribution.Amount : 0;
        var drainDamage = contribution.Metric == CombatMetricKind.Damage && contribution.Delivery == CombatDeliveryKind.Drain ? contribution.Amount : 0;
        var drainHealing = contribution.Metric == CombatMetricKind.Healing && contribution.Delivery == CombatDeliveryKind.Drain ? contribution.Amount : 0;
        var regenerationHealing = contribution.Metric == CombatMetricKind.Healing && contribution.Delivery == CombatDeliveryKind.Regeneration ? contribution.Amount : 0;

        var pairKey = (sourceId, targetId);
        ref var pair = ref CollectionsMarshal.GetValueRefOrAddDefault(_pairs, pairKey, out var pairExists);
        CombatPairRecord pairRecord;
        if (!pairExists)
        {
            pairRecord = new CombatPairRecord
            {
                SourceId = sourceId,
                TargetId = targetId,
                FirstObserved = observedAtMilliseconds,
                LastObserved = observedAtMilliseconds,
                FirstRevision = _revision
            };
            pair = pairRecord;
            _pairIndex.Add(pairKey);
        }
        else if (pair is { } existingPair)
        {
            pairRecord = existingPair;
        }
        else
        {
            throw new InvalidOperationException("Combat pair dictionary returned a null record.");
        }
        pairRecord.TotalDamage += damage;
        pairRecord.TotalHealing += totalHealing;
        pairRecord.TotalPeriodicHealing += periodicHealing;
        pairRecord.TotalDrainDamage += drainDamage;
        pairRecord.TotalDrainHealing += drainHealing;
        pairRecord.TotalRegenerationHealing += regenerationHealing;
        pairRecord.TotalShield += shieldGranted;
        pairRecord.TotalShieldAbsorbed += shieldAbsorbed;
        pairRecord.ShieldCount += shieldGrantCount;
        pairRecord.ShieldAbsorbedCount += shieldAbsorbedCount;
        pairRecord.LastSkillCode = observation.SkillCode;
        pairRecord.FirstRevision = pairRecord.FirstRevision > 0 ? Math.Min(pairRecord.FirstRevision, _revision) : _revision;
        pairRecord.Revision = _revision;
        ApplyObservedAt(pairRecord, observedAtMilliseconds);
        _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.PairUpdated, sourceId, pairKey, _revision));
        MarkDetailRevision(sourceId, _revision);
        MarkDetailRevision(targetId, _revision);

        var source = GetOrAddCombatant(sourceId, observedAtMilliseconds);
        source.OutgoingDamage += damage;
        source.OutgoingHealing += totalHealing;
        source.OutgoingShield += shieldGranted;
        source.OutgoingShieldAbsorbed += shieldAbsorbed;
        source.OutgoingShieldCount += shieldGrantCount;
        source.OutgoingShieldAbsorbedCount += shieldAbsorbedCount;
        source.Revision = _revision;
        ApplyObservedAt(source, observedAtMilliseconds);
        _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.CombatantUpdated, sourceId, default, _revision));

        if (targetId > 0)
        {
            var target = GetOrAddCombatant(targetId, observedAtMilliseconds);
            target.IncomingDamage += damage;
            target.IncomingHealing += totalHealing;
            target.IncomingShield += shieldGranted;
            target.IncomingShieldAbsorbed += shieldAbsorbed;
            target.IncomingShieldCount += shieldGrantCount;
            target.IncomingShieldAbsorbedCount += shieldAbsorbedCount;
            target.Revision = _revision;
            ApplyObservedAt(target, observedAtMilliseconds);
            _changeLog.Add(new CombatSnapshotChange(CombatSnapshotChangeKind.CombatantUpdated, targetId, default, _revision));
        }

    }

    private static void ApplyObservedAt(CombatPairRecord record, long observedAtMilliseconds)
    {
        record.FirstObserved = Math.Min(record.FirstObserved, observedAtMilliseconds);
        record.LastObserved = Math.Max(record.LastObserved, observedAtMilliseconds);
    }

    private static void ApplyObservedAt(CombatantRecord record, long observedAtMilliseconds)
    {
        record.FirstObserved = Math.Min(record.FirstObserved, observedAtMilliseconds);
        record.LastObserved = Math.Max(record.LastObserved, observedAtMilliseconds);
    }

    public bool TryGetPair(int sourceId, int targetId, out CombatPairRecord? pair) => _pairs.TryGetValue((sourceId, targetId), out pair);

    public bool TryGetCombatant(int combatantId, out CombatantRecord? combatant) => _combatants.TryGetValue(combatantId, out combatant);

    public IReadOnlyCollection<(int Source, int Target)> GetOutgoingPairs(int sourceId) =>
        _pairIndex.GetOutgoing(sourceId) ?? [];

    public IReadOnlyCollection<(int Source, int Target)> GetIncomingPairs(int targetId) =>
        _pairIndex.GetIncoming(targetId) ?? [];

    public bool TryGetLastCombatActivityObservedAt(int combatantId, out long observedAtMilliseconds) => TryGetLastCombatActivityObservedAt(combatantId, static _ => true, out observedAtMilliseconds);

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

    public ref readonly CombatEventRecord GetEvent(int index) => ref _events.GetEvent(index);

    internal EventSegment<CombatEventRecord> FreezeEventSegment() => _events.Freeze();

    public bool TryGetEventByRevision(long revision, out CombatEventRecord record)
    {
        if (revision > 0 && _events.Count > 0)
        {
            var firstRevision = _events[0].Revision;
            var index = revision - firstRevision;
            if ((ulong)index < (uint)_events.Count)
            {
                var candidate = _events[(int)index];
                if (candidate.Revision == revision)
                {
                    record = candidate;
                    return true;
                }
            }
        }

        record = default;
        return false;
    }

    public long GetCombatantDetailRevision(int combatantId) => combatantId > 0 && _detailRevisionByCombatant.TryGetValue(combatantId, out var revision) ? revision : 0;

    private CombatantRecord GetOrAddCombatant(int combatantId, long observedAtMilliseconds)
    {
        ref var record = ref CollectionsMarshal.GetValueRefOrAddDefault(_combatants, combatantId, out var exists);

        if (!exists)
        {
            record = new CombatantRecord
            {
                CombatantId = combatantId,
                FirstObserved = observedAtMilliseconds,
                LastObserved = observedAtMilliseconds
            };
        }

        return record!;
    }

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

    private static bool HasCombatActivity(CombatPairRecord pair) =>
        pair.TotalDamage > 0 ||
        pair.TotalHealing > 0 ||
        pair.TotalShield > 0 ||
        pair.TotalShieldAbsorbed > 0;

    public void Clear()
    {
        _pairs.Clear();
        _combatants.Clear();
        _pairIndex.Clear();
        _events = new EventJournal<CombatEventRecord>();
        _changeLog.Clear();
        _detailRevisionByCombatant.Clear();
        _revision = 0;
    }

    public SnapshotChangeCursor CreateCursor(long afterRevision) => new(afterRevision, 0);

    public SnapshotChangeBatch<CombatSnapshotChange> ReadChanges(SnapshotChangeCursor cursor, int maxChanges)
        => _changeLog.ReadChanges(cursor, maxChanges);

    public SnapshotChangeCopyResult CopyChanges(SnapshotChangeCursor cursor, Span<CombatSnapshotChange> destination)
        => _changeLog.CopyChanges(cursor, destination);

    private static int ResolveChangeCapacity(int eventCapacity) => eventCapacity <= 0 ? 0 : eventCapacity <= int.MaxValue / 3 ? eventCapacity * 3 : int.MaxValue;

}

public sealed class CombatPairRecord
{
    public int SourceId { get; init; }
    public int TargetId { get; init; }
    public long TotalDamage { get; set; }
    public long TotalHealing { get; set; }
    public long TotalPeriodicHealing { get; set; }
    public long TotalDrainDamage { get; set; }
    public long TotalDrainHealing { get; set; }
    public long TotalRegenerationHealing { get; set; }
    public long TotalShield { get; set; }
    public long TotalShieldAbsorbed { get; set; }
    public int ShieldCount { get; set; }
    public int ShieldAbsorbedCount { get; set; }
    public int LastSkillCode { get; set; }
    public long FirstObserved { get; set; }
    public long LastObserved { get; set; }
    public long FirstRevision { get; set; }
    public long Revision { get; set; }

}

public readonly record struct CombatEventRecord
{
    public int SourceId { get; init; }
    public int TargetId { get; init; }
    public CombatWireObservation Observation { get; init; }
    public RawPacketReference Raw { get; init; }
    public long ObservedAtMilliseconds { get; init; }
    public long SourceObservationOrdinal { get; init; }
    public long Revision { get; init; }
    public CombatEventKey EventKey { get; init; }
    public CombatContribution Contribution { get; init; }
    public bool ContributesDamage => Contribution.Metric == CombatMetricKind.Damage;
    public bool ContributesHealing => Contribution.Metric == CombatMetricKind.Healing;
    public bool ContributesShieldGrant => Contribution.Metric == CombatMetricKind.ShieldGranted;
    public bool ContributesShieldAbsorbed => Contribution.Metric == CombatMetricKind.ShieldAbsorbed;
}

public sealed class CombatantRecord
{
    public int CombatantId { get; init; }
    public long OutgoingDamage { get; set; }
    public long IncomingDamage { get; set; }
    public long OutgoingHealing { get; set; }
    public long IncomingHealing { get; set; }
    public long OutgoingShield { get; set; }
    public long IncomingShield { get; set; }
    public long OutgoingShieldAbsorbed { get; set; }
    public long IncomingShieldAbsorbed { get; set; }
    public int OutgoingShieldCount { get; set; }
    public int IncomingShieldCount { get; set; }
    public int OutgoingShieldAbsorbedCount { get; set; }
    public int IncomingShieldAbsorbedCount { get; set; }
    public long FirstObserved { get; set; }
    public long LastObserved { get; set; }
    public long Revision { get; set; }
}
