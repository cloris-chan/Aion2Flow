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
    public void SkillSemantics_Expose_Current_Client_Tables_And_Node_Facets()
    {
        var shared = ResourceCatalog.LoadShared();
        var semantics = shared.SkillSemantics;
        var graph = shared.SkillSemanticOwnerGraph;

        Assert.Equal(30_470, semantics.Effects.Count);
        Assert.Equal(10_160, semantics.EffectFilters.Count);
        Assert.Equal(324, semantics.EffectFilterLocations.Count);
        Assert.Equal(71_533, semantics.EffectLevels.Count);
        Assert.Equal(2_421, semantics.Projectiles.Count);
        Assert.Equal(6_574, semantics.Abnormals.Count);
        Assert.Equal(12_681, semantics.AbnormalEffects.Count);
        Assert.Equal(15_851, semantics.AbnormalEffectLevels.Count);
        Assert.Equal(61, semantics.AbnormalEffectTypes.Count);
        Assert.Equal(30, semantics.AbnormalOverlapFx.Count);
        Assert.Equal(3, semantics.AbnormalProperties.Count);
        Assert.Equal(3_269, semantics.AbnormalStrings.Count);

        Assert.True(graph.TryResolveEffect(101000011, out var directHeal));
        Assert.Equal(SkillSemanticFacet.Healing, directHeal.DirectFacets);
        Assert.Equal(SkillSemanticFacet.Healing, directHeal.Facets);

        Assert.True(graph.TryResolveEffect(1406004012, out var dotApplication));
        Assert.Equal(SkillSemanticFacet.None, dotApplication.DirectFacets);
        Assert.Equal(
            SkillSemanticFacet.DamageOverTime | SkillSemanticFacet.Debuff,
            dotApplication.Facets & (SkillSemanticFacet.DamageOverTime | SkillSemanticFacet.Debuff));

        Assert.True(graph.Profiles.TryGetValue(14060040, out var griffonArrow));
        Assert.Equal(
            SkillSemanticFacet.Damage | SkillSemanticFacet.DamageOverTime | SkillSemanticFacet.Debuff,
            griffonArrow.Facets & (SkillSemanticFacet.Damage | SkillSemanticFacet.DamageOverTime | SkillSemanticFacet.Debuff));
        Assert.Contains(140600401, griffonArrow.EffectGroupIds);
        Assert.Contains(1406004012, griffonArrow.EffectIds);
    }

    [Fact]
    public void SkillSemantics_Preserve_Effect_Slots_And_Typed_Value_Links()
    {
        var shared = ResourceCatalog.LoadShared();
        var graph = shared.SkillSemanticOwnerGraph;

        Assert.True(graph.EffectSlots.Count > 20_000);
        Assert.True(graph.EffectSlotsBySkillId.TryGetValue(14060040, out var griffonArrowSlots));
        Assert.Contains(griffonArrowSlots, static slot =>
            slot.EffectGroupIds.Contains(140600401) &&
            slot.EffectIds.Contains(1406004012) &&
            (slot.Facets & SkillSemanticFacet.DamageOverTime) != 0);

        Assert.True(graph.TryResolveResourceReference(400840, 4008, out var slotResolution));
        Assert.Equal(SkillSemanticResourceNodeKind.SkillEffectGroup, slotResolution.NodeKind);
        Assert.Equal(40084, slotResolution.NodeId);
        Assert.NotNull(slotResolution.Slot);
        Assert.Equal(4008, slotResolution.Slot!.SkillId);

        Assert.True(shared.SkillSemantics.Effects[1406004012].Links.AppliedAbnormalId > 0);
        Assert.Contains(shared.SkillSemantics.Effects.Values, static effect => effect.Links.TriggeredSkillId > 0);
        Assert.Contains(shared.SkillSemantics.AbnormalEffects.Values, static effect => effect.Links.LinkedAbnormalId > 0);
        Assert.Contains(shared.SkillSemantics.AbnormalEffects.Values, static effect => effect.Links.TriggeredSkillId > 0);
    }

    [Fact]
    public void SkillSemanticOwnerGraph_Resolves_All_Known_Transitive_Semantic_Edges()
    {
        var graph = ResourceCatalog.LoadShared().SkillSemanticOwnerGraph;
        var transitiveKinds = new[]
        {
            SkillSemanticOwnerEdgeKind.AbnormalTriggeredSkill,
            SkillSemanticOwnerEdgeKind.EffectTriggeredSkill,
            SkillSemanticOwnerEdgeKind.AbnormalLinkedAbnormal
        };

        var edges = graph.Edges.Where(edge => transitiveKinds.Contains(edge.Kind)).ToArray();

        Assert.NotEmpty(edges);
        Assert.All(edges, static edge => Assert.True(edge.IsResolved));
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
        Assert.Equal(40, clericSkill.SkillLvMax);
        Assert.Equal(SkillClientSkillType.Active, clericSkill.SkillType);
        Assert.Equal(SkillClientSkillDispositionType.Negative, clericSkill.SkillDispositionType);
        Assert.Equal(SkillClientSkillDamageType.Magic, clericSkill.SkillDamageType);
        Assert.Equal(SkillClientTargetProcessType.MainTarget, clericSkill.TargetProcessType);
        Assert.Equal(SkillClientSkillCategoryType.Attack, clericSkill.CategoryTypeList & SkillClientSkillCategoryType.Attack);
        Assert.Equal(170000031, clericSkill.TargetFilterId);
        Assert.Equal(2000f, clericSkill.NeedSkillUseRange);
        Assert.Equal(2000f, clericSkill.NeedSkillUseHeightRange);
        Assert.False(clericSkill.NeedWeaponUseRange);
        Assert.False(clericSkill.NeedWeaponUseHeightRange);
        Assert.Equal(1900f, clericSkill.NeedSkillFollowRange);
        Assert.Equal(1900f, clericSkill.NeedSkillFollowHeightRange);
        Assert.True(clericSkill.CoolTimeStat);
        Assert.Equal(0f, clericSkill.NeedCoolTime);
        Assert.Equal(120, clericSkill.NeedCostMp);
        Assert.Equal(0L, clericSkill.NeedCostHp);
        Assert.Equal(1, clericSkill.SealStoneConsumptionCount);
        Assert.Equal(22, clericSkill.AutoUseId);
        Assert.False(clericSkill.AutoBattleOff);
        Assert.Equal(1, clericSkill.WideCastingAlertSequence);
        Assert.Equal(SkillClientSkillAutoType.On, clericSkill.SkillAutoType);

        Assert.True(metadata.TryGetValue(1227265, out var npcSkill));
        Assert.Equal("SkillString_NPC_1227260", npcSkill.SourceKey);
        Assert.Equal(1227260, npcSkill.SourceSkillId);
        Assert.Equal(SkillSourceKeyRelation.GenericSourceKey, npcSkill.SourceRelation);
        Assert.Equal(SkillClientSkillType.System, npcSkill.SkillType);
        Assert.Equal(SkillClientSkillDispositionType.Negative, npcSkill.SkillDispositionType);
        Assert.Equal(SkillClientSkillDamageType.Magic, npcSkill.SkillDamageType);
        Assert.Equal(SkillClientTargetProcessType.Self, npcSkill.TargetProcessType);

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
        Assert.Equal(SkillClientNeedMainWeaponType.Orb, elementalistSkill.NeedMainWeaponTypes);
        Assert.Equal(SkillClientSkillCategoryType.Buff, elementalistSkill.CategoryTypeList);
        Assert.Equal(SkillClientRotateType.All, elementalistSkill.RotateType);
        Assert.True(elementalistSkill.IgnoreUseAim);
        Assert.False(elementalistSkill.ExtendingWeaponDistanceRange);
        Assert.False(elementalistSkill.ExtendingWeaponFilter);
        Assert.Equal(SkillClientProjectileTargetLocationType.ProjectileFire, elementalistSkill.ProjectileTargetLocationType);
        Assert.False(elementalistSkill.NotFollow);
        Assert.True(elementalistSkill.HideSkillfloater);
        Assert.False(elementalistSkill.IsAttackOnSkill);
        Assert.False(elementalistSkill.AttackOffBySkillStart);
        Assert.False(elementalistSkill.ProxyUsingSkill);
        Assert.False(elementalistSkill.UseSkillFlush);
        Assert.False(elementalistSkill.CanUseWhenPeaceState);
        Assert.Equal(13, elementalistSkill.AutoUseId);

        Assert.True(metadata.TryGetValue(16250000, out var autoUseBanSkill));
        Assert.Equal(1003, autoUseBanSkill.BanGroupId);
        Assert.Equal(5, autoUseBanSkill.AutoUseId);
        Assert.Equal(160000001, autoUseBanSkill.TargetFilterId);
        Assert.True(autoUseBanSkill.CoolTimeStat);
        Assert.Equal(90000f, autoUseBanSkill.NeedCoolTime);
        Assert.Equal(200, autoUseBanSkill.NeedCostMp);
        Assert.Equal(SkillClientSkillAutoType.On, autoUseBanSkill.SkillAutoType);

        Assert.True(metadata.TryGetValue(16200000, out var nestedElementalistSkill));
        Assert.Equal("STR_SKILL_PC_ELEMENTALIST_16200000", nestedElementalistSkill.SourceKey);
        Assert.Equal("Elementalist_Skill020_LvUp", nestedElementalistSkill.ParentKey);
        Assert.Equal(16190050, nestedElementalistSkill.TopLevelSkillId);

        Assert.True(metadata.TryGetValue(11030000, out var chainSkill));
        Assert.Equal(11020000, chainSkill.ChainSkillPrevSkillId);
        Assert.False(chainSkill.RotateImmediately);
        Assert.False(chainSkill.RotateSync);
        Assert.False(chainSkill.IgnoreLockOn);
        Assert.False(chainSkill.IgnoreCollision);
        Assert.Equal(10000, chainSkill.ChainSkillActivateRate);
        Assert.Equal(3000, chainSkill.ChainSkillAvailableTime);
        Assert.False(chainSkill.IgnoreUseAim);
        Assert.True(chainSkill.ExtendingWeaponDistanceRange);
        Assert.True(chainSkill.ExtendingWeaponFilter);
        Assert.False(chainSkill.ProxyUsingSkill);
        Assert.True(chainSkill.UseSkillFlush);
        Assert.False(chainSkill.CanUseWhenPeaceState);

        Assert.True(metadata.TryGetValue(10000001, out var commonSkill));
        Assert.Equal(0, commonSkill.RowBaseSkillId);
        Assert.Equal(SkillClientSkillCategoryType.Heal, commonSkill.CategoryTypeList);

        Assert.True(metadata.TryGetValue(9007, out var autoLoadSkill));
        Assert.Equal(SkillClientAutoLoadType.Time, autoLoadSkill.AutoLoadType);
        Assert.Equal(2, autoLoadSkill.SkillLvMax);
        Assert.Equal(3, autoLoadSkill.AutoLoadCount);
        Assert.Equal(new[] { 5000, 10000, 15000 }, autoLoadSkill.AutoLoadTimeList);
        Assert.True(autoLoadSkill.ProxyUsingSkill);

        Assert.True(metadata.TryGetValue(17410000, out var autoLoadEffectSkill));
        Assert.True(autoLoadEffectSkill.ShowChainSkillHudUI);
        Assert.False(autoLoadEffectSkill.IsStigmaSkill);
        Assert.Equal(174100001, autoLoadEffectSkill.ToggleOnAbnormalId);
        Assert.Equal(SkillClientAutoLoadType.None, autoLoadEffectSkill.AutoLoadType);
        Assert.Equal(25, autoLoadEffectSkill.SkillLvMax);
        Assert.Equal(0, autoLoadEffectSkill.AutoLoadCount);
        Assert.Empty(autoLoadEffectSkill.AutoLoadTimeList);
        Assert.Equal("None", autoLoadEffectSkill.CastingDecalName);

        Assert.True(metadata.TryGetValue(1200504, out var casterFxSkill));
        Assert.Equal("AB_WaterBind_SplashHit", casterFxSkill.SkillCasterFx);
        Assert.Equal("None", casterFxSkill.AnimationName);
        Assert.Equal(2700, casterFxSkill.ImpulseStrength);

        Assert.True(metadata.TryGetValue(3000021, out var targetFxSkill));
        Assert.Equal("SE_GodStone_Splash_Fire", targetFxSkill.SkillTargetFx);

        Assert.True(metadata.TryGetValue(9166, out var castingAnimationSkill));
        Assert.Equal("KrallWar_01_Skill04_Dummy", castingAnimationSkill.AnimationName);
        Assert.Equal("KrallWar_01_Skill04_Casting", castingAnimationSkill.CastingAnimationName);

        Assert.True(metadata.TryGetValue(5001, out var groundAnimationSkill));
        Assert.Equal(SkillClientAutoLoadType.Stack, groundAnimationSkill.AutoLoadType);
        Assert.Equal(1, groundAnimationSkill.SkillLvMax);
        Assert.Equal(3, groundAnimationSkill.AutoLoadCount);
        Assert.Empty(groundAnimationSkill.AutoLoadTimeList);
        Assert.Equal("Throw_01", groundAnimationSkill.AnimationName);
        Assert.Equal("Throw_01_Ground", groundAnimationSkill.GroundAnimationName);
        Assert.Equal(50011, groundAnimationSkill.TargetFilterId);
        Assert.Equal(3000f, groundAnimationSkill.NeedSkillUseRange);
        Assert.Equal(3000f, groundAnimationSkill.NeedSkillUseHeightRange);
        Assert.Equal(3000, groundAnimationSkill.GroundRadius);
        Assert.Equal(500, groundAnimationSkill.GroundHeight);
        Assert.Equal(400, groundAnimationSkill.GroundSkillRadius);
        Assert.Equal(1, groundAnimationSkill.WideCastingAlertSequence);

        Assert.True(metadata.TryGetValue(16030047, out var layoutOnlySkill));
        Assert.Equal("None", layoutOnlySkill.SourceKey);
        Assert.Equal(0, layoutOnlySkill.SourceSkillId);
        Assert.Equal(SkillSourceKeyRelation.None, layoutOnlySkill.SourceRelation);
        Assert.Equal("None", layoutOnlySkill.ParentKey);
        Assert.Equal(16030047, layoutOnlySkill.TopLevelSkillId);
        Assert.Equal(SkillClientSkillType.System, layoutOnlySkill.SkillType);
        Assert.Equal(SkillClientSkillDamageType.Magic, layoutOnlySkill.SkillDamageType);
        Assert.Equal(16030000, layoutOnlySkill.RowBaseSkillId);
    }

    [Fact]
    public void SkillClientMetadata_Exposes_Public_Usmap_Overlap_Metadata()
    {
        var metadata = ResourceCatalog.LoadShared().SkillClientMetadata;

        Assert.True(metadata.TryGetValue(17050250, out var clericSkill));
        Assert.False(clericSkill.IsBasicSkill);
        Assert.False(clericSkill.IsInterruptSkill);
        Assert.True(clericSkill.IsCancelSkill);
        Assert.Equal(0, clericSkill.ChargeId);
        Assert.False(clericSkill.IgnoreSpeedStat);
        Assert.False(clericSkill.IgnoreCannotUseSkill);
        Assert.False(clericSkill.IgnoreCastingStat);
        Assert.Equal(0, clericSkill.CastingTime);
        Assert.Equal(17050000, clericSkill.GroupCoolTimeId);
        Assert.True(clericSkill.MoveableInUse);
        Assert.False(clericSkill.MoveableInCasting);
        Assert.False(clericSkill.MoveableInGround);
        Assert.False(clericSkill.GlideInUse);
        Assert.False(clericSkill.UnableWhenCanNotMoveControl);
        Assert.True(clericSkill.AdjustMoveSpeed);
        Assert.False(clericSkill.SkillAutoFollowIgnore);
        Assert.True(clericSkill.SkillCombatType);
        Assert.Equal(SkillClientNeedMainWeaponType.Mace, clericSkill.NeedMainWeaponTypes);

        Assert.True(metadata.TryGetValue(11100000, out var chargeSkill));
        Assert.True(chargeSkill.IsCancelSkill);
        Assert.Equal(11100000, chargeSkill.ChargeId);
        Assert.Equal(11100000, chargeSkill.GroupCoolTimeId);
        Assert.True(chargeSkill.SkillCombatType);
        Assert.Equal(SkillClientNeedMainWeaponType.Greatsword, chargeSkill.NeedMainWeaponTypes);

        Assert.True(metadata.TryGetValue(9166, out var castingSkill));
        Assert.Equal(5000, castingSkill.CastingTime);
        Assert.Equal(9166, castingSkill.GroupCoolTimeId);
        Assert.True(castingSkill.SkillCombatType);

        Assert.True(metadata.TryGetValue(9080, out var ignoreSpeedSkill));
        Assert.Equal(SkillClientSkillSubType.Instant, ignoreSpeedSkill.SkillSubType);
        Assert.True(ignoreSpeedSkill.IgnoreSpeedStat);
        Assert.True(ignoreSpeedSkill.MoveableInUse);
        Assert.False(ignoreSpeedSkill.SkillCombatType);

        Assert.True(metadata.TryGetValue(1101, out var glideSkill));
        Assert.True(glideSkill.GlideInUse);
        Assert.True(glideSkill.MoveableInUse);

        Assert.True(metadata.TryGetValue(9935, out var autoFollowIgnoredSkill));
        Assert.True(autoFollowIgnoredSkill.SkillAutoFollowIgnore);
        Assert.Equal(9933, autoFollowIgnoredSkill.GroupCoolTimeId);
        Assert.True(autoFollowIgnoredSkill.SkillCombatType);
    }

    [Theory]
    [InlineData(11010047, 11420000)]
    [InlineData(17040250, 17040000)]
    [InlineData(19010040, 19010000)]
    [InlineData(19010047, 19010000)]
    [InlineData(19150350, 19150000)]
    [InlineData(19160351, 19150000)]
    [InlineData(19190120, 19190000)]
    [InlineData(1227237, 1227237)]
    [InlineData(12090230, 12090000)]
    public void SkillBaseProjections_Expose_Resource_Base_Projection(
        int skillCode,
        int expectedBaseSkillId)
    {
        var projections = ResourceCatalog.LoadShared().SkillBaseProjections;

        Assert.True(projections.TryGetValue(skillCode, out var projection));
        Assert.Equal(skillCode, projection.SkillCode);
        Assert.Equal(expectedBaseSkillId, projection.BaseSkillId);
    }

    [Theory]
    [InlineData(17040257, 17050000)]
    [InlineData(16030047, 16030000)]
    [InlineData(16257000, 16257000)]
    [InlineData(18370047, 18370000)]
    public void SkillBaseProjections_Expose_Packet_Base_Projection(
        int skillCode,
        int expectedBaseSkillId)
    {
        var skills = ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).Skills;
        var projections = ResourceCatalog.LoadShared().SkillBaseProjections;

        Assert.DoesNotContain(skills, skill => skill.SkillId == skillCode);
        Assert.True(projections.TryGetValue(skillCode, out var projection));
        Assert.Equal(skillCode, projection.SkillCode);
        Assert.Equal(expectedBaseSkillId, projection.BaseSkillId);
    }

    [Fact]
    public void SkillBaseProjections_Expose_Structured_Lookups()
    {
        var shared = ResourceCatalog.LoadShared();

        Assert.True(shared.SkillBaseProjectionsByBaseSkillId.TryGetValue(16030000, out var elementalBaseProjections));
        Assert.Contains(elementalBaseProjections, projection =>
            projection.SkillCode == 16030047 &&
            projection.BaseSkillId == 16030000);

        Assert.True(shared.SkillBaseProjectionsByBaseSkillId.TryGetValue(17050000, out var clericBaseProjections));
        Assert.Contains(clericBaseProjections, projection =>
            projection.SkillCode == 17040257 &&
            projection.BaseSkillId == 17050000);
    }

    [Theory]
    [InlineData(30011101, 3001110)]
    [InlineData(12272651, 1227265)]
    [InlineData(160300471, 16030047)]
    [InlineData(170402571, 17040257)]
    public void SkillEffectReferences_Map_Reference_Codes_Back_To_Owner_Skills(int referenceCode, int expectedSkillId)
    {
        var references = ResourceCatalog.LoadShared().SkillEffectReferences;

        Assert.Contains(references, reference => reference.SkillId == expectedSkillId && reference.References(referenceCode));
    }

    [Fact]
    public void SkillEffectReferences_Expose_Typed_Effect_List_Edges()
    {
        var references = ResourceCatalog.LoadShared().SkillEffectReferences;

        Assert.Contains(references, reference =>
            reference.SkillId == 5001 &&
            reference.Slot == 0 &&
            reference.Kind == SkillEffectReferenceKind.SkillEffectFilterId &&
            reference.EffectCode == 50011);
        Assert.Contains(references, reference =>
            reference.SkillId == 5001 &&
            reference.Slot == 0 &&
            reference.Kind == SkillEffectReferenceKind.SkillEffectGroupId &&
            reference.EffectCode == 50011);
        Assert.Contains(references, reference =>
            reference.SkillId == 5001 &&
            reference.Slot == 0 &&
            reference.Kind == SkillEffectReferenceKind.ProjectileId &&
            reference.EffectCode == 50011);
    }

    [Fact]
    public void SkillEffectReferences_Expose_Toggle_On_Abnormal_Relations()
    {
        var references = ResourceCatalog.LoadShared().SkillEffectReferences;

        Assert.Contains(references, reference =>
            reference.SkillId == 17410000 &&
            reference.Slot < 0 &&
            reference.Kind == SkillEffectReferenceKind.ToggleOnAbnormalId &&
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
            reference.Kind == SkillEffectReferenceKind.ToggleOnAbnormalId &&
            reference.EffectCode == 174100001);

        Assert.True(shared.SkillEffectReferencesByEffectCode.TryGetValue(174100001, out var autoLoadCodeReferences));
        Assert.Contains(autoLoadCodeReferences, reference =>
            reference.SkillId == 17410000 &&
            reference.Kind == SkillEffectReferenceKind.ToggleOnAbnormalId);

        Assert.True(shared.SkillEffectReferencesByEffectCode.TryGetValue(50011, out var multiKindReferences));
        Assert.Contains(multiKindReferences, reference => reference.SkillId == 5001 && reference.Kind == SkillEffectReferenceKind.SkillEffectFilterId);
        Assert.Contains(multiKindReferences, reference => reference.SkillId == 5001 && reference.Kind == SkillEffectReferenceKind.SkillEffectGroupId);
        Assert.Contains(multiKindReferences, reference => reference.SkillId == 5001 && reference.Kind == SkillEffectReferenceKind.ProjectileId);
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
    public void ResourcePackSectionIds_UseSemanticOrder()
    {
        var readerType = ResolveCatalogType("ResourcePackReader");
        var sectionIdType = readerType.GetNestedType("SectionId", BindingFlags.NonPublic)!;
        var expected = new (string Name, ushort Value)[]
        {
            ("SkillDefinitions", 1),
            ("SkillClientMetadata", 2),
            ("SkillBaseProjections", 3),
            ("SkillEffectReferences", 4),
            ("SkillRelatedSkills", 5),
            ("SkillSemanticStrings", 6),
            ("SkillEffects", 7),
            ("SkillEffectFilters", 8),
            ("SkillEffectFilterLocations", 9),
            ("SkillEffectLevels", 10),
            ("SkillProjectiles", 11),
            ("SkillAbnormals", 12),
            ("SkillAbnormalEffects", 13),
            ("SkillAbnormalEffectLevels", 14),
            ("SkillAbnormalEffectTypes", 15),
            ("SkillAbnormalOverlapFx", 16),
            ("SkillAbnormalProperties", 17),
            ("SkillAbnormalStrings", 18),
            ("NpcDefinitions", 19),
            ("NpcNameDefinitions", 20),
            ("KnownMapIds", 21),
            ("ServerCodes", 22),
            ("SkillNames", 101),
            ("NpcNames", 102),
            ("NpcCatalogNames", 103),
            ("MapNames", 104),
            ("ServerNames", 105)
        };
        var actual = Enum.GetNames(sectionIdType)
            .Select(name => (Name: name, Value: Convert.ToUInt16(Enum.Parse(sectionIdType, name))))
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SkillSemanticOwnerEdgeKinds_UseSemanticOrder()
    {
        Assert.Equal(
        [
            SkillSemanticOwnerEdgeKind.SkillEffectFilter,
            SkillSemanticOwnerEdgeKind.SkillEffectGroup,
            SkillSemanticOwnerEdgeKind.SkillProjectile,
            SkillSemanticOwnerEdgeKind.SkillToggleAbnormal,
            SkillSemanticOwnerEdgeKind.EffectGroupEffect,
            SkillSemanticOwnerEdgeKind.EffectLevel,
            SkillSemanticOwnerEdgeKind.EffectAbnormal,
            SkillSemanticOwnerEdgeKind.EffectTriggeredSkill,
            SkillSemanticOwnerEdgeKind.FilterTargetAbnormalCriterion,
            SkillSemanticOwnerEdgeKind.FilterTargetAbnormalGroupCriterion,
            SkillSemanticOwnerEdgeKind.ProjectileChain,
            SkillSemanticOwnerEdgeKind.ProjectileEffectFilter,
            SkillSemanticOwnerEdgeKind.ProjectileEffectGroup,
            SkillSemanticOwnerEdgeKind.ProjectileTargetFilter,
            SkillSemanticOwnerEdgeKind.AbnormalEffect,
            SkillSemanticOwnerEdgeKind.AbnormalEffectLevel,
            SkillSemanticOwnerEdgeKind.AbnormalTriggeredSkill,
            SkillSemanticOwnerEdgeKind.AbnormalLinkedAbnormal
        ],
        Enum.GetValues<SkillSemanticOwnerEdgeKind>());
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
