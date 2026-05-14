using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.Services;

namespace Cloris.Aion2Flow.Controls;

public sealed class DisplayContextProvider
{
    private DisplayContextProvider()
    {
    }

    public static readonly AttachedProperty<SceneDisplayContext?> DisplayContextProperty = AvaloniaProperty.RegisterAttached<DisplayContextProvider, Control, SceneDisplayContext?>("DisplayContext", inherits: true);

    public static SceneDisplayContext? GetDisplayContext(Control control) => control.GetValue(DisplayContextProperty);

    public static void SetDisplayContext(Control control, SceneDisplayContext? value) => control.SetValue(DisplayContextProperty, value);
}

public class EntityDisplay : UserControl
{
    public static readonly DirectProperty<EntityDisplay, int> EntityIdProperty =
        AvaloniaProperty.RegisterDirect<EntityDisplay, int>(nameof(EntityId), x => x.EntityId, (x, value) => x.EntityId = value);

    public static readonly DirectProperty<EntityDisplay, bool> ShowClassIconProperty =
        AvaloniaProperty.RegisterDirect<EntityDisplay, bool>(nameof(ShowClassIcon), x => x.ShowClassIcon, (x, value) => x.ShowClassIcon = value);

    public static readonly DirectProperty<EntityDisplay, string> TextClassesProperty =
        AvaloniaProperty.RegisterDirect<EntityDisplay, string>(nameof(TextClasses), x => x.TextClasses, (x, value) => x.TextClasses = value);

    public static readonly StyledProperty<bool> IsClassIconAlternateProperty =
        AvaloniaProperty.Register<EntityDisplay, bool>(nameof(IsClassIconAlternate));

    public static readonly StyledProperty<double> ClassIconOverlayOpacityProperty =
        AvaloniaProperty.Register<EntityDisplay, double>(nameof(ClassIconOverlayOpacity));

    private readonly Grid _layout;
    private readonly TextBlock _textBlock;
    private Image? _classImage;
    private Image? _overlayImage;
    private TranslateTransform? _classImageTransform;
    private Panel? _iconHost;
    private string _appliedTextClasses = string.Empty;
    private string _currentText = string.Empty;
    private IImage? _currentIcon;
    private bool _isIconVisible;

    public EntityDisplay()
    {
        _textBlock = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        _layout = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto),new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 6,
            Children =
            {
                _textBlock
            }
        };
        Content = _layout;
        Grid.SetColumn(_textBlock, 0);
        Grid.SetColumnSpan(_textBlock, 2);
        UpdateDisplay();
    }

    public int EntityId
    {
        get;
        set => SetAndRaise(EntityIdProperty, ref field, value);
    }

    public bool ShowClassIcon
    {
        get;
        set => SetAndRaise(ShowClassIconProperty, ref field, value);
    } = true;

    public string TextClasses
    {
        get;
        set => SetAndRaise(TextClassesProperty, ref field, value ?? string.Empty);
    } = string.Empty;

    public bool IsClassIconAlternate
    {
        get => GetValue(IsClassIconAlternateProperty);
        set => SetValue(IsClassIconAlternateProperty, value);
    }

    public double ClassIconOverlayOpacity
    {
        get => GetValue(ClassIconOverlayOpacityProperty);
        set => SetValue(ClassIconOverlayOpacityProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (ShouldUpdate(change.Property))
        {
            UpdateDisplay();
        }
        else if (change.Property == IsClassIconAlternateProperty)
        {
            UpdateClassIconTransform();
        }
        else if (change.Property == ClassIconOverlayOpacityProperty)
        {
            UpdateClassIconOverlayOpacity();
        }
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        UpdateDisplay();
    }

    protected virtual bool ShouldUpdate(AvaloniaProperty property)
        => property == EntityIdProperty ||
           property == ShowClassIconProperty ||
           property == TextClassesProperty ||
           property == DisplayContextProvider.DisplayContextProperty;

    protected virtual string ResolveText(SceneDisplayContext? context, int entityId)
        => context?.ResolveEntityName(entityId) ?? FormatEntityId(entityId);

    protected virtual CharacterClass? ResolveClass(SceneDisplayContext? context, int entityId)
        => context?.ResolvePcClass(entityId);

    protected void UpdateDisplay()
    {
        _appliedTextClasses = DisplayClassList.Apply(_textBlock.Classes, _appliedTextClasses, TextClasses);

        var context = DisplayContextProvider.GetDisplayContext(this);
        var entityId = EntityId;
        var text = ResolveText(context, entityId);
        if (!string.Equals(_currentText, text, StringComparison.Ordinal))
        {
            _textBlock.Text = text;
            _currentText = text;
        }

        var icon = ShowClassIcon && context is not null
            ? DisplayIconCache.ResolveClassIcon(ResolveClass(context, entityId))
            : null;
        if (!ReferenceEquals(_currentIcon, icon))
        {
            if (icon is not null)
            {
                EnsureIconHost().Source = icon;
            }
            else
            {
                _classImage?.Source = null;
            }

            _currentIcon = icon;
        }

        SetIconVisible(icon is not null);
    }

    protected static string FormatEntityId(int entityId)
        => entityId > 0 ? entityId.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    private void SetIconVisible(bool visible)
    {
        if (_isIconVisible == visible)
        {
            return;
        }

        if (visible)
        {
            EnsureIconHost().IsVisible = true;
        }
        else
        {
            _iconHost?.IsVisible = false;
            UpdateClassIconTransform();
        }

        Grid.SetColumn(_textBlock, visible ? 1 : 0);
        Grid.SetColumnSpan(_textBlock, visible ? 1 : 2);
        _isIconVisible = visible;
    }

    private Image EnsureIconHost()
    {
        if (_classImage is not null)
        {
            return _classImage;
        }

        _classImageTransform = new TranslateTransform();
        _classImage = new Image
        {
            Name = "BaseImage",
            Width = 30,
            Height = 60,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = _classImageTransform
        };
        _overlayImage = new Image
        {
            Name = "OverlayImage",
            Width = 32,
            Height = 32,
            Source = DisplayIconCache.OverlayIcon,
            Opacity = ClassIconOverlayOpacity,
            ZIndex = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _iconHost = new Panel
        {
            Width = 32,
            Height = 32,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Panel
                {
                    Width = 30,
                    Height = 30,
                    ClipToBounds = true,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { _classImage }
                },
                _overlayImage
            }
        };
        _layout.Children.Insert(0, _iconHost);
        return _classImage;
    }

    private void UpdateClassIconTransform()
    {
        if (_classImage is null)
        {
            return;
        }

        (_classImageTransform ??= new TranslateTransform()).Y = IsClassIconAlternate ? -30 : 0;
        _classImage.RenderTransform ??= _classImageTransform;
    }

    private void UpdateClassIconOverlayOpacity()
    {
        if (_overlayImage is not null)
        {
            _overlayImage.Opacity = ClassIconOverlayOpacity;
        }
    }
}

