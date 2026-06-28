using System.Globalization;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.Services;

public sealed class GameResourceService : IDisposable
{
    private readonly LanguageService _languageService;
    private readonly Lock _lock = new();

    public event EventHandler<string>? ResourcesChanged;

    public string CurrentLanguage { get; private set; }
    public SkillCollection Skills { get; private set; } = [];
    public IReadOnlyDictionary<int, NpcCatalogEntry> NpcCatalog { get; private set; } = new Dictionary<int, NpcCatalogEntry>();
    public IReadOnlyDictionary<string, NpcName> NpcNames { get; private set; } = new Dictionary<string, NpcName>(StringComparer.Ordinal);
    public IReadOnlyDictionary<uint, string> Maps { get; private set; } = new Dictionary<uint, string>();
    public IReadOnlyDictionary<int, ServerNameCatalogEntry> ServerNames { get; private set; } = new Dictionary<int, ServerNameCatalogEntry>();

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
            if (TryResolveSkillName(Skills, skillCode, out var name))
            {
                return name;
            }

            if (CombatResourceRegistry.TryResolveSkillIdByEffectRef(ResourceEffectRef.FromRaw(unchecked((uint)skillCode)), out var ownerSkillId) &&
                TryResolveSkillName(Skills, ownerSkillId, out name))
            {
                return name;
            }

            var displaySkillId = CombatResourceRegistry.ResolveDisplaySkillIdForCode(skillCode);
            if (displaySkillId != skillCode && TryResolveSkillName(Skills, displaySkillId, out name))
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

        if (CombatResourceRegistry.TryResolveSkillIdByEffectRef(ResourceEffectRef.FromRaw(unchecked((uint)skillCode)), out var ownerSkillId))
        {
            assetName = SkillIconCatalog.ResolveAssetName(ownerSkillId);
            if (assetName is not null)
            {
                return assetName;
            }
        }

        var displaySkillId = CombatResourceRegistry.ResolveDisplaySkillIdForCode(skillCode);
        if (displaySkillId != skillCode)
        {
            assetName = SkillIconCatalog.ResolveAssetName(displaySkillId);
            if (assetName is not null)
            {
                return assetName;
            }
        }

        return null;
    }

    private static bool TryResolveSkillName(SkillCollection skills, int skillCode, out string name)
    {
        if (skills.TryGetValue(skillCode, out var skill) && !string.IsNullOrWhiteSpace(skill.Name))
        {
            name = skill.Name;
            return true;
        }

        name = string.Empty;
        return false;
    }

    public bool TryResolveNpcCatalogEntry(int npcCode, out NpcCatalogEntry entry)
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

        IReadOnlyDictionary<uint, string> snapshot;
        lock (_lock)
        {
            snapshot = Maps;
        }

        return ResourceDatabase.ResolveMapName(mapId, snapshot);
    }

    public string ResolveServerName(int code)
    {
        if (code <= 0)
        {
            return string.Empty;
        }

        IReadOnlyDictionary<int, ServerNameCatalogEntry> snapshot;
        lock (_lock)
        {
            snapshot = ServerNames;
        }

        return ResourceDatabase.ResolveServerName(code, snapshot);
    }

    public string ResolveShortServerName(int code)
    {
        if (code <= 0)
        {
            return string.Empty;
        }

        IReadOnlyDictionary<int, ServerNameCatalogEntry> snapshot;
        lock (_lock)
        {
            snapshot = ServerNames;
        }

        return ResourceDatabase.ResolveShortServerName(code, snapshot);
    }

    private void OnLanguageChanged(object? sender, string language)
    {
        Reload(language);
    }

    private void Reload(string language)
    {
        var skills = ResourceDatabase.LoadSkills(language);
        var npcCatalog = ResourceDatabase.LoadNpcCatalog(language);
        var npcNames = ResourceDatabase.LoadNpcNames(language);
        var maps = ResourceDatabase.LoadMaps(language);
        var serverNames = ResourceDatabase.LoadServerNames(language);

        lock (_lock)
        {
            CurrentLanguage = language;
            Skills = skills;
            NpcCatalog = npcCatalog;
            NpcNames = npcNames;
            Maps = maps;
            ServerNames = serverNames;
        }

        CombatResourceRegistry.UpdateDisplayResources(skills, npcCatalog);
        ResourcesChanged?.Invoke(this, language);
    }
}
