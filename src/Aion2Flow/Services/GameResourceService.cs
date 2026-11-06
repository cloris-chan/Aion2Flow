using System.Collections.Frozen;
using System.Globalization;
using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.Services;

public sealed class GameResourceService : IDisposable
{
    private readonly ClientSettingsService? _clientSettings;
    private readonly bool _isPinned;
    private readonly Lock _lock = new();
    private readonly Dictionary<(ClientProfile Profile, ResourceCatalogSnapshot Resources), GameResourceService> _contextResources = [];
    private ResourceCatalogSnapshot _catalog = null!;
    private FrozenSet<int> _playerProfessionBaseSkillIds = Array.Empty<int>().ToFrozenSet();

    public event EventHandler<string>? ResourcesChanged;

    public string CurrentLanguage { get; private set; } = string.Empty;
    public SceneClientContext ClientContext { get; private set; }
    public ClientProfile Profile => ClientContext.Profile;
    public CombatResourceRegistry CombatResources => ClientContext.CombatResources;
    public ResourceCatalogSnapshot Catalog => _catalog;
    public SkillDisplayCatalog Skills { get; private set; } = [];
    public IReadOnlyDictionary<int, NpcDisplayEntry> NpcCatalog { get; private set; } = new Dictionary<int, NpcDisplayEntry>();
    public IReadOnlyDictionary<uint, string> Maps { get; private set; } = new Dictionary<uint, string>();
    public IReadOnlyDictionary<int, ServerNameEntry> ServerNames { get; private set; } = new Dictionary<int, ServerNameEntry>();

    public GameResourceService(ClientSettingsService clientSettings)
    {
        _clientSettings = clientSettings;
        ClientContext = new SceneClientContext(clientSettings.CurrentProfile, ResourceCatalog.Load(clientSettings.CurrentProfile));
        Reload(clientSettings.ResourceLanguage);
        _clientSettings.ClientChanged += OnClientChanged;
        _clientSettings.ResourceLanguageChanged += OnResourceLanguageChanged;
    }

    public GameResourceService(ClientProfile profile, string? language = null)
        : this(new SceneClientContext(profile, ResourceCatalog.Load(profile)), language)
    {
    }

    public GameResourceService(SceneClientContext clientContext, string? language = null)
    {
        _isPinned = true;
        ClientContext = clientContext;
        Reload(language);
    }

    public GameResourceService GetForClient(SceneClientContext clientContext)
    {
        ArgumentNullException.ThrowIfNull(clientContext);
        if (_isPinned && Profile == clientContext.Profile &&
            ReferenceEquals(ClientContext.Resources, clientContext.Resources))
            return this;
        var key = (clientContext.Profile, clientContext.Resources);
        lock (_lock)
        {
            if (_contextResources.TryGetValue(key, out var resources))
                return resources;
            resources = new GameResourceService(clientContext, CurrentLanguage);
            _contextResources.Add(key, resources);
            return resources;
        }
    }

    public bool SetResourceLanguage(string language)
    {
        if (_clientSettings is not null)
            return _clientSettings.SetResourceLanguage(language);
        var resolvedLanguage = ResourceCatalog.GetResourceSet(Profile).ResolveLanguage(language);
        if (string.Equals(CurrentLanguage, resolvedLanguage, StringComparison.Ordinal))
            return false;
        ReloadWithContexts(resolvedLanguage);
        return true;
    }

    public void Dispose()
    {
        if (_clientSettings is not null)
        {
            _clientSettings.ClientChanged -= OnClientChanged;
            _clientSettings.ResourceLanguageChanged -= OnResourceLanguageChanged;
        }
        foreach (var resources in _contextResources.Values)
            resources.Dispose();
        _contextResources.Clear();
    }

    public string ResolveSkillName(int skillCode)
    {
        lock (_lock)
        {
            var catalog = _catalog;
            if (TryResolveSkillName(Skills, skillCode, out var name))
            {
                return name;
            }

            if (TryResolveSkillIdByEffectRef(catalog, unchecked((uint)skillCode), out var ownerSkillId) &&
                TryResolveSkillNameBySkillOrBase(catalog, ownerSkillId, out name))
            {
                return name;
            }

            var baseSkillId = ResolveBaseSkillIdForCode(catalog, skillCode);
            if (baseSkillId != skillCode && TryResolveSkillName(Skills, baseSkillId, out name))
            {
                return name;
            }
        }

        return skillCode.ToString(CultureInfo.InvariantCulture);
    }

