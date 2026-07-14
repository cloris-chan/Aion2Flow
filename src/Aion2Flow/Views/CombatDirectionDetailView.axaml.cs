using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Views;

public partial class CombatDirectionDetailView : UserControl
{
    public CombatDirectionDetailView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public event EventHandler<CombatDetailSelectionRequestedEventArgs>? SelectionRequested;

    private void DamageSectionTapped(object? sender, TappedEventArgs e)
        => RequestSectionSelection(CombatContributionCategory.Damage, e);

    private void HealingSectionTapped(object? sender, TappedEventArgs e)
        => RequestSectionSelection(CombatContributionCategory.Healing, e);

    private void ShieldSectionTapped(object? sender, TappedEventArgs e)
        => RequestSectionSelection(CombatContributionCategory.Shield, e);

    private void SkillDetailRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: SkillDetailRowViewModel row } control ||
            DataContext is not CombatDirectionDetailViewModel detail)
        {
            return;
        }

        var host = control.FindAncestorOfType<ItemsControl>();
        var selection = host?.Name switch
        {
            "DamageRows" => (detail.DamageSection, CombatContributionCategory.Damage),
            "HealingRows" => (detail.HealingSection, CombatContributionCategory.Healing),
            "ShieldRows" => (detail.ShieldSection, CombatContributionCategory.Shield),
            _ => ((SkillDetailSectionViewModel Section, CombatContributionCategory Category)?)null
        };

        if (selection is null)
            return;

        selection.Value.Section.SelectRow(row);
        SelectionRequested?.Invoke(this, new CombatDetailSelectionRequestedEventArgs(selection.Value.Category, row.BaseKey, row.DisplayName));
        e.Handled = true;
    }

    private void RequestSectionSelection(CombatContributionCategory category, TappedEventArgs e)
    {
        if (e.Source is Visual source &&
            (source is Button || source.FindAncestorOfType<Button>() is not null))
        {
            return;
        }

        if (DataContext is not CombatDirectionDetailViewModel detail)
            return;

        var section = category switch
        {
            CombatContributionCategory.Damage => detail.DamageSection,
            CombatContributionCategory.Healing => detail.HealingSection,
            CombatContributionCategory.Shield => detail.ShieldSection,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
        };

        section.SelectRow(null);
        SelectionRequested?.Invoke(this, new CombatDetailSelectionRequestedEventArgs(category, null, null));
        e.Handled = true;
    }
}
