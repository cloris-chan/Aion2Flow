using Cloris.Aion2Flow.Combat.Classification;

namespace Cloris.Aion2Flow.Combat.Metrics;

public sealed class SkillMetrics
{
    public int SkillCode { get; }
    public CombatEventKind EventKind { get; }
    public CombatValueKind PrimaryValueKind { get; private set; }
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
    public string SkillName => CombatEventClassifier.DisplaySkillNameFor(SkillCode);
    public int MultiHitTimes { get; set; }
    public int BackTimes { get; set; }
    public int PerfectTimes { get; set; }
    public int SmiteTimes { get; set; }
    public int ParryTimes { get; set; }
    public int BlockTimes { get; set; }
    public int EnduranceTimes { get; set; }
    public int RegenerationTimes { get; set; }
    public int DefensivePerfectTimes { get; set; }

    public SkillMetrics(ParsedCombatPacket packet)
    {
        SkillCode = packet.SkillCode;
        EventKind = packet.EventKind;
        PrimaryValueKind = packet.ValueKind;
    }

    private SkillMetrics(int skillCode, CombatEventKind eventKind, CombatValueKind primaryValueKind)
    {
        SkillCode = skillCode;
        EventKind = eventKind;
        PrimaryValueKind = primaryValueKind;
    }

    public SkillMetrics DeepClone()
    {
        return new SkillMetrics(SkillCode, EventKind, PrimaryValueKind)
        {
            DamageAmount = DamageAmount,
            PeriodicDamageAmount = PeriodicDamageAmount,
            PeriodicDamageTimes = PeriodicDamageTimes,
            HealingAmount = HealingAmount,
            HealingTimes = HealingTimes,
            SupportTimes = SupportTimes,
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
            BackTimes = BackTimes,
            PerfectTimes = PerfectTimes,
            SmiteTimes = SmiteTimes,
            ParryTimes = ParryTimes,
            BlockTimes = BlockTimes,
            EnduranceTimes = EnduranceTimes,
            RegenerationTimes = RegenerationTimes,
            DefensivePerfectTimes = DefensivePerfectTimes
        };
    }

    public void ProcessEvent(ParsedCombatPacket packet)
    {
        var contributesOutcomeOnly =
            packet.AttemptContribution > 0 ||
            packet.HitContribution > 0 ||
            (packet.Modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) != 0;
        var isOutcomeOnlyDamageAttempt =
            packet.Damage <= 0 &&
            contributesOutcomeOnly &&
            packet.EventKind == CombatEventKind.Damage &&
            packet.ValueKind is CombatValueKind.Damage or CombatValueKind.PeriodicDamage or CombatValueKind.DrainDamage or CombatValueKind.Unknown;

        if (packet.Damage <= 0 &&
            !contributesOutcomeOnly &&
            packet.ValueKind is not CombatValueKind.Support &&
            packet.EventKind != CombatEventKind.Support)
        {
            return;
        }

        if (isOutcomeOnlyDamageAttempt)
        {
            ApplyDamageAttemptMetrics(packet);
            return;
        }

        switch (packet.ValueKind)
        {
            case CombatValueKind.PeriodicHealing:
                HealingTimes++;
                HealingAmount += packet.Damage;
                PeriodicHealingTimes++;
                PeriodicHealingAmount += packet.Damage;
                PrimaryValueKind = ResolvePrimaryValueKind();
                return;
            case CombatValueKind.DrainHealing:
                HealingTimes++;
                HealingAmount += packet.Damage;
                DrainHealingTimes++;
                DrainHealingAmount += packet.Damage;
                PrimaryValueKind = ResolvePrimaryValueKind();
                return;
            case CombatValueKind.Healing:
                HealingTimes++;
                HealingAmount += packet.Damage;
                if (packet.EffectTag == PacketEffectTag.RegenerationHealing)
                {
                    RegenerationHealingTimes++;
                    RegenerationHealingAmount += packet.Damage;
                }
                PrimaryValueKind = ResolvePrimaryValueKind();
                return;
            case CombatValueKind.Shield:
            case CombatValueKind.Support:
                SupportTimes++;
                if (packet.ValueKind == CombatValueKind.Shield)
                {
                    if (packet.EffectTag == PacketEffectTag.ShieldAbsorbed)
                    {
                        if (packet.Damage > 0)
                        {
                            ShieldAbsorbedAmount += packet.Damage;
                            ShieldAbsorbedTimes++;
                        }
                    }
                    else
                    {
                        ShieldAmount += packet.Damage;
                        ShieldTimes++;
                    }
                }

                PrimaryValueKind = ResolvePrimaryValueKind();
                return;
            case CombatValueKind.PeriodicDamage:
                PeriodicDamageTimes++;
                PeriodicDamageAmount += packet.Damage;
                PrimaryValueKind = ResolvePrimaryValueKind();
                return;
            case CombatValueKind.DrainDamage:
                DrainDamageTimes++;
                DrainDamageAmount += packet.Damage;
                goto case CombatValueKind.Damage;
            case CombatValueKind.Damage:
            case CombatValueKind.Unknown:
                break;
        }

        if (packet.EventKind == CombatEventKind.Healing)
        {
            HealingTimes++;
            HealingAmount += packet.Damage;
            PrimaryValueKind = ResolvePrimaryValueKind();
            return;
        }

        if (packet.EventKind == CombatEventKind.Support)
        {
            SupportTimes++;
            PrimaryValueKind = ResolvePrimaryValueKind();
            return;
        }

        ApplyDamageAttemptMetrics(packet);
    }

