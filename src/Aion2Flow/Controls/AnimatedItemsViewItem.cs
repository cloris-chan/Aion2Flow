using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Cloris.Aion2Flow.Controls;

public sealed class AnimatedItemsViewItem : ContentControl
{
    public static readonly DirectProperty<AnimatedItemsViewItem, bool> IsSelectedProperty = AvaloniaProperty.RegisterDirect<AnimatedItemsViewItem, bool>(nameof(IsSelected), static item => item.IsSelected);

    private bool _isSelected;

    internal AnimatedItemsView? Owner { get; set; }
    internal int Generation { get; set; }
    internal double VirtualTop { get; set; }
    internal bool IsViewportVisible { get; set; }
    internal TimeSpan ConfiguredAddRemoveDuration { get; set; } = TimeSpan.MinValue;
    internal TimeSpan ConfiguredMoveDuration { get; set; } = TimeSpan.MinValue;

    public bool IsSelected
    {
        get => _isSelected;
        internal set
        {
            if (SetAndRaise(IsSelectedProperty, ref _isSelected, value))
                PseudoClasses.Set(":selected", value);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Owner?.SelectedItem = Content;
    }
}
