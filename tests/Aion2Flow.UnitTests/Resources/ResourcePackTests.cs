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
    public void Load_Loads_Shared_And_Locale_Packs(string language)
    {
        var shared = ResourceCatalog.LoadShared();
        var snapshot = ResourceCatalog.Load(language);

        Assert.Same(shared, snapshot.Shared);
        Assert.Equal(language, snapshot.Language);
        Assert.True(snapshot.SkillDefinitions.Count > 13_000);
        Assert.True(snapshot.Skills.Count > 13_000);
        Assert.True(snapshot.NpcCatalog.Count > 12_000);
        Assert.True(snapshot.NpcNames.Count > 6_000);
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
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ResourceCatalog.Load("ja-JP"));
    }

    [Fact]
    public void NpcNames_Contains_Known_Npc_Resource_Key()
    {
        var npcNames = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcNames;

        Assert.True(npcNames.TryGetValue("M_L1_DH_1_MOB_BeritraD_03", out var npc));
        Assert.Equal("崇拜者德基許", npc.Name);
        Assert.Equal("M", npc.KeyPrefix);
        Assert.Equal("String_STR_M_L1_DH_1_MOB_BeritraD_03_body", npc.SourceKey);
    }

    [Fact]
    public void NpcCatalog_Contains_Known_Numeric_Code()
    {
        var catalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;

        Assert.True(catalog.TryGetValue(2000002, out var npc));
        Assert.Equal("德拉克紐特弓手", npc.Name);
        Assert.Equal(NpcCatalogKind.Monster, npc.Kind);
    }

    [Fact]
    public void NpcCatalog_Contains_Bridged_Current_Client_Entry()
    {
        var catalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;

        Assert.True(catalog.TryGetValue(2405210, out var npc));
        Assert.Equal("盜賊團掠奪者", npc.Name);
        Assert.Equal(NpcCatalogKind.Monster, npc.Kind);
    }

    [Fact]
    public void NpcCatalog_Contains_Summon_Kind_Entry()
    {
        var catalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;

        Assert.True(catalog.TryGetValue(2920015, out var npc));
        Assert.Equal("結縛圈套", npc.Name);
        Assert.Equal(NpcCatalogKind.Summon, npc.Kind);
    }

    [Fact]
    public void NpcCatalog_Classifies_TrainingScarecrow_As_TrainingDummy()
    {
        var catalog = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).NpcCatalog;

        Assert.True(catalog.TryGetValue(2500075, out var npc));
        Assert.Equal("訓練用稻草人", npc.Name);
        Assert.Equal(NpcCatalogKind.TrainingDummy, npc.Kind);
        Assert.True(catalog.TryGetValue(2400032, out var cityDummy));
        Assert.Equal("訓練用稻草人", cityDummy.Name);
        Assert.Equal(NpcCatalogKind.TrainingDummy, cityDummy.Kind);
    }

    [Theory]
    [InlineData(ResourceLanguage.English, 12240010, "Judgment", SkillCategory.Templar, SkillSourceType.PcSkill, "STR_SKILL_PC_TEMPLAR_12240010")]
    [InlineData(ResourceLanguage.TraditionalChinese, 17121450, "痊癒光輝", SkillCategory.Cleric, SkillSourceType.PcSkill, "STR_SKILL_PC_CLERIC_17121450")]
    [InlineData(ResourceLanguage.English, 11800008, "Murderous Burst", SkillCategory.Gladiator, SkillSourceType.PcSkill, "STR_SKILL_PC_GLADIATOR_11800008")]
    [InlineData(ResourceLanguage.English, 19010000, "Flurry", SkillCategory.Brawler, SkillSourceType.PcSkill, "STR_SKILL_PC_FIGHTER_19010000")]
    [InlineData(ResourceLanguage.TraditionalChinese, 19150350, "升天擊[暴走]", SkillCategory.Brawler, SkillSourceType.PcSkill, "STR_SKILL_PC_FIGHTER_19160350")]
    [InlineData(ResourceLanguage.TraditionalChinese, 19160351, "升天擊第1階段", SkillCategory.Brawler, SkillSourceType.PcSkill, "STR_SKILL_PC_FIGHTER_19150001")]
    [InlineData(ResourceLanguage.TraditionalChinese, 19190120, "爆裂拳[暴走]", SkillCategory.Brawler, SkillSourceType.PcSkill, "STR_SKILL_PC_FIGHTER_19200120")]
    [InlineData(ResourceLanguage.TraditionalChinese, 16001316, "風之精靈：暴風", SkillCategory.Elementalist, SkillSourceType.PcSkill, "STR_SKILL_PC_ELEMENTALIST_16001312")]
    [InlineData(ResourceLanguage.TraditionalChinese, 3001110, "神石：海格黛的束縛", SkillCategory.Item, SkillSourceType.ClientSkill, "SkillString_ITEM_3001110")]
    public void Skills_Expose_SkillDat_Based_Identity_With_Localized_Text(
        string language,
        int skillId,
        string expectedName,
        SkillCategory expectedCategory,
        SkillSourceType expectedSourceType,
        string expectedSourceKey)
    {
        var skills = ResourceCatalog.Load(language).Skills;

        Assert.True(skills.TryGetValue(skillId, out var skill));
        Assert.Equal(expectedName, skill.Name);
        Assert.Equal(expectedCategory, skill.Category);
        Assert.Equal(expectedSourceType, skill.SourceType);
        Assert.Equal(expectedSourceKey, skill.SourceKey);
    }

    [Fact]
    public void Skills_Expose_Triggered_Sibling_Metadata()
    {
        var skills = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).Skills;

        Assert.True(skills.TryGetValue(17040250, out var judgmentLightning));
        Assert.Contains(17050250, judgmentLightning.EnumerateTriggeredSkillIds());
    }

    [Fact]
    public void Skills_Expose_Full_SkillDat_Client_Record_Set()
    {
        var skills = ResourceCatalog.Load(ResourceLanguage.English).Skills;

        Assert.True(skills.Count > 13_000);
        Assert.True(skills.TryGetValue(1227237, out var npcAttack));
        Assert.Equal("Attack", npcAttack.Name);
        Assert.Equal(SkillCategory.Npc, npcAttack.Category);
        Assert.Equal(SkillSourceType.ClientSkill, npcAttack.SourceType);
        Assert.Equal("SkillString_NPC_Attack", npcAttack.SourceKey);

        Assert.True(skills.TryGetValue(1227265, out var namedNpcSkill));
        Assert.Equal("Wraith Surge", namedNpcSkill.Name);
        Assert.Equal("SkillString_NPC_1227260", namedNpcSkill.SourceKey);
    }

    [Fact]
    public void SkillClientMetadata_Exposes_SkillDat_Metadata()
    {
        var metadata = ResourceCatalog.LoadShared().SkillClientMetadata;

        Assert.True(metadata.TryGetValue(17050250, out var clericSkill));
        Assert.Equal("Active", clericSkill.ActionType);
        Assert.Equal("Negative", clericSkill.DispositionType);
        Assert.Equal("Magic", clericSkill.DamageType);
        Assert.Equal("MainTarget", clericSkill.TargetProcessType);
        Assert.Equal("Attack", clericSkill.ClientCategoryType);

        Assert.True(metadata.TryGetValue(1227265, out var npcSkill));
        Assert.Equal("System", npcSkill.ActionType);
        Assert.Equal("Negative", npcSkill.DispositionType);
        Assert.Equal("Magic", npcSkill.DamageType);
        Assert.Equal("Self", npcSkill.TargetProcessType);
    }

    [Theory]
    [InlineData(17040250, 17040000, 17040250, 17040000, 0b10010, 0, false)]
    [InlineData(19010040, 19010000, 19010040, 19010000, 0b01000, 0, false)]
    [InlineData(19010047, 19010000, 19010047, 19010000, 0b01000, 7, false)]
    [InlineData(19150350, 19150000, 19150350, 19150000, 0b10100, 0, true)]
    [InlineData(19160351, 19160000, 19160351, 19160000, 0b10100, 1, false)]
    [InlineData(19190120, 19190000, 19190120, 19190000, 0b00011, 0, true)]
    [InlineData(1227237, 1227237, 1227237, 1227237, 0, 0, false)]
    [InlineData(12090230, 12090000, 12090230, 12090000, 0b00110, 0, true)]
    public void SkillDisplayProjections_Expose_Resource_Display_Projection(
        int skillCode,
        int expectedPresentationSkillId,
        int expectedDisplaySkillId,
        int expectedBaseSkillId,
        int expectedSpecializationMask,
        int expectedVariantState,
        bool expectedIsChargeSkill)
    {
        var projections = ResourceCatalog.LoadShared().SkillDisplayProjections;

        Assert.True(projections.TryGetValue(skillCode, out var projection));
        Assert.Equal(skillCode, projection.SkillCode);
        Assert.Equal(expectedPresentationSkillId, projection.PresentationSkillId);
        Assert.Equal(expectedDisplaySkillId, projection.DisplaySkillId);
        Assert.Equal(expectedBaseSkillId, projection.BaseSkillId);
        Assert.Equal(expectedSpecializationMask, projection.SpecializationMask);
        Assert.Equal(expectedVariantState, projection.VariantState);
        Assert.Equal(expectedIsChargeSkill, projection.IsChargeSkill);
    }

    [Theory]
    [InlineData(17040257, 17050000, 17050000, 17040000, 0b10010, 7)]
    [InlineData(16030047, 16030000, 16030040, 16030000, 0b01000, 7)]
    [InlineData(16257000, 16107000, 16107000, 16250000, 0, 0)]
    [InlineData(18370047, 18370000, 18370000, 18370000, 0b01000, 7)]
    public void SkillDisplayProjections_Expose_Packet_Display_Projection(
        int skillCode,
        int expectedPresentationSkillId,
        int expectedDisplaySkillId,
        int expectedBaseSkillId,
        int expectedSpecializationMask,
        int expectedVariantState)
    {
        var skills = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).Skills;
        var projections = ResourceCatalog.LoadShared().SkillDisplayProjections;

        Assert.DoesNotContain(skills, skill => skill.SkillId == skillCode);
        Assert.True(projections.TryGetValue(skillCode, out var projection));
        Assert.Equal(skillCode, projection.SkillCode);
        Assert.Equal(expectedPresentationSkillId, projection.PresentationSkillId);
        Assert.Equal(expectedDisplaySkillId, projection.DisplaySkillId);
        Assert.Equal(expectedBaseSkillId, projection.BaseSkillId);
        Assert.Equal(expectedSpecializationMask, projection.SpecializationMask);
        Assert.Equal(expectedVariantState, projection.VariantState);
    }

    [Theory]
    [InlineData(30011101, 3001110)]
    [InlineData(12272651, 1227265)]
    public void SkillEffectReferences_Map_Effect_Ids_Back_To_Owner_Skills(int effectId, int expectedSkillId)
    {
        var references = ResourceCatalog.LoadShared().SkillEffectReferences;

        Assert.Contains(references, reference => reference.SkillId == expectedSkillId && reference.References(effectId));
    }

    [Theory]
    [InlineData(1, "ICON_TE_SKILL_001.webp")]
    [InlineData(12240010, "ICON_TE_SKILL_004.webp")]
    [InlineData(16300243, "ICON_EL_SKILL_030.webp")]
    [InlineData(17440000, "ICON_CL_SKILL_046.webp")]
    [InlineData(17440047, "ICON_CL_SKILL_046.webp")]
    [InlineData(19150350, "ICON_GT_SKILL_015.webp")]
    [InlineData(19160351, "ICON_GT_SKILL_016.webp")]
    [InlineData(19190120, "ICON_GT_SKILL_019.webp")]
    [InlineData(19200120, "ICON_GT_SKILL_020.webp")]
    [InlineData(16001316, "ICON_EL_SKILL_024.webp")]
    [InlineData(3001110, "Icon_Item_Usable_Godstone_WP_r_004.webp")]
    [InlineData(30011101, "Icon_Item_Usable_Godstone_WP_r_004.webp")]
    [InlineData(3000122, "Icon_Item_Usable_Godstone_WP_r_016.webp")]
    [InlineData(30001221, "Icon_Item_Usable_Godstone_WP_r_016.webp")]
    public void Generated_SkillIconCatalog_Resolves_Asset_Outside_Pack(int skillCode, string expectedAssetName)
    {
        var assetName = SkillIconCatalog.ResolveAssetName(skillCode);

        Assert.Equal(expectedAssetName, assetName);
        Assert.True(File.Exists(ResolveSkillIconPath(assetName)));
    }

    [Theory]
    [InlineData(20u, "渾沌艾雷修藍塔下層")]
    [InlineData(22u, "渾沌艾雷修藍塔中層")]
    [InlineData(50u, "萬神殿")]
    [InlineData(1000u, "波伊塔")]
    [InlineData(1010u, "斐爾特朗")]
    [InlineData(154001u, "科赫塔監視哨所")]
    [InlineData(200003u, "惡夢")]
    [InlineData(503001u, "深淵迴廊")]
    [InlineData(503006u, "深淵迴廊")]
    [InlineData(504006u, "深淵迴廊")]
    [InlineData(600002u, "克勞洞穴")]
    [InlineData(600011u, "烏努庫庫峽谷")]
    [InlineData(600091u, "凶猛的角岩窟")]
    [InlineData(600121u, "無之搖籃")]
    [InlineData(840037u, "褪色的脈動書庫")]
    [InlineData(500017u, "布里特拉空襲")]
    public void Maps_Resolve_Client_Table_Scene_Id_Aliases(uint mapId, string expectedName)
    {
        var snapshot = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese);

        Assert.Equal(expectedName, snapshot.ResolveMapName(mapId));
    }

    [Theory]
    [InlineData(ResourceLanguage.English, 1001, "Siel", "SIE")]
    [InlineData(ResourceLanguage.TraditionalChinese, 1001, "希埃爾", "希埃")]
    [InlineData(ResourceLanguage.TraditionalChinese, 2001, "伊斯拉佩爾", "伊斯")]
    public void ServerNames_Resolve_ServerName_Dat_Names(string language, int code, string expectedServerName, string expectedShortServerName)
    {
        var snapshot = ResourceCatalog.Load(language);

        Assert.True(snapshot.ServerNames.TryGetValue(code, out var server));
        Assert.Equal(code, server.Code);
        Assert.Equal(expectedServerName, server.ServerName);
        Assert.Equal(expectedShortServerName, server.ShortServerName);
        Assert.Equal(expectedServerName, snapshot.ResolveServerName(code));
        Assert.Equal(expectedShortServerName, snapshot.ResolveShortServerName(code));
    }

    [Fact]
    public void Resources_Assembly_Does_Not_Reference_Sqlite()
    {
        var references = typeof(ResourceCatalog).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, assembly => string.Equals(assembly.Name, "Microsoft.Data.Sqlite", StringComparison.Ordinal));
    }

    [Fact]
    public void Output_Does_Not_Contain_Resources_Db()
    {
        var outputDirectory = AppContext.BaseDirectory;

        Assert.False(File.Exists(Path.Combine(outputDirectory, "resources.db")));
    }

    [Fact]
    public void Manifest_Contains_Shared_And_Locale_Embedded_Packs()
    {
        var assembly = typeof(ResourceCatalog).Assembly;
        var resourceNames = assembly.GetManifestResourceNames().ToHashSet(StringComparer.Ordinal);
        var manifestType = ResolveGeneratedType("ResourcePackManifest");
        var sharedResourceName = Assert.IsType<string>(manifestType.GetField("SharedResourceName", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        var locales = Assert.IsType<IEnumerable>(manifestType.GetProperty("Locales", BindingFlags.Public | BindingFlags.Static)!.GetValue(null), exactMatch: false);
        var localeResourceNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var locale in locales)
        {
            var localeType = locale.GetType();
            var language = Assert.IsType<string>(localeType.GetProperty("Language")!.GetValue(locale));
            var resourceName = Assert.IsType<string>(localeType.GetProperty("ResourceName")!.GetValue(locale));
            localeResourceNames.Add(language, resourceName);
        }

        Assert.Contains(sharedResourceName, resourceNames);
        Assert.Equal(
            new[] { ResourceLanguage.English, ResourceLanguage.Korean, ResourceLanguage.TraditionalChinese },
            localeResourceNames.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.All(localeResourceNames.Values, resourceName => Assert.Contains(resourceName, resourceNames));
    }

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

    private static Type ResolveGeneratedType(string name)
        => typeof(ResourceCatalog).Assembly.GetType($"Cloris.Aion2Flow.Resources.Generated.{name}", throwOnError: true)!;

    private static Type ResolveCatalogType(string name)
        => typeof(ResourceCatalog).Assembly.GetType($"Cloris.Aion2Flow.Resources.Catalog.{name}", throwOnError: true)!;

    private static void AssertLoadPackPayloadFails(string resourceName, byte expectedKind, int expectedUncompressedLength, ulong expectedChecksum)
    {
        var readerType = ResolveCatalogType("ResourcePackReader");
        var method = readerType.GetMethod("LoadPackPayload", BindingFlags.NonPublic | BindingFlags.Static)!;
        var ex = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [resourceName, expectedKind, expectedUncompressedLength, expectedChecksum]));
        Assert.IsType<InvalidDataException>(ex.InnerException);
    }

    private static string ResolveSkillIconPath(string? assetName)
    {
        Assert.False(string.IsNullOrWhiteSpace(assetName));
        foreach (var root in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var current in EnumerateParents(new DirectoryInfo(root)))
            {
                var candidate = Path.Combine(current.FullName, "Aion2Flow", "Assets", "Images", "Skills", assetName!);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                var srcCandidate = Path.Combine(current.FullName, "src", "Aion2Flow", "Assets", "Images", "Skills", assetName!);
                if (File.Exists(srcCandidate))
                {
                    return srcCandidate;
                }
            }
        }

        return assetName!;
    }

    private static IEnumerable<DirectoryInfo> EnumerateParents(DirectoryInfo? start)
    {
        for (var current = start; current is not null; current = current.Parent)
        {
            yield return current;
        }
    }
}
