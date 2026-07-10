using System.Collections.Frozen;

namespace Cloris.Aion2Flow.Resources.Catalog;

public sealed class SkillSemanticCatalog(
    IReadOnlyDictionary<int, SkillEffectDefinition> effects,
    IReadOnlyDictionary<int, SkillEffectFilterDefinition> effectFilters,
    IReadOnlyDictionary<int, SkillEffectFilterLocationDefinition> effectFilterLocations,
    IReadOnlyDictionary<int, SkillEffectLevelDefinition> effectLevels,
    IReadOnlyDictionary<int, SkillProjectileDefinition> projectiles,
    IReadOnlyDictionary<int, SkillAbnormalDefinition> abnormals,
    IReadOnlyDictionary<int, SkillAbnormalEffectDefinition> abnormalEffects,
    IReadOnlyDictionary<int, SkillAbnormalEffectLevelDefinition> abnormalEffectLevels,
    IReadOnlyDictionary<string, SkillAbnormalEffectTypeDefinition> abnormalEffectTypes,
    IReadOnlyList<SkillAbnormalOverlapFxDefinition> abnormalOverlapFx,
    IReadOnlyDictionary<string, SkillAbnormalPropertyDefinition> abnormalProperties,
    IReadOnlyDictionary<string, SkillAbnormalStringDefinition> abnormalStrings)
{
    public IReadOnlyDictionary<int, SkillEffectDefinition> Effects { get; } = effects;
    public IReadOnlyDictionary<int, SkillEffectFilterDefinition> EffectFilters { get; } = effectFilters;
    public IReadOnlyDictionary<int, SkillEffectFilterLocationDefinition> EffectFilterLocations { get; } = effectFilterLocations;
    public IReadOnlyDictionary<int, SkillEffectLevelDefinition> EffectLevels { get; } = effectLevels;
    public IReadOnlyDictionary<int, SkillProjectileDefinition> Projectiles { get; } = projectiles;
    public IReadOnlyDictionary<int, SkillAbnormalDefinition> Abnormals { get; } = abnormals;
    public IReadOnlyDictionary<int, SkillAbnormalEffectDefinition> AbnormalEffects { get; } = abnormalEffects;
    public IReadOnlyDictionary<int, SkillAbnormalEffectLevelDefinition> AbnormalEffectLevels { get; } = abnormalEffectLevels;
    public IReadOnlyDictionary<string, SkillAbnormalEffectTypeDefinition> AbnormalEffectTypes { get; } = abnormalEffectTypes;
    public IReadOnlyList<SkillAbnormalOverlapFxDefinition> AbnormalOverlapFx { get; } = abnormalOverlapFx;
    public IReadOnlyDictionary<string, SkillAbnormalPropertyDefinition> AbnormalProperties { get; } = abnormalProperties;
    public IReadOnlyDictionary<string, SkillAbnormalStringDefinition> AbnormalStrings { get; } = abnormalStrings;

    public IReadOnlyDictionary<int, IReadOnlyList<SkillEffectDefinition>> EffectsByGroupId { get; } = BuildLookup(effects.Values, static value => value.GroupId);
    public IReadOnlyDictionary<string, IReadOnlyList<SkillEffectLevelDefinition>> EffectLevelsByGroupId { get; } = BuildLookup(effectLevels.Values, static value => value.GroupId, StringComparer.Ordinal);
    public IReadOnlyDictionary<uint, IReadOnlyList<SkillAbnormalDefinition>> AbnormalsByGroupId { get; } = BuildLookup(abnormals.Values, static value => value.GroupId);
    public IReadOnlyDictionary<int, IReadOnlyList<SkillAbnormalEffectDefinition>> AbnormalEffectsByAbnormalId { get; } = BuildLookup(abnormalEffects.Values, static value => value.AbnormalId);
    public IReadOnlyDictionary<string, IReadOnlyList<SkillAbnormalEffectLevelDefinition>> AbnormalEffectLevelsByGroupId { get; } = BuildLookup(abnormalEffectLevels.Values, static value => value.GroupId, StringComparer.Ordinal);
    public IReadOnlyDictionary<int, IReadOnlyList<SkillAbnormalOverlapFxDefinition>> AbnormalOverlapFxByAbnormalId { get; } = BuildLookup(abnormalOverlapFx, static value => value.AbnormalId);

    private static IReadOnlyDictionary<TKey, IReadOnlyList<TValue>> BuildLookup<TKey, TValue>(
        IEnumerable<TValue> values,
        Func<TValue, TKey> keySelector,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        var groups = new Dictionary<TKey, List<TValue>>(comparer);
        foreach (var value in values)
        {
            var key = keySelector(value);
            if (!groups.TryGetValue(key, out var group))
            {
                group = [];
                groups.Add(key, group);
            }

            group.Add(value);
        }

        var result = new Dictionary<TKey, IReadOnlyList<TValue>>(groups.Count, comparer);
        foreach (var (key, group) in groups)
        {
            result.Add(key, group.ToArray());
        }

        return result.ToFrozenDictionary(comparer);
    }
}
