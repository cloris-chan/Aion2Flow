using Avalonia.Markup.Xaml;
using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.Views;

public partial class ShieldDetailView : CombatSkillDetailView
{
    public ShieldDetailView()
        : base(CombatContributionCategory.Shield, "ShieldRows")
    {
        AvaloniaXamlLoader.Load(this);
        InitializeSkillSelectionMode();
    }
}
