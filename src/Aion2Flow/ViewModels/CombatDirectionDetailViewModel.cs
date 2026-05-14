using Cloris.Aion2Flow.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

public readonly record struct DetailCounterpartOption(int CombatantId, long DamageAmount, double DamageShare, long HealingAmount, double HealingShare, long ShieldAmount, double ShieldShare);

public sealed partial class CombatDirectionDetailViewModel(LocalizationService localization, UiFrameBatchService frameBatchService, string counterpartTitleKey) : ObservableObject
{
    public DetailCounterpartFilterViewModel DamageCounterpartFilter { get; } = new DetailCounterpartFilterViewModel(localization, counterpartTitleKey);

    public DetailCounterpartFilterViewModel SupportCounterpartFilter { get; } = new DetailCounterpartFilterViewModel(localization, counterpartTitleKey);

    public SkillDetailSectionViewModel DamageSection { get; } = new(frameBatchService);

    public SkillDetailSectionViewModel HealingSection { get; } = new(frameBatchService);

    public SkillDetailSectionViewModel ShieldSection { get; } = new(frameBatchService);

    public void Clear()
    {
        DamageCounterpartFilter.Clear();
        SupportCounterpartFilter.Clear();
        DamageSection.Clear();
        HealingSection.Clear();
        ShieldSection.Clear();
    }
}
