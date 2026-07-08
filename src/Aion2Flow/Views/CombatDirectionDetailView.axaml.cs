using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Views;

public partial class CombatDirectionDetailView : UserControl
{
    public CombatDirectionDetailView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void SkillDetailRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: SkillDetailRowViewModel row } control ||
            DataContext is not CombatDirectionDetailViewModel detail)
        {
            return;
        }

        var host = control.FindAncestorOfType<ItemsControl>();
        var section = host?.Name switch
        {
            "DamageRows" => detail.DamageSection,
            "HealingRows" => detail.HealingSection,
            "ShieldRows" => detail.ShieldSection,
            _ => null
        };

        section?.SelectRow(row);
        e.Handled = true;
    }
}