    public bool ContainsSkill(int skillCode)
    {
        lock (_lock)
        {
            return skillCode > 0 && Skills.Contains(skillCode);
        }
    }

    public int ResolveBaseSkillIdForCode(int skillCode)
    {
        lock (_lock)
        {
            return ResolveBaseSkillIdForCode(_catalog, skillCode);
        }
    }

    public bool IsPlayerProfessionSkill(int skillCode)
    {
        if (skillCode <= 0)
        {
            return false;
        }

        lock (_lock)
        {
            var baseSkillId = ResolveBaseSkillIdForCode(_catalog, skillCode);
            return _playerProfessionBaseSkillIds.Contains(baseSkillId);
        }
    }

    public int ResolveMaxAvailableCount(int skillCode)
    {
        if (skillCode <= 0)
        {
            return 0;
        }

        lock (_lock)
        {
            var catalog = _catalog;
            if (TryResolveMaxAvailableCount(catalog, skillCode, out var maxAvailableCount))
            {
                return maxAvailableCount;
            }

            if (TryResolveSkillIdByEffectRef(catalog, unchecked((uint)skillCode), out var ownerSkillId) &&
                TryResolveMaxAvailableCount(catalog, ownerSkillId, out maxAvailableCount))
            {
                return maxAvailableCount;
            }

            var baseSkillId = ResolveBaseSkillIdForCode(catalog, skillCode);
            return baseSkillId != skillCode &&
                   TryResolveMaxAvailableCount(catalog, baseSkillId, out maxAvailableCount)
                ? maxAvailableCount
                : 0;
        }
    }

    public string? ResolveSkillIconAssetName(int skillCode)
    {
        ResourceCatalogSnapshot snapshot;
        lock (_lock)
        {
            snapshot = _catalog;
        }

        var assetName = snapshot.ResolveSkillIconAssetName(skillCode);
        if (assetName is not null || skillCode <= 0)
            return assetName;

        if (TryResolveSkillIdByEffectRef(snapshot, unchecked((uint)skillCode), out var ownerSkillId))
        {
            if (TryResolveSkillIconAssetNameBySkillOrBase(snapshot, ownerSkillId, out assetName))
                return assetName;
        }

        var baseSkillId = ResolveBaseSkillIdForCode(snapshot, skillCode);
        if (baseSkillId != skillCode)
        {
            assetName = snapshot.ResolveSkillIconAssetName(baseSkillId);
            if (assetName is not null)
            {
                return assetName;
            }
        }

        return null;
    }

    private bool TryResolveSkillNameBySkillOrBase(ResourceCatalogSnapshot snapshot, int skillCode, out string name)
    {
        if (TryResolveSkillName(Skills, skillCode, out name))
        {
            return true;
        }

        var baseSkillId = ResolveBaseSkillIdForCode(snapshot, skillCode);
        return baseSkillId != skillCode && TryResolveSkillName(Skills, baseSkillId, out name);
    }

    private static bool TryResolveSkillIconAssetNameBySkillOrBase(ResourceCatalogSnapshot snapshot, int skillCode, out string? assetName)
    {
        assetName = snapshot.ResolveSkillIconAssetName(skillCode);
        if (assetName is not null)
        {
            return true;
        }

        var baseSkillId = ResolveBaseSkillIdForCode(snapshot, skillCode);
        if (baseSkillId == skillCode)
        {
            return false;
        }

        assetName = snapshot.ResolveSkillIconAssetName(baseSkillId);
        return assetName is not null;
    }

    private static int ResolveBaseSkillIdForCode(ResourceCatalogSnapshot snapshot, int skillCode)
    {
        if (skillCode <= 0)
        {
            return 0;
        }

        return snapshot.SkillBaseProjections.TryGetValue(skillCode, out var projection) && projection.BaseSkillId > 0
            ? projection.BaseSkillId
            : skillCode;
    }

