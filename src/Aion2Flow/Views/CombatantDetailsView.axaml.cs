using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;

namespace Cloris.Aion2Flow.Views;

public partial class CombatantDetailsView : UserControl
{
    public static readonly StyledProperty<ScrollBarVisibility> VerticalScrollBarVisibilityProperty =
        AvaloniaProperty.Register<CombatantDetailsView, ScrollBarVisibility>(nameof(VerticalScrollBarVisibility), ScrollBarVisibility.Hidden);

    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => GetValue(VerticalScrollBarVisibilityProperty);
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    public CombatantDetailsView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
