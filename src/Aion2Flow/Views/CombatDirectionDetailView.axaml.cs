using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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

    private void ChildSelectionRequested(object? sender, CombatDetailSelectionRequestedEventArgs e)
        => SelectionRequested?.Invoke(this, e);

    private void ApplySkillSelectionMode()
    {
        if (this.FindControl<DamageDetailView>("DamageDetail") is { } damage)
            damage.EnableSkillSelection = EnableSkillSelection;
        if (this.FindControl<HealingDetailView>("HealingDetail") is { } healing)
            healing.EnableSkillSelection = EnableSkillSelection;
        if (this.FindControl<ShieldDetailView>("ShieldDetail") is { } shield)
            shield.EnableSkillSelection = EnableSkillSelection;
    }
}
