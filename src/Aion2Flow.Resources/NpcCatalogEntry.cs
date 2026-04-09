namespace Cloris.Aion2Flow.Resources;

public readonly record struct NpcCatalogEntry(
    int Code,
    string Name,
    NpcCatalogKind Kind);
