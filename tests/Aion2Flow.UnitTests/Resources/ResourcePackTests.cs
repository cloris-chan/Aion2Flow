using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.Resources.Generated;

namespace Cloris.Aion2Flow.Tests.Resources;

public sealed class ResourcePackTests
{
    [Theory]
    [InlineData(ResourceLanguage.English)]
    [InlineData(ResourceLanguage.Korean)]
    [InlineData(ResourceLanguage.TraditionalChinese)]
    public void Load_Loads_Runtime_Shared_And_Locale_Packs(string language)
    {
        var shared = ResourceCatalog.LoadShared();
        var snapshot = ResourceCatalog.Load(language);

        Assert.Same(shared, snapshot.Shared);
        Assert.Equal(language, snapshot.Language);
        Assert.Equal(15_249, snapshot.SkillDefinitions.Count);
        Assert.Equal(15_249, snapshot.Skills.Count);
        Assert.Equal(12_576, snapshot.NpcCatalog.Count);
        Assert.True(snapshot.Maps.Count > 600);
        Assert.True(snapshot.ServerNames.Count > 100);
    }

    [Fact]
    public void LoadShared_Returns_Cached_Shared_Catalog()
    {
        var first = ResourceCatalog.LoadShared();
        var second = ResourceCatalog.LoadShared();

        Assert.Same(first, second);
    }

