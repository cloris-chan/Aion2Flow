using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.Controls;

public partial class DurationMetricTile : UserControl
{
    public static readonly DirectProperty<DurationMetricTile, string?> LabelProperty =
        AvaloniaProperty.RegisterDirect<DurationMetricTile, string?>(
            nameof(Label),
            control => control.Label,
            (control, value) => control.Label = value);

    public static readonly DirectProperty<DurationMetricTile, TimeSpan> DurationProperty =
        AvaloniaProperty.RegisterDirect<DurationMetricTile, TimeSpan>(
            nameof(Duration),
            control => control.Duration,
            (control, value) => control.Duration = value);

    public static readonly DirectProperty<DurationMetricTile, EncounterTimeDisplayFormat> DisplayFormatProperty =
        AvaloniaProperty.RegisterDirect<DurationMetricTile, EncounterTimeDisplayFormat>(
            nameof(DisplayFormat),
            control => control.DisplayFormat,
            (control, value) => control.DisplayFormat = value);

    public static readonly DirectProperty<DurationMetricTile, object?> StableWidthScopeKeyProperty =
        AvaloniaProperty.RegisterDirect<DurationMetricTile, object?>(
            nameof(StableWidthScopeKey),
            control => control.StableWidthScopeKey,
            (control, value) => control.StableWidthScopeKey = value);

    public DurationMetricTile()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public string? Label
    {
        get;
        set => SetAndRaise(LabelProperty, ref field, value);
    }

    public TimeSpan Duration
    {
        get;
        set => SetAndRaise(DurationProperty, ref field, value);
    }

    public EncounterTimeDisplayFormat DisplayFormat
    {
        get;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }

            SetAndRaise(DisplayFormatProperty, ref field, value);
        }
    } = EncounterTimeDisplayFormat.DecimalSeconds;

    public object? StableWidthScopeKey
    {
        get;
        set => SetAndRaise(StableWidthScopeKeyProperty, ref field, value);
    }
}
