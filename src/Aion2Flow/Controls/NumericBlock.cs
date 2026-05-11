using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Cloris.Aion2Flow.Controls;

internal readonly record struct NumericBlockDiagnostics(
    int MeasureCount,
    int RenderCount,
    int TextFormatCount,
    int GlyphRebuildCount,
    int TypefaceResolveCount,
    int ForegroundResolveCount,
    int MeasureInvalidationCount,
    int VisualInvalidationCount);

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

    public static readonly DirectProperty<NumericBlock, bool> UseCompactNotationProperty =
        AvaloniaProperty.RegisterDirect<NumericBlock, bool>(
            nameof(UseCompactNotation),
            control => control.UseCompactNotation,
            (control, value) => control.UseCompactNotation = value);

    public static readonly StyledProperty<bool> UsePercentageNotationProperty =
        AvaloniaProperty.Register<NumericBlock, bool>(nameof(UsePercentageNotation), false);

    public static readonly DirectProperty<NumericBlock, double> CompactThresholdProperty =
        AvaloniaProperty.RegisterDirect<NumericBlock, double>(
            nameof(CompactThreshold),
            control => control.CompactThreshold,
            (control, value) => control.CompactThreshold = value);

    public static readonly DirectProperty<NumericBlock, int> CompactSignificantDigitsProperty =
        AvaloniaProperty.RegisterDirect<NumericBlock, int>(
            nameof(CompactSignificantDigits),
            control => control.CompactSignificantDigits,
            (control, value) => control.CompactSignificantDigits = value);

    public static readonly DirectProperty<NumericBlock, string?> PrefixProperty =
        AvaloniaProperty.RegisterDirect<NumericBlock, string?>(
            nameof(Prefix),
            control => control.Prefix,
            (control, value) => control.Prefix = value);

    public static readonly DirectProperty<NumericBlock, string?> SuffixProperty =
        AvaloniaProperty.RegisterDirect<NumericBlock, string?>(
            nameof(Suffix),
            control => control.Suffix,
            (control, value) => control.Suffix = value);

    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        TextBlock.TextAlignmentProperty.AddOwner<NumericBlock>();

    public static readonly DirectProperty<NumericBlock, double?> FixedTextWidthProperty =
        AvaloniaProperty.RegisterDirect<NumericBlock, double?>(
            nameof(FixedTextWidth),
            control => control.FixedTextWidth,
            (control, value) => control.FixedTextWidth = value);

    public static readonly DirectProperty<NumericBlock, object?> StableWidthScopeKeyProperty =
        AvaloniaProperty.RegisterDirect<NumericBlock, object?>(
            nameof(StableWidthScopeKey),
            control => control.StableWidthScopeKey,
            (control, value) => control.StableWidthScopeKey = value);

    private const int InitialBufferCapacity = 32;
    private const int MaxCachedTypefaces = 64;
    private const int MaxCachedGlyphs = 4096;

    private static readonly Lock s_cacheGate = new();
    private static readonly Dictionary<TypefaceCacheKey, CachedTypeface> s_typefaceCache = [];
    private static readonly Dictionary<GlyphCacheKey, CachedGlyph> s_glyphCache = [];

    private readonly GlyphInfoBuffer _glyphInfos = new();
    private char[] _characterBuffer = new char[InitialBufferCapacity];
    private char[] _formatBuffer = new char[InitialBufferCapacity];
    private GlyphInfo[] _glyphInfoBuffer = new GlyphInfo[InitialBufferCapacity];
    private CachedTypeface? _cachedTypeface;
    private GlyphRun? _glyphRun;
    private Rect _glyphBounds;
    private Size _desiredSize;
    private IBrush? _resolvedForeground;
    private TypefaceCacheKey _cachedTypefaceKey;
    private double _cachedBaseline;
    private double _stableDesiredWidth;
    private int _formattedCharacterCount;
    private bool _isFormattedTextDirty = true;
    private bool _isGlyphRunDirty = true;
    private bool _isTypefaceDirty = true;
    private bool _isForegroundDirty = true;
    private int _measureCount;
    private int _renderCount;
    private int _textFormatCount;
    private int _glyphRebuildCount;
    private int _typefaceResolveCount;
    private int _foregroundResolveCount;
    private int _measureInvalidationCount;
    private int _visualInvalidationCount;

    public NumericBlock()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateForeground();
    }

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
            InvalidateFormattedText();
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
        get;
        set => SetAndRaise(UseCompactNotationProperty, ref field, value);
    }

    public bool UsePercentageNotation
    {
        get => GetValue(UsePercentageNotationProperty);
        set => SetValue(UsePercentageNotationProperty, value);
    }

    public double CompactThreshold
    {
        get;
        set => SetAndRaise(CompactThresholdProperty, ref field, value);
    } = 1000d;

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

    public TextAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public double? FixedTextWidth
    {
        get;
        set => SetAndRaise(FixedTextWidthProperty, ref field, value);
    }

    public object? StableWidthScopeKey
    {
        get;
        set => SetAndRaise(StableWidthScopeKeyProperty, ref field, value);
    }

    public void ResetStableWidth()
    {
        ResetStableWidthCore();
        InvalidateGlyphRun(formatDirty: false, forceMeasure: true);
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
            change.Property == SuffixProperty)
        {
            ResetStableWidthCore();
            InvalidateGlyphRun(formatDirty: true, forceMeasure: true);
        }
        else if (change.Property == FontFamilyProperty ||
            change.Property == FontSizeProperty ||
            change.Property == FontStyleProperty ||
            change.Property == FontWeightProperty ||
            change.Property == FontStretchProperty)
        {
            InvalidateTypeface();
            ResetStableWidthCore();
            InvalidateGlyphRun(formatDirty: false, forceMeasure: true);
        }
        else if (change.Property == FixedTextWidthProperty ||
            change.Property == StableWidthScopeKeyProperty)
        {
            ResetStableWidthCore();
            InvalidateGlyphRun(formatDirty: false, forceMeasure: true);
        }
        else if (change.Property == ForegroundProperty)
        {
            InvalidateForeground();
        }
        else if (change.Property == TextAlignmentProperty)
        {
            RequestVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _measureCount++;
        EnsureGlyphRun();
        return _desiredSize;
    }

    public override void Render(DrawingContext context)
    {
        _renderCount++;
        base.Render(context);
        EnsureGlyphRun();

        if (_glyphRun is null)
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
            context.DrawGlyphRun(foreground, _glyphRun);
        }
    }

    private void EnsureGlyphRun()
    {
        if (!_isGlyphRunDirty)
        {
            return;
        }

        RebuildGlyphRun();
    }

    private void RebuildGlyphRun()
    {
        _isGlyphRunDirty = false;
        _glyphRun = null;
        _glyphBounds = default;
        _desiredSize = default;

        if (_isFormattedTextDirty)
        {
            UpdateFormattedCharacters();
        }

        var fontSize = FontSize > 0 ? FontSize : 12d;
        if (fontSize <= 0)
        {
            return;
        }

        var characterCount = _formattedCharacterCount;
        if (characterCount <= 0)
        {
            return;
        }

        if (!EnsureTypeface(fontSize))
        {
            return;
        }

        EnsureGlyphBufferCapacity(characterCount);
        var advanceWidth = 0d;
        for (var index = 0; index < characterCount; index++)
        {
            var cachedGlyph = ResolveGlyph(_characterBuffer[index]);
            _glyphInfoBuffer[index] = new GlyphInfo(cachedGlyph.GlyphIndex, index, cachedGlyph.Advance, default);
            advanceWidth += cachedGlyph.Advance;
        }

        _glyphInfos.SetBuffer(_glyphInfoBuffer, characterCount);

        _glyphRebuildCount++;
        _glyphRun = new GlyphRun(
            _cachedTypeface!.GlyphTypeface,
            fontSize,
            new ReadOnlyMemory<char>(_characterBuffer, 0, characterCount),
            _glyphInfos,
            new Point(0, _cachedBaseline),
            0);

        _glyphBounds = _glyphRun.Bounds;
        var width = Math.Ceiling(Math.Max(_glyphBounds.Width, advanceWidth));
        var height = Math.Ceiling(Math.Max(_glyphBounds.Height, fontSize));
        _desiredSize = ApplyStableWidth(width, height);
    }

    private bool UpdateFormattedCharacters()
    {
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

        _textFormatCount++;
        var characterCount = FormatValue(formatOptions, ref _formatBuffer);
        var formattedTextChanged =
            characterCount != _formattedCharacterCount ||
            !_formatBuffer.AsSpan(0, characterCount).SequenceEqual(_characterBuffer.AsSpan(0, _formattedCharacterCount));

        if (formattedTextChanged)
        {
            (_characterBuffer, _formatBuffer) = (_formatBuffer, _characterBuffer);
            _formattedCharacterCount = characterCount;
        }

        _isFormattedTextDirty = false;
        return formattedTextChanged;
    }

    private void InvalidateFormattedText()
    {
        if (_isGlyphRunDirty)
        {
            _isFormattedTextDirty = true;
            return;
        }

        if (!UpdateFormattedCharacters())
        {
            return;
        }

        var previousDesiredSize = _desiredSize;
        RebuildGlyphRun();

        if (!NearlyEquals(previousDesiredSize, _desiredSize))
        {
            RequestMeasure();
        }

        RequestVisual();
    }

    private int FormatValue(in NumericFormatOptions formatOptions, ref char[] buffer)
    {
        while (true)
        {
            if (NumericFormatter.TryFormat(Value, buffer, formatOptions, out var charsWritten))
            {
                return charsWritten;
            }

            EnsureCharacterBufferCapacity(ref buffer, buffer.Length * 2);
        }
    }

    private void InvalidateGlyphRun(bool formatDirty, bool forceMeasure)
    {
        if (formatDirty)
        {
            _isFormattedTextDirty = true;
        }

        _isGlyphRunDirty = true;
        if (forceMeasure)
        {
            RequestMeasure();
        }

        RequestVisual();
    }

    private IBrush? ResolveForeground()
    {
        if (!_isForegroundDirty)
        {
            return _resolvedForeground;
        }

        _foregroundResolveCount++;
        _isForegroundDirty = false;
        _resolvedForeground = Foreground;
        if (_resolvedForeground is not null)
        {
            return _resolvedForeground;
        }

        if (Application.Current?.TryGetResource("ThemeForegroundBrush", ActualThemeVariant, out var resource) == true &&
            resource is IBrush themeForeground)
        {
            _resolvedForeground = themeForeground;
            return _resolvedForeground;
        }

        _resolvedForeground = Brushes.White;
        return _resolvedForeground;
    }

    private bool EnsureTypeface(double fontSize)
    {
        var fontFamily = FontFamily ?? FontFamily.Default;
        var key = new TypefaceCacheKey(
            fontFamily.ToString() ?? string.Empty,
            fontSize,
            FontStyle,
            FontWeight,
            FontStretch);

        if (!_isTypefaceDirty &&
            _cachedTypeface is not null &&
            _cachedTypefaceKey.Equals(key))
        {
            return true;
        }

        if (TryGetCachedTypeface(key, out var cachedTypeface))
        {
            UseCachedTypeface(key, cachedTypeface);
            return true;
        }

        _typefaceResolveCount++;
        var typeface = new Typeface(fontFamily, FontStyle, FontWeight, FontStretch);
        if (!FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface) || glyphTypeface is null)
        {
            _cachedTypeface = null;
            _isTypefaceDirty = false;
            return false;
        }

        var fontMetrics = glyphTypeface.Metrics;
        var glyphAdvanceScale = fontMetrics.DesignEmHeight > 0
            ? fontSize / fontMetrics.DesignEmHeight
            : 1d;
        var ascent = fontMetrics.DesignEmHeight > 0
            ? fontMetrics.Ascent * fontSize / fontMetrics.DesignEmHeight
            : fontSize;
        cachedTypeface = new CachedTypeface(glyphTypeface, Math.Abs(ascent), glyphAdvanceScale);
        CacheTypeface(key, cachedTypeface);
        UseCachedTypeface(key, cachedTypeface);
        return true;
    }

    private CachedGlyph ResolveGlyph(char character)
    {
        var key = new GlyphCacheKey(_cachedTypefaceKey, character);
        lock (s_cacheGate)
        {
            if (s_glyphCache.TryGetValue(key, out var cachedGlyph))
            {
                return cachedGlyph;
            }
        }

        var resolvedGlyph = ResolveUncachedGlyph(character);
        lock (s_cacheGate)
        {
            if (s_glyphCache.Count >= MaxCachedGlyphs)
            {
                s_glyphCache.Clear();
            }

            s_glyphCache[key] = resolvedGlyph;
        }

        return resolvedGlyph;
    }

    private CachedGlyph ResolveUncachedGlyph(char character)
    {
        var cachedTypeface = _cachedTypeface!;
        var glyphTypeface = cachedTypeface.GlyphTypeface;
        var characterToGlyphMap = glyphTypeface.CharacterToGlyphMap;
        if (!characterToGlyphMap.TryGetGlyph(character, out var glyphIndex))
        {
            glyphIndex = characterToGlyphMap.GetGlyph('?');
        }

        var glyphAdvance = glyphTypeface.TryGetHorizontalGlyphAdvance(glyphIndex, out var advance)
            ? advance
            : 0d;
        return new CachedGlyph(glyphIndex, glyphAdvance * cachedTypeface.GlyphAdvanceScale);
    }

    private Size ApplyStableWidth(double width, double height)
    {
        if (FixedTextWidth is { } fixedTextWidth && IsValidFixedTextWidth(fixedTextWidth))
        {
            return new Size(fixedTextWidth, height);
        }

        _stableDesiredWidth = Math.Max(_stableDesiredWidth, width);
        return new Size(_stableDesiredWidth, height);
    }

    private void ResetStableWidthCore()
    {
        _stableDesiredWidth = 0d;
    }

    private void UseCachedTypeface(TypefaceCacheKey key, CachedTypeface cachedTypeface)
    {
        _cachedTypefaceKey = key;
        _cachedTypeface = cachedTypeface;
        _cachedBaseline = cachedTypeface.Baseline;
        _isTypefaceDirty = false;
    }

    private static bool TryGetCachedTypeface(TypefaceCacheKey key, out CachedTypeface cachedTypeface)
    {
        lock (s_cacheGate)
        {
            if (s_typefaceCache.TryGetValue(key, out var value))
            {
                cachedTypeface = value;
                return true;
            }
        }

        cachedTypeface = null!;
        return false;
    }

    private static void CacheTypeface(TypefaceCacheKey key, CachedTypeface cachedTypeface)
    {
        lock (s_cacheGate)
        {
            if (s_typefaceCache.Count >= MaxCachedTypefaces)
            {
                s_typefaceCache.Clear();
                s_glyphCache.Clear();
            }

            s_typefaceCache[key] = cachedTypeface;
        }
    }

    private void InvalidateTypeface()
    {
        _isTypefaceDirty = true;
    }

    private void InvalidateForeground()
    {
        _isForegroundDirty = true;
        RequestVisual();
    }

    private void RequestMeasure()
    {
        _measureInvalidationCount++;
        InvalidateMeasure();
    }

    private void RequestVisual()
    {
        _visualInvalidationCount++;
        InvalidateVisual();
    }

    private static bool IsValidFixedTextWidth(double width) =>
        !double.IsNaN(width) && !double.IsInfinity(width) && width >= 0d;

    private static bool NearlyEquals(Size left, Size right) =>
        Math.Abs(left.Width - right.Width) < 0.001d &&
        Math.Abs(left.Height - right.Height) < 0.001d;

    internal NumericBlockDiagnostics GetDiagnostics() =>
        new(
            _measureCount,
            _renderCount,
            _textFormatCount,
            _glyphRebuildCount,
            _typefaceResolveCount,
            _foregroundResolveCount,
            _measureInvalidationCount,
            _visualInvalidationCount);

    internal void ResetDiagnostics()
    {
        _measureCount = 0;
        _renderCount = 0;
        _textFormatCount = 0;
        _glyphRebuildCount = 0;
        _typefaceResolveCount = 0;
        _foregroundResolveCount = 0;
        _measureInvalidationCount = 0;
        _visualInvalidationCount = 0;
    }

    internal string GetFormattedTextForDiagnostics()
    {
        if (_isFormattedTextDirty)
        {
            UpdateFormattedCharacters();
        }

        return new string(_characterBuffer, 0, _formattedCharacterCount);
    }

    internal Size MeasureForDiagnostics(Size availableSize) => MeasureOverride(availableSize);

    internal static void ClearStaticCachesForDiagnostics()
    {
        lock (s_cacheGate)
        {
            s_typefaceCache.Clear();
            s_glyphCache.Clear();
        }
    }

    private readonly record struct TypefaceCacheKey(
        string FontFamily,
        double FontSize,
        FontStyle FontStyle,
        FontWeight FontWeight,
        FontStretch FontStretch);

    private readonly record struct GlyphCacheKey(TypefaceCacheKey Typeface, char Character);

    private sealed record CachedTypeface(
        GlyphTypeface GlyphTypeface,
        double Baseline,
        double GlyphAdvanceScale);

    private readonly struct CachedGlyph(ushort glyphIndex, double advance)
    {
        public readonly ushort GlyphIndex = glyphIndex;
        public readonly double Advance = advance;
    }

    private static void EnsureCharacterBufferCapacity(ref char[] buffer, int requiredCapacity)
    {
        if (buffer.Length >= requiredCapacity)
        {
            return;
        }

        var nextCapacity = Math.Max(requiredCapacity, buffer.Length * 2);
        Array.Resize(ref buffer, nextCapacity);
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
