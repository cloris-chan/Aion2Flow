using System.Collections;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public struct SkillMetrics(in CombatObservation observation)
{
    private readonly CombatValueKind _primaryValueKind = observation.ValueKind;

    public int SkillCode { get; private set; } = observation.SkillCode;
    public CombatEventKind EventKind { get; private set; } = observation.EventKind;
    public readonly CombatValueKind PrimaryValueKind => ResolvePrimaryValueKind();
    public long DamageAmount { get; set; }
    public long PeriodicDamageAmount { get; set; }
    public int PeriodicDamageTimes { get; set; }
    public long HealingAmount { get; set; }
    public int HealingTimes { get; set; }
    public int SupportTimes { get; set; }
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
        return new SkillMetricsSnapshot(
            SkillCode,
            EventKind,
            PrimaryValueKind,
            DamageAmount,
            PeriodicDamageAmount,
            PeriodicDamageTimes,
            HealingAmount,
            HealingTimes,
            SupportTimes,
            PeriodicHealingAmount,
            PeriodicHealingTimes,
            DrainDamageAmount,
            DrainDamageTimes,
            DrainHealingAmount,
            DrainHealingTimes,
            RegenerationHealingAmount,
            RegenerationHealingTimes,
            ShieldAmount,
            ShieldTimes,
            ShieldAbsorbedAmount,
            ShieldAbsorbedTimes,
            CriticalTimes,
            Times,
            AttemptTimes,
            EvadeTimes,
            InvincibleTimes,
            MultiHitTimes,
            BackTimes,
            PerfectTimes,
            SmiteTimes,
            ParryTimes,
            BlockTimes,
            PerfectParryTimes,
            PerfectBlockTimes,
            EnduranceTimes,
            RegenerationTimes);
    }

    private void ApplyDamageAttemptMetrics(long damage, DamageModifiers modifiers, in CombatContribution contribution)
    {
        DamageAmount += damage;
        var hitContribution = contribution.HitCount;

        Times += hitContribution;
        AttemptTimes += contribution.AttemptCount;
        EvadeTimes += contribution.EvadeCount;
        InvincibleTimes += contribution.InvincibleCount;
        MultiHitTimes += contribution.MultiHitCount;

        if (hitContribution > 0 && (modifiers & DamageModifiers.Critical) != 0) CriticalTimes += hitContribution;
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

    public void ProcessObservation(in CombatObservation observation)
    {
        var contribution = CombatContributionClassifier.Evaluate(in observation);
        ProcessCore(observation.EventKind, observation.ValueKind, observation.EffectTag, observation.Modifiers, observation.Damage, in contribution);
    }

    private void ProcessCore(CombatEventKind eventKind, CombatValueKind valueKind, PacketEffectTag effectTag, DamageModifiers modifiers, long damage, in CombatContribution contribution)
    {
        if (!contribution.CountsAsDamage &&
            !contribution.CountsAsHealing &&
            !contribution.CountsAsShieldGrant &&
            !contribution.CountsAsShieldAbsorbed &&
            valueKind is not CombatValueKind.Support &&
            eventKind != CombatEventKind.Support)
        {
            return;
        }

        if (contribution.CountsAsDamage && damage <= 0)
        {
            ApplyDamageAttemptMetrics(damage, modifiers, in contribution);
            return;
        }

        switch (valueKind)
        {
            case CombatValueKind.PeriodicHealing:
                HealingTimes++;
                HealingAmount += damage;
                PeriodicHealingTimes++;
                PeriodicHealingAmount += damage;
                return;
            case CombatValueKind.DrainHealing:
                HealingTimes++;
                HealingAmount += damage;
                DrainHealingTimes++;
                DrainHealingAmount += damage;
                return;
            case CombatValueKind.Healing:
                HealingTimes++;
                HealingAmount += damage;
                if (effectTag == PacketEffectTag.RegenerationHealing)
                {
                    RegenerationHealingTimes++;
                    RegenerationHealingAmount += damage;
                }
                return;
            case CombatValueKind.Shield:
            case CombatValueKind.Support:
                SupportTimes++;
                if (valueKind == CombatValueKind.Shield)
                {
                    if (contribution.CountsAsShieldAbsorbed)
                    {
                        ShieldAbsorbedAmount += damage;
                        ShieldAbsorbedTimes++;
                    }
                    else if (contribution.CountsAsShieldGrant)
                    {
                        ShieldAmount += damage;
                        ShieldTimes++;
                    }
                }

                return;
            case CombatValueKind.PeriodicDamage:
                PeriodicDamageTimes++;
                PeriodicDamageAmount += damage;
                return;
            case CombatValueKind.DrainDamage:
                DrainDamageTimes++;
                DrainDamageAmount += damage;
                goto case CombatValueKind.Damage;
            case CombatValueKind.Damage:
            case CombatValueKind.Unknown:
                break;
        }

        if (eventKind == CombatEventKind.Healing)
        {
            HealingTimes++;
            HealingAmount += damage;
            return;
        }

        if (eventKind == CombatEventKind.Support)
        {
            SupportTimes++;
            return;
        }

        ApplyDamageAttemptMetrics(damage, modifiers, in contribution);
    }

    private readonly CombatValueKind ResolvePrimaryValueKind()
    {
        var best = CombatValueKind.Unknown;
        var bestAmount = -1L;
        var bestTimes = -1;
        var bestPriority = int.MinValue;

        Consider(CombatValueKind.DrainHealing, DrainHealingAmount, DrainHealingTimes, ref best, ref bestAmount, ref bestTimes, ref bestPriority);
        Consider(CombatValueKind.PeriodicHealing, PeriodicHealingAmount, PeriodicHealingTimes, ref best, ref bestAmount, ref bestTimes, ref bestPriority);
        Consider(CombatValueKind.Healing, HealingAmount, HealingTimes, ref best, ref bestAmount, ref bestTimes, ref bestPriority);
        Consider(CombatValueKind.PeriodicDamage, PeriodicDamageAmount, PeriodicDamageTimes, ref best, ref bestAmount, ref bestTimes, ref bestPriority);
        Consider(CombatValueKind.Damage, DamageAmount, Times, ref best, ref bestAmount, ref bestTimes, ref bestPriority);
        Consider(CombatValueKind.Shield, ShieldAmount, ShieldTimes, ref best, ref bestAmount, ref bestTimes, ref bestPriority);
        Consider(CombatValueKind.Support, 0, SupportTimes, ref best, ref bestAmount, ref bestTimes, ref bestPriority);

        return best == CombatValueKind.Unknown
            ? _primaryValueKind
            : best;

        static void Consider(
            CombatValueKind kind, long amount, int times,
            ref CombatValueKind best, ref long bestAmount, ref int bestTimes, ref int bestPriority)
        {
            if (amount <= 0 && times <= 0)
            {
                return;
            }

            var priority = GetValueKindPriority(kind);
            if (amount > bestAmount ||
                (amount == bestAmount && times > bestTimes) ||
                (amount == bestAmount && times == bestTimes && priority > bestPriority))
            {
                best = kind;
                bestAmount = amount;
                bestTimes = times;
                bestPriority = priority;
            }
        }
    }

    private static int GetValueKindPriority(CombatValueKind kind)
    {
        return kind switch
        {
            CombatValueKind.DrainHealing => 75,
            CombatValueKind.PeriodicHealing => 70,
            CombatValueKind.Healing => 60,
            CombatValueKind.PeriodicDamage => 50,
            CombatValueKind.Damage => 40,
            CombatValueKind.Shield => 30,
            CombatValueKind.Support => 20,
            _ => 0
        };
    }
}

