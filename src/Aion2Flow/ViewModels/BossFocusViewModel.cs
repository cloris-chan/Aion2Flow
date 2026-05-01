using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class BossFocusViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double Hp { get; set; }

    [ObservableProperty]
    public partial double MaxHp { get; set; } = 1d;

    [ObservableProperty]
    public partial double HpRatio { get; set; } = 1d;

    [ObservableProperty]
    public partial string HpText { get; set; } = "--";

    [ObservableProperty]
    public partial string MaxHpText { get; set; } = "--";

    public void Update(string displayName, int hp, int maxHp)
        => Update(displayName, hp, maxHp, hasHp: true);

    public void Update(string displayName, int hp, int maxHp, bool hasHp)
    {
        var resolvedMaxHp = Math.Max(1, maxHp);
        DisplayName = displayName;
        if (hasHp)
        {
            Hp = Math.Max(0, hp);
            MaxHp = resolvedMaxHp;
            HpRatio = Math.Clamp(Hp / resolvedMaxHp, 0d, 1d);
            HpText = Hp.ToString("N0", CultureInfo.CurrentCulture);
            MaxHpText = MaxHp.ToString("N0", CultureInfo.CurrentCulture);
        }
        else
        {
            Hp = 0;
            MaxHp = 1;
            HpRatio = 0;
            HpText = "--";
            MaxHpText = "--";
        }
        IsVisible = true;
    }

    public void Clear()
    {
        IsVisible = false;
        DisplayName = string.Empty;
        Hp = 0;
        MaxHp = 1;
        HpRatio = 1;
        HpText = "--";
        MaxHpText = "--";
    }
}
