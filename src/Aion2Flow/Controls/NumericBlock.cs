using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Cloris.Aion2Flow.Controls;

public sealed class NumericBlock : Control
{
    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextBlock.FontFamilyProperty.AddOwner<NumericBlock>();

    public static readonly StyledProperty<double> FontSizeProperty =
        TextBlock.FontSizeProperty.AddOwner<NumericBlock>();

    public static readonly StyledProperty<FontStyle> FontStyleProperty =
        TextBlock.FontStyleProperty.AddOwner<NumericBlock>();

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        TextBlock.FontWeightProperty.AddOwner<NumericBlock>();

    public static readonly StyledProperty<FontStretch> FontStretchProperty =
        TextBlock.FontStretchProperty.AddOwner<NumericBlock>();

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextBlock.ForegroundProperty.AddOwner<NumericBlock>();

    public static readonly DirectProperty<NumericBlock, double> ValueProperty =
        AvaloniaProperty.RegisterDirect<NumericBlock, double>(
            nameof(Value),
            control => control.Value,
            (control, value) => control.Value = value);

    public static readonly StyledProperty<int> FractionDigitsProperty =
        AvaloniaProperty.Register<NumericBlock, int>(nameof(FractionDigits), 0);

    public static readonly StyledProperty<bool> TrimTrailingZerosProperty =
        AvaloniaProperty.Register<NumericBlock, bool>(nameof(TrimTrailingZeros), true);

    public static readonly StyledProperty<bool> UseGroupingProperty =
        AvaloniaProperty.Register<NumericBlock, bool>(nameof(UseGrouping), false);

    public static readonly StyledProperty<bool> UseCompactNotationProperty =
        AvaloniaProperty.Register<NumericBlock, bool>(nameof(UseCompactNotation), false);

    public static readonly StyledProperty<bool> UsePercentageNotationProperty =
        AvaloniaProperty.Register<NumericBlock, bool>(nameof(UsePercentageNotation), false);

    public static readonly StyledProperty<double> CompactThresholdProperty =
        AvaloniaProperty.Register<NumericBlock, double>(nameof(CompactThreshold), 1000d);

    public static readonly StyledProperty<int> CompactSignificantDigitsProperty =
        AvaloniaProperty.Register<NumericBlock, int>(nameof(CompactSignificantDigits), 3);

    public static readonly StyledProperty<string?> PrefixProperty =
        AvaloniaProperty.Register<NumericBlock, string?>(nameof(Prefix));

    public static readonly StyledProperty<string?> SuffixProperty =
        AvaloniaProperty.Register<NumericBlock, string?>(nameof(Suffix));

    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        TextBlock.TextAlignmentProperty.AddOwner<NumericBlock>();

    private const int InitialBufferCapacity = 32;

    private readonly GlyphInfoBuffer _glyphInfos = new();
    private char[] _characterBuffer = new char[InitialBufferCapacity];
    private GlyphInfo[] _glyphInfoBuffer = new GlyphInfo[InitialBufferCapacity];
    private GlyphRun? _glyphRun;
    private Geometry? _glyphGeometry;
    private Rect _glyphBounds;
    private Size _desiredSize;
    private bool _isGlyphRunDirty = true;