public readonly record struct SkillMetricsSnapshot(
    int SkillCode,
    CombatEventKind EventKind,
    CombatValueKind PrimaryValueKind,
    long DamageAmount,
    long PeriodicDamageAmount,
    int PeriodicDamageTimes,
    long HealingAmount,
    int HealingTimes,
    int SupportTimes,
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
    int BackTimes,
    int PerfectTimes,
    int SmiteTimes,
    int ParryTimes,
    int BlockTimes,
    int PerfectParryTimes,
    int PerfectBlockTimes,
    int EnduranceTimes,
    int RegenerationTimes)
{
}

public readonly record struct SkillMetricsSnapshotEntry(int SkillCode, SkillMetricsSnapshot Metrics)
{
    public void Deconstruct(out int skillCode, out SkillMetricsSnapshot metrics)
    {
        skillCode = SkillCode;
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

    internal static CombatSkillBreakdownSnapshot From(Dictionary<int, SkillMetrics> metrics)
    {
        if (metrics.Count == 0)
        {
            return Empty;
        }

        var entries = new SkillMetricsSnapshotEntry[metrics.Count];
        var index = 0;
        foreach (var (skillCode, skillMetrics) in metrics)
        {
            entries[index++] = new SkillMetricsSnapshotEntry(skillCode, skillMetrics.ToSnapshot());
        }

        Array.Sort(entries, static (left, right) => left.SkillCode.CompareTo(right.SkillCode));
        return new CombatSkillBreakdownSnapshot(entries);
    }

}

public readonly struct SkillMetricsSnapshotMap : IReadOnlyDictionary<int, SkillMetricsSnapshot>
{
    private readonly SkillMetricsSnapshotEntry[]? _entries;

    internal SkillMetricsSnapshotMap(SkillMetricsSnapshotEntry[] entries)
    {
        _entries = entries;
    }

    public int Count => Entries.Length;

    public KeyCollection Keys => new(_entries);

    public ValueCollection Values => new(_entries);

    IEnumerable<int> IReadOnlyDictionary<int, SkillMetricsSnapshot>.Keys => Keys;

    IEnumerable<SkillMetricsSnapshot> IReadOnlyDictionary<int, SkillMetricsSnapshot>.Values => Values;

    private ReadOnlySpan<SkillMetricsSnapshotEntry> Entries => _entries ?? [];

    public SkillMetricsSnapshot this[int key]
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

    public bool ContainsKey(int key)
    {
        return FindIndex(key) >= 0;
    }

    public bool TryGetValue(int key, out SkillMetricsSnapshot value)
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

    public Enumerator GetEnumerator()
    {
        return new Enumerator(_entries);
    }

    IEnumerator<KeyValuePair<int, SkillMetricsSnapshot>> IEnumerable<KeyValuePair<int, SkillMetricsSnapshot>>.GetEnumerator()
    {
        var entries = _entries ?? [];
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            yield return new KeyValuePair<int, SkillMetricsSnapshot>(entry.SkillCode, entry.Metrics);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<KeyValuePair<int, SkillMetricsSnapshot>>)this).GetEnumerator();
    }

    private int FindIndex(int key)
    {
        var entries = Entries;
        var low = 0;
        var high = entries.Length - 1;

        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var midKey = entries[mid].SkillCode;
            if (midKey == key)
            {
                return mid;
            }

            if (midKey < key)
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

    public readonly struct KeyCollection : IReadOnlyCollection<int>
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

        IEnumerator<int> IEnumerable<int>.GetEnumerator()
        {
            var entries = _entries ?? [];
            for (var i = 0; i < entries.Length; i++)
            {
                yield return entries[i].SkillCode;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<int>)this).GetEnumerator();
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

            public int Current { get; private set; }

            public bool MoveNext()
            {
                var entries = _entries;
                var next = _index + 1;
                if (entries is null || (uint)next >= (uint)entries.Length)
                {
                    return false;
                }

                _index = next;
                Current = entries[next].SkillCode;
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
