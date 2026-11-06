namespace Cloris.Aion2Flow.Resources.Catalog;

public sealed class ResourceSetDescriptor
{
    private readonly Func<int, string?> _resolveSkillIcon;

    internal ResourceSetDescriptor(
        string id,
        string revision,
        string defaultLanguage,
        byte stringMask,
        ResourcePackEntry sharedPack,
        ResourceLocalePackEntry[] localePacks,
        Func<int, string?> resolveSkillIcon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultLanguage);
        ArgumentNullException.ThrowIfNull(resolveSkillIcon);
        if (localePacks.Length == 0 ||
            localePacks.Select(static entry => entry.Language).Distinct(StringComparer.Ordinal).Count() != localePacks.Length ||
            !localePacks.Any(entry => string.Equals(entry.Language, defaultLanguage, StringComparison.Ordinal)))
        {
            throw new ArgumentException("A resource set requires unique locales including its default language.", nameof(localePacks));
        }

        Id = id;
        Revision = revision;
        DefaultLanguage = defaultLanguage;
        StringMask = stringMask;
        SharedPack = sharedPack;
        LocalePacks = Array.AsReadOnly((ResourceLocalePackEntry[])localePacks.Clone());
        Languages = Array.AsReadOnly(localePacks.Select(static entry => entry.Language).ToArray());
        _resolveSkillIcon = resolveSkillIcon;
    }

    public string Id { get; }
    public string Revision { get; }
    public string DefaultLanguage { get; }
    public IReadOnlyList<string> Languages { get; }

    public string ResolveLanguage(string? requestedLanguage)
    {
        if (requestedLanguage is not null)
        {
            for (var i = 0; i < Languages.Count; i++)
            {
                if (string.Equals(Languages[i], requestedLanguage, StringComparison.Ordinal))
                    return requestedLanguage;
            }
        }

        return DefaultLanguage;
    }

    public string? ResolveSkillIconAssetName(int skillCode) => _resolveSkillIcon(skillCode);

    internal byte StringMask { get; }
    internal ResourcePackEntry SharedPack { get; }
    internal IReadOnlyList<ResourceLocalePackEntry> LocalePacks { get; }

    internal ResourceLocalePackEntry GetLocale(string language)
    {
        foreach (var entry in LocalePacks)
        {
            if (string.Equals(entry.Language, language, StringComparison.Ordinal))
                return entry;
        }

        throw new ArgumentOutOfRangeException(nameof(language), language, "The resource set does not contain this language.");
    }
}

internal readonly record struct ResourcePackEntry(string ResourceName, int UncompressedLength, ulong Checksum);
internal readonly record struct ResourceLocalePackEntry(string Language, string ResourceName, int UncompressedLength, ulong Checksum);
