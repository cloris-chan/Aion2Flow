using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Cloris.Aion2Flow.Presentation;

internal sealed class AppTextRenderingPolicy
{
    internal static readonly TextOptions ApplicationOptions = new()
    {
        TextRenderingMode = TextRenderingMode.Antialias,
        TextHintingMode = TextHintingMode.None,
        BaselinePixelAlignment = BaselinePixelAlignment.Unaligned
    };

    internal static readonly AttachedProperty<TextOptions> OptionsProperty =
        AvaloniaProperty.RegisterAttached<AppTextRenderingPolicy, TopLevel, TextOptions>("Options");

    static AppTextRenderingPolicy()
    {
        OptionsProperty.Changed.AddClassHandler<TopLevel>(static (topLevel, change) =>
            TextOptions.SetTextOptions(topLevel, change.GetNewValue<TextOptions>()));
    }

    private AppTextRenderingPolicy()
    {
    }

    internal static Style CreateTopLevelStyle() => new(static selector => selector.OfType<TopLevel>())
    {
        Setters =
        {
            new Setter(OptionsProperty, ApplicationOptions)
        }
    };
}
