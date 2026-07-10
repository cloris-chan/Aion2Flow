using System.Collections.Frozen;
using System.Globalization;

namespace Cloris.Aion2Flow.Resources.Catalog;

internal sealed class SkillSemanticFacetIndex
{
    private const int MaximumPropagationPasses = 256;

    private SkillSemanticFacetIndex(
        IReadOnlyDictionary<int, SkillSemanticFacet> skillFacets,
        IReadOnlyDictionary<int, SkillSemanticFacet> effectGroupFacets,
        IReadOnlyDictionary<int, SkillSemanticFacet> directEffectFacets,
        IReadOnlyDictionary<int, SkillSemanticFacet> effectFacets,
        IReadOnlyDictionary<int, SkillSemanticFacet> projectileFacets,
        IReadOnlyDictionary<int, SkillSemanticFacet> abnormalFacets,
        IReadOnlyDictionary<int, SkillSemanticFacet> abnormalEffectFacets)
    {
        SkillFacets = skillFacets;
        EffectGroupFacets = effectGroupFacets;
        DirectEffectFacets = directEffectFacets;
        EffectFacets = effectFacets;
        ProjectileFacets = projectileFacets;
        AbnormalFacets = abnormalFacets;
        AbnormalEffectFacets = abnormalEffectFacets;
    }

