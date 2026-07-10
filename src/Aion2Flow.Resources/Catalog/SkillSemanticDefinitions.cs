namespace Cloris.Aion2Flow.Resources.Catalog;

public sealed record SkillEffectDefinition(
    int Id,
    int GroupId,
    SkillSemanticEffectType EffectType,
    string TargetHitFx,
    string TargetCriticalFx,
    string TargetFailFx,
    string TargetAdditionalHitFx,
    bool HitAnimationIgnored,
    SkillSemanticHitFxMaterialType HitFxMaterialType,
    int HitFxMaterialIndex,
    SkillSemanticHideFloaterType HideFloaterType,
    int AggroRatio,
    int AggroAbsolute,
    IReadOnlyList<string> EffectValues,
    string LevelGroupId,
    IReadOnlyList<SkillSemanticCasterDirection> CasterDirections,
    IReadOnlyList<SkillEffectCondition> Conditions,
    SkillEffectSemanticLinks Links);

public readonly record struct SkillEffectSemanticLinks(int AppliedAbnormalId, int TriggeredSkillId);

public sealed record SkillEffectCondition(
    SkillSemanticEffectConditionType ConditionType,
    IReadOnlyList<string> Values,
    bool ConditionalExpression);

public sealed record SkillEffectFilterDefinition(
    int Id,
    SkillSemanticEffectRangeType RangeType,
    bool RangeNeedsTarget,
    IReadOnlyList<int> RangeValues,
    bool RelationshipEnemy,
    bool RelationshipFriendly,
    bool RelationshipNoRelation,
    bool TargetDeadPlayer,
    bool TargetIncludesEnvironmentObject,
    bool TargetRiftPvpMode,
    SkillSemanticTargetFilterType TargetFilterType,
    int TargetCountMin,
    int TargetCountMax,
    bool TargetCountCaster,
    int RangeNoticePreviousTime,
    bool RangeNoticeAttached,
    bool RangeNoticeTargetLocation,
    SkillSemanticRangeNoticeSelectColor RangeNoticeSelectColor,
    SkillSemanticRangeNoticeFillType RangeNoticeFillType,
    SkillSemanticNoticeStyleType NoticeStyleType,
    SkillSemanticFilterTargetAbnormalType TargetAbnormalType,
    IReadOnlyList<int> TargetAbnormalIds,
    IReadOnlyList<int> TargetAbnormalGroupIds,
    IReadOnlyList<SkillSemanticAbnormalEffectType> TargetAbnormalEffectTypes);

public sealed record SkillEffectFilterLocationDefinition(
    int Id,
    SkillSemanticEffectRangeLocationType RangeLocationType,
    bool RangeLocationNeedsTarget,
    IReadOnlyList<int> RangeLocationValues,
    int RangeCountMin,
    int RangeCountMax);

public sealed record SkillEffectLevelDefinition(
    int Id,
    string GroupId,
    byte Level,
    int AggroRatio,
    int AggroAbsolute,
    IReadOnlyList<string> EffectValues);

public sealed record SkillProjectileDefinition(
    int Id,
    SkillSemanticProjectileType ProjectileType,
    int Speed,
    string ResourceKey,
    string ProjectileResource,
    SkillSemanticProjectileMovementType MovementType,
    int MovementTypeValue,
    string ArrivalSocket,
    string ArrivalSplashHitFx,
    int ArrivalSplashHitFxPreviewTime,
    int ArrivalFixTimeMin,
    int ArrivalFixTimeMax,
    string StuckResource,
    string BeamResource,
    int ArrivalLifetime,
    SkillSemanticCollideShapeType CollideShapeType,
    int CollideShapeValue01,
    int CollideShapeValue02,
    int CollideShapeValue03,
    int CollideMoveValue01,
    int CollideMoveValue02,
    int CollideCountMin,
    int CollideCountMax,
    int CollideOffsetAngle,
    SkillSemanticCollideMultiShotType CollideMultiShotType,
    int CollideMultiShotValue01,
    int CollideMultiShotValue02,
    int CollideMultiShotValue03,
    int CollideMultiShotValue04,
    int CollideNoticePreviousTime,
    int OffsetX,
    int OffsetY,
    int ChainProjectileId,
    int ChainTargetFilterId,
    int ChainSkillEffectFilterId,
    int ChainSkillEffectGroupId,
    int ChainTargetDuplicateBanTime,
    string TargetResource);

public sealed record SkillAbnormalDefinition(
    int Id,
    string StringKey,
    uint GroupId,
    uint OverlapCount,
    SkillSemanticAbnormalType AbnormalType,
    string FrontAnimation,
    string FrontFx,
    string BackAnimation,
    string BackFx,
    string DeathFx,
    string BeamFx,
    SkillSemanticAbnormalDisplayCategory DisplayCategory,
    int DisplayPriority,
    string Icon,
    string IconFx,
    bool IconVisible,
    SkillSemanticWeaponType IconRequiredWeaponType,
    bool PartyVisible,
    bool DetailIconVisible,
    bool AffixIconVisible,
    SkillSemanticAbnormalHideFloaterType HideFloaterType,
    SkillSemanticDamageType SkillDamageType,
    int IconPriority,
    byte EffectLevelMax,
    SkillSemanticAbnormalOverlapTimeType OverlapTimeType,
    SkillSemanticAbnormalReplaceType ReplaceType,
    bool HideRemainTimeEffect,
    bool SelfDeletable,
    SkillSemanticAbnormalFxVisibleType FxVisibleType,
    bool RestartFx,
    SkillSemanticAbnormalEffectType EffectType,
    int EffectFilterIdForNotice,
    bool CurrentClientFlag);

public sealed record SkillAbnormalEffectDefinition(
    int Id,
    int AbnormalId,
    string EffectFx,
    SkillSemanticAbnormalEffectType EffectType,
    IReadOnlyList<string> Values,
    string LevelGroupId,
    bool CurrentClientFlag,
    SkillAbnormalEffectSemanticLinks Links);

public readonly record struct SkillAbnormalEffectSemanticLinks(int LinkedAbnormalId, int TriggeredSkillId);

public sealed record SkillAbnormalEffectLevelDefinition(
    int Id,
    string GroupId,
    byte Level,
    IReadOnlyList<string> Values);

public sealed record SkillAbnormalEffectTypeDefinition(
    string Name,
    SkillSemanticAbnormalEffectType EffectType,
    string CannotUseSkillReason,
    IReadOnlyList<SkillSemanticCharacterControlType> CannotControlTypes,
    bool KeepLockOn,
    bool BoneHit,
    bool Ragdoll,
    string BoneHitCheckName,
    string BoneHitAttachName,
    SkillSemanticAbnormalEffectAniHitType AniHitType);

public sealed record SkillAbnormalOverlapFxDefinition(int AbnormalId, int OverlapCount, string FrontFx);

public sealed record SkillAbnormalPropertyDefinition(
    string PropertyName,
    IReadOnlyList<SkillSemanticAbnormalEffectType> EffectTypes);

public sealed record SkillAbnormalStringDefinition(
    string Name,
    string DescriptionNameKey,
    string DescriptionSummaryKey,
    string DescriptionEffectKey);
