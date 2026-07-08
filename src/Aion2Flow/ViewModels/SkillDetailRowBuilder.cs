using System.Globalization;
using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.Services;

namespace Cloris.Aion2Flow.ViewModels;

internal static class SkillDetailRowBuilder
{
    public static void BuildDamageRows(
        Dictionary<CombatEventKey, SkillMetrics> skills,
        Dictionary<CombatEventKey, int> eventCounts,
        SceneDisplayContext? displayContext,
        LocalizationService localization,
        List<SkillDetailRowData> rows,
        Dictionary<SkillBaseKey, int> rowIndexes)
    {
        foreach (var (_, skill) in skills)
        {
            if (IsHiddenDamageOutcomeSkill(skill.SkillCode))
                continue;

            var totalAmount = skill.DamageAmount + skill.PeriodicDamageAmount;
            var directHits = skill.Times;
            var attempts = skill.AttemptTimes;
            var periodicHits = skill.PeriodicDamageTimes;
            var evades = skill.EvadeTimes;
            var invincible = skill.InvincibleTimes;
            if (totalAmount <= 0 && directHits <= 0 && periodicHits <= 0 && attempts <= 0 && evades <= 0 && invincible <= 0)
                continue;

            var baseProjection = ResolveSkillBaseProjection(skill.EventKey, displayContext, localization);
            var row = new SkillDetailRowData
            {
                BaseKey = baseProjection.Key,
                SkillCode = baseProjection.SkillCode,
                DisplayName = baseProjection.DisplayName,
                EventCount = ResolveEventCount(eventCounts, skill.EventKey),
                TotalAmount = totalAmount,
                DirectAmount = skill.DamageAmount,
                PeriodicAmount = skill.PeriodicDamageAmount,
                Hits = directHits,
                Attempts = attempts,
                PeriodicHits = periodicHits,
                Evades = evades,
                Invincible = invincible,
                Criticals = skill.CriticalTimes,
                Back = skill.BackTimes,
                Parry = skill.ParryTimes,
                PerfectParry = skill.PerfectParryTimes,
                Perfect = skill.PerfectTimes,
                Smite = skill.SmiteTimes,
                MultiHit = skill.MultiHitTimes,
                Front = skill.FrontTimes,
                Endurance = skill.EnduranceTimes,
                Regeneration = skill.RegenerationTimes,
                Block = skill.BlockTimes,
                PerfectBlock = skill.PerfectBlockTimes,
            };
            SkillDetailBaseAggregator.AddOrMerge(rows, rowIndexes, in row);
        }

        SortRowsAndApplySharePercent(rows);
    }

    public static void BuildHealingRows(
        Dictionary<CombatEventKey, SkillMetrics> skills,
        Dictionary<CombatEventKey, int> eventCounts,
        SceneDisplayContext? displayContext,
        LocalizationService localization,
        List<SkillDetailRowData> rows,
        Dictionary<SkillBaseKey, int> rowIndexes)
    {
        foreach (var (_, skill) in skills)
        {
            var directHealingAmount = Math.Max(0L, skill.HealingAmount - skill.PeriodicHealingAmount - skill.DrainHealingAmount - skill.RegenerationHealingAmount);
            var directHealingHits = Math.Max(0, skill.HealingTimes - skill.PeriodicHealingTimes - skill.DrainHealingTimes - skill.RegenerationHealingTimes);
            var totalAmount = directHealingAmount + skill.PeriodicHealingAmount + skill.DrainHealingAmount + skill.RegenerationHealingAmount;
            var totalHits = directHealingHits + skill.PeriodicHealingTimes + skill.DrainHealingTimes + skill.RegenerationHealingTimes;
            if (totalAmount <= 0 && totalHits <= 0)
                continue;

            var baseProjection = ResolveSkillBaseProjection(skill.EventKey, displayContext, localization);
            var row = new SkillDetailRowData
            {
                BaseKey = baseProjection.Key,
                SkillCode = baseProjection.SkillCode,
                DisplayName = baseProjection.DisplayName,
                EventCount = ResolveEventCount(eventCounts, skill.EventKey),
                TotalAmount = totalAmount,
                DirectAmount = directHealingAmount,
                PeriodicAmount = skill.PeriodicHealingAmount,
                DrainAmount = skill.DrainHealingAmount,
                RegenerationAmount = skill.RegenerationHealingAmount,
                Hits = totalHits,
                Attempts = totalHits,
                PeriodicHits = skill.PeriodicHealingTimes,
            };
            SkillDetailBaseAggregator.AddOrMerge(rows, rowIndexes, in row);
        }

        SortRowsAndApplySharePercent(rows);
    }