public sealed class PcDisplay : EntityDisplay
{
    protected override string ResolveText(SceneDisplayContext? context, int entityId)
        => context?.ResolvePcName(entityId) ?? FormatEntityId(entityId);
}

public sealed class NpcDisplay : EntityDisplay
{
    public static readonly DirectProperty<NpcDisplay, int> NpcCodeProperty =
        AvaloniaProperty.RegisterDirect<NpcDisplay, int>(nameof(NpcCode), x => x.NpcCode, (x, value) => x.NpcCode = value);

    public NpcDisplay()
    {
        ShowClassIcon = false;
    }

    public int NpcCode
    {
        get;
        set => SetAndRaise(NpcCodeProperty, ref field, value);
    }

    protected override bool ShouldUpdate(AvaloniaProperty property)
        => base.ShouldUpdate(property) || property == NpcCodeProperty;

    protected override string ResolveText(SceneDisplayContext? context, int entityId)
    {
        if (NpcCode > 0)
        {
            return context?.ResolveNpcCodeName(NpcCode) ?? NpcCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return context?.ResolveNpcName(entityId) ?? FormatEntityId(entityId);
    }

    protected override CharacterClass? ResolveClass(SceneDisplayContext? context, int entityId)
        => null;
}

public sealed class SkillDisplay : TextDisplayControl
{
    public static readonly DirectProperty<SkillDisplay, int> SkillCodeProperty =
        AvaloniaProperty.RegisterDirect<SkillDisplay, int>(nameof(SkillCode), x => x.SkillCode, (x, value) => x.SkillCode = value);

    public int SkillCode
    {
        get;
        set => SetAndRaise(SkillCodeProperty, ref field, value);
    }

    protected override bool ShouldUpdate(AvaloniaProperty property)
        => base.ShouldUpdate(property) || property == SkillCodeProperty;

    protected override string ResolveText(SceneDisplayContext? context)
        => context?.ResolveSkillName(SkillCode) ?? (SkillCode > 0 ? SkillCode.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
}

public sealed class MapDisplay : TextDisplayControl
{
    public static readonly DirectProperty<MapDisplay, uint> MapIdProperty =
        AvaloniaProperty.RegisterDirect<MapDisplay, uint>(nameof(MapId), x => x.MapId, (x, value) => x.MapId = value);