    public double Value
    {
        get;
        set
        {
            if (field.Equals(value))
            {
                return;
            }

            SetAndRaise(ValueProperty, ref field, value);
            InvalidateGlyphRun();
        }
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontStyle FontStyle
    {
        get => GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontStretch FontStretch
    {
        get => GetValue(FontStretchProperty);
        set => SetValue(FontStretchProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public int FractionDigits
    {
        get => GetValue(FractionDigitsProperty);
        set => SetValue(FractionDigitsProperty, value);
    }

    public bool TrimTrailingZeros
    {
        get => GetValue(TrimTrailingZerosProperty);
        set => SetValue(TrimTrailingZerosProperty, value);
    }

    public bool UseGrouping
    {
        get => GetValue(UseGroupingProperty);
        set => SetValue(UseGroupingProperty, value);
    }

    public bool UseCompactNotation
    {
        get => GetValue(UseCompactNotationProperty);
        set => SetValue(UseCompactNotationProperty, value);
    }

    public bool UsePercentageNotation
    {
        get => GetValue(UsePercentageNotationProperty);
        set => SetValue(UsePercentageNotationProperty, value);
    }

    public double CompactThreshold
    {
        get => GetValue(CompactThresholdProperty);
        set => SetValue(CompactThresholdProperty, value);
    }

    public int CompactSignificantDigits
    {
        get => GetValue(CompactSignificantDigitsProperty);
        set => SetValue(CompactSignificantDigitsProperty, value);
    }

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

    public TextAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FractionDigitsProperty ||
            change.Property == TrimTrailingZerosProperty ||
            change.Property == UseGroupingProperty ||
            change.Property == UseCompactNotationProperty ||
            change.Property == UsePercentageNotationProperty ||
            change.Property == CompactThresholdProperty ||
            change.Property == CompactSignificantDigitsProperty ||
            change.Property == PrefixProperty ||
            change.Property == SuffixProperty ||
            change.Property == FontFamilyProperty ||
            change.Property == FontSizeProperty ||
            change.Property == FontStyleProperty ||
            change.Property == FontWeightProperty ||
            change.Property == FontStretchProperty)
        {
            InvalidateGlyphRun();
        }
        else if (change.Property == ForegroundProperty || change.Property == TextAlignmentProperty)
        {
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureGlyphRun();
        return _desiredSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        EnsureGlyphRun();

        if (_glyphGeometry is null)
        {
            return;
        }

        var foreground = ResolveForeground();
        if (foreground is null)
        {
            return;
        }

        var bounds = Bounds;
        var x = TextAlignment switch
        {
            TextAlignment.Center => Math.Max(0, (bounds.Width - _glyphBounds.Width) * 0.5),
            TextAlignment.Right => Math.Max(0, bounds.Width - _glyphBounds.Width),
            _ => 0
        };

        var y = Math.Max(0, (bounds.Height - _glyphBounds.Height) * 0.5);
        var translation = Matrix.CreateTranslation(x - _glyphBounds.X, y - _glyphBounds.Y);

        using (context.PushTransform(translation))
        {
            context.DrawGeometry(foreground, null, _glyphGeometry);
        }
    }

    private void EnsureGlyphRun()
    {
        if (!_isGlyphRunDirty)
        {
            return;
        }

        _isGlyphRunDirty = false;
        _glyphRun = null;
        _glyphGeometry = null;
        _glyphBounds = default;
        _desiredSize = default;

        var fontSize = FontSize > 0 ? FontSize : 12d;
        if (fontSize <= 0)
        {
            return;
        }

        var formatOptions = new NumericFormatOptions(
            FractionDigits,
            TrimTrailingZeros,
            UseGrouping,
            UseCompactNotation,
            UsePercentageNotation,
            CompactThreshold,
            CompactSignificantDigits,
            Prefix,
            Suffix);

        var characterCount = FormatValue(formatOptions);
        if (characterCount <= 0)
        {
            return;
        }

        var typeface = new Typeface(FontFamily ?? FontFamily.Default, FontStyle, FontWeight, FontStretch);
        if (!FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface) || glyphTypeface is null)
        {
            return;
        }

        var fontMetrics = glyphTypeface.Metrics;
        var glyphAdvanceScale = fontMetrics.DesignEmHeight > 0
            ? fontSize / fontMetrics.DesignEmHeight
            : 1d;

        EnsureGlyphBufferCapacity(characterCount);
        var characterToGlyphMap = glyphTypeface.CharacterToGlyphMap;
        for (var index = 0; index < characterCount; index++)
        {
            var character = _characterBuffer[index];
            if (!characterToGlyphMap.TryGetGlyph(character, out var glyphIndex))
            {
                glyphIndex = characterToGlyphMap.GetGlyph('?');
            }

            var glyphAdvance = glyphTypeface.TryGetHorizontalGlyphAdvance(glyphIndex, out var advance)
                ? advance
                : 0d;

            _glyphInfoBuffer[index] = new GlyphInfo(
                glyphIndex,
                index,
                glyphAdvance * glyphAdvanceScale,
                default);
        }

        _glyphInfos.SetBuffer(_glyphInfoBuffer, characterCount);

        var baseline = fontMetrics.DesignEmHeight > 0
            ? fontMetrics.Ascent * fontSize / fontMetrics.DesignEmHeight
            : fontSize;

        _glyphRun = new GlyphRun(
            glyphTypeface,
            fontSize,
            new ReadOnlyMemory<char>(_characterBuffer, 0, characterCount),
            _glyphInfos,
            new Point(0, baseline),
            0);

        _glyphGeometry = _glyphRun.BuildGeometry();
        _glyphBounds = _glyphGeometry.Bounds;
        var width = Math.Ceiling(_glyphBounds.Width);
        var height = Math.Ceiling(Math.Max(_glyphBounds.Height, fontSize));
        _desiredSize = new Size(width, height);
    }

    private int FormatValue(in NumericFormatOptions formatOptions)
    {
        while (true)
        {
            if (NumericFormatter.TryFormat(Value, _characterBuffer, formatOptions, out var charsWritten))
            {
                return charsWritten;
            }

            EnsureCharacterBufferCapacity(_characterBuffer.Length * 2);
        }
    }

    private void InvalidateGlyphRun()
    {
        _isGlyphRunDirty = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private IBrush? ResolveForeground()
    {
        if (Foreground is not null)
        {
            return Foreground;
        }

        if (Application.Current?.TryGetResource("ThemeForegroundBrush", ActualThemeVariant, out var resource) == true &&
            resource is IBrush themeForeground)
        {
            return themeForeground;
        }

        return Brushes.White;
    }

    private void EnsureCharacterBufferCapacity(int requiredCapacity)
    {
        if (_characterBuffer.Length >= requiredCapacity)
        {
            return;
        }

        var nextCapacity = Math.Max(requiredCapacity, _characterBuffer.Length * 2);
        Array.Resize(ref _characterBuffer, nextCapacity);
    }

    private void EnsureGlyphBufferCapacity(int requiredCapacity)
    {
        if (_glyphInfoBuffer.Length >= requiredCapacity)
        {
            return;
        }

        var nextCapacity = Math.Max(requiredCapacity, _glyphInfoBuffer.Length * 2);
        Array.Resize(ref _glyphInfoBuffer, nextCapacity);
    }

    private sealed class GlyphInfoBuffer : IReadOnlyList<GlyphInfo>
    {
        private GlyphInfo[] _buffer = [];
        private int _count;

        public GlyphInfo this[int index] => _buffer[index];

        public int Count => _count;

        public void SetBuffer(GlyphInfo[] buffer, int count)
        {
            _buffer = buffer;
            _count = count;
        }

        public IEnumerator<GlyphInfo> GetEnumerator()
        {
            for (var index = 0; index < _count; index++)
            {
                yield return _buffer[index];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
