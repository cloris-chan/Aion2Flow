using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Cloris.Aion2Flow.Resources;
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

public abstract class IconTextDisplay : UserControl
{
    public static readonly DirectProperty<IconTextDisplay, int> EntityIdProperty =
        AvaloniaProperty.RegisterDirect<IconTextDisplay, int>(nameof(EntityId), x => x.EntityId, (x, value) => x.EntityId = value);

    public static readonly DirectProperty<IconTextDisplay, bool> ShowIconProperty =
        AvaloniaProperty.RegisterDirect<IconTextDisplay, bool>(nameof(ShowIcon), x => x.ShowIcon, (x, value) => x.ShowIcon = value);

    public static readonly StyledProperty<bool> IsIconAlternateProperty =
        AvaloniaProperty.Register<IconTextDisplay, bool>(nameof(IsIconAlternate));

    public static readonly StyledProperty<double> IconOverlayOpacityProperty =
        AvaloniaProperty.Register<IconTextDisplay, double>(nameof(IconOverlayOpacity));

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<IconTextDisplay, double>(nameof(IconSize), 30);

    public static readonly StyledProperty<double> IconSpacingProperty =
        AvaloniaProperty.Register<IconTextDisplay, double>(nameof(IconSpacing), 4);

    private readonly Grid _layout;
    private readonly TextBlock _textBlock;
    private Image? _iconImage;
    private Image? _overlayImage;
    private TranslateTransform? _iconImageTransform;
    private Panel? _iconHost;
    private Panel? _iconViewport;
    private string _currentText = string.Empty;
    private IImage? _currentIcon;
    private bool _currentIconUsesSpriteSheet;
    private bool _isIconVisible;

