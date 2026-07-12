namespace Cloris.Aion2Flow.Resources.Catalog;

public sealed class ResourceLocaleCatalog(string language, IReadOnlyDictionary<int, string> skillNames, IReadOnlyDictionary<int, string> npcCatalogNames, IReadOnlyDictionary<uint, string> maps, IReadOnlyDictionary<int, ServerNameEntry> serverNames)
{
    public string Language { get; } = language;
    public IReadOnlyDictionary<int, string> SkillNames { get; } = skillNames;
    public IReadOnlyDictionary<int, string> NpcCatalogNames { get; } = npcCatalogNames;
    public IReadOnlyDictionary<uint, string> Maps { get; } = maps;
    public IReadOnlyDictionary<int, ServerNameEntry> ServerNames { get; } = serverNames;
}
