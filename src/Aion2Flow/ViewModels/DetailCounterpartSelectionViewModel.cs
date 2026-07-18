using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class DetailCounterpartSelectionViewModel(
    int combatantId,
    long damageAmount,
    double damageShare,
    long healingAmount,
    double healingShare,
    long shieldAmount,
    double shieldShare,
    long manaChange,
    bool initiallySelected) : ObservableObject
{
    public int CombatantId { get; } = combatantId;

    [ObservableProperty]
    public partial long DamageAmount { get; set; } = damageAmount;

    [ObservableProperty]
    public partial double DamageShare { get; set; } = damageShare;

    [ObservableProperty]
    public partial long HealingAmount { get; set; } = healingAmount;

    [ObservableProperty]
    public partial double HealingShare { get; set; } = healingShare;

    [ObservableProperty]
    public partial long ShieldAmount { get; set; } = shieldAmount;

    [ObservableProperty]
    public partial double ShieldShare { get; set; } = shieldShare;

    [ObservableProperty]
    public partial long ManaChange { get; set; } = manaChange;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = initiallySelected;

    public void ApplyFrom(DetailCounterpartOption option)
    {
        DamageAmount = option.DamageAmount;
        DamageShare = option.DamageShare;
        HealingAmount = option.HealingAmount;
        HealingShare = option.HealingShare;
        ShieldAmount = option.ShieldAmount;
        ShieldShare = option.ShieldShare;
        ManaChange = option.ManaChange;
    }

    public event EventHandler? SelectionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
