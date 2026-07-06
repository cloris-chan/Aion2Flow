namespace Cloris.Aion2Flow.Resources.Catalog;

public readonly record struct NpcDisplayEntry(int Code, string Name, NpcCatalogKind Kind, NpcHpDisplayScale HpDisplayScale)
{
    public int HpDisplayDivisor => (int)HpDisplayScale;
}
