using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Cloris.Aion2Flow.Controls;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Views;

public abstract class CombatSkillDetailView : UserControl
{
    public static readonly StyledProperty<bool> EnableSkillSelectionProperty = AvaloniaProperty.Register<CombatSkillDetailView, bool>(nameof(EnableSkillSelection), true);

    private readonly CombatContributionCategory _category;
    private readonly string _rowsControlName;

    protected CombatSkillDetailView(CombatContributionCategory category, string rowsControlName)
    {
        _category = category;
        _rowsControlName = rowsControlName;
    }

    public event EventHandler<CombatDetailSelectionRequestedEventArgs>? SelectionRequested;

    public bool EnableSkillSelection { get => GetValue(EnableSkillSelectionProperty); set => SetValue(EnableSkillSelectionProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == EnableSkillSelectionProperty)
            ApplySkillSelectionMode();
    }

    protected void InitializeSkillSelectionMode() => ApplySkillSelectionMode();

    protected void SectionTapped(object? sender, TappedEventArgs e)
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

        ResolveSection(detail).SelectRow(null);
        SelectionRequested?.Invoke(this, new CombatDetailSelectionRequestedEventArgs(_category, null, null));
        e.Handled = true;
    }

    protected void SkillDetailRowTapped(object? sender, TappedEventArgs e)
    {
        if (!EnableSkillSelection ||
            sender is not Control { DataContext: SkillDetailRowViewModel row } ||
            DataContext is not CombatDirectionDetailViewModel detail)
        {
            return;
        }

        ResolveSection(detail).SelectRow(row);
        SelectionRequested?.Invoke(this, new CombatDetailSelectionRequestedEventArgs(_category, row.BaseKey, row.DisplayName));
        e.Handled = true;
    }

    private SkillDetailSectionViewModel ResolveSection(CombatDirectionDetailViewModel detail) => _category switch
    {
        CombatContributionCategory.Damage => detail.DamageSection,
        CombatContributionCategory.Healing => detail.HealingSection,
        CombatContributionCategory.Shield => detail.ShieldSection,
        _ => throw new InvalidOperationException($"Unsupported combat skill detail category: {_category}.")
    };

    private void ApplySkillSelectionMode()
    {
        PseudoClasses.Set(":skill-selection-enabled", EnableSkillSelection);
        if (this.FindControl<AnimatedItemsView>(_rowsControlName) is { } list)
            list.IsSelectionEnabled = EnableSkillSelection;
    }
}