    private static bool TryResolveMaxAvailableCount(ResourceCatalogSnapshot snapshot, int skillCode, out int maxAvailableCount)
    {
        if (snapshot.SkillDefinitions.TryGetValue(skillCode, out var definition) && definition.MaxAvailableCount > 1)
        {
            maxAvailableCount = definition.MaxAvailableCount;
            return true;
        }

        maxAvailableCount = 0;
        return false;
    }

    private static bool TryResolveSkillIdByEffectRef(ResourceCatalogSnapshot snapshot, uint rawId, out int skillId)
    {
        if (rawId != 0 && snapshot.EffectSkillIds.TryGetValue(rawId, out skillId))
        {
            return true;
        }

        skillId = 0;
        return false;
    }

    private static bool TryResolveSkillName(SkillDisplayCatalog skills, int skillCode, out string name)
    {
        if (skills.TryGetValue(skillCode, out var skillDisplayEntry) && !string.IsNullOrWhiteSpace(skillDisplayEntry.Name))
        {
            name = skillDisplayEntry.Name;
            return true;
        }

        name = string.Empty;
        return false;
    }

    public bool TryResolveNpcCatalogEntry(int npcCode, out NpcDisplayEntry entry)
    {
        lock (_lock)
        {
            if (NpcCatalog.TryGetValue(npcCode, out entry))
            {
                return true;
            }
        }

        entry = default;
        return false;
    }

    public string ResolveNpcName(int npcCode)
    {
        if (npcCode <= 0)
        {
            return string.Empty;
        }

        return TryResolveNpcCatalogEntry(npcCode, out var entry) && !string.IsNullOrWhiteSpace(entry.Name)
            ? entry.Name
            : $"NPC-{npcCode.ToString(CultureInfo.InvariantCulture)}";
    }

    public string ResolveMapName(uint mapId)
    {
        if (mapId == 0)
        {
            return string.Empty;
        }

        ResourceCatalogSnapshot snapshot;
        lock (_lock)
        {
            snapshot = _catalog;
        }

        return snapshot.ResolveMapName(mapId);
    }

    public string ResolveServerName(int code)
    {
        if (code <= 0)
        {
            return string.Empty;
        }

        ResourceCatalogSnapshot snapshot;
        lock (_lock)
        {
            snapshot = _catalog;
        }

        return snapshot.ResolveServerName(code);
    }

    public string ResolveShortServerName(int code)
    {
        if (code <= 0)
        {
            return string.Empty;
        }

        ResourceCatalogSnapshot snapshot;
        lock (_lock)
        {
            snapshot = _catalog;
        }

        return snapshot.ResolveShortServerName(code);
    }

    private void OnResourceLanguageChanged(object? sender, string language)
    {
        if (string.Equals(CurrentLanguage, language, StringComparison.Ordinal))
            return;
        ReloadWithContexts(language);
    }

    private void OnClientChanged(object? sender, ClientProfile profile)
    {
        ClientContext = SceneClientContext.Create(profile);
        ReloadWithContexts(ClientContext.Resources.ResourceSet.ResolveLanguage(CurrentLanguage));
    }

    private void ReloadWithContexts(string language)
    {
        Reload(language);
        lock (_lock)
        {
            foreach (var resources in _contextResources.Values)
                resources.ReloadWithContexts(language);
        }
    }

    private void Reload(string? language)
    {
        var catalog = ResourceCatalog.Localize(ClientContext.Resources, language);
        var playerProfessionBaseSkillIds = catalog.Skills
            .Where(PlayerProfessionSkillFilter.Includes)
            .Select(skill => ResolveBaseSkillIdForCode(catalog, skill.SkillId))
            .Where(static skillId => skillId > 0)
            .ToFrozenSet();

        lock (_lock)
        {
            CurrentLanguage = catalog.Language;
            _catalog = catalog;
            _playerProfessionBaseSkillIds = playerProfessionBaseSkillIds;
            Skills = catalog.Skills;
            NpcCatalog = catalog.NpcCatalog;
            Maps = catalog.Maps;
            ServerNames = catalog.ServerNames;
        }

        ResourcesChanged?.Invoke(this, catalog.Language);
    }
}
