using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Cloris.Aion2Flow.Controls;

public sealed class AnimatedItemsViewItem : ContentControl
{
    public static readonly DirectProperty<AnimatedItemsViewItem, bool> IsSelectedProperty =
        AvaloniaProperty.RegisterDirect<AnimatedItemsViewItem, bool>(
            nameof(IsSelected),
            item => item.IsSelected);

    public static readonly DirectProperty<AnimatedItemsViewItem, bool> IsAddingProperty =
        AvaloniaProperty.RegisterDirect<AnimatedItemsViewItem, bool>(
            nameof(IsAdding),
            item => item.IsAdding);

    public static readonly DirectProperty<AnimatedItemsViewItem, bool> IsRemovingProperty =
        AvaloniaProperty.RegisterDirect<AnimatedItemsViewItem, bool>(
            nameof(IsRemoving),
            item => item.IsRemoving);


    internal AnimatedItemsView? Owner { get; set; }

    public bool IsSelected
    {
        get;
        internal set
        {
            SetAndRaise(IsSelectedProperty, ref field, value);
            PseudoClasses.Set(":selected", value);
        }
    }

    public bool IsAdding
    {
        get;
        internal set
        {
            SetAndRaise(IsAddingProperty, ref field, value);
            PseudoClasses.Set(":adding", value);
        }
    }

    public bool IsRemoving
    {
        get;
        internal set
        {
            SetAndRaise(IsRemovingProperty, ref field, value);
            PseudoClasses.Set(":removing", value);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !IsRemoving)
        {
            Owner?.SelectedItem = Content;
        }
    }
}