    protected IconTextDisplay()
    {
        Classes.Add("IconTextDisplay");

        _textBlock = new TextBlock
        {
            Name = "PART_Text",
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _textBlock.Classes.Add("IconTextDisplayText");

        _layout = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = IconSpacing,
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

    public bool ShowIcon
    {
        get;
        set => SetAndRaise(ShowIconProperty, ref field, value);
    } = true;

    public bool IsIconAlternate
    {
        get => GetValue(IsIconAlternateProperty);
        set => SetValue(IsIconAlternateProperty, value);
    }

    public double IconOverlayOpacity
    {
        get => GetValue(IconOverlayOpacityProperty);
        set => SetValue(IconOverlayOpacityProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public double IconSpacing
    {
        get => GetValue(IconSpacingProperty);
        set => SetValue(IconSpacingProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (ShouldUpdateDisplay(change.Property))
        {
            UpdateDisplay();
        }
        else if (change.Property == IsIconAlternateProperty)
        {
            UpdateIconTransform();
        }
        else if (change.Property == IconOverlayOpacityProperty)
        {
            UpdateIconOverlayOpacity();
        }
        else if (change.Property == IconSizeProperty)
        {
            UpdateIconLayout();
        }
        else if (change.Property == IconSpacingProperty)
        {
            _layout.ColumnSpacing = Math.Max(0, IconSpacing);
        }
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        UpdateDisplay();
    }

    protected virtual bool ShouldUpdateDisplay(AvaloniaProperty property)
        => property == EntityIdProperty ||
           property == ShowIconProperty ||
           property == DisplayContextProvider.DisplayContextProperty;

    protected abstract string ResolveTextCore(SceneDisplayContext? context, int entityId);

    protected abstract DisplayIcon? ResolveIconCore(SceneDisplayContext? context, int entityId);

    protected static string FormatEntityId(int entityId)
        => entityId > 0 ? entityId.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    private void UpdateDisplay()
    {
        var context = DisplayContextProvider.GetDisplayContext(this);
        var entityId = EntityId;
        var text = ResolveTextCore(context, entityId);
        if (!string.Equals(_currentText, text, StringComparison.Ordinal))
        {
            _textBlock.Text = text;
            _currentText = text;
        }

        var icon = ShowIcon ? ResolveIconCore(context, entityId) : null;
        var iconSource = icon?.Source;
        var usesSpriteSheet = icon?.UsesSpriteSheet ?? false;
        if (!ReferenceEquals(_currentIcon, iconSource) || _currentIconUsesSpriteSheet != usesSpriteSheet)
        {
            if (iconSource is not null)
            {
                EnsureIconHost().Source = iconSource;
            }
            else
            {
                _iconImage?.Source = null;
            }

            _currentIcon = iconSource;
            _currentIconUsesSpriteSheet = usesSpriteSheet;
            UpdateIconLayout();
        }

        SetIconVisible(iconSource is not null);
    }

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
            UpdateIconTransform();
        }

        Grid.SetColumn(_textBlock, visible ? 1 : 0);
        Grid.SetColumnSpan(_textBlock, visible ? 1 : 2);
        _isIconVisible = visible;
    }

    private Image EnsureIconHost()
    {
        if (_iconImage is not null)
        {
            return _iconImage;
        }

        var iconSize = EffectiveIconSize;
        var frameSize = EffectiveIconFrameSize;
        _iconImageTransform = new TranslateTransform();
        _iconImage = new Image
        {
            Name = "PART_Icon",
            Width = iconSize,
            Height = iconSize * 2,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = _iconImageTransform
        };
        _iconImage.Classes.Add("IconTextDisplayIcon");

        _overlayImage = new Image
        {
            Name = "PART_IconOverlay",
            Width = frameSize,
            Height = frameSize,
            Source = DisplayIconCache.OverlayIcon,
            Opacity = IconOverlayOpacity,
            ZIndex = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _overlayImage.Classes.Add("IconTextDisplayIconOverlay");

        _iconViewport = new Panel
        {
            Width = iconSize,
            Height = iconSize,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _iconImage }
        };

        _iconHost = new Panel
        {
            Name = "PART_IconHost",
            Width = frameSize,
            Height = frameSize,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _iconViewport,
                _overlayImage
            }
        };
        _iconHost.Classes.Add("IconTextDisplayIconHost");

        _layout.Children.Insert(0, _iconHost);
        return _iconImage;
    }

    private void UpdateIconTransform()
    {
        if (_iconImage is null)
        {
            return;
        }

        (_iconImageTransform ??= new TranslateTransform()).Y = _currentIconUsesSpriteSheet && IsIconAlternate ? -EffectiveIconSize : 0;
        _iconImage.RenderTransform ??= _iconImageTransform;
    }

    private void UpdateIconLayout()
    {
        if (_iconImage is null)
        {
            return;
        }

        var iconSize = EffectiveIconSize;
        var frameSize = EffectiveIconFrameSize;
        if (_iconHost is not null)
        {
            _iconHost.Width = frameSize;
            _iconHost.Height = frameSize;
        }

        if (_iconViewport is not null)
        {
            _iconViewport.Width = iconSize;
            _iconViewport.Height = iconSize;
        }

        if (_overlayImage is not null)
        {
            _overlayImage.Width = frameSize;
            _overlayImage.Height = frameSize;
        }

        if (_currentIconUsesSpriteSheet)
        {
            _iconImage.Width = iconSize;
            _iconImage.Height = iconSize * 2;
            _iconImage.Stretch = Stretch.Fill;
            _overlayImage!.IsVisible = true;
        }
        else
        {
            _iconImage.Width = iconSize;
            _iconImage.Height = iconSize;
            _iconImage.Stretch = Stretch.Uniform;
            _overlayImage!.IsVisible = false;
        }

        UpdateIconTransform();
    }

    private void UpdateIconOverlayOpacity()
    {
        _overlayImage?.Opacity = IconOverlayOpacity;
    }

    private double EffectiveIconSize => Math.Max(1, IconSize);

    private double EffectiveIconFrameSize => EffectiveIconSize + 2;

    protected readonly record struct DisplayIcon(IImage Source, bool UsesSpriteSheet);
}

public sealed class CombatantDisplay : IconTextDisplay
{
    protected override string ResolveTextCore(SceneDisplayContext? context, int entityId)
        => context?.ResolveEntityName(entityId) ?? FormatEntityId(entityId);

    protected override DisplayIcon? ResolveIconCore(SceneDisplayContext? context, int entityId)
    {
        if (context is null)
        {
            return null;
        }

        var classIcon = DisplayIconCache.ResolveClassIcon(context.ResolvePcClass(entityId));
        if (classIcon is not null)
        {
            return new DisplayIcon(classIcon, UsesSpriteSheet: true);
        }

        var npcIcon = DisplayIconCache.ResolveNpcMarkerIcon(context.ResolveNpcCatalogEntry(entityId));
        return npcIcon is null ? null : new DisplayIcon(npcIcon, UsesSpriteSheet: false);
    }
}

public sealed class PcDisplay : IconTextDisplay
{
    protected override string ResolveTextCore(SceneDisplayContext? context, int entityId)
        => context?.ResolvePcName(entityId) ?? FormatEntityId(entityId);

    protected override DisplayIcon? ResolveIconCore(SceneDisplayContext? context, int entityId)
    {
        var classIcon = context is null ? null : DisplayIconCache.ResolveClassIcon(context.ResolvePcClass(entityId));
        return classIcon is null ? null : new DisplayIcon(classIcon, UsesSpriteSheet: true);
    }
}

public sealed class NpcDisplay : IconTextDisplay
{
    public static readonly DirectProperty<NpcDisplay, int> NpcCodeProperty =
        AvaloniaProperty.RegisterDirect<NpcDisplay, int>(nameof(NpcCode), x => x.NpcCode, (x, value) => x.NpcCode = value);

    public int NpcCode
    {
        get;
        set => SetAndRaise(NpcCodeProperty, ref field, value);
    }

    protected override bool ShouldUpdateDisplay(AvaloniaProperty property)
        => base.ShouldUpdateDisplay(property) || property == NpcCodeProperty;

    protected override string ResolveTextCore(SceneDisplayContext? context, int entityId)
    {
        if (NpcCode > 0)
        {
            return context?.ResolveNpcCodeName(NpcCode) ?? NpcCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return context?.ResolveNpcName(entityId) ?? FormatEntityId(entityId);
    }

    protected override DisplayIcon? ResolveIconCore(SceneDisplayContext? context, int entityId)
    {
        if (context is null)
        {
            return null;
        }

        if (NpcCode > 0)
        {
            var entry = context.ResolveNpcCodeCatalogEntry(NpcCode);
            return new DisplayIcon(DisplayIconCache.ResolveNpcMarkerIcon(entry?.Kind ?? NpcCatalogKind.Unknown), UsesSpriteSheet: false);
        }

        var npcIcon = DisplayIconCache.ResolveNpcMarkerIcon(context.ResolveNpcCatalogEntry(entityId));
        return npcIcon is null ? null : new DisplayIcon(npcIcon, UsesSpriteSheet: false);
    }
}

public sealed class SkillDisplay : IconTextDisplay
{
    public static readonly DirectProperty<SkillDisplay, int> SkillCodeProperty =
        AvaloniaProperty.RegisterDirect<SkillDisplay, int>(nameof(SkillCode), x => x.SkillCode, (x, value) => x.SkillCode = value);

    public int SkillCode
    {
        get;
        set => SetAndRaise(SkillCodeProperty, ref field, value);
    }

    protected override bool ShouldUpdateDisplay(AvaloniaProperty property)
        => base.ShouldUpdateDisplay(property) || property == SkillCodeProperty;

    protected override string ResolveTextCore(SceneDisplayContext? context, int entityId)
        => context?.ResolveSkillName(SkillCode) ?? (SkillCode > 0 ? SkillCode.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);

    protected override DisplayIcon? ResolveIconCore(SceneDisplayContext? context, int entityId)
        => null;
}

public sealed class MapDisplay : IconTextDisplay
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

    protected override bool ShouldUpdateDisplay(AvaloniaProperty property)
        => base.ShouldUpdateDisplay(property) || property == MapIdProperty || property == UseBracketsProperty;

    protected override string ResolveTextCore(SceneDisplayContext? context, int entityId)
    {
        var text = context?.ResolveMapName(MapId) ?? string.Empty;
        if (string.IsNullOrEmpty(text) && MapId > 0)
        {
            text = MapId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return UseBrackets ? $"[{text}]" : text;
    }

    protected override DisplayIcon? ResolveIconCore(SceneDisplayContext? context, int entityId)
        => null;
}

internal static class DisplayIconCache
{
    public static IImage OverlayIcon { get => field ??= Load("Overlay.webp"); }
    private static IImage NpcBossMarkerIcon { get => field ??= Load("UT_Marker_Monster_Boss.png"); }
    private static IImage NpcDefaultMarkerIcon { get => field ??= Load("UT_Marker_Default.png"); }
    private static IImage NpcMonsterMarkerIcon { get => field ??= Load("UT_Marker_SkillMaster.png"); }
    private static IImage NpcObjectMarkerIcon { get => field ??= Load("UT_Marker_Envobj.png"); }
    private static IImage NpcSummonMarkerIcon { get => field ??= Load("UT_Marker_Summon_Common.png"); }
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

    public static IImage? ResolveNpcMarkerIcon(NpcCatalogEntry? entry)
        => entry is null ? null : ResolveNpcMarkerIcon(entry.Value.Kind);

    public static IImage ResolveNpcMarkerIcon(NpcCatalogKind kind)
        => kind switch
        {
            NpcCatalogKind.Summon => NpcSummonMarkerIcon,
            NpcCatalogKind.Boss => NpcBossMarkerIcon,
            NpcCatalogKind.Object => NpcObjectMarkerIcon,
            NpcCatalogKind.Monster => NpcMonsterMarkerIcon,
            _ => NpcDefaultMarkerIcon
        };

    internal static string ResolveNpcMarkerIconAssetName(NpcCatalogKind kind)
        => kind switch
        {
            NpcCatalogKind.Summon => "UT_Marker_Summon_Common.png",
            NpcCatalogKind.Boss => "UT_Marker_Monster_Boss.png",
            NpcCatalogKind.Object => "UT_Marker_Envobj.png",
            NpcCatalogKind.Monster => "UT_Marker_SkillMaster.png",
            _ => "UT_Marker_Default.png"
        };

    private static Bitmap Load(string fileName)
        => new(AssetLoader.Open(new Uri($"avares://Aion2Flow/Assets/Images/{fileName}")));
}
