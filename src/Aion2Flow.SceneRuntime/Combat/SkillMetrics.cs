using System.Collections;
using Cloris.Aion2Flow.Protocol.Combat;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public struct SkillMetrics(CombatEventKey eventKey)
{
    public CombatEventKey EventKey { get; private set; } = eventKey;
    public int SkillCode { get; private set; } = eventKey.SkillCode;
    public long DamageAmount { get; set; }
    public long PeriodicDamageAmount { get; set; }
    public int PeriodicDamageTimes { get; set; }
    public long HealingAmount { get; set; }
    public int HealingTimes { get; set; }
    public long PeriodicHealingAmount { get; set; }
    public int PeriodicHealingTimes { get; set; }
    public long DrainDamageAmount { get; set; }
    public int DrainDamageTimes { get; set; }
    public long DrainHealingAmount { get; set; }
    public int DrainHealingTimes { get; set; }
    public long RegenerationHealingAmount { get; set; }
    public int RegenerationHealingTimes { get; set; }
    public long ShieldAmount { get; set; }
    public int ShieldTimes { get; set; }
    public long ShieldAbsorbedAmount { get; set; }
    public int ShieldAbsorbedTimes { get; set; }
    public int CriticalTimes { get; set; }
    public int Times { get; set; }
    public int AttemptTimes { get; set; }
    public int EvadeTimes { get; set; }
    public int InvincibleTimes { get; set; }
    public int MultiHitTimes { get; set; }
    public int FrontTimes { get; set; }
    public int BackTimes { get; set; }
    public int PerfectTimes { get; set; }
    public int SmiteTimes { get; set; }
    public int ParryTimes { get; set; }
    public int BlockTimes { get; set; }
    public int PerfectParryTimes { get; set; }
    public int PerfectBlockTimes { get; set; }
    public int EnduranceTimes { get; set; }
    public int RegenerationTimes { get; set; }

    internal readonly SkillMetricsSnapshot ToSnapshot()
    {
        return new SkillMetricsSnapshot
        {
            EventKey = EventKey,
            SkillCode = SkillCode,
            DamageAmount = DamageAmount,
            PeriodicDamageAmount = PeriodicDamageAmount,
            PeriodicDamageTimes = PeriodicDamageTimes,
            HealingAmount = HealingAmount,
            HealingTimes = HealingTimes,
            PeriodicHealingAmount = PeriodicHealingAmount,
            PeriodicHealingTimes = PeriodicHealingTimes,
            DrainDamageAmount = DrainDamageAmount,
            DrainDamageTimes = DrainDamageTimes,
            DrainHealingAmount = DrainHealingAmount,
            DrainHealingTimes = DrainHealingTimes,
            RegenerationHealingAmount = RegenerationHealingAmount,
            RegenerationHealingTimes = RegenerationHealingTimes,
            ShieldAmount = ShieldAmount,
            ShieldTimes = ShieldTimes,
            ShieldAbsorbedAmount = ShieldAbsorbedAmount,
            ShieldAbsorbedTimes = ShieldAbsorbedTimes,
            CriticalTimes = CriticalTimes,
            Times = Times,
            AttemptTimes = AttemptTimes,
            EvadeTimes = EvadeTimes,
            InvincibleTimes = InvincibleTimes,
            MultiHitTimes = MultiHitTimes,
            FrontTimes = FrontTimes,
            BackTimes = BackTimes,
            PerfectTimes = PerfectTimes,
            SmiteTimes = SmiteTimes,
            ParryTimes = ParryTimes,
            BlockTimes = BlockTimes,
            PerfectParryTimes = PerfectParryTimes,
            PerfectBlockTimes = PerfectBlockTimes,
            EnduranceTimes = EnduranceTimes,
            RegenerationTimes = RegenerationTimes
        };
    }

    public void ProcessMechanic(in CombatMechanicOccurrence mechanic)
    {
        var hitContribution = mechanic.HitCount;

        Times += hitContribution;
        AttemptTimes += mechanic.AttemptCount;
        EvadeTimes += mechanic.EvadeCount;
        InvincibleTimes += mechanic.InvincibleCount;
        MultiHitTimes += mechanic.MultiHitCount;

        var modifiers = mechanic.Modifiers;
        if (hitContribution > 0 && (modifiers & DamageModifiers.Critical) != 0) CriticalTimes += hitContribution;
        if (hitContribution > 0 && (modifiers & DamageModifiers.Front) != 0) FrontTimes += hitContribution;
        if (hitContribution > 0 && (modifiers & DamageModifiers.Back) != 0) BackTimes += hitContribution;
        if (hitContribution > 0 && (modifiers & DamageModifiers.Parry) != 0) ParryTimes += hitContribution;
        if (hitContribution > 0 && (modifiers & DamageModifiers.Smite) != 0) SmiteTimes += hitContribution;
        if (hitContribution > 0 && (modifiers & DamageModifiers.Perfect) != 0) PerfectTimes += hitContribution;
        if (hitContribution > 0 && (modifiers & DamageModifiers.Block) != 0) BlockTimes += hitContribution;
        if (hitContribution > 0 && (modifiers & (DamageModifiers.Parry | DamageModifiers.DefensivePerfect)) == (DamageModifiers.Parry | DamageModifiers.DefensivePerfect)) PerfectParryTimes += hitContribution;
        if (hitContribution > 0 && (modifiers & (DamageModifiers.Block | DamageModifiers.DefensivePerfect)) == (DamageModifiers.Block | DamageModifiers.DefensivePerfect)) PerfectBlockTimes += hitContribution;
        if (hitContribution > 0 && (modifiers & DamageModifiers.Endurance) != 0) EnduranceTimes += hitContribution;
        if (hitContribution > 0 && (modifiers & DamageModifiers.Regeneration) != 0) RegenerationTimes += hitContribution;
    }

    public void ProcessContribution(in CombatContribution contribution)
    {
        switch (contribution.Metric)
        {
            case CombatMetricKind.Damage:
                ProcessDamage(in contribution);
                break;
            case CombatMetricKind.Healing:
                ProcessHealing(in contribution);
                break;
            case CombatMetricKind.ShieldGranted:
                ShieldAmount += contribution.Amount;
                ShieldTimes++;
                break;
            case CombatMetricKind.ShieldAbsorbed:
                ShieldAbsorbedAmount += contribution.Amount;
                ShieldAbsorbedTimes++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(contribution), contribution.Metric, "Combat contribution metric is invalid.");
        }
    }

    private void ProcessDamage(in CombatContribution contribution)
    {
        if (contribution.Delivery == CombatDeliveryKind.Periodic)
        {
            PeriodicDamageAmount += contribution.Amount;
            PeriodicDamageTimes++;
            return;
        }

        DamageAmount += contribution.Amount;
        if (contribution.Delivery == CombatDeliveryKind.Drain)
        {
            DrainDamageAmount += contribution.Amount;
            DrainDamageTimes++;
        }
    }

    private void ProcessHealing(in CombatContribution contribution)
    {
        HealingAmount += contribution.Amount;
        HealingTimes++;
        switch (contribution.Delivery)
        {
            case CombatDeliveryKind.Periodic:
                PeriodicHealingAmount += contribution.Amount;
                PeriodicHealingTimes++;
                break;
            case CombatDeliveryKind.Drain:
                DrainHealingAmount += contribution.Amount;
                DrainHealingTimes++;
                break;
            case CombatDeliveryKind.Regeneration:
                RegenerationHealingAmount += contribution.Amount;
                RegenerationHealingTimes++;
                break;
        }
    }
}

