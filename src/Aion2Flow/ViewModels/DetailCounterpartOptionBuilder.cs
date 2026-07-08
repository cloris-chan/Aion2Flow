using System.Globalization;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.Services;

namespace Cloris.Aion2Flow.ViewModels;

internal sealed class DetailCounterpartOptionBuilder
{
    private struct CounterpartAggregateMetrics
    {
        public long DamageAmount;
        public long HealingAmount;
        public long ShieldAmount;
    }

    private readonly Dictionary<int, CounterpartAggregateMetrics> _outgoingDamageMetrics = [];
    private readonly Dictionary<int, CounterpartAggregateMetrics> _outgoingSupportMetrics = [];
    private readonly Dictionary<int, CounterpartAggregateMetrics> _incomingDamageMetrics = [];
    private readonly Dictionary<int, CounterpartAggregateMetrics> _incomingSupportMetrics = [];
    private readonly List<int> _optionIds = [];
    private readonly List<DetailCounterpartOption> _options = [];

    public void Accumulate(ReadOnlySpan<CombatDetailEvent> detailEvents, int combatantId)
    {
        _outgoingDamageMetrics.Clear();
        _outgoingSupportMetrics.Clear();
        _incomingDamageMetrics.Clear();
        _incomingSupportMetrics.Clear();

        foreach (ref readonly var detailEvent in detailEvents)
        {
            TryAccumulate(_outgoingDamageMetrics, in detailEvent, DetailSectionKind.OutgoingDamage, combatantId);
            if (!TryAccumulate(_outgoingSupportMetrics, in detailEvent, DetailSectionKind.OutgoingHealing, combatantId))
                TryAccumulate(_outgoingSupportMetrics, in detailEvent, DetailSectionKind.OutgoingShield, combatantId);

            TryAccumulate(_incomingDamageMetrics, in detailEvent, DetailSectionKind.IncomingDamage, combatantId);
            if (!TryAccumulate(_incomingSupportMetrics, in detailEvent, DetailSectionKind.IncomingHealing, combatantId))
                TryAccumulate(_incomingSupportMetrics, in detailEvent, DetailSectionKind.IncomingShield, combatantId);
        }
    }

    public IReadOnlyCollection<DetailCounterpartOption> BuildOutgoingDamageOptions(SceneDisplayContext? displayContext)
        => BuildOptions(_outgoingDamageMetrics, displayContext);

    public IReadOnlyCollection<DetailCounterpartOption> BuildOutgoingSupportOptions(SceneDisplayContext? displayContext)
        => BuildOptions(_outgoingSupportMetrics, displayContext);

    public IReadOnlyCollection<DetailCounterpartOption> BuildIncomingDamageOptions(SceneDisplayContext? displayContext)
        => BuildOptions(_incomingDamageMetrics, displayContext);

    public IReadOnlyCollection<DetailCounterpartOption> BuildIncomingSupportOptions(SceneDisplayContext? displayContext)
        => BuildOptions(_incomingSupportMetrics, displayContext);

    private IReadOnlyCollection<DetailCounterpartOption> BuildOptions(Dictionary<int, CounterpartAggregateMetrics> metricsByCombatantId, SceneDisplayContext? displayContext)
    {
        _optionIds.Clear();
        _options.Clear();

        long totalDamage = 0, totalHealing = 0, totalShield = 0;
        foreach (var metrics in metricsByCombatantId.Values)
        {
            totalDamage += metrics.DamageAmount;
            totalHealing += metrics.HealingAmount;
            totalShield += metrics.ShieldAmount;
        }

        _optionIds.AddRange(metricsByCombatantId.Keys);
        _optionIds.Sort((left, right) =>
        {
            var leftMetrics = metricsByCombatantId[left];
            var rightMetrics = metricsByCombatantId[right];
            var cmp = (rightMetrics.DamageAmount + rightMetrics.HealingAmount + rightMetrics.ShieldAmount)
                .CompareTo(leftMetrics.DamageAmount + leftMetrics.HealingAmount + leftMetrics.ShieldAmount);
            if (cmp != 0)
                return cmp;

            cmp = rightMetrics.DamageAmount.CompareTo(leftMetrics.DamageAmount);
            if (cmp != 0)
                return cmp;

            cmp = rightMetrics.HealingAmount.CompareTo(leftMetrics.HealingAmount);
            if (cmp != 0)
                return cmp;

            cmp = rightMetrics.ShieldAmount.CompareTo(leftMetrics.ShieldAmount);
            if (cmp != 0)
                return cmp;

            var leftName = displayContext?.GetEntitySortKey(left) ?? left.ToString(CultureInfo.InvariantCulture);
            var rightName = displayContext?.GetEntitySortKey(right) ?? right.ToString(CultureInfo.InvariantCulture);
            return StringComparer.CurrentCulture.Compare(leftName, rightName);
        });

        foreach (var combatantId in _optionIds)
        {
            var metrics = metricsByCombatantId[combatantId];
            _options.Add(new DetailCounterpartOption(
                combatantId,
                metrics.DamageAmount,
                totalDamage > 0 ? metrics.DamageAmount / (double)totalDamage : 0d,
                metrics.HealingAmount,
                totalHealing > 0 ? metrics.HealingAmount / (double)totalHealing : 0d,
                metrics.ShieldAmount,
                totalShield > 0 ? metrics.ShieldAmount / (double)totalShield : 0d));
        }

        return _options;
    }

    private static bool TryAccumulate(Dictionary<int, CounterpartAggregateMetrics> metricsByCombatantId, in CombatDetailEvent detailEvent, DetailSectionKind sectionKind, int combatantId)
    {
        if (!SkillDetailSectionRules.Matches(in detailEvent, sectionKind, combatantId) ||
            !SkillDetailSectionRules.Contributes(in detailEvent, sectionKind))
        {
            return false;
        }

        var counterpartCombatantId = SkillDetailSectionRules.GetCounterpartCombatantId(in detailEvent, sectionKind);
        if (counterpartCombatantId <= 0)
            return true;

        metricsByCombatantId.TryGetValue(counterpartCombatantId, out var metrics);
        var amount = SkillDetailSectionRules.GetContributionAmount(in detailEvent, sectionKind);
        switch (sectionKind)
        {
            case DetailSectionKind.OutgoingDamage:
            case DetailSectionKind.IncomingDamage:
                metrics.DamageAmount += amount;
                break;
            case DetailSectionKind.OutgoingHealing:
            case DetailSectionKind.IncomingHealing:
                metrics.HealingAmount += amount;
                break;
            case DetailSectionKind.OutgoingShield:
            case DetailSectionKind.IncomingShield:
                metrics.ShieldAmount += amount;
                break;
        }

        metricsByCombatantId[counterpartCombatantId] = metrics;
        return true;
    }
}
