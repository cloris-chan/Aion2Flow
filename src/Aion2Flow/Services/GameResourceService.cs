using System.Globalization;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.Resources.Generated;
using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.Services;

public sealed class GameResourceService : IDisposable
{
    private readonly LanguageService _languageService;
    private readonly Lock _lock = new();
    private ResourceCatalogSnapshot _catalog = null!;

    public event EventHandler<string>? ResourcesChanged;

    public string CurrentLanguage { get; private set; }
    public SkillDisplayCatalog Skills { get; private set; } = [];
    public IReadOnlyDictionary<int, NpcDisplayEntry> NpcCatalog { get; private set; } = new Dictionary<int, NpcDisplayEntry>();
    public IReadOnlyDictionary<string, LocalizedNpcName> NpcNames { get; private set; } = new Dictionary<string, LocalizedNpcName>(StringComparer.Ordinal);
    public IReadOnlyDictionary<uint, string> Maps { get; private set; } = new Dictionary<uint, string>();
    public IReadOnlyDictionary<int, ServerNameEntry> ServerNames { get; private set; } = new Dictionary<int, ServerNameEntry>();

    public GameResourceService(LanguageService languageService)
    {
        _languageService = languageService;
        _languageService.LanguageChanged += OnLanguageChanged;
        CurrentLanguage = _languageService.CurrentLanguage;
        Reload(CurrentLanguage);
    }

    public void Dispose()
    {
        _languageService.LanguageChanged -= OnLanguageChanged;
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

    public string? ResolveSkillIconAssetName(int skillCode)
    {
        var assetName = SkillIconCatalog.ResolveAssetName(skillCode);
        if (assetName is not null || skillCode <= 0)
        {
            return assetName;
        }

        ResourceCatalogSnapshot snapshot;
        lock (_lock)
        {
            snapshot = _catalog;
        }

        if (TryResolveSkillIdByEffectRef(snapshot, unchecked((uint)skillCode), out var ownerSkillId))
        {
            if (TryResolveSkillIconAssetNameBySkillOrBase(snapshot, ownerSkillId, out assetName))
                return assetName;
        }

        var baseSkillId = ResolveBaseSkillIdForCode(snapshot, skillCode);
        if (baseSkillId != skillCode)
        {
            assetName = SkillIconCatalog.ResolveAssetName(baseSkillId);
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
        assetName = SkillIconCatalog.ResolveAssetName(skillCode);
        if (assetName is not null)
        {
            return true;
        }

        var baseSkillId = ResolveBaseSkillIdForCode(snapshot, skillCode);
        if (baseSkillId == skillCode)
        {
            return false;
        }

        assetName = SkillIconCatalog.ResolveAssetName(baseSkillId);
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

    private void OnLanguageChanged(object? sender, string language)
    {
        Reload(language);
    }

    private void Reload(string language)
    {
        var catalog = ResourceCatalog.Load(language);

        lock (_lock)
        {
            CurrentLanguage = language;
            _catalog = catalog;
            Skills = catalog.Skills;
            NpcCatalog = catalog.NpcCatalog;
            NpcNames = catalog.NpcNames;
            Maps = catalog.Maps;
            ServerNames = catalog.ServerNames;
        }

        CombatResourceRegistry.UpdateDisplayResources(catalog.Skills, catalog.NpcCatalog);
        ResourcesChanged?.Invoke(this, language);
    }
}
