using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Cloris.Aion2Flow.Controls;

public partial class MetricTile : UserControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<MetricTile, string?>(nameof(Label));

    public static readonly DirectProperty<MetricTile, double> ValueProperty =
        AvaloniaProperty.RegisterDirect<MetricTile, double>(
            nameof(Value),
            control => control.Value,
            (control, value) => control.Value = value);

    public static readonly DirectProperty<MetricTile, int> FractionDigitsProperty =
        AvaloniaProperty.RegisterDirect<MetricTile, int>(
            nameof(FractionDigits),
            control => control.FractionDigits,
            (control, value) => control.FractionDigits = value);

    public static readonly DirectProperty<MetricTile, bool> TrimTrailingZerosProperty =
        AvaloniaProperty.RegisterDirect<MetricTile, bool>(
            nameof(TrimTrailingZeros),
            control => control.TrimTrailingZeros,
            (control, value) => control.TrimTrailingZeros = value);

    public static readonly DirectProperty<MetricTile, bool> UseGroupingProperty =
        AvaloniaProperty.RegisterDirect<MetricTile, bool>(
            nameof(UseGrouping),
            control => control.UseGrouping,
            (control, value) => control.UseGrouping = value);

    public static readonly DirectProperty<MetricTile, bool> UseCompactNotationProperty =
        AvaloniaProperty.RegisterDirect<MetricTile, bool>(
            nameof(UseCompactNotation),
            control => control.UseCompactNotation,
            (control, value) => control.UseCompactNotation = value);

    public static readonly DirectProperty<MetricTile, bool> UsePercentageNotationProperty =
        AvaloniaProperty.RegisterDirect<MetricTile, bool>(
            nameof(UsePercentageNotation),
            control => control.UsePercentageNotation,
            (control, value) => control.UsePercentageNotation = value);

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

    public static readonly StyledProperty<string?> PrefixProperty =
        AvaloniaProperty.Register<MetricTile, string?>(nameof(Prefix));

    public static readonly StyledProperty<string?> SuffixProperty =
        AvaloniaProperty.Register<MetricTile, string?>(nameof(Suffix));

    public MetricTile()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public double Value
    {
        get;
        set => SetAndRaise(ValueProperty, ref field, value);
    }

    public int FractionDigits
    {
        get;
        set => SetAndRaise(FractionDigitsProperty, ref field, value);
    }

    public bool TrimTrailingZeros
    {
        get;
        set => SetAndRaise(TrimTrailingZerosProperty, ref field, value);
    } = true;

    public bool UseGrouping
    {
        get;
        set => SetAndRaise(UseGroupingProperty, ref field, value);
    } = true;

    public bool UseCompactNotation
    {
        get;
        set => SetAndRaise(UseCompactNotationProperty, ref field, value);
    }

    public bool UsePercentageNotation
    {
        get;
        set => SetAndRaise(UsePercentageNotationProperty, ref field, value);
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
        get => GetValue(PrefixProperty);
        set => SetValue(PrefixProperty, value);
    }

    public string? Suffix
    {
        get => GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }
}
