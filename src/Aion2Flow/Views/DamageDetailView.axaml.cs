using Avalonia.Markup.Xaml;
using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.Views;

public partial class DamageDetailView : CombatSkillDetailView
{
    public DamageDetailView() : base(CombatContributionCategory.Damage, "DamageRows")
    {
        AvaloniaXamlLoader.Load(this);
        InitializeSkillSelectionMode();
    }
}
