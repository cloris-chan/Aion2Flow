using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Cloris.Aion2Flow.Controls;

public partial class MetricSplitTile : UserControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<MetricSplitTile, string?>(nameof(Label));

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

    public static readonly DirectProperty<MetricSplitTile, int> PrimaryFractionDigitsProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, int>(
            nameof(PrimaryFractionDigits),
            control => control.PrimaryFractionDigits,
            (control, value) => control.PrimaryFractionDigits = value);

    public static readonly DirectProperty<MetricSplitTile, int> SecondaryFractionDigitsProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, int>(
            nameof(SecondaryFractionDigits),
            control => control.SecondaryFractionDigits,
            (control, value) => control.SecondaryFractionDigits = value);

    public static readonly DirectProperty<MetricSplitTile, bool> PrimaryTrimTrailingZerosProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, bool>(
            nameof(PrimaryTrimTrailingZeros),
            control => control.PrimaryTrimTrailingZeros,
            (control, value) => control.PrimaryTrimTrailingZeros = value);

    public static readonly DirectProperty<MetricSplitTile, bool> SecondaryTrimTrailingZerosProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, bool>(
            nameof(SecondaryTrimTrailingZeros),
            control => control.SecondaryTrimTrailingZeros,
            (control, value) => control.SecondaryTrimTrailingZeros = value);

    public static readonly DirectProperty<MetricSplitTile, bool> PrimaryUseGroupingProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, bool>(
            nameof(PrimaryUseGrouping),
            control => control.PrimaryUseGrouping,
            (control, value) => control.PrimaryUseGrouping = value);

    public static readonly DirectProperty<MetricSplitTile, bool> SecondaryUseGroupingProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, bool>(
            nameof(SecondaryUseGrouping),
            control => control.SecondaryUseGrouping,
            (control, value) => control.SecondaryUseGrouping = value);

    public static readonly DirectProperty<MetricSplitTile, bool> PrimaryUsePercentageNotationProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, bool>(
            nameof(PrimaryUsePercentageNotation),
            control => control.PrimaryUsePercentageNotation,
            (control, value) => control.PrimaryUsePercentageNotation = value);

    public static readonly DirectProperty<MetricSplitTile, bool> SecondaryUsePercentageNotationProperty =
        AvaloniaProperty.RegisterDirect<MetricSplitTile, bool>(
            nameof(SecondaryUsePercentageNotation),
            control => control.SecondaryUsePercentageNotation,
            (control, value) => control.SecondaryUsePercentageNotation = value);

    public static readonly StyledProperty<string?> PrimaryPrefixProperty =
        AvaloniaProperty.Register<MetricSplitTile, string?>(nameof(PrimaryPrefix));

    public static readonly StyledProperty<string?> PrimarySuffixProperty =
        AvaloniaProperty.Register<MetricSplitTile, string?>(nameof(PrimarySuffix));

    public static readonly StyledProperty<string?> SecondaryPrefixProperty =
        AvaloniaProperty.Register<MetricSplitTile, string?>(nameof(SecondaryPrefix));

    public static readonly StyledProperty<string?> SecondarySuffixProperty =
        AvaloniaProperty.Register<MetricSplitTile, string?>(nameof(SecondarySuffix));

    public MetricSplitTile()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
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

    public int PrimaryFractionDigits
    {
        get;
        set => SetAndRaise(PrimaryFractionDigitsProperty, ref field, value);
    }

    public int SecondaryFractionDigits
    {
        get;
        set => SetAndRaise(SecondaryFractionDigitsProperty, ref field, value);
    } = 1;

    public bool PrimaryTrimTrailingZeros
    {
        get;
        set => SetAndRaise(PrimaryTrimTrailingZerosProperty, ref field, value);
    } = true;

    public bool SecondaryTrimTrailingZeros
    {
        get;
        set => SetAndRaise(SecondaryTrimTrailingZerosProperty, ref field, value);
    }

    public bool PrimaryUseGrouping
    {
        get;
        set => SetAndRaise(PrimaryUseGroupingProperty, ref field, value);
    } = true;

    public bool SecondaryUseGrouping
    {
        get;
        set => SetAndRaise(SecondaryUseGroupingProperty, ref field, value);
    }

    public bool PrimaryUsePercentageNotation
    {
        get;
        set => SetAndRaise(PrimaryUsePercentageNotationProperty, ref field, value);
    }

    public bool SecondaryUsePercentageNotation
    {
        get;
        set => SetAndRaise(SecondaryUsePercentageNotationProperty, ref field, value);
    }

    public string? PrimaryPrefix
    {
        get => GetValue(PrimaryPrefixProperty);
        set => SetValue(PrimaryPrefixProperty, value);
    }

    public string? PrimarySuffix
    {
        get => GetValue(PrimarySuffixProperty);
        set => SetValue(PrimarySuffixProperty, value);
    }

    public string? SecondaryPrefix
    {
        get => GetValue(SecondaryPrefixProperty);
        set => SetValue(SecondaryPrefixProperty, value);
    }

    public string? SecondarySuffix
    {
        get => GetValue(SecondarySuffixProperty);
        set => SetValue(SecondarySuffixProperty, value);
    }
}
