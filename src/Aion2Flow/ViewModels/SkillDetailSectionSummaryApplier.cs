using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.ViewModels;

internal static class SkillDetailSectionSummaryApplier
{
    public static void Apply(
        SkillDetailSectionViewModel section,
        Dictionary<CombatEventKey, SkillMetrics> skills,
        List<SkillDetailRowData> rows,
        DetailSectionKind sectionKind,
        double durationSeconds,
        bool usesSceneDuration)
    {
        section.ReplaceRows(rows);
        section.SkillCount = rows.Count;
        section.HasSkills = rows.Count > 0;
        section.DurationSeconds = durationSeconds;
        section.UsesSceneDuration = usesSceneDuration;

        if (sectionKind is DetailSectionKind.OutgoingDamage or DetailSectionKind.IncomingDamage)
        {
            ApplyDamageSection(section, skills, durationSeconds);
            return;
        }

        long totalAmount = 0, directAmount = 0, periodicAmount = 0, drainAmount = 0, regenerationAmount = 0, shieldAmount = 0, shieldAbsorbedAmount = 0;
        int hits = 0, attempts = 0, periodicHits = 0, evades = 0, invincible = 0, criticals = 0;

        var span = CollectionsMarshal.AsSpan(rows);
        foreach (ref var row in span)
        {
            totalAmount += row.TotalAmount;
            directAmount += row.DirectAmount;
            periodicAmount += row.PeriodicAmount;
            drainAmount += row.DrainAmount;
            regenerationAmount += row.RegenerationAmount;
            shieldAmount += row.ShieldAmount;
            shieldAbsorbedAmount += row.ShieldAbsorbedAmount;
            hits += row.Hits;
            attempts += row.Attempts;
            periodicHits += row.PeriodicHits;
            evades += row.Evades;
            invincible += row.Invincible;
            criticals += row.Criticals;
        }

        section.Total = totalAmount;
        section.DirectTotal = directAmount;
        section.PeriodicTotal = periodicAmount;
        section.DrainTotal = drainAmount;
        section.RegenerationTotal = regenerationAmount;
        section.Shield = shieldAmount;
        section.ShieldAbsorbed = shieldAbsorbedAmount;
        section.Hits = hits;
        section.Attempts = attempts;
        section.PeriodicHits = periodicHits;
        section.Evades = evades;
        section.Invincible = invincible;
        section.Criticals = criticals;
        section.PerfectCount = 0;
        section.SmiteCount = 0;
        section.MultiHitCount = 0;
        section.FrontCount = 0;
        section.BackCount = 0;
        section.ParryCount = 0;
        section.BlockCount = 0;
        section.PerfectParryCount = 0;
        section.PerfectBlockCount = 0;
        section.EnduranceCount = 0;
        section.RegenerationCount = 0;

        section.PerSecond = durationSeconds > 0 ? totalAmount / durationSeconds : 0d;

        section.HitRate = 0d;
        section.CriticalRate = 0d;
        section.SmiteRate = 0d;
        section.MultiHitRate = 0d;
        section.FrontRate = 0d;
        section.ParryRate = 0d;
        section.PerfectRate = 0d;
        section.PerfectParryRate = 0d;
        section.EnduranceRate = 0d;
        section.BackRate = 0d;
        section.RegenerationRate = 0d;
        section.BlockRate = 0d;
        section.PerfectBlockRate = 0d;
        section.EvadeRate = 0d;
        section.InvincibleRate = 0d;
    }

    private static void ApplyDamageSection(SkillDetailSectionViewModel section, Dictionary<CombatEventKey, SkillMetrics> skills, double durationSeconds)
    {
        long total = 0, directTotal = 0, periodicTotal = 0;
        int totalHits = 0, totalAttempts = 0, totalPeriodicHits = 0;
        int critical = 0, perfect = 0, smite = 0, multiHit = 0, front = 0;
        int parry = 0, block = 0, endurance = 0, regeneration = 0, back = 0;
        int perfectParry = 0, perfectBlock = 0;
        int evades = 0, invincible = 0;

        foreach (var (_, skill) in skills)
        {
            directTotal += skill.DamageAmount;
            periodicTotal += skill.PeriodicDamageAmount;
            total += skill.DamageAmount + skill.PeriodicDamageAmount;
            totalHits += skill.Times;
            totalAttempts += skill.AttemptTimes;
            totalPeriodicHits += skill.PeriodicDamageTimes;
            evades += skill.EvadeTimes;
            invincible += skill.InvincibleTimes;
            critical += skill.CriticalTimes;
            perfect += skill.PerfectTimes;
            smite += skill.SmiteTimes;
            multiHit += skill.MultiHitTimes;
            front += skill.FrontTimes;
            parry += skill.ParryTimes;
            block += skill.BlockTimes;
            perfectParry += skill.PerfectParryTimes;
            perfectBlock += skill.PerfectBlockTimes;
            endurance += skill.EnduranceTimes;
            regeneration += skill.RegenerationTimes;
            back += skill.BackTimes;
        }

        section.Total = total;
        section.DirectTotal = directTotal;
        section.PeriodicTotal = periodicTotal;
        section.DrainTotal = 0;
        section.Hits = totalHits;
        section.Attempts = totalAttempts;
        section.PeriodicHits = totalPeriodicHits;
        section.Evades = evades;
        section.Invincible = invincible;
        section.Criticals = critical;
        section.PerfectCount = perfect;
        section.SmiteCount = smite;
        section.MultiHitCount = multiHit;
        section.FrontCount = front;
        section.BackCount = back;
        section.ParryCount = parry;
        section.BlockCount = block;
        section.PerfectParryCount = perfectParry;
        section.PerfectBlockCount = perfectBlock;
        section.EnduranceCount = endurance;
        section.RegenerationCount = regeneration;

        section.PerSecond = durationSeconds > 0 ? section.Total / durationSeconds : 0d;

        section.HitRate = totalAttempts > 0 ? totalHits / (double)totalAttempts : 0d;
        section.CriticalRate = totalHits > 0 ? critical / (double)totalHits : 0d;
        section.PerfectRate = totalHits > 0 ? perfect / (double)totalHits : 0d;
        section.SmiteRate = totalHits > 0 ? smite / (double)totalHits : 0d;
        section.MultiHitRate = totalHits > 0 ? multiHit / (double)totalHits : 0d;
        section.FrontRate = totalHits > 0 ? front / (double)totalHits : 0d;
        section.ParryRate = totalHits > 0 ? parry / (double)totalHits : 0d;
        section.BlockRate = totalHits > 0 ? block / (double)totalHits : 0d;
        section.PerfectParryRate = totalHits > 0 ? perfectParry / (double)totalHits : 0d;
        section.PerfectBlockRate = totalHits > 0 ? perfectBlock / (double)totalHits : 0d;
        section.EnduranceRate = totalHits > 0 ? endurance / (double)totalHits : 0d;
        section.RegenerationRate = totalHits > 0 ? regeneration / (double)totalHits : 0d;
        section.BackRate = totalHits > 0 ? back / (double)totalHits : 0d;
        section.EvadeRate = totalAttempts > 0 ? evades / (double)totalAttempts : 0d;
        section.InvincibleRate = totalAttempts > 0 ? invincible / (double)totalAttempts : 0d;
    }
}