    public IReadOnlyDictionary<int, SkillSemanticFacet> SkillFacets { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> EffectGroupFacets { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> DirectEffectFacets { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> EffectFacets { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> ProjectileFacets { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> AbnormalFacets { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> AbnormalEffectFacets { get; }

    public static SkillSemanticFacetIndex Build(
        SkillSemanticCatalog semantics,
        IReadOnlyDictionary<int, SkillEffectReference[]> referencesBySkillId)
    {
        var skillFacets = referencesBySkillId.Keys.ToDictionary(static id => id, static _ => SkillSemanticFacet.None);
        var effectGroupFacets = semantics.EffectsByGroupId.Keys.ToDictionary(static id => id, static _ => SkillSemanticFacet.None);
        var directEffectFacets = semantics.Effects.Values.ToDictionary(static row => row.Id, ClassifyEffect);
        var effectFacets = new Dictionary<int, SkillSemanticFacet>(directEffectFacets);
        var projectileFacets = semantics.Projectiles.Keys.ToDictionary(static id => id, static _ => SkillSemanticFacet.None);
        var abnormalFacets = semantics.Abnormals.Values.ToDictionary(static row => row.Id, ClassifyAbnormal);
        var abnormalEffectFacets = semantics.AbnormalEffects.Values.ToDictionary(static row => row.Id, ClassifyAbnormalEffect);

        Propagate(
            semantics,
            referencesBySkillId,
            skillFacets,
            effectGroupFacets,
            effectFacets,
            projectileFacets,
            abnormalFacets,
            abnormalEffectFacets);

        foreach (var skillId in referencesBySkillId.Keys)
        {
            if (skillFacets[skillId] == SkillSemanticFacet.None)
            {
                skillFacets[skillId] = SkillSemanticFacet.Support;
            }
        }

        Propagate(
            semantics,
            referencesBySkillId,
            skillFacets,
            effectGroupFacets,
            effectFacets,
            projectileFacets,
            abnormalFacets,
            abnormalEffectFacets);

        return new SkillSemanticFacetIndex(
            skillFacets.ToFrozenDictionary(),
            effectGroupFacets.ToFrozenDictionary(),
            directEffectFacets.ToFrozenDictionary(),
            effectFacets.ToFrozenDictionary(),
            projectileFacets.ToFrozenDictionary(),
            abnormalFacets.ToFrozenDictionary(),
            abnormalEffectFacets.ToFrozenDictionary());
    }

    private static void Propagate(
        SkillSemanticCatalog semantics,
        IReadOnlyDictionary<int, SkillEffectReference[]> referencesBySkillId,
        Dictionary<int, SkillSemanticFacet> skillFacets,
        Dictionary<int, SkillSemanticFacet> effectGroupFacets,
        Dictionary<int, SkillSemanticFacet> effectFacets,
        Dictionary<int, SkillSemanticFacet> projectileFacets,
        Dictionary<int, SkillSemanticFacet> abnormalFacets,
        IReadOnlyDictionary<int, SkillSemanticFacet> abnormalEffectFacets)
    {
        for (var pass = 0; pass < MaximumPropagationPasses; pass++)
        {
            var changed = false;

            foreach (var abnormal in semantics.Abnormals.Values)
            {
                var facets = abnormalFacets[abnormal.Id];
                if (semantics.AbnormalEffectsByAbnormalId.TryGetValue(abnormal.Id, out var effects))
                {
                    foreach (var effect in effects)
                    {
                        facets |= abnormalEffectFacets[effect.Id];
                        if (effect.Links.LinkedAbnormalId is var linkedAbnormalId and > 0 &&
                            abnormalFacets.TryGetValue(linkedAbnormalId, out var linkedFacets))
                        {
                            facets |= linkedFacets;
                        }

                        if (effect.Links.TriggeredSkillId is var triggeredSkillId and > 0 &&
                            skillFacets.TryGetValue(triggeredSkillId, out var triggeredFacets))
                        {
                            facets |= triggeredFacets;
                        }
                    }
                }

                changed |= Include(abnormalFacets, abnormal.Id, facets);
            }

            foreach (var effect in semantics.Effects.Values)
            {
                var facets = effectFacets[effect.Id];
                if (effect.Links.AppliedAbnormalId is var abnormalId and > 0 &&
                    abnormalFacets.TryGetValue(abnormalId, out var appliedFacets))
                {
                    facets |= appliedFacets;
                }

                if (effect.Links.TriggeredSkillId is var triggeredSkillId and > 0 &&
                    skillFacets.TryGetValue(triggeredSkillId, out var triggeredFacets))
                {
                    facets |= triggeredFacets;
                }

                changed |= Include(effectFacets, effect.Id, facets);
            }

            foreach (var (groupId, effects) in semantics.EffectsByGroupId)
            {
                var facets = effectGroupFacets[groupId];
                foreach (var effect in effects)
                {
                    facets |= effectFacets[effect.Id];
                }

                changed |= Include(effectGroupFacets, groupId, facets);
            }

            foreach (var projectile in semantics.Projectiles.Values)
            {
                var facets = projectileFacets[projectile.Id];
                if (projectile.ChainProjectileId > 0 && projectileFacets.TryGetValue(projectile.ChainProjectileId, out var chainFacets))
                {
                    facets |= chainFacets;
                }

                if (projectile.ChainSkillEffectGroupId > 0 && effectGroupFacets.TryGetValue(projectile.ChainSkillEffectGroupId, out var groupFacets))
                {
                    facets |= groupFacets;
                }

                changed |= Include(projectileFacets, projectile.Id, facets);
            }

            foreach (var (skillId, references) in referencesBySkillId)
            {
                var facets = skillFacets[skillId];
                foreach (var reference in references)
                {
                    facets |= reference.Kind switch
                    {
                        SkillEffectReferenceKind.SkillEffectGroupId => effectGroupFacets.GetValueOrDefault(reference.EffectCode),
                        SkillEffectReferenceKind.ProjectileId => projectileFacets.GetValueOrDefault(reference.EffectCode),
                        SkillEffectReferenceKind.ToggleOnAbnormalId => abnormalFacets.GetValueOrDefault(reference.EffectCode),
                        _ => SkillSemanticFacet.None
                    };
                }

                changed |= Include(skillFacets, skillId, facets);
            }

            if (!changed)
            {
                return;
            }
        }

        throw new InvalidDataException($"Skill semantic facet propagation did not converge within {MaximumPropagationPasses.ToString(CultureInfo.InvariantCulture)} passes.");
    }

    private static bool Include(Dictionary<int, SkillSemanticFacet> values, int id, SkillSemanticFacet facets)
    {
        var combined = values[id] | facets;
        if (combined == values[id])
        {
            return false;
        }

        values[id] = combined;
        return true;
    }

    private static SkillSemanticFacet ClassifyEffect(SkillEffectDefinition effect)
        => effect.EffectType.Value switch
        {
            "Damage" or "SelfDamage" => SkillSemanticFacet.Damage,
            "HpHeal" or "HpHealRatio" or "HpHeal_Item" or "HpHealRatio_Item" => SkillSemanticFacet.Healing,
            "Abnormal" or "Abnormal_True" or "Abnormal_Cc" or "Abnormal_Passive" or "CasterSkill" or "NoEffect" => SkillSemanticFacet.None,
            _ => SkillSemanticFacet.Support
        };

    private static SkillSemanticFacet ClassifyAbnormal(SkillAbnormalDefinition abnormal)
    {
        if (string.Equals(abnormal.AbnormalType.Value, "DeBuff", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(abnormal.DisplayCategory.Value, "Debuff", StringComparison.OrdinalIgnoreCase))
        {
            return SkillSemanticFacet.Debuff;
        }

        if (string.Equals(abnormal.AbnormalType.Value, "Buff", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(abnormal.AbnormalType.Value, "Passive", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(abnormal.DisplayCategory.Value, "Buff", StringComparison.OrdinalIgnoreCase))
        {
            return SkillSemanticFacet.Buff;
        }

        return SkillSemanticFacet.Support;
    }

    private static SkillSemanticFacet ClassifyAbnormalEffect(SkillAbnormalEffectDefinition effect)
        => effect.EffectType.Value switch
        {
            "Dot_NormalCalc" or "Dot_TargetMaxHP" or "Dot_Dmg" or "Dot_TargetHP" => SkillSemanticFacet.DamageOverTime,
            "Hot" or "Hot_Ratio" or "Hot_Item" or "HotRatio_Item" => SkillSemanticFacet.HealingOverTime,
            "HpBarrier" => SkillSemanticFacet.Shield,
            "DamageReflect" or "EndDamage_AbnormalOwnerStat" => SkillSemanticFacet.Damage,
            "ConvertDamageToHpHeal" => SkillSemanticFacet.Healing,
            _ => SkillSemanticFacet.None
        };
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
