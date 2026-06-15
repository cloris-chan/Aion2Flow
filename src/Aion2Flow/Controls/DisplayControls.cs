using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Identity;
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
    private static readonly SolidColorBrush LightNameForeground = new(Color.Parse("#72e1ff"));
    private static readonly SolidColorBrush DarkNameForeground = new(Color.Parse("#d275ff"));

    public static readonly DirectProperty<IconTextDisplay, int> EntityIdProperty = AvaloniaProperty.RegisterDirect<IconTextDisplay, int>(nameof(EntityId), x => x.EntityId, (x, value) => x.EntityId = value);

    public static readonly DirectProperty<IconTextDisplay, bool> ShowIconProperty = AvaloniaProperty.RegisterDirect<IconTextDisplay, bool>(nameof(ShowIcon), x => x.ShowIcon, (x, value) => x.ShowIcon = value);

    public static readonly StyledProperty<bool> IsIconAlternateProperty = AvaloniaProperty.Register<IconTextDisplay, bool>(nameof(IsIconAlternate));

    public static readonly StyledProperty<double> IconSizeProperty = AvaloniaProperty.Register<IconTextDisplay, double>(nameof(IconSize), 30);

    public static readonly StyledProperty<double> IconSpacingProperty = AvaloniaProperty.Register<IconTextDisplay, double>(nameof(IconSpacing), 4);

    private readonly Grid _layout;
    private readonly MarqueeTextPresenter _textPresenter;
    private readonly TextBlock _textBlock;
    private Image? _iconImage;
    private TranslateTransform? _iconImageTransform;
    private Panel? _iconHost;
    private Panel? _iconViewport;
    private string _currentText = string.Empty;
    private IImage? _currentIcon;
    private bool _currentIconUsesSpriteSheet;
    private bool _isIconVisible;
    private IBrush? _currentTextForeground;

    protected IconTextDisplay()
    {
        Classes.Add("IconTextDisplay");
        Effect = new DropShadowDirectionEffect
        {
            BlurRadius = 4,
            ShadowDepth = 0,
            Opacity = 0.95,
            Color = Color.Parse("#D0000000")
        };

        _textPresenter = new MarqueeTextPresenter();
        _textBlock = _textPresenter.TextBlock;

        _layout = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = IconSpacing,
            Children =
            {
                _textPresenter
            }
        };
        Content = _layout;
        Grid.SetColumn(_textPresenter, 0);
        Grid.SetColumnSpan(_textPresenter, 2);
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

    protected virtual bool ShouldUpdateDisplay(AvaloniaProperty property) => property == EntityIdProperty || property == ShowIconProperty || property == DisplayContextProvider.DisplayContextProperty;

    protected virtual void UpdateStateCore(SceneDisplayContext? context, int entityId)
    {
    }

    protected void SetTextForeground(IBrush? foreground)
    {
        if (ReferenceEquals(_currentTextForeground, foreground))
        {
            return;
        }

        if (foreground is null)
        {
            _textBlock.ClearValue(TextBlock.ForegroundProperty);
        }
        else
        {
            _textBlock.Foreground = foreground;
        }

        _currentTextForeground = foreground;
    }

    protected static IBrush? ResolveFactionNameForeground(Faction faction)
        => faction switch
        {
            Faction.Light => LightNameForeground,
            Faction.Dark => DarkNameForeground,
            _ => null
        };

    protected abstract string ResolveTextCore(SceneDisplayContext? context, int entityId);

    protected abstract DisplayIcon? ResolveIconCore(SceneDisplayContext? context, int entityId);

    protected static string FormatEntityId(int entityId) => entityId > 0 ? entityId.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    private void UpdateDisplay()
    {
        var context = DisplayContextProvider.GetDisplayContext(this);
        var entityId = EntityId;
        UpdateStateCore(context, entityId);
        var text = ResolveTextCore(context, entityId);
        if (!string.Equals(_currentText, text, StringComparison.Ordinal))
        {
            _textBlock.Text = text;
            _textPresenter.Restart();
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

        Grid.SetColumn(_textPresenter, visible ? 1 : 0);
        Grid.SetColumnSpan(_textPresenter, visible ? 1 : 2);
        _textPresenter.Restart();
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
            Children = { _iconViewport }
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

        if (_currentIconUsesSpriteSheet)
        {
            _iconImage.Width = iconSize;
            _iconImage.Height = iconSize * 2;
            _iconImage.Stretch = Stretch.Fill;
        }
        else
        {
            _iconImage.Width = iconSize;
            _iconImage.Height = iconSize;
            _iconImage.Stretch = Stretch.Uniform;
        }

        UpdateIconTransform();
    }

    private double EffectiveIconSize => Math.Max(1, IconSize);

    private double EffectiveIconFrameSize => EffectiveIconSize;

    protected readonly record struct DisplayIcon(IImage Source, bool UsesSpriteSheet);
}

