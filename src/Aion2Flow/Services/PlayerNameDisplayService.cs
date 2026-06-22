using System.Globalization;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.Services.Settings;

namespace Cloris.Aion2Flow.Services;

public sealed class PlayerNameDisplayService : IDisposable
{
    public const string LocalPlayerMarker = "⭐";

    private readonly LocalizationService _localization;
    private bool _showPlayerNames;
    private PlayerSelfMarkerDisplayMode _selfMarkerDisplayMode;
    private bool _showShortServerName;
    private bool _showLegionName;
    private bool _tintPlayerNamesByFaction;

    public PlayerNameDisplayService(SettingsService settingsService, LocalizationService localization)
    {
        _localization = localization;
        var settings = settingsService.Current;
        _showPlayerNames = settings.ShowPlayerNames;
        _selfMarkerDisplayMode = settings.PlayerSelfMarkerDisplayMode;
        _showShortServerName = settings.ShowPlayerShortServerName;
        _showLegionName = settings.ShowPlayerLegionName;
        _tintPlayerNamesByFaction = settings.TintPlayerNamesByFaction;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public event EventHandler? DisplayChanged;

    public bool ShowPlayerNames
    {
        get => _showPlayerNames;
        set => SetAndNotify(ref _showPlayerNames, value);
    }

    public PlayerSelfMarkerDisplayMode SelfMarkerDisplayMode
    {
        get => _selfMarkerDisplayMode;
        set => SetAndNotify(ref _selfMarkerDisplayMode, value);
    }

    public bool ShowShortServerName
    {
        get => _showShortServerName;
        set => SetAndNotify(ref _showShortServerName, value);
    }

    public bool ShowLegionName
    {
        get => _showLegionName;
        set => SetAndNotify(ref _showLegionName, value);
    }

    public bool TintPlayerNamesByFaction
    {
        get => _tintPlayerNamesByFaction;
        set => SetAndNotify(ref _tintPlayerNamesByFaction, value);
    }

    public string FormatPcName(SceneDisplayContext? context, int entityId)
    {
        if (entityId <= 0)
        {
            return string.Empty;
        }

        var showPlayerNames = ShowPlayerNames;
        var name = showPlayerNames
            ? FormatKnownName(context, entityId)
            : FormatAnonymousName(context?.ResolvePcClass(entityId), context?.ResolvePcAnonymousOrdinal(entityId) ?? 1);

        return ShouldShowLocalPlayerMarker(context?.IsLocalPlayer(entityId) == true, showPlayerNames)
            ? LocalPlayerMarker + name
            : name;
    }

    public string FormatAnonymousName(CharacterClass? characterClass, int ordinal)
    {
        var normalizedOrdinal = Math.Max(1, ordinal);
        var label = characterClass is null or CharacterClass.None
            ? _localization["PlayerName_AnonymousPlayer"]
            : _localization[$"CharacterClass_{characterClass.Value}"];
        if (string.IsNullOrWhiteSpace(label))
        {
            label = _localization["PlayerName_AnonymousPlayer"];
        }

        return string.Create(CultureInfo.InvariantCulture, $"{label} {normalizedOrdinal}");
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private string FormatKnownName(SceneDisplayContext? context, int entityId)
    {
        var name = context?.ResolvePcName(entityId) ?? entityId.ToString(CultureInfo.InvariantCulture);
        if (context is null || !context.TryResolvePcMetadata(entityId, out var metadata))
        {
            return name;
        }

        if (ShowShortServerName && metadata.OriginServerId is > 0)
        {
            var serverName = context.ResolveShortServerName(metadata.OriginServerId.Value);
            if (!string.IsNullOrWhiteSpace(serverName))
            {
                name += $"[{serverName}]";
            }
        }

        if (ShowLegionName && metadata.HasLegionName)
        {
            name += $"<{metadata.LegionName}>";
        }

        return name;
    }

    private bool ShouldShowLocalPlayerMarker(bool isLocalPlayer, bool showPlayerNames)
        => isLocalPlayer && SelfMarkerDisplayMode switch
        {
            PlayerSelfMarkerDisplayMode.WhenNamesHidden => !showPlayerNames,
            PlayerSelfMarkerDisplayMode.Always => true,
            _ => false
        };

    private void SetAndNotify<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        DisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        DisplayChanged?.Invoke(this, EventArgs.Empty);
    }
}
