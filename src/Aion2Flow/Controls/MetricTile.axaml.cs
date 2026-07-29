using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Cloris.Aion2Flow.Controls;

public partial class MetricTile : UserControl
{
    public static readonly DirectProperty<MetricTile, string?> LabelProperty =
        AvaloniaProperty.RegisterDirect<MetricTile, string?>(
            nameof(Label),
            control => control.Label,
            (control, value) => control.Label = value);

    public static readonly DirectProperty<MetricTile, double> ValueProperty =
        AvaloniaProperty.RegisterDirect<MetricTile, double>(
            nameof(Value),
            control => control.Value,
            (control, value) => control.Value = value);

    public static readonly DirectProperty<MetricTile, string?> FormatterProperty =
        AvaloniaProperty.RegisterDirect<MetricTile, string?>(
            nameof(Formatter),
            control => control.Formatter,
            (control, value) => control.Formatter = value);

    public static readonly DirectProperty<MetricTile, bool> UseCompactNotationProperty =
        AvaloniaProperty.RegisterDirect<MetricTile, bool>(
            nameof(UseCompactNotation),
            control => control.UseCompactNotation,
            (control, value) => control.UseCompactNotation = value);

    public static readonly DirectProperty<MetricTile, double> CompactThresholdProperty =
        AvaloniaProperty.RegisterDirect<MetricTile, double>(
            nameof(CompactThreshold),
            control => control.CompactThreshold,
            (control, value) => control.CompactThreshold = value);

    public static readonly DirectProperty<MetricTile, int> CompactSignificantDigitsProperty =
        AvaloniaProperty.RegisterDirect<MetricTile, int>(
            nameof(CompactSignificantDigits),
            control => control.CompactSignificantDigits,
            (control, value) => control.CompactSignificantDigits = value);

    public static readonly DirectProperty<MetricTile, string?> PrefixProperty =
        AvaloniaProperty.RegisterDirect<MetricTile, string?>(
            nameof(Prefix),
            control => control.Prefix,
            (control, value) => control.Prefix = value);

    public static readonly DirectProperty<MetricTile, string?> SuffixProperty =
        AvaloniaProperty.RegisterDirect<MetricTile, string?>(
            nameof(Suffix),
            control => control.Suffix,
            (control, value) => control.Suffix = value);

    public MetricTile()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public string? Label
    {
        get;
        set => SetAndRaise(LabelProperty, ref field, value);
    }

    public double Value
    {
        get;
        set => SetAndRaise(ValueProperty, ref field, value);
    }

    public string? Formatter
    {
        get;
        set => SetAndRaise(FormatterProperty, ref field, value);
    } = "N0";

    public bool UseCompactNotation
    {
        get;
        set => SetAndRaise(UseCompactNotationProperty, ref field, value);
    }

    public double CompactThreshold
    {
        get;
        set => SetAndRaise(CompactThresholdProperty, ref field, value);
    } = 1000D;

    public int CompactSignificantDigits
    {
        get;
        set => SetAndRaise(CompactSignificantDigitsProperty, ref field, value);
    } = 3;

    public string? Prefix
    {
        get;
        set => SetAndRaise(PrefixProperty, ref field, value);
    }

    public string? Suffix
    {
        get;
        set => SetAndRaise(SuffixProperty, ref field, value);
    }
}