    public static readonly DirectProperty<MapDisplay, bool> UseBracketsProperty =
        AvaloniaProperty.RegisterDirect<MapDisplay, bool>(nameof(UseBrackets), x => x.UseBrackets, (x, value) => x.UseBrackets = value);

    public uint MapId
    {
        get;
        set => SetAndRaise(MapIdProperty, ref field, value);
    }

    public bool UseBrackets
    {
        get;
        set => SetAndRaise(UseBracketsProperty, ref field, value);
    }

    protected override bool ShouldUpdate(AvaloniaProperty property)
        => base.ShouldUpdate(property) || property == MapIdProperty || property == UseBracketsProperty;

    protected override string ResolveText(SceneDisplayContext? context)
    {
        var text = context?.ResolveMapName(MapId) ?? string.Empty;
        if (string.IsNullOrEmpty(text) && MapId > 0)
        {
            text = MapId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return UseBrackets ? $"[{text}]" : text;
    }
}

public abstract class TextDisplayControl : UserControl
{
    public static readonly DirectProperty<TextDisplayControl, string> TextClassesProperty =
        AvaloniaProperty.RegisterDirect<TextDisplayControl, string>(nameof(TextClasses), x => x.TextClasses, (x, value) => x.TextClasses = value);

    private readonly TextBlock _textBlock = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private string _appliedTextClasses = string.Empty;
    private string _currentText = string.Empty;

    protected TextDisplayControl()
    {
        Content = _textBlock;
    }

    public string TextClasses
    {
        get;
        set => SetAndRaise(TextClassesProperty, ref field, value ?? string.Empty);
    } = string.Empty;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (ShouldUpdate(change.Property))
        {
            UpdateText();
        }
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        UpdateText();
    }

    protected virtual bool ShouldUpdate(AvaloniaProperty property)
        => property == TextClassesProperty || property == DisplayContextProvider.DisplayContextProperty;

    protected abstract string ResolveText(SceneDisplayContext? context);

    private void UpdateText()
    {
        _appliedTextClasses = DisplayClassList.Apply(_textBlock.Classes, _appliedTextClasses, TextClasses);

        var text = ResolveText(DisplayContextProvider.GetDisplayContext(this));
        if (!string.Equals(_currentText, text, StringComparison.Ordinal))
        {
            _textBlock.Text = text;
            _currentText = text;
        }
    }
}

internal static class DisplayClassList
{
    public static string Apply(Classes classes, string applied, string? nextValue)
    {
        var next = nextValue ?? string.Empty;
        if (string.Equals(applied, next, StringComparison.Ordinal))
            return applied;

        Apply(classes, applied, add: false);
        Apply(classes, next, add: true);
        return next;
    }

    private static void Apply(Classes classes, string value, bool add)
    {
        if (value.Length == 0)
            return;

        var index = 0;
        while (index < value.Length)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index]))
                index++;

            var start = index;
            while (index < value.Length && !char.IsWhiteSpace(value[index]))
                index++;

            if (index == start)
                continue;

            var className = start == 0 && index == value.Length
                ? value
                : value.AsSpan(start, index - start).ToString();
            if (add)
                classes.Add(className);
            else
                classes.Remove(className);
        }
    }
}

internal static class DisplayIconCache
{
    public static IImage OverlayIcon { get => field ??= Load("Overlay.webp"); }
    private static IImage GladiatorIcon { get => field ??= Load("Gladiator.webp"); }
    private static IImage TemplarIcon { get => field ??= Load("Templar.webp"); }
    private static IImage AssassinIcon { get => field ??= Load("Assassin.webp"); }
    private static IImage RangerIcon { get => field ??= Load("Ranger.webp"); }
    private static IImage SorcererIcon { get => field ??= Load("Sorcerer.webp"); }
    private static IImage ElementalistIcon { get => field ??= Load("Elementalist.webp"); }
    private static IImage ClericIcon { get => field ??= Load("Cleric.webp"); }
    private static IImage ChanterIcon { get => field ??= Load("Chanter.webp"); }

    public static IImage? ResolveClassIcon(CharacterClass? characterClass)
    {
        return characterClass switch
        {
            CharacterClass.Gladiator => GladiatorIcon,
            CharacterClass.Templar => TemplarIcon,
            CharacterClass.Assassin => AssassinIcon,
            CharacterClass.Ranger => RangerIcon,
            CharacterClass.Sorcerer => SorcererIcon,
            CharacterClass.Elementalist => ElementalistIcon,
            CharacterClass.Cleric => ClericIcon,
            CharacterClass.Chanter => ChanterIcon,
            _ => null,
        };
    }

    private static Bitmap Load(string fileName)
        => new(AssetLoader.Open(new Uri($"avares://Aion2Flow/Assets/Images/{fileName}")));
}
