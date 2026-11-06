using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.Resources.Catalog;

namespace Cloris.Aion2Flow.Tests.Resources;

public sealed class ResourcePackStreamTests
{
    [Fact]
    public void SharedPackCanBeReadFromAnOwnedStreamWithoutClosingIt()
    {
        var descriptor = ResourceCatalog.GetResourceSet(ClientProfiles.Purple);
        using var stream = OpenPack(descriptor.SharedPack.ResourceName);

        var shared = ResourcePackReader.ReadSharedPack(stream, descriptor.SharedPack);

        Assert.Equal(ResourceCatalog.LoadShared().SkillDefinitions.Count, shared.SkillDefinitions.Count);
        Assert.Equal(ResourceCatalog.LoadShared().NpcDefinitions.Count, shared.NpcDefinitions.Count);
        Assert.Equal(ResourceCatalog.LoadShared().EffectSkillIds.OrderBy(static pair => pair.Key), shared.EffectSkillIds.OrderBy(static pair => pair.Key));
        Assert.True(stream.CanRead);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Theory]
    [InlineData(ResourceLanguage.English)]
    [InlineData(ResourceLanguage.Korean)]
    [InlineData(ResourceLanguage.TraditionalChinese)]
    public void LocaleStreamRoundTripPreservesAllDisplayEntries(string language)
    {
        var descriptor = ResourceCatalog.GetResourceSet(ClientProfiles.Purple);
        var entry = descriptor.GetLocale(language);
        using var stream = OpenPack(entry.ResourceName);

        var locale = ResourcePackReader.ReadLocalePack(stream, entry, descriptor.StringMask);
        var expected = ResourcePackReader.LoadLocale(descriptor, language);

        Assert.Equal(language, locale.Language);
        Assert.Equal(expected.SkillNames.OrderBy(static pair => pair.Key), locale.SkillNames.OrderBy(static pair => pair.Key));
        Assert.Equal(expected.NpcCatalogNames.OrderBy(static pair => pair.Key), locale.NpcCatalogNames.OrderBy(static pair => pair.Key));
        Assert.Equal(expected.Maps.OrderBy(static pair => pair.Key), locale.Maps.OrderBy(static pair => pair.Key));
        Assert.Equal(expected.ServerNames.OrderBy(static pair => pair.Key), locale.ServerNames.OrderBy(static pair => pair.Key));
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedPackRejectsTruncatedOrTrailingBytes(bool trailing)
    {
        var descriptor = ResourceCatalog.GetResourceSet(ClientProfiles.Purple);
        using var source = OpenPack(descriptor.SharedPack.ResourceName);
        using var bytes = new MemoryStream();
        source.CopyTo(bytes);
        if (trailing)
            bytes.WriteByte(0x42);
        else
            bytes.SetLength(bytes.Length - 1);
        bytes.Position = 0;

        Assert.Throws<InvalidDataException>(() => ResourcePackReader.ReadSharedPack(bytes, descriptor.SharedPack));
        Assert.True(bytes.CanRead);
    }

    private static Stream OpenPack(string name)
        => typeof(ResourceCatalog).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException(name);
}