    [Fact]
    public void Load_Rejects_Unsupported_Language()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ResourceCatalog.Load("ja-JP"));

    [Fact]
    public void NpcCatalog_Contains_Known_Numeric_Code()
    {
        var catalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;

        Assert.True(catalog.TryGetValue(2000002, out var npc));
        Assert.Equal("德拉克紐特弓手", npc.Name);
        Assert.Equal(NpcCatalogKind.Monster, npc.Kind);
        Assert.Equal(NpcHpDisplayScale.Normal, npc.HpDisplayScale);
    }

    [Fact]
    public void NpcCatalog_Contains_Bridged_Current_Client_Entry()
    {
        var catalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;

        Assert.True(catalog.TryGetValue(2405210, out var npc));
        Assert.Equal("盜賊團掠奪者", npc.Name);
        Assert.Equal(NpcCatalogKind.Monster, npc.Kind);
    }

    [Theory]
    [InlineData(2110465, NpcHpDisplayScale.LevelScaled)]
    [InlineData(2340057, NpcHpDisplayScale.LevelScaled)]
    [InlineData(2702110, NpcHpDisplayScale.LevelScaled)]
    [InlineData(2980079, NpcHpDisplayScale.Normal)]
    public void NpcCatalog_Exposes_Hp_Display_Scale(int npcCode, NpcHpDisplayScale expectedScale)
    {
        var catalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;

        Assert.True(catalog.TryGetValue(npcCode, out var npc));
        Assert.Equal(expectedScale, npc.HpDisplayScale);
    }

    [Fact]
    public void NpcCatalog_Preserves_Runtime_Kinds()
    {
        var catalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;

        Assert.True(catalog.TryGetValue(2920015, out var summon));
        Assert.Equal("結縛圈套", summon.Name);
        Assert.Equal(NpcCatalogKind.Summon, summon.Kind);
        Assert.True(catalog.TryGetValue(2500075, out var trainingDummy));
        Assert.Equal("訓練用稻草人", trainingDummy.Name);
        Assert.Equal(NpcCatalogKind.TrainingDummy, trainingDummy.Kind);
    }

    [Theory]
    [InlineData(ResourceLanguage.English, 12240010, "Judgment", SkillCategory.Templar, SkillSourceType.PcSkill)]
    [InlineData(ResourceLanguage.TraditionalChinese, 17121450, "痊癒光輝", SkillCategory.Cleric, SkillSourceType.PcSkill)]
    [InlineData(ResourceLanguage.English, 11800008, "Murderous Burst", SkillCategory.Gladiator, SkillSourceType.PcSkill)]
    [InlineData(ResourceLanguage.English, 19010000, "Flurry", SkillCategory.Brawler, SkillSourceType.PcSkill)]
    [InlineData(ResourceLanguage.TraditionalChinese, 19150350, "升天擊[暴走]", SkillCategory.Brawler, SkillSourceType.PcSkill)]
    [InlineData(ResourceLanguage.TraditionalChinese, 19160351, "升天擊第1階段", SkillCategory.Brawler, SkillSourceType.PcSkill)]
    [InlineData(ResourceLanguage.TraditionalChinese, 16001316, "風之精靈：暴風", SkillCategory.Elementalist, SkillSourceType.PcSkill)]
    [InlineData(ResourceLanguage.TraditionalChinese, 3001110, "神石：海格黛的束縛", SkillCategory.Item, SkillSourceType.ClientSkill)]
    public void Skills_Expose_Runtime_Identity_With_Localized_Text(
        string language,
        int skillId,
        string expectedName,
        SkillCategory expectedCategory,
        SkillSourceType expectedSourceType)
    {
        var skills = ResourceCatalog.Load(language).Skills;

        Assert.True(skills.TryGetValue(skillId, out var skill));
        Assert.Equal(expectedName, skill.Name);
        Assert.Equal(expectedCategory, skill.Category);
        Assert.Equal(expectedSourceType, skill.SourceType);
    }

    [Fact]
    public void Skills_Include_Current_Localized_Npc_Skills()
    {
        var skills = ResourceCatalog.Load(ResourceLanguage.English).Skills;

        Assert.True(skills.TryGetValue(1227237, out var attack));
        Assert.Equal("Attack", attack.Name);
        Assert.Equal(SkillCategory.Npc, attack.Category);
        Assert.Equal(SkillSourceType.ClientSkill, attack.SourceType);
        Assert.True(skills.TryGetValue(1227265, out var namedSkill));
        Assert.Equal("Wraith Surge", namedSkill.Name);
    }

    [Fact]
    public void SharedCatalog_Exposes_Only_Runtime_Resource_Data()
    {
        var properties = typeof(ResourceSharedCatalog)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
        [
            nameof(ResourceSharedCatalog.EffectSkillIds),
            nameof(ResourceSharedCatalog.NpcDefinitions),
            nameof(ResourceSharedCatalog.SkillBaseProjections),
            nameof(ResourceSharedCatalog.SkillDefinitions),
            nameof(ResourceSharedCatalog.SkillSemanticRuntimeIndex)
        ],
        properties);
    }

    [Fact]
    public void RuntimeSemanticIndex_Loads_Compact_Current_Client_Index()
    {
        var runtime = ResourceCatalog.LoadShared().SkillSemanticRuntimeIndex;

        Assert.Equal(15_988, runtime.SkillCount);
        Assert.Equal(25_002, runtime.SlotCount);
        Assert.True(runtime.NodeCount > 50_000);
        Assert.True(runtime.NodeSlotReferenceCount > runtime.SlotCount);
        Assert.True(runtime.TryResolveEffect(101000011, out var directHeal));
        Assert.Equal(SkillSemanticFacet.Healing, directHeal.DirectFacets);
        Assert.Equal(SkillSemanticFacet.Healing, directHeal.Facets);
        Assert.True(runtime.TryResolveEffect(1406004012, out var dotApplication));
        Assert.Equal(SkillSemanticFacet.None, dotApplication.DirectFacets);
        Assert.Equal(
            SkillSemanticFacet.DamageOverTime | SkillSemanticFacet.Debuff,
            dotApplication.Facets & (SkillSemanticFacet.DamageOverTime | SkillSemanticFacet.Debuff));
    }

    [Fact]
    public void RuntimeSemanticIndex_Preserves_Domain_Specific_Resource_Resolution()
    {
        var runtime = ResourceCatalog.LoadShared().SkillSemanticRuntimeIndex;

        Assert.True(runtime.TryResolveDirectResourceReference(400840, 4008, out var slotResolution));
        Assert.Equal(SkillSemanticResourceNodeKind.SkillEffectGroup, slotResolution.NodeKind);
        Assert.Equal(40084, slotResolution.NodeId);
        Assert.NotNull(slotResolution.Slot);
        Assert.Equal(4008, slotResolution.Slot!.Value.SkillId);
        Assert.True(runtime.TryResolveDirectResourceReference(1742001011, 17420010, out var directCollision));
        Assert.Equal(SkillSemanticResourceNodeKind.SkillEffect, directCollision.NodeKind);
        Assert.Equal(SkillSemanticFacet.Buff, directCollision.Facets & SkillSemanticFacet.Buff);
        Assert.True(runtime.TryResolvePeriodicResourceReference(1742001011, 17420010, out var periodicCollision));
        Assert.Equal(SkillSemanticResourceNodeKind.SkillAbnormalEffect, periodicCollision.NodeKind);
        Assert.Equal(SkillSemanticFacet.Shield, periodicCollision.Facets & SkillSemanticFacet.Shield);
    }

    [Theory]
    [InlineData(11010047, 11420000)]
    [InlineData(17040250, 17040000)]
    [InlineData(19010040, 19010000)]
    [InlineData(19160351, 19150000)]
    [InlineData(12090230, 12090000)]
    [InlineData(17040257, 17050000)]
    [InlineData(16030047, 16030000)]
    [InlineData(18370047, 18370000)]
    public void SkillBaseProjections_Contain_Only_NonIdentity_Runtime_Mappings(int skillCode, int expectedBaseSkillId)
    {
        var projections = ResourceCatalog.LoadShared().SkillBaseProjections;

        Assert.True(projections.TryGetValue(skillCode, out var projection));
        Assert.Equal(expectedBaseSkillId, projection.BaseSkillId);
        Assert.NotEqual(projection.SkillCode, projection.BaseSkillId);
    }

    [Theory]
    [InlineData(1227237)]
    [InlineData(16257000)]
    public void SkillBaseProjections_Do_Not_Store_Identity_Mappings(int skillCode)
        => Assert.False(ResourceCatalog.LoadShared().SkillBaseProjections.ContainsKey(skillCode));

    [Theory]
    [InlineData(30011101u, 3001110)]
    [InlineData(12272651u, 1227265)]
    [InlineData(160300471u, 16030047)]
    [InlineData(170402571u, 17040257)]
    public void EffectSkillIds_Map_Unambiguous_References_To_Owner_Skills(uint referenceCode, int expectedSkillId)
    {
        var effectSkillIds = ResourceCatalog.LoadShared().EffectSkillIds;

        Assert.True(effectSkillIds.TryGetValue(referenceCode, out var skillId));
        Assert.Equal(expectedSkillId, skillId);
    }

    [Theory]
    [InlineData(1, "ICON_TE_SKILL_001.webp")]
    [InlineData(12240010, "ICON_TE_SKILL_004.webp")]
    [InlineData(16030047, "ICON_EL_SKILL_003.webp")]
    [InlineData(16300243, "ICON_EL_SKILL_030.webp")]
    [InlineData(17270040, "ICON_CL_SKILL_026.webp")]
    [InlineData(17280010, "ICON_CL_SKILL_027.webp")]
    [InlineData(17290000, "ICON_CL_SKILL_028.webp")]
    [InlineData(17420010, "ICON_CL_SKILL_042.webp")]
    [InlineData(17440047, "ICON_CL_SKILL_046.webp")]
    [InlineData(19150350, "ICON_GT_SKILL_015.webp")]
    [InlineData(19160351, "ICON_GT_SKILL_016.webp")]
    [InlineData(19190120, "ICON_GT_SKILL_019.webp")]
    [InlineData(19200120, "ICON_GT_SKILL_020.webp")]
    [InlineData(16001316, "ICON_EL_SKILL_024.webp")]
    [InlineData(3001110, "Icon_Item_Usable_Godstone_WP_r_004.webp")]
    [InlineData(30011101, "Icon_Item_Usable_Godstone_WP_r_004.webp")]
    public void Generated_SkillIconCatalog_Resolves_Asset_Outside_Pack(int skillCode, string expectedAssetName)
    {
        var assetName = SkillIconCatalog.ResolveAssetName(skillCode);

        Assert.Equal(expectedAssetName, assetName);
        Assert.True(File.Exists(ResolveSkillIconPath(assetName)));
    }

    [Theory]
    [InlineData(20u, "渾沌艾雷修藍塔下層")]
    [InlineData(50u, "萬神殿")]
    [InlineData(1000u, "波伊塔")]
    [InlineData(154001u, "科赫塔監視哨所")]
    [InlineData(200003u, "惡夢")]
    [InlineData(503006u, "深淵迴廊")]
    [InlineData(600091u, "凶猛的角岩窟")]
    [InlineData(840037u, "褪色的脈動書庫")]
    public void Maps_Resolve_Client_Table_Scene_Id_Aliases(uint mapId, string expectedName)
        => Assert.Equal(expectedName, ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).ResolveMapName(mapId));

    [Theory]
    [InlineData(ResourceLanguage.English, 1001, "Siel", "SIE")]
    [InlineData(ResourceLanguage.TraditionalChinese, 1001, "希埃爾", "希埃")]
    [InlineData(ResourceLanguage.TraditionalChinese, 2001, "伊斯拉佩爾", "伊斯")]
    public void ServerNames_Resolve_ServerName_Dat_Names(string language, int code, string expectedServerName, string expectedShortServerName)
    {
        var snapshot = ResourceCatalog.Load(language);

        Assert.True(snapshot.ServerNames.TryGetValue(code, out var server));
        Assert.Equal(expectedServerName, server.ServerName);
        Assert.Equal(expectedShortServerName, server.ShortServerName);
        Assert.Equal(expectedServerName, snapshot.ResolveServerName(code));
        Assert.Equal(expectedShortServerName, snapshot.ResolveShortServerName(code));
    }

    [Fact]
    public void Runtime_Packs_Contain_Only_Current_Sections()
    {
        var manifestType = ResolveGeneratedType("ResourcePackManifest");
        var sharedResourceName = Assert.IsType<string>(manifestType.GetField("SharedResourceName", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        var sharedUncompressedLength = Assert.IsType<int>(manifestType.GetField("SharedUncompressedLength", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        var locales = ReadLocaleManifestEntries(manifestType);

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], ReadSectionIds(sharedResourceName));
        Assert.True(sharedUncompressedLength < 3_000_000, $"Shared runtime pack expanded to {sharedUncompressedLength} bytes.");
        Assert.All(locales.Values, resourceName => Assert.Equal([101, 102, 103, 104], ReadSectionIds(resourceName)));
    }

    [Fact]
    public void Manifest_Contains_Shared_And_Locale_Embedded_Packs()
    {
        var assembly = typeof(ResourceCatalog).Assembly;
        var resourceNames = assembly.GetManifestResourceNames().ToHashSet(StringComparer.Ordinal);
        var manifestType = ResolveGeneratedType("ResourcePackManifest");
        var sharedResourceName = Assert.IsType<string>(manifestType.GetField("SharedResourceName", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        var locales = ReadLocaleManifestEntries(manifestType);

        Assert.Contains(sharedResourceName, resourceNames);
        Assert.Equal(
            new[] { ResourceLanguage.English, ResourceLanguage.Korean, ResourceLanguage.TraditionalChinese },
            locales.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.All(locales.Values, resourceName => Assert.Contains(resourceName, resourceNames));
    }

    [Fact]
    public void ResourcePackSectionIds_Use_Runtime_Semantic_Order()
    {
        var sectionIdType = ResolveCatalogType("ResourcePackReader").GetNestedType("SectionId", BindingFlags.NonPublic)!;
        var expected = new (string Name, ushort Value)[]
        {
            ("SkillDefinitions", 1),
            ("SkillBaseProjections", 2),
            ("SkillEffectOwners", 3),
            ("SkillSemanticRuntimeSkillIds", 4),
            ("SkillSemanticRuntimeSlots", 5),
            ("SkillSemanticRuntimeNodes", 6),
            ("SkillSemanticRuntimeNodeSlots", 7),
            ("NpcDefinitions", 8),
            ("SkillNames", 101),
            ("NpcCatalogNames", 102),
            ("MapNames", 103),
            ("ServerNames", 104)
        };
        var actual = Enum.GetNames(sectionIdType)
            .Select(name => (Name: name, Value: Convert.ToUInt16(Enum.Parse(sectionIdType, name))))
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Resources_Assembly_Does_Not_Reference_Sqlite()
        => Assert.DoesNotContain(typeof(ResourceCatalog).Assembly.GetReferencedAssemblies(), assembly => string.Equals(assembly.Name, "Microsoft.Data.Sqlite", StringComparison.Ordinal));

    [Fact]
    public void Output_Does_Not_Contain_Resources_Db()
        => Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "resources.db")));

    [Fact]
    public void ResourcePackReader_Fails_For_Invalid_Header_Expectations()
    {
        var manifestType = ResolveGeneratedType("ResourcePackManifest");
        var decoderType = ResolveGeneratedType("ResourcePackDecoder");
        var resourceName = Assert.IsType<string>(manifestType.GetField("SharedResourceName", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        var uncompressedLength = Assert.IsType<int>(manifestType.GetField("SharedUncompressedLength", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        var checksum = Assert.IsType<ulong>(manifestType.GetField("SharedChecksum", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        var sharedPackKind = Assert.IsType<byte>(decoderType.GetField("SharedPackKind", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        var localePackKind = Assert.IsType<byte>(decoderType.GetField("LocalePackKind", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));

        AssertLoadPackPayloadFails("Cloris.Aion2Flow.Resources.Packs.missing.bin", sharedPackKind, uncompressedLength, checksum);
        AssertLoadPackPayloadFails(resourceName, localePackKind, uncompressedLength, checksum);
        AssertLoadPackPayloadFails(resourceName, sharedPackKind, uncompressedLength + 1, checksum);
        AssertLoadPackPayloadFails(resourceName, sharedPackKind, uncompressedLength, checksum + 1);
    }

    private static Dictionary<string, string> ReadLocaleManifestEntries(Type manifestType)
    {
        var locales = Assert.IsType<IEnumerable>(manifestType.GetProperty("Locales", BindingFlags.Public | BindingFlags.Static)!.GetValue(null), exactMatch: false);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var locale in locales)
        {
            var localeType = locale.GetType();
            var language = Assert.IsType<string>(localeType.GetProperty("Language")!.GetValue(locale));
            var resourceName = Assert.IsType<string>(localeType.GetProperty("ResourceName")!.GetValue(locale));
            result.Add(language, resourceName);
        }

        return result;
    }

    private static ushort[] ReadSectionIds(string resourceName)
    {
        var manifestType = ResolveGeneratedType("ResourcePackManifest");
        var decoderType = ResolveGeneratedType("ResourcePackDecoder");
        var isShared = resourceName.EndsWith("shared.bin", StringComparison.Ordinal);
        var kindField = isShared ? "SharedPackKind" : "LocalePackKind";
        var expectedKind = Assert.IsType<byte>(decoderType.GetField(kindField, BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        int expectedLength;
        ulong expectedChecksum;
        if (isShared)
        {
            expectedLength = Assert.IsType<int>(manifestType.GetField("SharedUncompressedLength", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
            expectedChecksum = Assert.IsType<ulong>(manifestType.GetField("SharedChecksum", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        }
        else
        {
            var locale = Assert.IsType<IEnumerable>(manifestType.GetProperty("Locales", BindingFlags.Public | BindingFlags.Static)!.GetValue(null), exactMatch: false)
                .Cast<object>()
                .Single(entry => string.Equals((string)entry.GetType().GetProperty("ResourceName")!.GetValue(entry)!, resourceName, StringComparison.Ordinal));
            expectedLength = (int)locale.GetType().GetProperty("UncompressedLength")!.GetValue(locale)!;
            expectedChecksum = (ulong)locale.GetType().GetProperty("Checksum")!.GetValue(locale)!;
        }

        var payload = LoadPackPayload(resourceName, expectedKind, expectedLength, expectedChecksum);
        var cursor = payload.AsSpan();
        cursor = cursor[(sizeof(uint) + sizeof(ushort))..];
        var sectionCount = BinaryPrimitives.ReadInt32LittleEndian(cursor);
        cursor = cursor[sizeof(int)..];
        var ids = new ushort[sectionCount];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = BinaryPrimitives.ReadUInt16LittleEndian(cursor);
            cursor = cursor[(sizeof(ushort) + sizeof(int) + sizeof(int) + sizeof(ulong))..];
        }

        Array.Sort(ids);
        return ids;
    }

    private static byte[] LoadPackPayload(string resourceName, byte expectedKind, int expectedUncompressedLength, ulong expectedChecksum)
    {
        var method = ResolveCatalogType("ResourcePackReader").GetMethod("LoadPackPayload", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsType<byte[]>(method.Invoke(null, [resourceName, expectedKind, expectedUncompressedLength, expectedChecksum]));
    }

    private static void AssertLoadPackPayloadFails(string resourceName, byte expectedKind, int expectedUncompressedLength, ulong expectedChecksum)
    {
        var method = ResolveCatalogType("ResourcePackReader").GetMethod("LoadPackPayload", BindingFlags.NonPublic | BindingFlags.Static)!;
        var ex = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [resourceName, expectedKind, expectedUncompressedLength, expectedChecksum]));
        Assert.IsType<InvalidDataException>(ex.InnerException);
    }

    private static Type ResolveGeneratedType(string name)
        => typeof(ResourceCatalog).Assembly.GetType($"Cloris.Aion2Flow.Resources.Generated.{name}", throwOnError: true)!;

    private static Type ResolveCatalogType(string name)
        => typeof(ResourceCatalog).Assembly.GetType($"Cloris.Aion2Flow.Resources.Catalog.{name}", throwOnError: true)!;

    private static string ResolveSkillIconPath(string? assetName)
    {
        Assert.False(string.IsNullOrWhiteSpace(assetName));
        foreach (var root in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var current in EnumerateParents(new DirectoryInfo(root)))
            {
                var candidate = Path.Combine(current.FullName, "Aion2Flow", "Assets", "Images", "Skills", assetName!);
                if (File.Exists(candidate))
                    return candidate;
                candidate = Path.Combine(current.FullName, "src", "Aion2Flow", "Assets", "Images", "Skills", assetName!);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return assetName!;
    }

    private static IEnumerable<DirectoryInfo> EnumerateParents(DirectoryInfo? start)
    {
        for (var current = start; current is not null; current = current.Parent)
            yield return current;
    }
}