public sealed class CombatantDisplay : UserControl
{
    public static readonly DirectProperty<CombatantDisplay, int> EntityIdProperty = AvaloniaProperty.RegisterDirect<CombatantDisplay, int>(nameof(EntityId), x => x.EntityId, (x, value) => x.EntityId = value);

    public static readonly DirectProperty<CombatantDisplay, bool> ShowIconProperty = AvaloniaProperty.RegisterDirect<CombatantDisplay, bool>(nameof(ShowIcon), x => x.ShowIcon, (x, value) => x.ShowIcon = value);

    public static readonly StyledProperty<bool> IsIconAlternateProperty = AvaloniaProperty.Register<CombatantDisplay, bool>(nameof(IsIconAlternate));

    public static readonly StyledProperty<double> IconSizeProperty = AvaloniaProperty.Register<CombatantDisplay, double>(nameof(IconSize), 30);

    public static readonly StyledProperty<double> IconSpacingProperty = AvaloniaProperty.Register<CombatantDisplay, double>(nameof(IconSpacing), 4);

    private PcDisplay? _pcDisplay;
    private NpcDisplay? _npcDisplay;
    private SelectedCombatantDisplayKind _selectedKind = SelectedCombatantDisplayKind.Unset;

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
        if (change.Property == EntityIdProperty ||
            change.Property == DisplayContextProvider.DisplayContextProperty)
        {
            UpdateSelectedDisplay();
        }
        else if (change.Property == ShowIconProperty ||
                 change.Property == IsIconAlternateProperty ||
                 change.Property == IconSizeProperty ||
                 change.Property == IconSpacingProperty)
        {
            SyncChildDisplayProperties();
        }
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        UpdateSelectedDisplay();
    }

    private void UpdateSelectedDisplay()
    {
        var entityId = EntityId;
        if (entityId <= 0)
        {
            Content = null;
            _pcDisplay = null;
            _npcDisplay = null;
            _selectedKind = SelectedCombatantDisplayKind.Unset;
            return;
        }

        var context = DisplayContextProvider.GetDisplayContext(this);
        var nextKind = ResolveDisplayKind(context, entityId);
        var nextDisplay = GetOrCreateDisplay(nextKind);

        if (_selectedKind != nextKind)
        {
            Content = nextDisplay;
            _selectedKind = nextKind;
        }

        nextDisplay.EntityId = entityId;
    }

    private void SyncChildDisplayProperties()
    {
        if (_pcDisplay is not null)
        {
            SyncChildDisplayProperties(_pcDisplay);
        }

        if (_npcDisplay is not null)
        {
            SyncChildDisplayProperties(_npcDisplay);
        }
    }

    private IconTextDisplay GetOrCreateDisplay(SelectedCombatantDisplayKind kind)
    {
        if (kind == SelectedCombatantDisplayKind.Npc)
        {
            return _npcDisplay ??= CreateChildDisplay<NpcDisplay>();
        }

        return _pcDisplay ??= CreateChildDisplay<PcDisplay>();
    }

    private TDisplay CreateChildDisplay<TDisplay>()
        where TDisplay : IconTextDisplay, new()
    {
        var display = new TDisplay();
        SyncChildDisplayProperties(display);
        display.EntityId = EntityId;
        return display;
    }

    private void SyncChildDisplayProperties(IconTextDisplay display)
    {
        display.ShowIcon = ShowIcon;
        display.IsIconAlternate = IsIconAlternate;
        display.IconSize = IconSize;
        display.IconSpacing = IconSpacing;
    }

    private static SelectedCombatantDisplayKind ResolveDisplayKind(SceneDisplayContext? context, int entityId)
    {
        if (context is not null)
        {
            if (context.HasPcMetadata(entityId))
            {
                return SelectedCombatantDisplayKind.Pc;
            }

            if (context.HasNpcCode(entityId))
            {
                return SelectedCombatantDisplayKind.Npc;
            }
        }

        return SelectedCombatantDisplayKind.Pc;
    }

    private enum SelectedCombatantDisplayKind
    {
        Unset,
        Pc,
        Npc
    }
}

public sealed class PcDisplay : IconTextDisplay
{
    protected override void UpdateStateCore(SceneDisplayContext? context, int entityId)
    {
        var faction = context?.ResolveFaction(entityId) ?? Faction.Unknown;
        SetTextForeground(ResolveFactionNameForeground(faction));
    }

    protected override string ResolveTextCore(SceneDisplayContext? context, int entityId) => context?.ResolvePcName(entityId) ?? FormatEntityId(entityId);