public readonly record struct SkillMetricsSnapshot(
    CombatEventKey EventKey,
    int SkillCode,
    long DamageAmount,
    long PeriodicDamageAmount,
    int PeriodicDamageTimes,
    long HealingAmount,
    int HealingTimes,
    long PeriodicHealingAmount,
    int PeriodicHealingTimes,
    long DrainDamageAmount,
    int DrainDamageTimes,
    long DrainHealingAmount,
    int DrainHealingTimes,
    long RegenerationHealingAmount,
    int RegenerationHealingTimes,
    long ShieldAmount,
    int ShieldTimes,
    long ShieldAbsorbedAmount,
    int ShieldAbsorbedTimes,
    int CriticalTimes,
    int Times,
    int AttemptTimes,
    int EvadeTimes,
    int InvincibleTimes,
    int MultiHitTimes,
    int FrontTimes,
    int BackTimes,
    int PerfectTimes,
    int SmiteTimes,
    int ParryTimes,
    int BlockTimes,
    int PerfectParryTimes,
    int PerfectBlockTimes,
    int EnduranceTimes,
    int RegenerationTimes);

public readonly record struct SkillMetricsSnapshotEntry(CombatEventKey EventKey, SkillMetricsSnapshot Metrics)
{
    public int SkillCode => EventKey.SkillCode;

    public void Deconstruct(out CombatEventKey eventKey, out SkillMetricsSnapshot metrics)
    {
        eventKey = EventKey;
        metrics = Metrics;
    }
}

