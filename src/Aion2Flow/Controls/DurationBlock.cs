using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.Controls;

public sealed class DurationBlock : Panel
{
    public static readonly DirectProperty<DurationBlock, TimeSpan> DurationProperty =
        AvaloniaProperty.RegisterDirect<DurationBlock, TimeSpan>(
            nameof(Duration),
            control => control.Duration,
            (control, value) => control.Duration = value);

    public static readonly DirectProperty<DurationBlock, EncounterTimeDisplayFormat> DisplayFormatProperty =
        AvaloniaProperty.RegisterDirect<DurationBlock, EncounterTimeDisplayFormat>(
            nameof(DisplayFormat),
            control => control.DisplayFormat,
            (control, value) => control.DisplayFormat = value);

    public static readonly DirectProperty<DurationBlock, object?> StableWidthScopeKeyProperty =
        AvaloniaProperty.RegisterDirect<DurationBlock, object?>(
            nameof(StableWidthScopeKey),
            control => control.StableWidthScopeKey,
            (control, value) => control.StableWidthScopeKey = value);

    private const string SeparatorPrefix = ":";

    private readonly NumericBlock _decimalSecondsBlock = new()
    {
        Formatter = "0.0",
        Suffix = "s",
        TextAlignment = TextAlignment.Right
    };

    private readonly NumericBlock _minutesBlock = new()
    {
        Formatter = "00",
        TextAlignment = TextAlignment.Right
    };

    private readonly NumericBlock _secondsBlock = new()
    {
        Formatter = "00",
        Prefix = SeparatorPrefix,
        TextAlignment = TextAlignment.Right
    };

    private double _stableDesiredWidth;

    public DurationBlock()
    {
        Children.Add(_decimalSecondsBlock);
        Children.Add(_minutesBlock);
        Children.Add(_secondsBlock);
        UpdatePartVisibility();
        UpdateValues();
    }

    public TimeSpan Duration
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            SetAndRaise(DurationProperty, ref field, value);
            UpdateValues();
        }
    }

    public EncounterTimeDisplayFormat DisplayFormat
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }

            SetAndRaise(DisplayFormatProperty, ref field, value);
            ResetStableWidth();
            UpdatePartVisibility();
            UpdateValues();
        }
    } = EncounterTimeDisplayFormat.DecimalSeconds;

    public object? StableWidthScopeKey
    {
        get;
        set
        {
            if (Equals(field, value))
            {
                return;
            }

            SetAndRaise(StableWidthScopeKeyProperty, ref field, value);
            _decimalSecondsBlock.StableWidthScopeKey = value;
            _minutesBlock.StableWidthScopeKey = value;
            _secondsBlock.StableWidthScopeKey = value;
            ResetContainerStableWidth();
        }
    }

    internal NumericBlock DecimalSecondsBlockForDiagnostics => _decimalSecondsBlock;

    internal NumericBlock MinutesBlockForDiagnostics => _minutesBlock;

    internal NumericBlock SecondsBlockForDiagnostics => _secondsBlock;

    protected override Size MeasureOverride(Size availableSize)
    {
        var childConstraint = new Size(double.PositiveInfinity, availableSize.Height);
        double contentWidth;
        double contentHeight;

        if (DisplayFormat == EncounterTimeDisplayFormat.DecimalSeconds)
        {
            _decimalSecondsBlock.Measure(childConstraint);
            contentWidth = _decimalSecondsBlock.DesiredSize.Width;
            contentHeight = _decimalSecondsBlock.DesiredSize.Height;
        }
        else
        {
            _minutesBlock.Measure(childConstraint);
            _secondsBlock.Measure(childConstraint);
            contentWidth = _minutesBlock.DesiredSize.Width + _secondsBlock.DesiredSize.Width;
            contentHeight = Math.Max(
                _minutesBlock.DesiredSize.Height,
                _secondsBlock.DesiredSize.Height);
        }

        _stableDesiredWidth = Math.Max(_stableDesiredWidth, contentWidth);
        return new Size(_stableDesiredWidth, contentHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (DisplayFormat == EncounterTimeDisplayFormat.DecimalSeconds)
        {
            var width = _decimalSecondsBlock.DesiredSize.Width;
            var x = Math.Max(0d, finalSize.Width - width);
            _decimalSecondsBlock.Arrange(new Rect(x, 0d, width, finalSize.Height));
            return finalSize;
        }

        var minutesWidth = _minutesBlock.DesiredSize.Width;
        var secondsWidth = _secondsBlock.DesiredSize.Width;
        var xOffset = Math.Max(0d, finalSize.Width - minutesWidth - secondsWidth);
        _minutesBlock.Arrange(new Rect(
            xOffset,
            0d,
            minutesWidth,
            finalSize.Height));
        _secondsBlock.Arrange(new Rect(
            xOffset + minutesWidth,
            0d,
            secondsWidth,
            finalSize.Height));
        return finalSize;
    }

    private void UpdatePartVisibility()
    {
        var showDecimalSeconds =
            DisplayFormat == EncounterTimeDisplayFormat.DecimalSeconds;
        _decimalSecondsBlock.IsVisible = showDecimalSeconds;
        _minutesBlock.IsVisible = !showDecimalSeconds;
        _secondsBlock.IsVisible = !showDecimalSeconds;
    }

    private void UpdateValues()
    {
        var normalizedDuration =
            Duration < TimeSpan.Zero ? TimeSpan.Zero : Duration;

        if (DisplayFormat == EncounterTimeDisplayFormat.DecimalSeconds)
        {
            _decimalSecondsBlock.Value = normalizedDuration.TotalSeconds;
            return;
        }

        var totalSeconds =
            normalizedDuration.Ticks / TimeSpan.TicksPerSecond;
        var totalMinutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        _minutesBlock.Value = totalMinutes;
        _secondsBlock.Value = seconds;
    }

    private void ResetStableWidth()
    {
        _decimalSecondsBlock.ResetStableWidth();
        _minutesBlock.ResetStableWidth();
        _secondsBlock.ResetStableWidth();
        ResetContainerStableWidth();
    }

    private void ResetContainerStableWidth()
    {
        _stableDesiredWidth = 0d;
        InvalidateMeasure();
    }
}
