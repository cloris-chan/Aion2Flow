using System.Globalization;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.Services;

namespace Cloris.Aion2Flow.ViewModels;

internal sealed class DetailCounterpartOptionBuilder
{
    private enum CounterpartSortGroup : byte
    {
        Damage,
        Healing,
        Shield,
        Resource
    }

    private struct CounterpartAggregateMetrics
    {
        public long DamageAmount;
        public long HealingAmount;
        public long ShieldAmount;
        public long ManaChange;
    }

    private readonly Dictionary<int, CounterpartAggregateMetrics> _outgoingDamageMetrics = [];
    private readonly Dictionary<int, CounterpartAggregateMetrics> _outgoingHealingMetrics = [];
    private readonly Dictionary<int, CounterpartAggregateMetrics> _outgoingShieldMetrics = [];
    private readonly Dictionary<int, CounterpartAggregateMetrics> _outgoingResourceMetrics = [];
    private readonly Dictionary<int, CounterpartAggregateMetrics> _incomingDamageMetrics = [];
    private readonly Dictionary<int, CounterpartAggregateMetrics> _incomingHealingMetrics = [];
    private readonly Dictionary<int, CounterpartAggregateMetrics> _incomingShieldMetrics = [];
    private readonly Dictionary<int, CounterpartAggregateMetrics> _incomingResourceMetrics = [];
    private readonly List<int> _optionIds = [];
    private readonly List<DetailCounterpartOption> _options = [];

    public void Accumulate(
        ReadOnlySpan<CombatMetricDetailEvent> metricEvents,
        ReadOnlySpan<CombatMechanicDetailEvent> mechanicEvents,
        ReadOnlySpan<CombatResourceDetailEvent> resourceEvents,
        int combatantId)
    {
        _outgoingDamageMetrics.Clear();
        _outgoingHealingMetrics.Clear();
        _outgoingShieldMetrics.Clear();
        _outgoingResourceMetrics.Clear();
        _incomingDamageMetrics.Clear();
        _incomingHealingMetrics.Clear();
        _incomingShieldMetrics.Clear();
        _incomingResourceMetrics.Clear();

        foreach (ref readonly var detailEvent in metricEvents)
        {
            TryAccumulate(_outgoingDamageMetrics, in detailEvent, DetailSectionKind.OutgoingDamage, combatantId);
            TryAccumulate(_outgoingHealingMetrics, in detailEvent, DetailSectionKind.OutgoingHealing, combatantId);
            TryAccumulate(_outgoingShieldMetrics, in detailEvent, DetailSectionKind.OutgoingShield, combatantId);

            TryAccumulate(_incomingDamageMetrics, in detailEvent, DetailSectionKind.IncomingDamage, combatantId);
            TryAccumulate(_incomingHealingMetrics, in detailEvent, DetailSectionKind.IncomingHealing, combatantId);
            TryAccumulate(_incomingShieldMetrics, in detailEvent, DetailSectionKind.IncomingShield, combatantId);
        }

        foreach (ref readonly var detailEvent in mechanicEvents)
        {
            TryAccumulateMechanic(_outgoingDamageMetrics, in detailEvent, DetailSectionKind.OutgoingDamage, combatantId);
            TryAccumulateMechanic(_incomingDamageMetrics, in detailEvent, DetailSectionKind.IncomingDamage, combatantId);
        }

        foreach (ref readonly var detailEvent in resourceEvents)
        {
            TryAccumulateResource(_outgoingResourceMetrics, in detailEvent, DetailSectionKind.OutgoingResource, combatantId);
            TryAccumulateResource(_incomingResourceMetrics, in detailEvent, DetailSectionKind.IncomingResource, combatantId);
        }
    }

    public IReadOnlyCollection<DetailCounterpartOption> BuildOutgoingDamageOptions(SceneDisplayContext? displayContext)
        => BuildOptions(_outgoingDamageMetrics, CounterpartSortGroup.Damage, displayContext);

    public IReadOnlyCollection<DetailCounterpartOption> BuildOutgoingHealingOptions(SceneDisplayContext? displayContext)
        => BuildOptions(_outgoingHealingMetrics, CounterpartSortGroup.Healing, displayContext);

