using Avalonia.Markup.Xaml;
using Cloris.Aion2Flow.Assets.Icons;
using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.Styles;

public sealed partial class AppTheme : Avalonia.Styling.Styles
{
    public AppTheme()
    {
        AvaloniaXamlLoader.Load(this);
        Resources.MergedDictionaries.Add(new IconGeometries());
        Add(AppTextRenderingPolicy.CreateTopLevelStyle());
    }
}
