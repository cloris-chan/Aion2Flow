using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Cloris.Aion2Flow.Views;

public partial class CombatantDetailsFlyoutView : UserControl
{
    private Border? _rootCard;

	public CombatantDetailsFlyoutView()
	{
		AvaloniaXamlLoader.Load(this);
        _rootCard = Content as Border ?? this.FindControl<Border>("RootCard");
	}

    public void ConfigureViewport(double availableWidth, double availableHeight)
    {
        var rootCard = _rootCard ??= Content as Border ?? this.FindControl<Border>("RootCard");
        if (rootCard is null)
        {
            return;
        }

        var width = availableWidth > 0
            ? Math.Min(1080d, availableWidth * 0.92d)
            : 920d;
        if (availableWidth > 0 && availableWidth < 760d)
        {
            width = availableWidth;
        }
        else
        {
            width = Math.Max(760d, width);
        }

        var height = availableHeight > 0
            ? Math.Min(840d, availableHeight * 0.92d)
            : 720d;
        if (availableHeight > 0 && availableHeight < 560d)
        {
            height = availableHeight;
        }
        else
        {
            height = Math.Max(560d, height);
        }

        rootCard.Width = width;
        rootCard.MaxWidth = width;
        rootCard.MaxHeight = height;
    }
}
