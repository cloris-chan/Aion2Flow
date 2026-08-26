using System.Collections.Frozen;
using System.Globalization;

namespace Cloris.Aion2Flow.Resources.Catalog;

internal sealed class SkillSemanticValueIndex
{
    private const int MaximumPropagationPasses = 256;

    private static readonly FrozenSet<string> KnownNonQuantifiedEffectTypes = new[]
    {
        "Abnormal", "Abnormal_Cc", "Abnormal_Passive", "Abnormal_True", "AggroTransfer", "APBurn", "APHeal", "APHealRatio",
        "CasterSkill", "CombatExpCharge_Item", "DecreaseCooltime", "Despawn", "Dispel", "Dispel_Cc", "Dispel_True", "DpHeal",
        "DpHeal_Item", "DpHealRatio", "DpHealRatio_Item", "ExchangeResource", "ExpCharge_Item", "Explode_Dot", "FpHeal", "FpHeal_Item",
        "FpHealRatio", "FpHealRatio_Item", "GroggyAttackCountDecreasePoint", "GroggyAttackCountIncreasePoint", "GroggyGuardDecreaseEnergy",
        "GroggyGuardIncreaseEnergy", "IncreaseCooltime", "IncreaseDp", "MPBurn", "MPBurnRatio", "MpHeal", "MpHeal_Item", "MpHealRatio",
        "MpHealRatio_Item", "Multiply_Dot", "NoEffect", "OpHeal", "OpHeal_Item", "OpHealRatio", "OpHealRatio_Item", "PlayAct",
        "Pre_Cast_Teleport_Caster", "Pre_SkillStart_Teleport_Caster", "Rebirth", "Spawn", "SpHeal", "SpHeal_Item", "SpHealRatio",
        "SpHealRatio_Item", "Teleport_Caster", "Toggle"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> KnownNonQuantifiedAbnormalEffectTypes = new[]
    {
        "AbnormalClear", "ActivateSkill", "AddAbnormalByCondition", "AdjustMaxAddDamage", "AdjustMaxSkillRate", "AdjustMinAddDamage",
        "AdjustMinSkillRate", "AdjustSecondaryStatMaxValue", "AdjustSecondaryStatMinValue", "Aerial", "ArenaActiveParticipation", "BattlePass", "BeamPierce", "Bind",
        "Bleed", "Blind", "BlockActive", "Blockade", "BlockAniHit", "BlockComposite", "BlockFlight", "BlockGlide", "BlockInteract", "BlockInteractGather",
        "BlockItemCasting", "BlockItemEquipItem", "BlockJump", "BlockLooting", "BlockMoveControl", "BlockMovePosition", "BlockRaidEnter",
        "BlockRotate", "BlockSelfRebirth", "BlockSkill", "BlockSpringboard", "BlockSprint", "BlockTargeting", "BlockTargetSkill",
        "CastingCancel", "CastSkill", "CastSkillByCaster", "CastSkillToCaster", "ChangeJumpZvelocity", "ChangeRelationshipEntity",
        "CloneSkillEffect", "CondDefenceBlock", "CondDefenceEvade", "CondDefenceMiss", "CondDefenceShieldBlock", "CondDefenceWeaponBlock", "ConvertDamageToMP",
        "DamageMultiply", "DamageTransfer", "DecreaseCastingTime", "DecreaseCooltimeByContribute", "DecreaseCooltimeByKill", "DeletableFear",
        "Dpot", "Dpot_Item", "Dpot_Ratio", "DpotRatio_Item", "EndCastSkill", "EndCastSkillByCaster", "EndCastSkillToCaster", "EndMotion",
        "EquipItemStatAmplify_Accessory", "EquipItemStatAmplify_Weapon", "ExposeMinimapAsGenocider", "Fear", "FixSkillLevel",
        "ForceCriticalDamage", "ForceCriticalDamageByCaster", "ForceKill", "ForceKillByGetDamage", "ForceMissDamage",
        "ForceMissDamageToCaster", "Fot", "Fot_Item", "Fot_Ratio", "FotRatio_Item", "Frozen", "GaugeEffect", "GetDamageMultiply",
        "GetDamageMultiplyByCaster", "GimmickA", "GimmickB", "Groggy", "GroggyGuard", "GroggyPause", "HandToHandCombat", "Hold",
        "IgnoreBackAttack", "IgnoreCollision", "IgnoreFrontAttack", "IgnorePenaltyLoss_Exp_PvE", "IgnorePenaltyLoss_Exp_PvP",
        "IgnorePenaltyLoss_Item_PvE", "IgnorePenaltyLoss_Item_PvP", "ImmuneAbnormal", "ImmuneBind", "ImmuneDecreaseHP",
        "ImmuneGroggyGuardDecreaseEnergy", "ImmuneSkill", "ImmuneSkillEffectGroup", "KillZone", "KiskBlock", "KnockdownInstant", "Loophole",
        "Mimic", "Mot", "Mot_Item", "Mot_Ratio", "MotRatio_Item", "Move_CannotUseSkill", "Move_CanUseSkill", "NoEffect", "None",
        "OnExpireAbnormal", "OnGlide", "OnLanding", "Oot", "Oot_Item", "Oot_Ratio", "OotRatio_Item", "OverrideIconType", "Paralysis",
        "PetPolymorph", "Poison", "Polymorph", "Polymorph_CannotMove", "Racing", "ReverseMove", "RiftBlock", "Rotate", "Silence",
        "SkillProxy", "SkillTransform", "Sleep", "Slow", "Snare", "SoftMove", "SoftMoveDot", "Sot", "Sot_Item", "Sot_Ratio",
        "SotRatio_Item", "StanceCriticalDamage", "StanceMissDamage", "StanceShieldBlock", "StanceWeaponBlock", "StatChange", "StatConvert",
        "Stealth", "StealthSeek", "Stiffen", "Stone", "Stun", "Taunt", "TeleportCasterTarget", "ToggleAbnormal",
        "TriggerSkillByAddAbnormal", "TriggerSkillByAttackAll", "TriggerSkillByGetAbnormal", "TriggerSkillByGetDamage", "TriggerSkillByKill",
        "TriggerSkillBySelfBlock", "TriggerSkillBySelfEvade", "TriggerSkillBySelfShieldBlock", "TriggerSkillBySelfWeaponBlock", "TriggerSkillCheck_BossMonster",
        "UndeletableFear", "UnLockNpcLanguageType", "UsableItemCooltimeChange", "UseRebirth", "WeaponEquipable"
    }.ToFrozenSet(StringComparer.Ordinal);

    private SkillSemanticValueIndex(
        IReadOnlyDictionary<int, SkillSemanticValue> skillSemantics,
        IReadOnlyDictionary<int, SkillSemanticValue> effectGroupSemantics,
        IReadOnlyDictionary<int, SkillSemanticValue> directEffectSemantics,
        IReadOnlyDictionary<int, SkillSemanticValue> effectSemantics,
        IReadOnlyDictionary<int, SkillSemanticValue> projectileSemantics,
        IReadOnlyDictionary<int, SkillSemanticValue> directAbnormalSemantics,
        IReadOnlyDictionary<int, SkillSemanticValue> abnormalSemantics,
        IReadOnlyDictionary<int, SkillSemanticValue> abnormalEffectSemantics)
    {
        SkillSemantics = skillSemantics;
        EffectGroupSemantics = effectGroupSemantics;
        DirectEffectSemantics = directEffectSemantics;
        EffectSemantics = effectSemantics;
        ProjectileSemantics = projectileSemantics;
        DirectAbnormalSemantics = directAbnormalSemantics;
        AbnormalSemantics = abnormalSemantics;
        AbnormalEffectSemantics = abnormalEffectSemantics;
    }

    public IReadOnlyDictionary<int, SkillSemanticValue> SkillSemantics { get; }
    public IReadOnlyDictionary<int, SkillSemanticValue> EffectGroupSemantics { get; }
    public IReadOnlyDictionary<int, SkillSemanticValue> DirectEffectSemantics { get; }
    public IReadOnlyDictionary<int, SkillSemanticValue> EffectSemantics { get; }
    public IReadOnlyDictionary<int, SkillSemanticValue> ProjectileSemantics { get; }
    public IReadOnlyDictionary<int, SkillSemanticValue> DirectAbnormalSemantics { get; }
    public IReadOnlyDictionary<int, SkillSemanticValue> AbnormalSemantics { get; }
    public IReadOnlyDictionary<int, SkillSemanticValue> AbnormalEffectSemantics { get; }

    public static SkillSemanticValueIndex Build(
        SkillSemanticCatalog semantics,
        IReadOnlyDictionary<int, SkillEffectReference[]> referencesBySkillId)
    {
        var skillSemantics = referencesBySkillId.Keys.ToDictionary(static id => id, static _ => SkillSemanticValue.Empty);
        var effectGroupSemantics = semantics.EffectsByGroupId.Keys.ToDictionary(static id => id, static _ => SkillSemanticValue.Empty);
        var directEffectSemantics = semantics.Effects.Values.ToDictionary(static row => row.Id, ClassifyEffect);
        var effectSemantics = new Dictionary<int, SkillSemanticValue>(directEffectSemantics);
        var projectileSemantics = semantics.Projectiles.Keys.ToDictionary(static id => id, static _ => SkillSemanticValue.Empty);
        var directAbnormalSemantics = semantics.Abnormals.Values.ToDictionary(static row => row.Id, ClassifyAbnormal);
        var abnormalSemantics = new Dictionary<int, SkillSemanticValue>(directAbnormalSemantics);
        var abnormalEffectSemantics = semantics.AbnormalEffects.Values.ToDictionary(static row => row.Id, ClassifyAbnormalEffect);

        Propagate(
            semantics,
            referencesBySkillId,
            skillSemantics,
            effectGroupSemantics,
            effectSemantics,
            projectileSemantics,
            abnormalSemantics,
            abnormalEffectSemantics);

        return new SkillSemanticValueIndex(
            skillSemantics.ToFrozenDictionary(),
            effectGroupSemantics.ToFrozenDictionary(),
            directEffectSemantics.ToFrozenDictionary(),
            effectSemantics.ToFrozenDictionary(),
            projectileSemantics.ToFrozenDictionary(),
            directAbnormalSemantics.ToFrozenDictionary(),
            abnormalSemantics.ToFrozenDictionary(),
            abnormalEffectSemantics.ToFrozenDictionary());
    }

    private static void Propagate(
        SkillSemanticCatalog semantics,
        IReadOnlyDictionary<int, SkillEffectReference[]> referencesBySkillId,
        Dictionary<int, SkillSemanticValue> skillSemantics,
        Dictionary<int, SkillSemanticValue> effectGroupSemantics,
        Dictionary<int, SkillSemanticValue> effectSemantics,
        Dictionary<int, SkillSemanticValue> projectileSemantics,
        Dictionary<int, SkillSemanticValue> abnormalSemantics,
        IReadOnlyDictionary<int, SkillSemanticValue> abnormalEffectSemantics)
    {
        for (var pass = 0; pass < MaximumPropagationPasses; pass++)
        {
            var changed = false;

            foreach (var abnormal in semantics.Abnormals.Values)
            {
                var value = abnormalSemantics[abnormal.Id];
                if (semantics.AbnormalEffectsByAbnormalId.TryGetValue(abnormal.Id, out var effects))
                {
                    foreach (var effect in effects)
                    {
                        value |= abnormalEffectSemantics[effect.Id];
                        if (effect.Links.LinkedAbnormalId is var linkedAbnormalId and > 0 &&
                            abnormalSemantics.TryGetValue(linkedAbnormalId, out var linkedValue))
                        {
                            value |= linkedValue;
                        }

                        if (effect.Links.TriggeredSkillId is var triggeredSkillId and > 0 &&
                            skillSemantics.TryGetValue(triggeredSkillId, out var triggeredValue))
                        {
                            value |= triggeredValue;
                        }
                    }
                }

                changed |= Include(abnormalSemantics, abnormal.Id, value);
            }

            foreach (var effect in semantics.Effects.Values)
            {
                var value = effectSemantics[effect.Id];
                if (effect.Links.AppliedAbnormalId is var abnormalId and > 0 &&
                    abnormalSemantics.TryGetValue(abnormalId, out var appliedValue))
                {
                    value |= appliedValue;
                }

                if (effect.Links.TriggeredSkillId is var triggeredSkillId and > 0 &&
                    skillSemantics.TryGetValue(triggeredSkillId, out var triggeredValue))
                {
                    value |= triggeredValue;
                }

                changed |= Include(effectSemantics, effect.Id, value);
            }

            foreach (var (groupId, effects) in semantics.EffectsByGroupId)
            {
                var value = effectGroupSemantics[groupId];
                foreach (var effect in effects)
                    value |= effectSemantics[effect.Id];

                changed |= Include(effectGroupSemantics, groupId, value);
            }

            foreach (var projectile in semantics.Projectiles.Values)
            {
                var value = projectileSemantics[projectile.Id];
                if (projectile.ChainProjectileId > 0 && projectileSemantics.TryGetValue(projectile.ChainProjectileId, out var chainValue))
                    value |= chainValue;

                if (projectile.ChainSkillEffectGroupId > 0 && effectGroupSemantics.TryGetValue(projectile.ChainSkillEffectGroupId, out var groupValue))
                    value |= groupValue;

                changed |= Include(projectileSemantics, projectile.Id, value);
            }

            foreach (var (skillId, references) in referencesBySkillId)
            {
                var value = skillSemantics[skillId];
                foreach (var reference in references)
                {
                    value |= reference.Kind switch
                    {
                        SkillEffectReferenceKind.SkillEffectGroupId => effectGroupSemantics.GetValueOrDefault(reference.EffectCode),
                        SkillEffectReferenceKind.ProjectileId => projectileSemantics.GetValueOrDefault(reference.EffectCode),
                        SkillEffectReferenceKind.ToggleOnAbnormalId => abnormalSemantics.GetValueOrDefault(reference.EffectCode),
                        _ => SkillSemanticValue.Empty
                    };
                }

                changed |= Include(skillSemantics, skillId, value);
            }

            if (!changed)
                return;
        }

        throw new InvalidDataException($"Skill semantic propagation did not converge within {MaximumPropagationPasses.ToString(CultureInfo.InvariantCulture)} passes.");
    }

    private static bool Include(Dictionary<int, SkillSemanticValue> values, int id, SkillSemanticValue value)
    {
        var combined = values[id] | value;
        if (combined == values[id])
            return false;

        values[id] = combined;
        return true;
    }

    private static SkillSemanticValue ClassifyEffect(SkillEffectDefinition effect)
    {
        var type = effect.EffectType.Value;
        return type switch
        {
            "Damage" or "SelfDamage" => SkillSemanticValue.Classified(SkillQuantifiedFacet.DirectDamage),
            "HpHeal" or "HpHealRatio" or "HpHeal_Item" or "HpHealRatio_Item" => SkillSemanticValue.Classified(SkillQuantifiedFacet.DirectHealing),
            _ when KnownNonQuantifiedEffectTypes.Contains(type) => SkillSemanticValue.KnownNonQuantified,
            _ => SkillSemanticValue.Unclassified
        };
    }

    private static SkillSemanticValue ClassifyAbnormal(SkillAbnormalDefinition abnormal)
    {
        var auraFacets = SkillAuraFacet.None;
        if (string.Equals(abnormal.AbnormalType.Value, "DeBuff", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(abnormal.DisplayCategory.Value, "Debuff", StringComparison.OrdinalIgnoreCase))
        {
            auraFacets |= SkillAuraFacet.Debuff;
        }

        if (string.Equals(abnormal.AbnormalType.Value, "Buff", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(abnormal.AbnormalType.Value, "Passive", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(abnormal.AbnormalType.Value, "ItemPassive", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(abnormal.DisplayCategory.Value, "Buff", StringComparison.OrdinalIgnoreCase))
        {
            auraFacets |= SkillAuraFacet.Buff;
        }

        return auraFacets == SkillAuraFacet.None
            ? SkillSemanticValue.Unclassified
            : SkillSemanticValue.Classified(auraFacets: auraFacets);
    }

    private static SkillSemanticValue ClassifyAbnormalEffect(SkillAbnormalEffectDefinition effect)
    {
        var type = effect.EffectType.Value;
        return type switch
        {
            "Dot_NormalCalc" or "Dot_TargetMaxHP" or "Dot_Dmg" or "Dot_TargetHP" => SkillSemanticValue.Classified(SkillQuantifiedFacet.PeriodicDamage),
            "Hot" or "Hot_Ratio" or "Hot_Item" or "HotRatio_Item" => SkillSemanticValue.Classified(SkillQuantifiedFacet.PeriodicHealing),
            "HpBarrier" => SkillSemanticValue.Classified(SkillQuantifiedFacet.Shield),
            "DamageReflect" or "EndDamage_AbnormalOwnerStat" => SkillSemanticValue.Classified(SkillQuantifiedFacet.DirectDamage),
            "ConvertDamageToHpHeal" => SkillSemanticValue.Classified(SkillQuantifiedFacet.DirectHealing),
            _ when KnownNonQuantifiedAbnormalEffectTypes.Contains(type) => SkillSemanticValue.KnownNonQuantified,
            _ => SkillSemanticValue.Unclassified
        };
    }
}

internal static class SkillSemanticReferenceDecoder
{
    public static SkillEffectSemanticLinks DecodeEffectLinks(SkillSemanticEffectType effectType, IReadOnlyList<string> values)
    {
        var abnormalValueIndex = effectType.Value switch
        {
            "Abnormal" or "Abnormal_True" or "Abnormal_Cc" => 2,
            "Abnormal_Passive" => 0,
            _ => -1
        };
        var appliedAbnormalId = GetPositiveInt(values, abnormalValueIndex);
        var triggeredSkillId = string.Equals(effectType.Value, "CasterSkill", StringComparison.Ordinal)
            ? GetPositiveInt(values, 0)
            : 0;
        return new SkillEffectSemanticLinks(appliedAbnormalId, triggeredSkillId);
    }

    public static SkillAbnormalEffectSemanticLinks DecodeAbnormalEffectLinks(SkillSemanticAbnormalEffectType effectType, IReadOnlyList<string> values)
    {
        var triggeredSkillValueIndex = effectType.Value switch
        {
            "CastSkill" or "CastSkillByCaster" or "CastSkillToCaster" => 1,
            "EndCastSkill" or "EndCastSkillByCaster" or "EndCastSkillToCaster" => 0,
            _ when effectType.Value.StartsWith("TriggerSkill", StringComparison.Ordinal) => 0,
            _ => -1
        };
        var linkedAbnormalValueIndex = effectType.Value switch
        {
            "OnExpireAbnormal" or "AddAbnormalByCondition" or "ToggleAbnormal" => 0,
            _ => -1
        };
        return new SkillAbnormalEffectSemanticLinks(
            GetPositiveInt(values, linkedAbnormalValueIndex),
            GetPositiveInt(values, triggeredSkillValueIndex));
    }

    private static int GetPositiveInt(IReadOnlyList<string> values, int valueIndex)
    {
        return valueIndex >= 0 &&
            valueIndex < values.Count &&
            int.TryParse(values[valueIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
            value > 0
                ? value
                : 0;
    }
}
