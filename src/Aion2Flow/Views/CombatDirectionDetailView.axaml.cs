using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Views;

public partial class CombatDirectionDetailView : UserControl
{
    public static readonly StyledProperty<bool> EnableSkillSelectionProperty = AvaloniaProperty.Register<CombatDirectionDetailView, bool>(nameof(EnableSkillSelection), true);

    public CombatDirectionDetailView()
    {
        AvaloniaXamlLoader.Load(this);
        ApplySkillSelectionMode();
    }

    public event EventHandler<CombatDetailSelectionRequestedEventArgs>? SelectionRequested;

    public bool EnableSkillSelection { get => GetValue(EnableSkillSelectionProperty); set => SetValue(EnableSkillSelectionProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == EnableSkillSelectionProperty)
            ApplySkillSelectionMode();
    }

    private void DamageSectionTapped(object? sender, TappedEventArgs e)
        => RequestSectionSelection(CombatContributionCategory.Damage, e);

    private void HealingSectionTapped(object? sender, TappedEventArgs e)
        => RequestSectionSelection(CombatContributionCategory.Healing, e);

    private void ShieldSectionTapped(object? sender, TappedEventArgs e)
        => RequestSectionSelection(CombatContributionCategory.Shield, e);

    private void SkillDetailRowTapped(object? sender, TappedEventArgs e)
    {
        if (!EnableSkillSelection)
            return;

        if (sender is not Control { DataContext: SkillDetailRowViewModel row } control ||
            DataContext is not CombatDirectionDetailViewModel detail)
        {
            return;
        }

        var host = control.FindAncestorOfType<AnimatedItemsView>();
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
        if (!EnableSkillSelection)
            return;

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

    private void ApplySkillSelectionMode()
    {
        PseudoClasses.Set(":skill-selection-enabled", EnableSkillSelection);
        SetSelectionEnabled("DamageRows");
        SetSelectionEnabled("HealingRows");
        SetSelectionEnabled("ShieldRows");
    }

    private void SetSelectionEnabled(string name)
    {
        if (this.FindControl<AnimatedItemsView>(name) is { } list)
            list.IsSelectionEnabled = EnableSkillSelection;
    }
}
