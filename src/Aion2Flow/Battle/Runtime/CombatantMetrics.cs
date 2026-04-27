using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;

namespace Cloris.Aion2Flow.Battle.Runtime;

public sealed class CombatantMetrics(string nickname)
{
    public CharacterClass? CharacterClass { get; set; }

    public double DamagePerSecond { get; set; }
    public double HealingPerSecond { get; set; }

    public long DamageAmount { get; private set; }
    public long HealingAmount { get; private set; }
    public long PeriodicHealingAmount { get; private set; }
    public long DrainDamageAmount { get; private set; }
    public long DrainHealingAmount { get; private set; }
    public long RegenerationHealingAmount { get; private set; }
    public long ShieldAmount { get; private set; }
    public int ShieldTimes { get; private set; }
    public long ShieldAbsorbedAmount { get; private set; }
    public int ShieldAbsorbedTimes { get; private set; }
    public double DamageContribution { get; set; }

    public Dictionary<int, SkillMetrics> Skills { get; } = [];
    public string Nickname { get; } = nickname;

    private void AddDamageAmount(int amount) => DamageAmount += amount;
    private void AddHealingAmount(int amount) => HealingAmount += amount;
    private void AddPeriodicHealingAmount(int amount) => PeriodicHealingAmount += amount;
    private void AddDrainDamageAmount(int amount) => DrainDamageAmount += amount;
    private void AddDrainHealingAmount(int amount) => DrainHealingAmount += amount;
    private void AddRegenerationHealingAmount(int amount) => RegenerationHealingAmount += amount;
    private void AddShieldAmount(int amount) => ShieldAmount += amount;
    private void AddShieldTime() => ShieldTimes++;
    private void AddShieldAbsorbedAmount(int amount) => ShieldAbsorbedAmount += amount;
    private void AddShieldAbsorbedTime() => ShieldAbsorbedTimes++;

    public bool ProcessCombatEvent(ParsedCombatPacket packet)
    {
        var contributesOutcomeOnly =
            packet.AttemptContribution > 0 ||
            packet.HitContribution > 0 ||
            (packet.Modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) != 0;

        if (packet.Damage <= 0 &&
            !contributesOutcomeOnly &&
            packet.ValueKind is not CombatValueKind.Support &&
            packet.EventKind != CombatEventKind.Support)
        {
            return false;
        }

        if (!Skills.TryGetValue(packet.SkillCode, out var analyzedSkill))
        {
            analyzedSkill = new SkillMetrics(packet);
            Skills[packet.SkillCode] = analyzedSkill;
        }

        analyzedSkill.ProcessEvent(packet);

        switch (packet.ValueKind)
        {
            case CombatValueKind.PeriodicHealing:
                AddHealingAmount(packet.Damage);
                AddPeriodicHealingAmount(packet.Damage);
                return false;
            case CombatValueKind.DrainHealing:
                AddHealingAmount(packet.Damage);
                AddDrainHealingAmount(packet.Damage);
                return false;
            case CombatValueKind.Healing:
                AddHealingAmount(packet.Damage);
                if (packet.EffectTag == PacketEffectTag.RegenerationHealing)
                {
                    AddRegenerationHealingAmount(packet.Damage);
                }
                return false;
            case CombatValueKind.Shield:
                if (packet.EffectTag == PacketEffectTag.ShieldAbsorbed)
                {
                    if (packet.Damage > 0)
                    {
                        AddShieldAbsorbedAmount(packet.Damage);
                        AddShieldAbsorbedTime();
                    }
                }
                else
                {
                    AddShieldAmount(packet.Damage);
                    AddShieldTime();
                }
                return false;
            case CombatValueKind.Support:
                return false;
            case CombatValueKind.DrainDamage:
                AddDrainDamageAmount(packet.Damage);
                AddDamageAmount(packet.Damage);
                return true;
        }

        if (packet.EventKind == CombatEventKind.Healing)
        {
            AddHealingAmount(packet.Damage);
            return false;
        }

        if (packet.EventKind == CombatEventKind.Support)
        {
            return false;
        }

        AddDamageAmount(packet.Damage);
        return packet.Damage > 0;
    }

    public CombatantMetrics DeepClone()
    {
        var clone = new CombatantMetrics(Nickname)
        {
            CharacterClass = CharacterClass,
            DamagePerSecond = DamagePerSecond,
            HealingPerSecond = HealingPerSecond,
            DamageContribution = DamageContribution,
            DamageAmount = DamageAmount,
            HealingAmount = HealingAmount,
            PeriodicHealingAmount = PeriodicHealingAmount,
            DrainDamageAmount = DrainDamageAmount,
            DrainHealingAmount = DrainHealingAmount,
            RegenerationHealingAmount = RegenerationHealingAmount,
            ShieldAmount = ShieldAmount,
            ShieldTimes = ShieldTimes,
            ShieldAbsorbedAmount = ShieldAbsorbedAmount,
            ShieldAbsorbedTimes = ShieldAbsorbedTimes
        };

        foreach (var (skillCode, skill) in Skills)
        {
            clone.Skills[skillCode] = skill.DeepClone();
        }

        return clone;
    }
}