    protected override DisplayIcon? ResolveIconCore(SceneDisplayContext? context, int entityId)
    {
        var classIcon = context is null ? null : DisplayIconCache.ResolveClassIcon(context.ResolvePcClass(entityId));
        return classIcon is null ? null : new DisplayIcon(classIcon, UsesSpriteSheet: true);
    }
}

public sealed class NpcDisplay : IconTextDisplay
{
    public static readonly DirectProperty<NpcDisplay, int> NpcCodeProperty = AvaloniaProperty.RegisterDirect<NpcDisplay, int>(nameof(NpcCode), x => x.NpcCode, (x, value) => x.NpcCode = value);

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
    public static readonly DirectProperty<SkillDisplay, int> SkillCodeProperty = AvaloniaProperty.RegisterDirect<SkillDisplay, int>(nameof(SkillCode), x => x.SkillCode, (x, value) => x.SkillCode = value);
    public static readonly DirectProperty<SkillDisplay, string> FallbackTextProperty = AvaloniaProperty.RegisterDirect<SkillDisplay, string>(nameof(FallbackText), x => x.FallbackText, (x, value) => x.FallbackText = value);

    public int SkillCode
    {
        get;
        set => SetAndRaise(SkillCodeProperty, ref field, value);
    }

    public string FallbackText
    {
        get;
        set => SetAndRaise(FallbackTextProperty, ref field, value);
    } = string.Empty;

    protected override bool ShouldUpdateDisplay(AvaloniaProperty property)
        => base.ShouldUpdateDisplay(property) || property == SkillCodeProperty || property == FallbackTextProperty;

    protected override string ResolveTextCore(SceneDisplayContext? context, int entityId)
        => SkillCode > 0
            ? context?.ResolveSkillName(SkillCode) ?? SkillCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : FallbackText;

    protected override DisplayIcon? ResolveIconCore(SceneDisplayContext? context, int entityId)
    {
        var icon = DisplayIconCache.ResolveSkillIcon(context?.ResolveSkillIconAssetName(SkillCode));
        return icon is null ? null : new DisplayIcon(icon, UsesSpriteSheet: false);
    }
}

public sealed class MapDisplay : IconTextDisplay
{
    public static readonly DirectProperty<MapDisplay, uint> MapIdProperty = AvaloniaProperty.RegisterDirect<MapDisplay, uint>(nameof(MapId), x => x.MapId, (x, value) => x.MapId = value);

    public static readonly DirectProperty<MapDisplay, bool> UseBracketsProperty = AvaloniaProperty.RegisterDirect<MapDisplay, bool>(nameof(UseBrackets), x => x.UseBrackets, (x, value) => x.UseBrackets = value);

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

    protected override DisplayIcon? ResolveIconCore(SceneDisplayContext? context, int entityId) => null;
}

internal static class DisplayIconCache
{
    private static readonly Dictionary<string, IImage> SkillIcons = new(StringComparer.Ordinal);
    private static readonly Lock SkillIconsLock = new();
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
            NpcCatalogKind.TrainingDummy => NpcMonsterMarkerIcon,
            NpcCatalogKind.Monster => NpcMonsterMarkerIcon,
            _ => NpcDefaultMarkerIcon
        };

    internal static string ResolveNpcMarkerIconAssetName(NpcCatalogKind kind)
        => kind switch
        {
            NpcCatalogKind.Summon => "UT_Marker_Summon_Common.png",
            NpcCatalogKind.Boss => "UT_Marker_Monster_Boss.png",
            NpcCatalogKind.Object => "UT_Marker_Envobj.png",
            NpcCatalogKind.TrainingDummy => "UT_Marker_SkillMaster.png",
            NpcCatalogKind.Monster => "UT_Marker_SkillMaster.png",
            _ => "UT_Marker_Default.png"
        };

    public static IImage? ResolveSkillIcon(string? assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            return null;
        }

        lock (SkillIconsLock)
        {
            if (!SkillIcons.TryGetValue(assetName, out var icon))
            {
                icon = new Bitmap(AssetLoader.Open(ResolveSkillIconAssetUri(assetName)));
                SkillIcons.Add(assetName, icon);
            }

            return icon;
        }
    }

    internal static Uri ResolveSkillIconAssetUri(string assetName)
    {
        if (Path.GetFileName(assetName) != assetName ||
            !assetName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Skill icon asset name must be a WebP file name.", nameof(assetName));
        }

        return new Uri($"avares://Aion2Flow/Assets/Images/Skills/{assetName}");
    }

    private static Bitmap Load(string fileName) => new(AssetLoader.Open(new Uri($"avares://Aion2Flow/Assets/Images/{fileName}")));
}
