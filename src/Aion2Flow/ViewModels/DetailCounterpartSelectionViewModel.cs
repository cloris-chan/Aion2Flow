using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class DetailCounterpartSelectionViewModel : ObservableObject
{
    public DetailCounterpartSelectionViewModel(
        int combatantId,
        string displayName,
        long damageAmount,
        double damageShare,
        long healingAmount,
        double healingShare,
        long shieldAmount,
        double shieldShare,
        bool initiallySelected)
    {
        CombatantId = combatantId;
        DisplayName = displayName;
        DamageAmount = damageAmount;
        DamageShare = damageShare;
        HealingAmount = healingAmount;
        HealingShare = healingShare;
        ShieldAmount = shieldAmount;
        ShieldShare = shieldShare;
        isSelected = initiallySelected;
    }

    public int CombatantId { get; }

    public string DisplayName { get; }

    public long DamageAmount { get; }

    public double DamageShare { get; }

    public long HealingAmount { get; }

    public double HealingShare { get; }

    public long ShieldAmount { get; }

    public double ShieldShare { get; }

    [ObservableProperty]
    private bool isSelected;

    public event EventHandler? SelectionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