    private void ApplyDamageAttemptMetrics(ParsedCombatPacket packet)
    {
        DamageAmount += packet.Damage;
        var hitContribution = Math.Max(0, packet.HitContribution);
        var attemptContribution = Math.Max(hitContribution, Math.Max(0, packet.AttemptContribution));
        var evadeContribution = (packet.Modifiers & DamageModifiers.Evade) != 0
            ? attemptContribution
            : 0;
        var invincibleContribution = (packet.Modifiers & DamageModifiers.Invincible) != 0
            ? attemptContribution
            : 0;
        var multiHitContribution = (packet.Modifiers & DamageModifiers.MultiHit) != 0
            ? 1
            : 0;

        Times += hitContribution;
        AttemptTimes += attemptContribution;
        EvadeTimes += evadeContribution;
        InvincibleTimes += invincibleContribution;
        MultiHitTimes += multiHitContribution;

        if (hitContribution > 0 && packet.IsCritical) CriticalTimes += hitContribution;
        if (hitContribution > 0 && (packet.Modifiers & DamageModifiers.Back) != 0) BackTimes += hitContribution;
        if (hitContribution > 0 && (packet.Modifiers & DamageModifiers.Parry) != 0) ParryTimes += hitContribution;
        if (hitContribution > 0 && (packet.Modifiers & DamageModifiers.Smite) != 0) SmiteTimes += hitContribution;
        if (hitContribution > 0 && (packet.Modifiers & DamageModifiers.Perfect) != 0) PerfectTimes += hitContribution;
        if (hitContribution > 0 && (packet.Modifiers & DamageModifiers.Block) != 0) BlockTimes += hitContribution;
        if (hitContribution > 0 && (packet.Modifiers & DamageModifiers.Endurance) != 0) EnduranceTimes += hitContribution;
        if (hitContribution > 0 && (packet.Modifiers & DamageModifiers.Regeneration) != 0) RegenerationTimes += hitContribution;
        if (hitContribution > 0 && (packet.Modifiers & DamageModifiers.DefensivePerfect) != 0) DefensivePerfectTimes += hitContribution;
        PrimaryValueKind = ResolvePrimaryValueKind();
    }

    private CombatValueKind ResolvePrimaryValueKind()
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
            ? PrimaryValueKind
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
