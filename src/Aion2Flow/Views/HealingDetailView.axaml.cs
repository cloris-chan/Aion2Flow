using Avalonia.Markup.Xaml;
using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.Views;

public partial class HealingDetailView : CombatSkillDetailView
{
    public HealingDetailView()
        : base(CombatContributionCategory.Healing, "HealingRows")
    {
        AvaloniaXamlLoader.Load(this);
        InitializeSkillSelectionMode();
    }
}