    public IReadOnlyCollection<DetailCounterpartOption> BuildOutgoingShieldOptions(SceneDisplayContext? displayContext)
        => BuildOptions(_outgoingShieldMetrics, CounterpartSortGroup.Shield, displayContext);

    public IReadOnlyCollection<DetailCounterpartOption> BuildOutgoingResourceOptions(SceneDisplayContext? displayContext)
        => BuildOptions(_outgoingResourceMetrics, CounterpartSortGroup.Resource, displayContext);

    public IReadOnlyCollection<DetailCounterpartOption> BuildIncomingDamageOptions(SceneDisplayContext? displayContext)
        => BuildOptions(_incomingDamageMetrics, CounterpartSortGroup.Damage, displayContext);

    public IReadOnlyCollection<DetailCounterpartOption> BuildIncomingHealingOptions(SceneDisplayContext? displayContext)
        => BuildOptions(_incomingHealingMetrics, CounterpartSortGroup.Healing, displayContext);

    public IReadOnlyCollection<DetailCounterpartOption> BuildIncomingShieldOptions(SceneDisplayContext? displayContext)
        => BuildOptions(_incomingShieldMetrics, CounterpartSortGroup.Shield, displayContext);

    public IReadOnlyCollection<DetailCounterpartOption> BuildIncomingResourceOptions(SceneDisplayContext? displayContext)
        => BuildOptions(_incomingResourceMetrics, CounterpartSortGroup.Resource, displayContext);

    private IReadOnlyCollection<DetailCounterpartOption> BuildOptions(
        Dictionary<int, CounterpartAggregateMetrics> metricsByCombatantId,
        CounterpartSortGroup sortGroup,
        SceneDisplayContext? displayContext)
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
            var cmp = CompareMetrics(in leftMetrics, in rightMetrics, sortGroup);
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
                totalShield > 0 ? metrics.ShieldAmount / (double)totalShield : 0d,
                metrics.ManaChange));
        }

        return _options;
    }

    private static int CompareMetrics(
        in CounterpartAggregateMetrics left,
        in CounterpartAggregateMetrics right,
        CounterpartSortGroup sortGroup)
    {
        if (sortGroup == CounterpartSortGroup.Damage)
            return right.DamageAmount.CompareTo(left.DamageAmount);

        if (sortGroup == CounterpartSortGroup.Healing)
            return right.HealingAmount.CompareTo(left.HealingAmount);

        if (sortGroup == CounterpartSortGroup.Shield)
            return right.ShieldAmount.CompareTo(left.ShieldAmount);

        return right.ManaChange.CompareTo(left.ManaChange);
    }

    private static bool TryAccumulate(Dictionary<int, CounterpartAggregateMetrics> metricsByCombatantId, in CombatMetricDetailEvent detailEvent, DetailSectionKind sectionKind, int combatantId)
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

    private static void TryAccumulateMechanic(
        Dictionary<int, CounterpartAggregateMetrics> metricsByCombatantId,
        in CombatMechanicDetailEvent detailEvent,
        DetailSectionKind sectionKind,
        int combatantId)
    {
        if (!SkillDetailSectionRules.Matches(in detailEvent, sectionKind, combatantId))
            return;

        var counterpartCombatantId = SkillDetailSectionRules.GetCounterpartCombatantId(in detailEvent, sectionKind);
        if (counterpartCombatantId > 0)
            metricsByCombatantId.TryAdd(counterpartCombatantId, default);
    }

    private static void TryAccumulateResource(
        Dictionary<int, CounterpartAggregateMetrics> metricsByCombatantId,
        in CombatResourceDetailEvent detailEvent,
        DetailSectionKind sectionKind,
        int combatantId)
    {
        if (!SkillDetailSectionRules.Matches(in detailEvent, sectionKind, combatantId))
            return;

        if (detailEvent.Resource.Resource != CombatResourceKind.Mana)
            return;

        var counterpartCombatantId = SkillDetailSectionRules.GetCounterpartCombatantId(in detailEvent, sectionKind);
        if (counterpartCombatantId <= 0)
            return;

        metricsByCombatantId.TryGetValue(counterpartCombatantId, out var metrics);
        metrics.ManaChange += detailEvent.Amount;

        metricsByCombatantId[counterpartCombatantId] = metrics;
    }
}
