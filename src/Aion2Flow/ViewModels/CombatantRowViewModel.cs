using Cloris.Aion2Flow.Battle.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class CombatantRowViewModel : ObservableObject
{
    public int Id { get; set; }

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    public partial CharacterClass? CharacterClass { get; set; }

    [ObservableProperty]
    public partial double DamagePerSecond { get; set; }

    [ObservableProperty]
    public partial double HealingPerSecond { get; set; }

    [ObservableProperty]
    public partial double Damage { get; set; }

    [ObservableProperty]
    public partial double Healing { get; set; }

    [ObservableProperty]
    public partial double DamageContribution { get; set; }
}
