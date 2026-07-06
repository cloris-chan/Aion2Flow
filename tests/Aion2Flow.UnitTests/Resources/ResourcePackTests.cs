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
    public void SkillRelatedSkills_Expose_SkillDat_Packet_Alias_Relations()
    {
        var relatedSkills = ResourceCatalog.LoadShared().SkillRelatedSkills;

        Assert.Contains(relatedSkills, relation =>
            relation.OwnerSkillId == 17040250 &&
            relation.RelatedSkillCode == 17040257 &&
            relation.RelatedSourceSkillId == 17050000 &&
            relation.Kind == SkillRelationKind.PacketAlias &&
            relation.RelatedSourceKey == "STR_SKILL_PC_CLERIC_17050000" &&
            relation.ParentKey.Length == 0);
    }

    [Fact]
    public void SkillRelatedSkills_Expose_SkillDat_Nested_Block_Relations()
    {
        var relatedSkills = ResourceCatalog.LoadShared().SkillRelatedSkills;

        Assert.Contains(relatedSkills, relation =>
            relation.OwnerSkillId == 16190050 &&
            relation.RelatedSkillCode == 16200000 &&
            relation.RelatedSourceSkillId == 16200000 &&
            relation.Kind == SkillRelationKind.NestedBlock &&
            relation.RelatedSourceKey == "STR_SKILL_PC_ELEMENTALIST_16200000" &&
            relation.ParentKey == "Elementalist_Skill020_LvUp");
    }

    [Fact]
    public void SkillRelatedSkills_Expose_SkillDat_Chain_Reference_Relations()
    {
        var relatedSkills = ResourceCatalog.LoadShared().SkillRelatedSkills;

        Assert.Contains(relatedSkills, relation =>
            relation.OwnerSkillId == 11030000 &&
            relation.RelatedSkillCode == 11020000 &&
            relation.RelatedSourceSkillId == 11020000 &&
            relation.Kind == SkillRelationKind.ChainReference &&
            relation.RelatedSourceKey == "STR_SKILL_PC_GLADIATOR_11020000" &&
            relation.ParentKey.Length == 0);
    }

    [Fact]
    public void SkillRelatedSkills_Expose_SkillDat_Cancel_Exception_Relations()
    {
        var relatedSkills = ResourceCatalog.LoadShared().SkillRelatedSkills;

        Assert.Contains(relatedSkills, relation =>
            relation.OwnerSkillId == 17040250 &&
            relation.RelatedSkillCode == 17050250 &&
            relation.RelatedSourceSkillId == 17050250 &&
            relation.Kind == SkillRelationKind.CancelException &&
            relation.RelatedSourceKey == "STR_SKILL_PC_CLERIC_17050250" &&
            relation.ParentKey.Length == 0);

        Assert.Contains(relatedSkills, relation =>
            relation.OwnerSkillId == 19090130 &&
            relation.RelatedSkillCode == 19090130 &&
            relation.RelatedSourceSkillId == 19090130 &&
            relation.Kind == SkillRelationKind.CancelException &&
            relation.RelatedSourceKey == "STR_SKILL_PC_FIGHTER_19090130" &&
            relation.ParentKey.Length == 0);
    }

    [Fact]
    public void SkillRelatedSkills_Expose_SkillDat_Row_Base_Relations()
    {
        var relatedSkills = ResourceCatalog.LoadShared().SkillRelatedSkills;

        Assert.Contains(relatedSkills, relation =>
            relation.OwnerSkillId == 16030047 &&
            relation.RelatedSkillCode == 16030000 &&
            relation.RelatedSourceSkillId == 16030000 &&
            relation.Kind == SkillRelationKind.RowBase &&
            relation.RelatedSourceKey == "STR_SKILL_PC_ELEMENTALIST_16030000" &&
            relation.ParentKey == "None");
    }

    [Fact]
    public void SkillRelatedSkills_Expose_Structured_Lookups()
    {
        var shared = ResourceCatalog.LoadShared();

        Assert.True(shared.SkillRelatedSkillsByOwnerSkillId.TryGetValue(17040250, out var clericRelations));
        Assert.Contains(clericRelations, relation =>
            relation.RelatedSkillCode == 17040257 &&
            relation.RelatedSourceSkillId == 17050000 &&
            relation.Kind == SkillRelationKind.PacketAlias);
        Assert.Contains(clericRelations, relation =>
            relation.RelatedSkillCode == 17050250 &&
            relation.Kind == SkillRelationKind.CancelException);

        Assert.True(shared.SkillRelatedSkillsByRelatedSkillCode.TryGetValue(17040257, out var aliasTargetRelations));
        Assert.Contains(aliasTargetRelations, relation =>
            relation.OwnerSkillId == 17040250 &&
            relation.Kind == SkillRelationKind.PacketAlias);

        Assert.True(shared.SkillRelatedSkillsByRelatedSourceSkillId.TryGetValue(16030000, out var rowBaseSourceRelations));
        Assert.Contains(rowBaseSourceRelations, relation =>
            relation.OwnerSkillId == 16030047 &&
            relation.RelatedSkillCode == 16030000 &&
            relation.Kind == SkillRelationKind.RowBase);
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
        Assert.Equal("STR_SKILL_PC_CLERIC_17050250", clericSkill.SourceKey);
        Assert.Equal(17050250, clericSkill.SourceSkillId);
        Assert.Equal(SkillSourceKeyRelation.ExactRecord, clericSkill.SourceRelation);
        Assert.Equal("None", clericSkill.ParentKey);
        Assert.Equal(17050250, clericSkill.TopLevelSkillId);
        Assert.Equal("Active", clericSkill.ActionType);
        Assert.Equal("Negative", clericSkill.DispositionType);
        Assert.Equal("Magic", clericSkill.DamageType);
        Assert.Equal("MainTarget", clericSkill.TargetProcessType);
        Assert.Contains("Attack", clericSkill.ClientCategoryTypes);

        Assert.True(metadata.TryGetValue(1227265, out var npcSkill));
        Assert.Equal("SkillString_NPC_1227260", npcSkill.SourceKey);
        Assert.Equal(1227260, npcSkill.SourceSkillId);
        Assert.Equal(SkillSourceKeyRelation.GenericSourceKey, npcSkill.SourceRelation);
        Assert.Equal("System", npcSkill.ActionType);
        Assert.Equal("Negative", npcSkill.DispositionType);
        Assert.Equal("Magic", npcSkill.DamageType);
        Assert.Equal("Self", npcSkill.TargetProcessType);

        Assert.True(metadata.TryGetValue(16001316, out var sameFamilySkill));
        Assert.Equal("STR_SKILL_PC_ELEMENTALIST_16001312", sameFamilySkill.SourceKey);
        Assert.Equal(16001312, sameFamilySkill.SourceSkillId);
        Assert.Equal(SkillSourceKeyRelation.SameFamilyRecord, sameFamilySkill.SourceRelation);

        Assert.True(metadata.TryGetValue(19150350, out var stancePairSkill));
        Assert.Equal("STR_SKILL_PC_FIGHTER_19160350", stancePairSkill.SourceKey);
        Assert.Equal(19160350, stancePairSkill.SourceSkillId);
        Assert.Equal(SkillSourceKeyRelation.FighterStancePairRecord, stancePairSkill.SourceRelation);

        Assert.True(metadata.TryGetValue(17040257, out var packetAliasSkill));
        Assert.Equal("STR_SKILL_PC_CLERIC_17050000", packetAliasSkill.SourceKey);
        Assert.Equal(17050000, packetAliasSkill.SourceSkillId);
        Assert.Equal(SkillSourceKeyRelation.PacketAlias, packetAliasSkill.SourceRelation);

        Assert.True(metadata.TryGetValue(16190050, out var elementalistSkill));
        Assert.Equal("Elementalist_Skill019", elementalistSkill.ImplementationName);
        Assert.Equal("Skill/ICON_EL_SKILL_019", elementalistSkill.IconPath);
        Assert.Equal(["Orb"], elementalistSkill.WeaponTypes);
        Assert.Equal(["Buff"], elementalistSkill.ClientCategoryTypes);
        Assert.Equal("All", elementalistSkill.RotateType);
        Assert.Equal("ProjectileFire", elementalistSkill.TargetLocationType);

        Assert.True(metadata.TryGetValue(16200000, out var nestedElementalistSkill));
        Assert.Equal("STR_SKILL_PC_ELEMENTALIST_16200000", nestedElementalistSkill.SourceKey);
        Assert.Equal("Elementalist_Skill020_LvUp", nestedElementalistSkill.ParentKey);
        Assert.Equal(16190050, nestedElementalistSkill.TopLevelSkillId);

        Assert.True(metadata.TryGetValue(11030000, out var chainSkill));
        Assert.Equal(11020000, chainSkill.ChainRelatedSkillId);
        Assert.Equal(0, chainSkill.ChainFlags);
        Assert.Equal(10000, chainSkill.ChainPrimaryWindowMs);
        Assert.Equal(3000, chainSkill.ChainSecondaryWindowMs);

        Assert.True(metadata.TryGetValue(10000001, out var commonSkill));
        Assert.Equal(0, commonSkill.RowBaseSkillId);
        Assert.Equal(["Heal"], commonSkill.ClientCategoryTypes);

        Assert.True(metadata.TryGetValue(9007, out var autoLoadSkill));
        Assert.Equal("Time", autoLoadSkill.AutoLoadType);
        Assert.Equal([3, 3, 5000, 10000, 15000], autoLoadSkill.AutoLoadPayloadInts);

        Assert.True(metadata.TryGetValue(17410000, out var autoLoadEffectSkill));
        Assert.Equal(1, autoLoadEffectSkill.AutoLoadFlags);
        Assert.Equal(174100001, autoLoadEffectSkill.AutoLoadEffectDataId);
        Assert.Equal("None", autoLoadEffectSkill.AutoLoadType);

        Assert.True(metadata.TryGetValue(16030047, out var layoutOnlySkill));
        Assert.Equal("None", layoutOnlySkill.SourceKey);
        Assert.Equal(0, layoutOnlySkill.SourceSkillId);
        Assert.Equal(SkillSourceKeyRelation.None, layoutOnlySkill.SourceRelation);
        Assert.Equal("None", layoutOnlySkill.ParentKey);
        Assert.Equal(16030047, layoutOnlySkill.TopLevelSkillId);
        Assert.Equal("System", layoutOnlySkill.ActionType);
        Assert.Equal("Magic", layoutOnlySkill.DamageType);
        Assert.Equal("Skill/ICON_EL_SKILL_003", layoutOnlySkill.IconPath);
        Assert.Equal(16030000, layoutOnlySkill.RowBaseSkillId);
    }

    [Theory]
    [InlineData(11010047, 11420000, 11010047, 11420000)]
    [InlineData(17040250, 17040000, 17040250, 17040000)]
    [InlineData(19010040, 19010000, 19010040, 19010000)]
    [InlineData(19010047, 19010000, 19010047, 19010000)]
    [InlineData(19150350, 19150000, 19150350, 19150000)]
    [InlineData(19160351, 19150000, 19160351, 19150000)]
    [InlineData(19190120, 19190000, 19190120, 19190000)]
    [InlineData(1227237, 1227237, 1227237, 1227237)]
    [InlineData(12090230, 12090000, 12090230, 12090000)]
    public void SkillDisplayProjections_Expose_Resource_Display_Projection(
        int skillCode,
        int expectedPresentationSkillId,
        int expectedDisplaySkillId,
        int expectedBaseSkillId)
    {
        var projections = ResourceCatalog.LoadShared().SkillDisplayProjections;

        Assert.True(projections.TryGetValue(skillCode, out var projection));
        Assert.Equal(skillCode, projection.SkillCode);
        Assert.Equal(expectedPresentationSkillId, projection.PresentationSkillId);
        Assert.Equal(expectedDisplaySkillId, projection.DisplaySkillId);
        Assert.Equal(expectedBaseSkillId, projection.BaseSkillId);
    }

    [Theory]
    [InlineData(17040257, 17050000, 17050000, 17050000)]
    [InlineData(16030047, 16030000, 16030000, 16030000)]
    [InlineData(16257000, 16107000, 16107000, 16257000)]
    [InlineData(18370047, 18370000, 18370000, 18370000)]
    public void SkillDisplayProjections_Expose_Packet_Display_Projection(
        int skillCode,
        int expectedPresentationSkillId,
        int expectedDisplaySkillId,
        int expectedBaseSkillId)
    {
        var skills = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).Skills;
        var projections = ResourceCatalog.LoadShared().SkillDisplayProjections;

        Assert.DoesNotContain(skills, skill => skill.SkillId == skillCode);
        Assert.True(projections.TryGetValue(skillCode, out var projection));
        Assert.Equal(skillCode, projection.SkillCode);
        Assert.Equal(expectedPresentationSkillId, projection.PresentationSkillId);
        Assert.Equal(expectedDisplaySkillId, projection.DisplaySkillId);
        Assert.Equal(expectedBaseSkillId, projection.BaseSkillId);
    }

    [Fact]
    public void SkillDisplayProjections_Expose_Structured_Lookups()
    {
        var shared = ResourceCatalog.LoadShared();

        Assert.True(shared.SkillDisplayProjectionsByBaseSkillId.TryGetValue(16030000, out var elementalBaseProjections));
        Assert.Contains(elementalBaseProjections, projection =>
            projection.SkillCode == 16030047 &&
            projection.PresentationSkillId == 16030000 &&
            projection.DisplaySkillId == 16030000);

        Assert.True(shared.SkillDisplayProjectionsByPresentationSkillId.TryGetValue(17050000, out var clericPresentationProjections));
        Assert.Contains(clericPresentationProjections, projection =>
            projection.SkillCode == 17040257 &&
            projection.DisplaySkillId == 17050000 &&
            projection.BaseSkillId == 17050000);

        Assert.True(shared.SkillDisplayProjectionsByDisplaySkillId.TryGetValue(17050000, out var clericDisplayProjections));
        Assert.Contains(clericDisplayProjections, projection =>
            projection.SkillCode == 17040257 &&
            projection.PresentationSkillId == 17050000);
    }

    [Theory]
    [InlineData(30011101, 3001110)]
    [InlineData(12272651, 1227265)]
    [InlineData(160300471, 16030047)]
    [InlineData(170402571, 17040257)]
    public void SkillEffectReferences_Map_Effect_Ids_Back_To_Owner_Skills(int effectId, int expectedSkillId)
    {
        var references = ResourceCatalog.LoadShared().SkillEffectReferences;

        Assert.Contains(references, reference => reference.SkillId == expectedSkillId && reference.References(effectId));
    }

    [Fact]
    public void SkillEffectReferences_Expose_Typed_Effect_List_Edges()
    {
        var references = ResourceCatalog.LoadShared().SkillEffectReferences;

        Assert.Contains(references, reference =>
            reference.SkillId == 5001 &&
            reference.Slot == 0 &&
            reference.Kind == SkillEffectReferenceKind.EffectId &&
            reference.EffectCode == 50011);
        Assert.Contains(references, reference =>
            reference.SkillId == 5001 &&
            reference.Slot == 0 &&
            reference.Kind == SkillEffectReferenceKind.EffectDataId &&
            reference.EffectCode == 50011);
        Assert.Contains(references, reference =>
            reference.SkillId == 5001 &&
            reference.Slot == 0 &&
            reference.Kind == SkillEffectReferenceKind.AuxEffectId &&
            reference.EffectCode == 50011);
    }

    [Fact]
    public void SkillEffectReferences_Expose_AutoLoad_Effect_Data_Relations()
    {
        var references = ResourceCatalog.LoadShared().SkillEffectReferences;

        Assert.Contains(references, reference =>
            reference.SkillId == 17410000 &&
            reference.Slot < 0 &&
            reference.Kind == SkillEffectReferenceKind.AutoLoadEffectDataId &&
            reference.EffectCode == 174100001);
    }

    [Fact]
    public void SkillEffectReferences_Expose_Structured_Lookups()
    {
        var shared = ResourceCatalog.LoadShared();

        Assert.True(shared.SkillEffectReferencesBySkillId.TryGetValue(17410000, out var clericAutoLoadReferences));
        Assert.Contains(clericAutoLoadReferences, reference =>
            reference.SkillId == 17410000 &&
            reference.Slot < 0 &&
            reference.Kind == SkillEffectReferenceKind.AutoLoadEffectDataId &&
            reference.EffectCode == 174100001);

        Assert.True(shared.SkillEffectReferencesByEffectCode.TryGetValue(174100001, out var autoLoadCodeReferences));
        Assert.Contains(autoLoadCodeReferences, reference =>
            reference.SkillId == 17410000 &&
            reference.Kind == SkillEffectReferenceKind.AutoLoadEffectDataId);

        Assert.True(shared.SkillEffectReferencesByEffectCode.TryGetValue(50011, out var multiKindReferences));
        Assert.Contains(multiKindReferences, reference => reference.SkillId == 5001 && reference.Kind == SkillEffectReferenceKind.EffectId);
        Assert.Contains(multiKindReferences, reference => reference.SkillId == 5001 && reference.Kind == SkillEffectReferenceKind.EffectDataId);
        Assert.Contains(multiKindReferences, reference => reference.SkillId == 5001 && reference.Kind == SkillEffectReferenceKind.AuxEffectId);
    }

    [Theory]
    [InlineData(1, "ICON_TE_SKILL_001.webp")]
    [InlineData(12240010, "ICON_TE_SKILL_004.webp")]
    [InlineData(16030047, "ICON_EL_SKILL_003.webp")]
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