public readonly struct CombatSkillBreakdownSnapshot
{
    private static readonly SkillMetricsSnapshotEntry[] EmptyEntries = [];

    public static CombatSkillBreakdownSnapshot Empty { get; } = new(EmptyEntries);

    internal CombatSkillBreakdownSnapshot(SkillMetricsSnapshotEntry[] entries)
    {
        Skills = new SkillMetricsSnapshotMap(entries);
    }

    public SkillMetricsSnapshotMap Skills { get; }

    internal static CombatSkillBreakdownSnapshot From(Dictionary<CombatEventKey, SkillMetrics> metrics)
    {
        if (metrics.Count == 0)
        {
            return Empty;
        }

        var entries = new SkillMetricsSnapshotEntry[metrics.Count];
        var index = 0;
        foreach (var (eventKey, skillMetrics) in metrics)
        {
            entries[index++] = new SkillMetricsSnapshotEntry(eventKey, skillMetrics.ToSnapshot());
        }

        Array.Sort(entries, static (left, right) => left.EventKey.CompareTo(right.EventKey));
        return new CombatSkillBreakdownSnapshot(entries);
    }

}

public readonly struct SkillMetricsSnapshotMap : IReadOnlyDictionary<CombatEventKey, SkillMetricsSnapshot>
{
    private readonly SkillMetricsSnapshotEntry[]? _entries;

    internal SkillMetricsSnapshotMap(SkillMetricsSnapshotEntry[] entries)
    {
        _entries = entries;
    }

    public int Count => Entries.Length;

    public KeyCollection Keys => new(_entries);

    public ValueCollection Values => new(_entries);

    IEnumerable<CombatEventKey> IReadOnlyDictionary<CombatEventKey, SkillMetricsSnapshot>.Keys => Keys;

    IEnumerable<SkillMetricsSnapshot> IReadOnlyDictionary<CombatEventKey, SkillMetricsSnapshot>.Values => Values;

    private ReadOnlySpan<SkillMetricsSnapshotEntry> Entries => _entries ?? [];

    public SkillMetricsSnapshot this[CombatEventKey key]
    {
        get
        {
            if (TryGetValue(key, out var value))
            {
                return value;
            }

            throw new KeyNotFoundException($"The skill code '{key}' was not found in the snapshot.");
        }
    }

    public bool ContainsKey(CombatEventKey key)
    {
        return FindIndex(key) >= 0;
    }

    public bool TryGetValue(CombatEventKey key, out SkillMetricsSnapshot value)
    {
        var index = FindIndex(key);
        if (index >= 0)
        {
            value = Entries[index].Metrics;
            return true;
        }

        value = default;
        return false;
    }

    public ReadOnlySpan<SkillMetricsSnapshotEntry> AsSpan()
    {
        return Entries;
    }

    public bool ContainsSkillCode(int skillCode) => TryGetBySkillCode(skillCode, out _);

    public bool TryGetBySkillCode(int skillCode, out SkillMetricsSnapshot value)
    {
        if (skillCode > 0)
        {
            var entries = Entries;
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].SkillCode == skillCode)
                {
                    value = entries[i].Metrics;
                    return true;
                }

                if (entries[i].SkillCode > skillCode)
                    break;
            }
        }

        value = default;
        return false;
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(_entries);
    }

    IEnumerator<KeyValuePair<CombatEventKey, SkillMetricsSnapshot>> IEnumerable<KeyValuePair<CombatEventKey, SkillMetricsSnapshot>>.GetEnumerator()
    {
        var entries = _entries ?? [];
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            yield return new KeyValuePair<CombatEventKey, SkillMetricsSnapshot>(entry.EventKey, entry.Metrics);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<KeyValuePair<CombatEventKey, SkillMetricsSnapshot>>)this).GetEnumerator();
    }

    private int FindIndex(CombatEventKey key)
    {
        var entries = Entries;
        var low = 0;
        var high = entries.Length - 1;

        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var midKey = entries[mid].EventKey;
            var cmp = midKey.CompareTo(key);
            if (cmp == 0)
            {
                return mid;
            }

            if (cmp < 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return -1;
    }

    public struct Enumerator
    {
        private readonly SkillMetricsSnapshotEntry[]? _entries;
        private int _index;

        internal Enumerator(SkillMetricsSnapshotEntry[]? entries)
        {
            _entries = entries;
            _index = -1;
            Current = default;
        }

        public SkillMetricsSnapshotEntry Current { get; private set; }

        public bool MoveNext()
        {
            var entries = _entries;
            var next = _index + 1;
            if (entries is null || (uint)next >= (uint)entries.Length)
            {
                return false;
            }

            _index = next;
            Current = entries[next];
            return true;
        }
    }

    public readonly struct KeyCollection : IReadOnlyCollection<CombatEventKey>
    {
        private readonly SkillMetricsSnapshotEntry[]? _entries;

        internal KeyCollection(SkillMetricsSnapshotEntry[]? entries)
        {
            _entries = entries;
        }

        public int Count => (_entries ?? []).Length;

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_entries);
        }

        IEnumerator<CombatEventKey> IEnumerable<CombatEventKey>.GetEnumerator()
        {
            var entries = _entries ?? [];
            for (var i = 0; i < entries.Length; i++)
            {
                yield return entries[i].EventKey;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<CombatEventKey>)this).GetEnumerator();
        }

        public struct Enumerator
        {
            private readonly SkillMetricsSnapshotEntry[]? _entries;
            private int _index;

            internal Enumerator(SkillMetricsSnapshotEntry[]? entries)
            {
                _entries = entries;
                _index = -1;
                Current = default;
            }

            public CombatEventKey Current { get; private set; }

            public bool MoveNext()
            {
                var entries = _entries;
                var next = _index + 1;
                if (entries is null || (uint)next >= (uint)entries.Length)
                {
                    return false;
                }

                _index = next;
                Current = entries[next].EventKey;
                return true;
            }
        }
    }

    public readonly struct ValueCollection : IReadOnlyCollection<SkillMetricsSnapshot>
    {
        private readonly SkillMetricsSnapshotEntry[]? _entries;

        internal ValueCollection(SkillMetricsSnapshotEntry[]? entries)
        {
            _entries = entries;
        }

        public int Count => (_entries ?? []).Length;

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_entries);
        }

        IEnumerator<SkillMetricsSnapshot> IEnumerable<SkillMetricsSnapshot>.GetEnumerator()
        {
            var entries = _entries ?? [];
            for (var i = 0; i < entries.Length; i++)
            {
                yield return entries[i].Metrics;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<SkillMetricsSnapshot>)this).GetEnumerator();
        }

        public struct Enumerator
        {
            private readonly SkillMetricsSnapshotEntry[]? _entries;
            private int _index;

            internal Enumerator(SkillMetricsSnapshotEntry[]? entries)
            {
                _entries = entries;
                _index = -1;
                Current = default;
            }

            public SkillMetricsSnapshot Current { get; private set; }

            public bool MoveNext()
            {
                var entries = _entries;
                var next = _index + 1;
                if (entries is null || (uint)next >= (uint)entries.Length)
                {
                    return false;
                }

                _index = next;
                Current = entries[next].Metrics;
                return true;
            }
        }
    }
}
