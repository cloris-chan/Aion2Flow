namespace Cloris.Aion2Flow.Resources.Catalog;

public readonly record struct NpcDefinition(int Code, NpcCatalogKind Kind, NpcHpDisplayScale HpDisplayScale)
{
    public int HpDisplayDivisor => (int)HpDisplayScale;
}
