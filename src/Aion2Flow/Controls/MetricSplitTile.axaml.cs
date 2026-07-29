using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Cloris.Aion2Flow.Controls;

public partial class MetricSplitTile : UserControl
{
    public static readonly DirectProperty<MetricSplitTile, string?> LabelProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, string?>(
            nameof(Label),
            control => control.Label,
            (control, value) => control.Label = value);

    public static readonly DirectProperty<MetricSplitTile, double> PrimaryValueProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, double>(
            nameof(PrimaryValue),
            control => control.PrimaryValue,
            (control, value) => control.PrimaryValue = value);

    public static readonly DirectProperty<MetricSplitTile, double> SecondaryValueProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, double>(
            nameof(SecondaryValue),
            control => control.SecondaryValue,
            (control, value) => control.SecondaryValue = value);

    public static readonly DirectProperty<MetricSplitTile, string?> PrimaryFormatterProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, string?>(
            nameof(PrimaryFormatter),
            control => control.PrimaryFormatter,
            (control, value) => control.PrimaryFormatter = value);

    public static readonly DirectProperty<MetricSplitTile, string?> SecondaryFormatterProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, string?>(
            nameof(SecondaryFormatter),
            control => control.SecondaryFormatter,
            (control, value) => control.SecondaryFormatter = value);

    public static readonly DirectProperty<MetricSplitTile, string?> PrimaryPrefixProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, string?>(
            nameof(PrimaryPrefix),
            control => control.PrimaryPrefix,
            (control, value) => control.PrimaryPrefix = value);

    public static readonly DirectProperty<MetricSplitTile, string?> PrimarySuffixProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, string?>(
            nameof(PrimarySuffix),
            control => control.PrimarySuffix,
            (control, value) => control.PrimarySuffix = value);

    public static readonly DirectProperty<MetricSplitTile, string?> SecondaryPrefixProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, string?>(
            nameof(SecondaryPrefix),
            control => control.SecondaryPrefix,
            (control, value) => control.SecondaryPrefix = value);

    public static readonly DirectProperty<MetricSplitTile, string?> SecondarySuffixProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, string?>(
            nameof(SecondarySuffix),
            control => control.SecondarySuffix,
            (control, value) => control.SecondarySuffix = value);

    public MetricSplitTile()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public string? Label
    {
        get;
        set => SetAndRaise(LabelProperty, ref field, value);
    }

    public double PrimaryValue
    {
        get;
        set => SetAndRaise(PrimaryValueProperty, ref field, value);
    }

    public double SecondaryValue
    {
        get;
        set => SetAndRaise(SecondaryValueProperty, ref field, value);
    }

    public string? PrimaryFormatter
    {
        get;
        set => SetAndRaise(PrimaryFormatterProperty, ref field, value);
    } = "N0";

    public string? SecondaryFormatter
    {
        get;
        set => SetAndRaise(SecondaryFormatterProperty, ref field, value);
    } = "0.0";

    public string? PrimaryPrefix
    {
        get;
        set => SetAndRaise(PrimaryPrefixProperty, ref field, value);
    }

    public string? PrimarySuffix
    {
        get;
        set => SetAndRaise(PrimarySuffixProperty, ref field, value);
    }

    public string? SecondaryPrefix
    {
        get;
        set => SetAndRaise(SecondaryPrefixProperty, ref field, value);
    }

    public string? SecondarySuffix
    {
        get;
        set => SetAndRaise(SecondarySuffixProperty, ref field, value);
    }
}