    public static void BuildShieldRows(
        Dictionary<CombatEventKey, SkillMetrics> skills,
        Dictionary<CombatEventKey, int> eventCounts,
        SceneDisplayContext? displayContext,
        LocalizationService localization,
        List<SkillDetailRowData> rows,
        Dictionary<SkillBaseKey, int> rowIndexes)
    {
        foreach (var (_, skill) in skills)
        {
            if (skill.ShieldAmount <= 0 && skill.ShieldTimes <= 0 &&
                skill.ShieldAbsorbedAmount <= 0 && skill.ShieldAbsorbedTimes <= 0)
            {
                continue;
            }

            var baseProjection = ResolveSkillBaseProjection(skill.EventKey, displayContext, localization);
            var row = new SkillDetailRowData
            {
                BaseKey = baseProjection.Key,
                SkillCode = baseProjection.SkillCode,
                DisplayName = baseProjection.DisplayName,
                EventCount = ResolveEventCount(eventCounts, skill.EventKey),
                TotalAmount = skill.ShieldAmount,
                ShieldAmount = skill.ShieldAmount,
                ShieldAbsorbedAmount = skill.ShieldAbsorbedAmount,
                Hits = skill.ShieldTimes,
                Attempts = skill.ShieldTimes,
            };
            SkillDetailBaseAggregator.AddOrMerge(rows, rowIndexes, in row);
        }

        SortRowsAndApplySharePercent(rows);
    }

    private static bool IsHiddenDamageOutcomeSkill(int skillCode)
        => skillCode == SyntheticCombatSkillCodes.UnresolvedInvincible;

    private static int ResolveEventCount(Dictionary<CombatEventKey, int> eventCounts, CombatEventKey eventKey)
        => eventCounts.TryGetValue(eventKey, out var eventCount) ? eventCount : 0;

    private static DetailSkillBaseProjection ResolveSkillBaseProjection(CombatEventKey eventKey, SceneDisplayContext? displayContext, LocalizationService localization)
    {
        var key = SkillBaseKey.FromEventKey(eventKey);
        var skillCode = key.SkillCode == eventKey.SkillCode || displayContext?.ContainsSkill(key.SkillCode) == true
            ? key.SkillCode
            : eventKey.SkillCode;
        var displayEventKey = new CombatEventKey(skillCode, eventKey.BodyResourceEffectRef, eventKey.DetailResourceEffectRef);
        return new DetailSkillBaseProjection(key, skillCode, ResolveEventDisplayName(displayEventKey, displayContext, localization));
    }

    private readonly record struct DetailSkillBaseProjection(SkillBaseKey Key, int SkillCode, string DisplayName);

    private static string ResolveEventDisplayName(CombatEventKey eventKey, SceneDisplayContext? displayContext, LocalizationService localization)
    {
        if (eventKey.SkillCode > 0)
            return displayContext?.ResolveSkillName(eventKey.SkillCode) ?? eventKey.SkillCode.ToString(CultureInfo.InvariantCulture);

        if (displayContext is not null)
        {
            var bodyName = displayContext.ResolveSkillName(eventKey.BodyResourceEffectRef);
            if (!string.IsNullOrWhiteSpace(bodyName))
                return bodyName;

            var detailName = displayContext.ResolveSkillName(eventKey.DetailResourceEffectRef);
            if (!string.IsNullOrWhiteSpace(detailName))
                return detailName;
        }

        return eventKey.FormatFallbackLabel(localization["Skill_UnknownEffect"]);
    }

    private static void SortRowsAndApplySharePercent(List<SkillDetailRowData> rows)
    {
        rows.Sort((a, b) =>
        {
            var cmp = b.TotalAmount.CompareTo(a.TotalAmount);
            if (cmp != 0) return cmp;
            cmp = b.Hits.CompareTo(a.Hits);
            if (cmp != 0) return cmp;
            return CompareDetailRows(in a, in b);
        });

        var sectionTotal = 0L;
        foreach (ref var row in CollectionsMarshal.AsSpan(rows))
        {
            sectionTotal += row.TotalAmount;
        }

        if (sectionTotal <= 0)
            return;

        foreach (ref var row in CollectionsMarshal.AsSpan(rows))
        {
            row.SharePercent = row.TotalAmount / (double)sectionTotal;
        }
    }

    private static int CompareDetailRows(in SkillDetailRowData left, in SkillDetailRowData right)
    {
        var comparison = StringComparer.CurrentCulture.Compare(left.DisplayName, right.DisplayName);
        return comparison != 0 ? comparison : left.BaseKey.CompareTo(right.BaseKey);
    }
}
