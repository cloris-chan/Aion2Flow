using Cloris.Aion2Flow.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

public readonly record struct DetailCounterpartOption(
    int CombatantId,
    long DamageAmount,
    double DamageShare,
    long HealingAmount,
    double HealingShare,
    long ShieldAmount,
    double ShieldShare,
    long ManaChange);

public sealed partial class CombatDirectionDetailViewModel(LocalizationService localization, UiFrameBatchService frameBatchService, string counterpartTitleKey) : ObservableObject
{
    public DetailCounterpartFilterViewModel DamageCounterpartFilter { get; } = new DetailCounterpartFilterViewModel(localization, counterpartTitleKey);

    public DetailCounterpartFilterViewModel HealingCounterpartFilter { get; } = new DetailCounterpartFilterViewModel(localization, counterpartTitleKey);

    public DetailCounterpartFilterViewModel ShieldCounterpartFilter { get; } = new DetailCounterpartFilterViewModel(localization, counterpartTitleKey);

    public DetailCounterpartFilterViewModel ResourceCounterpartFilter { get; } = new DetailCounterpartFilterViewModel(localization, counterpartTitleKey);

    public SkillDetailSectionViewModel DamageSection { get; } = new(frameBatchService);

    public SkillDetailSectionViewModel HealingSection { get; } = new(frameBatchService);

    public SkillDetailSectionViewModel ShieldSection { get; } = new(frameBatchService);

    public ResourceDetailSectionViewModel ResourceSection { get; } = new(frameBatchService);

    public void Clear()
    {
        DamageCounterpartFilter.Clear();
        HealingCounterpartFilter.Clear();
        ShieldCounterpartFilter.Clear();
        ResourceCounterpartFilter.Clear();
        DamageSection.Clear();
        HealingSection.Clear();
        ShieldSection.Clear();
        ResourceSection.Clear();
    }
}
