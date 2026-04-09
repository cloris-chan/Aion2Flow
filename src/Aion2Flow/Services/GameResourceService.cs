using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Resources;
using System.Globalization;

namespace Cloris.Aion2Flow.Services;

public sealed class GameResourceService : IDisposable
{
    private readonly LanguageService _languageService;
    private readonly Lock _lock = new();

    public event EventHandler<string>? ResourcesChanged;

    public string CurrentLanguage { get; private set; }
    public SkillCollection Skills { get; private set; } = [];
    public IReadOnlyDictionary<int, NpcCatalogEntry> NpcCatalog { get; private set; } =
        new Dictionary<int, NpcCatalogEntry>();
    public IReadOnlyDictionary<string, NpcName> NpcNames { get; private set; } =
        new Dictionary<string, NpcName>(StringComparer.Ordinal);

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
            return Skills.TryGetValue(skillCode, out var skill) && !string.IsNullOrWhiteSpace(skill.Name)
                ? skill.Name
                : skillCode.ToString(CultureInfo.InvariantCulture);
        }
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

    private void OnLanguageChanged(object? sender, string language)
    {
        Reload(language);
    }

    private void Reload(string language)
    {
        var skills = ResourceDatabase.LoadSkills(language);
        var npcCatalog = ResourceDatabase.LoadNpcCatalog(language);
        var npcNames = ResourceDatabase.LoadNpcNames(language);

        lock (_lock)
        {
            CurrentLanguage = language;
            Skills = skills;
            NpcCatalog = npcCatalog;
            NpcNames = npcNames;
        }

        CombatMetricsEngine.UpdateDisplayResources(skills, npcCatalog);
        ResourcesChanged?.Invoke(this, language);
    }
}
