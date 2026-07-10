using System.Collections.Frozen;

namespace Cloris.Aion2Flow.Resources.Catalog;

internal static partial class ResourcePackReader
{
    private static string[] ReadSkillSemanticStrings(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var result = new string[section.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = ReadString(ref cursor);
        }

        RequireFullyRead(cursor);
        return result;
    }

    private static SkillSemanticCatalog ReadSkillSemanticCatalog(
        IReadOnlyDictionary<SectionId, ResourcePackSection> sections,
        string[] strings)
        => new(
            ReadSkillEffects(RequireSection(sections, SectionId.SkillEffects), strings),
            ReadSkillEffectFilters(RequireSection(sections, SectionId.SkillEffectFilters), strings),
            ReadSkillEffectFilterLocations(RequireSection(sections, SectionId.SkillEffectFilterLocations), strings),
            ReadSkillEffectLevels(RequireSection(sections, SectionId.SkillEffectLevels), strings),
            ReadSkillProjectiles(RequireSection(sections, SectionId.SkillProjectiles), strings),
            ReadSkillAbnormals(RequireSection(sections, SectionId.SkillAbnormals), strings),
            ReadSkillAbnormalEffects(RequireSection(sections, SectionId.SkillAbnormalEffects), strings),
            ReadSkillAbnormalEffectLevels(RequireSection(sections, SectionId.SkillAbnormalEffectLevels), strings),
            ReadSkillAbnormalEffectTypes(RequireSection(sections, SectionId.SkillAbnormalEffectTypes), strings),
            ReadSkillAbnormalOverlapFx(RequireSection(sections, SectionId.SkillAbnormalOverlapFx), strings),
            ReadSkillAbnormalProperties(RequireSection(sections, SectionId.SkillAbnormalProperties), strings),
            ReadSkillAbnormalStrings(RequireSection(sections, SectionId.SkillAbnormalStrings), strings));

    private static FrozenDictionary<int, SkillEffectDefinition> ReadSkillEffects(ResourcePackSection section, string[] strings)
    {
        var cursor = section.Payload.Span;
        var result = new Dictionary<int, SkillEffectDefinition>(section.Count);
        for (var i = 0; i < section.Count; i++)
        {
            var id = ReadInt32(ref cursor);
            var groupId = ReadInt32(ref cursor);
            var effectType = new SkillSemanticEffectType(ReadSemanticString(ref cursor, strings));
            var targetHitFx = ReadSemanticString(ref cursor, strings);
            var targetCriticalFx = ReadSemanticString(ref cursor, strings);
            var targetFailFx = ReadSemanticString(ref cursor, strings);
            var targetAdditionalHitFx = ReadSemanticString(ref cursor, strings);
            var hitAnimationIgnored = ReadBool(ref cursor);
            var hitFxMaterialType = new SkillSemanticHitFxMaterialType(ReadSemanticString(ref cursor, strings));
            var hitFxMaterialIndex = ReadInt32(ref cursor);
            var hideFloaterType = new SkillSemanticHideFloaterType(ReadSemanticString(ref cursor, strings));
            var aggroRatio = ReadInt32(ref cursor);
            var aggroAbsolute = ReadInt32(ref cursor);
            var effectValues = ReadSemanticStrings(ref cursor, strings);
            var definition = new SkillEffectDefinition(
                id,
                groupId,
                effectType,
                targetHitFx,
                targetCriticalFx,
                targetFailFx,
                targetAdditionalHitFx,
                hitAnimationIgnored,
                hitFxMaterialType,
                hitFxMaterialIndex,
                hideFloaterType,
                aggroRatio,
                aggroAbsolute,
                effectValues,
                ReadSemanticString(ref cursor, strings),
                ReadSemanticValues(ref cursor, strings, static value => new SkillSemanticCasterDirection(value)),
                ReadSkillEffectConditions(ref cursor, strings),
                SkillSemanticReferenceDecoder.DecodeEffectLinks(effectType, effectValues));
            result.Add(id, definition);
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary();
    }

    private static SkillEffectCondition[] ReadSkillEffectConditions(ref ReadOnlySpan<byte> cursor, string[] strings)
    {
        var count = ReadCollectionCount(ref cursor, "skill effect condition");
        var result = new SkillEffectCondition[count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new SkillEffectCondition(
                new SkillSemanticEffectConditionType(ReadSemanticString(ref cursor, strings)),
                ReadSemanticStrings(ref cursor, strings),
                ReadBool(ref cursor));
        }

        return result;
    }

    private static FrozenDictionary<int, SkillEffectFilterDefinition> ReadSkillEffectFilters(ResourcePackSection section, string[] strings)
    {
        var cursor = section.Payload.Span;
        var result = new Dictionary<int, SkillEffectFilterDefinition>(section.Count);
        for (var i = 0; i < section.Count; i++)
        {
            var id = ReadInt32(ref cursor);
            var definition = new SkillEffectFilterDefinition(
                id,
                new SkillSemanticEffectRangeType(ReadSemanticString(ref cursor, strings)),
                ReadBool(ref cursor),
                ReadInt32Array(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                new SkillSemanticTargetFilterType(ReadSemanticString(ref cursor, strings)),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadBool(ref cursor),
                ReadInt32(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                new SkillSemanticRangeNoticeSelectColor(ReadSemanticString(ref cursor, strings)),
                new SkillSemanticRangeNoticeFillType(ReadSemanticString(ref cursor, strings)),
                new SkillSemanticNoticeStyleType(ReadSemanticString(ref cursor, strings)),
                new SkillSemanticFilterTargetAbnormalType(ReadSemanticString(ref cursor, strings)),
                ReadInt32Array(ref cursor),
                ReadInt32Array(ref cursor),
                ReadSemanticValues(ref cursor, strings, static value => new SkillSemanticAbnormalEffectType(value)));
            result.Add(id, definition);
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary();
    }

    private static FrozenDictionary<int, SkillEffectFilterLocationDefinition> ReadSkillEffectFilterLocations(ResourcePackSection section, string[] strings)
    {
        var cursor = section.Payload.Span;
        var result = new Dictionary<int, SkillEffectFilterLocationDefinition>(section.Count);
        for (var i = 0; i < section.Count; i++)
        {
            var id = ReadInt32(ref cursor);
            result.Add(id, new SkillEffectFilterLocationDefinition(
                id,
                new SkillSemanticEffectRangeLocationType(ReadSemanticString(ref cursor, strings)),
                ReadBool(ref cursor),
                ReadInt32Array(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor)));
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary();
    }

    private static FrozenDictionary<int, SkillEffectLevelDefinition> ReadSkillEffectLevels(ResourcePackSection section, string[] strings)
    {
        var cursor = section.Payload.Span;
        var result = new Dictionary<int, SkillEffectLevelDefinition>(section.Count);
        for (var i = 0; i < section.Count; i++)
        {
            var id = ReadInt32(ref cursor);
            result.Add(id, new SkillEffectLevelDefinition(
                id,
                ReadSemanticString(ref cursor, strings),
                ReadByte(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadSemanticStrings(ref cursor, strings)));
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary();
    }

    private static FrozenDictionary<int, SkillProjectileDefinition> ReadSkillProjectiles(ResourcePackSection section, string[] strings)
    {
        var cursor = section.Payload.Span;
        var result = new Dictionary<int, SkillProjectileDefinition>(section.Count);
        for (var i = 0; i < section.Count; i++)
        {
            var id = ReadInt32(ref cursor);
            result.Add(id, new SkillProjectileDefinition(
                id,
                new SkillSemanticProjectileType(ReadSemanticString(ref cursor, strings)),
                ReadInt32(ref cursor),
                ReadSemanticString(ref cursor, strings),
                ReadSemanticString(ref cursor, strings),
                new SkillSemanticProjectileMovementType(ReadSemanticString(ref cursor, strings)),
                ReadInt32(ref cursor),
                ReadSemanticString(ref cursor, strings),
                ReadSemanticString(ref cursor, strings),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadSemanticString(ref cursor, strings),
                ReadSemanticString(ref cursor, strings),
                ReadInt32(ref cursor),
                new SkillSemanticCollideShapeType(ReadSemanticString(ref cursor, strings)),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                new SkillSemanticCollideMultiShotType(ReadSemanticString(ref cursor, strings)),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadSemanticString(ref cursor, strings)));
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary();
    }

    private static FrozenDictionary<int, SkillAbnormalDefinition> ReadSkillAbnormals(ResourcePackSection section, string[] strings)
    {
        var cursor = section.Payload.Span;
        var result = new Dictionary<int, SkillAbnormalDefinition>(section.Count);
        for (var i = 0; i < section.Count; i++)
        {
            var id = ReadInt32(ref cursor);
            result.Add(id, new SkillAbnormalDefinition(
                id,
                ReadSemanticString(ref cursor, strings),
                ReadUInt32(ref cursor),
                ReadUInt32(ref cursor),
                new SkillSemanticAbnormalType(ReadSemanticString(ref cursor, strings)),
                ReadSemanticString(ref cursor, strings),
                ReadSemanticString(ref cursor, strings),
                ReadSemanticString(ref cursor, strings),
                ReadSemanticString(ref cursor, strings),
                ReadSemanticString(ref cursor, strings),
                ReadSemanticString(ref cursor, strings),
                new SkillSemanticAbnormalDisplayCategory(ReadSemanticString(ref cursor, strings)),
                ReadInt32(ref cursor),
                ReadSemanticString(ref cursor, strings),
                ReadSemanticString(ref cursor, strings),
                ReadBool(ref cursor),
                new SkillSemanticWeaponType(ReadSemanticString(ref cursor, strings)),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                new SkillSemanticAbnormalHideFloaterType(ReadSemanticString(ref cursor, strings)),
                new SkillSemanticDamageType(ReadSemanticString(ref cursor, strings)),
                ReadInt32(ref cursor),
                ReadByte(ref cursor),
                new SkillSemanticAbnormalOverlapTimeType(ReadSemanticString(ref cursor, strings)),
                new SkillSemanticAbnormalReplaceType(ReadSemanticString(ref cursor, strings)),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                new SkillSemanticAbnormalFxVisibleType(ReadSemanticString(ref cursor, strings)),
                ReadBool(ref cursor),
                new SkillSemanticAbnormalEffectType(ReadSemanticString(ref cursor, strings)),
                ReadInt32(ref cursor),
                ReadBool(ref cursor)));
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary();
    }

    private static FrozenDictionary<int, SkillAbnormalEffectDefinition> ReadSkillAbnormalEffects(ResourcePackSection section, string[] strings)
    {
        var cursor = section.Payload.Span;
        var result = new Dictionary<int, SkillAbnormalEffectDefinition>(section.Count);
        for (var i = 0; i < section.Count; i++)
        {
            var id = ReadInt32(ref cursor);
            var abnormalId = ReadInt32(ref cursor);
            var effectFx = ReadSemanticString(ref cursor, strings);
            var effectType = new SkillSemanticAbnormalEffectType(ReadSemanticString(ref cursor, strings));
            var values = ReadSemanticStrings(ref cursor, strings);
            result.Add(id, new SkillAbnormalEffectDefinition(
                id,
                abnormalId,
                effectFx,
                effectType,
                values,
                ReadSemanticString(ref cursor, strings),
                ReadBool(ref cursor),
                SkillSemanticReferenceDecoder.DecodeAbnormalEffectLinks(effectType, values)));
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary();
    }

    private static FrozenDictionary<int, SkillAbnormalEffectLevelDefinition> ReadSkillAbnormalEffectLevels(ResourcePackSection section, string[] strings)
    {
        var cursor = section.Payload.Span;
        var result = new Dictionary<int, SkillAbnormalEffectLevelDefinition>(section.Count);
        for (var i = 0; i < section.Count; i++)
        {
            var id = ReadInt32(ref cursor);
            result.Add(id, new SkillAbnormalEffectLevelDefinition(
                id,
                ReadSemanticString(ref cursor, strings),
                ReadByte(ref cursor),
                ReadSemanticStrings(ref cursor, strings)));
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, SkillAbnormalEffectTypeDefinition> ReadSkillAbnormalEffectTypes(ResourcePackSection section, string[] strings)
    {
        var cursor = section.Payload.Span;
        var result = new Dictionary<string, SkillAbnormalEffectTypeDefinition>(section.Count, StringComparer.Ordinal);
        for (var i = 0; i < section.Count; i++)
        {
            var name = ReadSemanticString(ref cursor, strings);
            result.Add(name, new SkillAbnormalEffectTypeDefinition(
                name,
                new SkillSemanticAbnormalEffectType(ReadSemanticString(ref cursor, strings)),
                ReadSemanticString(ref cursor, strings),
                ReadSemanticValues(ref cursor, strings, static value => new SkillSemanticCharacterControlType(value)),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadBool(ref cursor),
                ReadSemanticString(ref cursor, strings),
                ReadSemanticString(ref cursor, strings),
                new SkillSemanticAbnormalEffectAniHitType(ReadSemanticString(ref cursor, strings))));
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static SkillAbnormalOverlapFxDefinition[] ReadSkillAbnormalOverlapFx(ResourcePackSection section, string[] strings)
    {
        var cursor = section.Payload.Span;
        var result = new SkillAbnormalOverlapFxDefinition[section.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new SkillAbnormalOverlapFxDefinition(
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                ReadSemanticString(ref cursor, strings));
        }

        RequireFullyRead(cursor);
        return result;
    }

    private static FrozenDictionary<string, SkillAbnormalPropertyDefinition> ReadSkillAbnormalProperties(ResourcePackSection section, string[] strings)
    {
        var cursor = section.Payload.Span;
        var result = new Dictionary<string, SkillAbnormalPropertyDefinition>(section.Count, StringComparer.Ordinal);
        for (var i = 0; i < section.Count; i++)
        {
            var name = ReadSemanticString(ref cursor, strings);
            result.Add(name, new SkillAbnormalPropertyDefinition(
                name,
                ReadSemanticValues(ref cursor, strings, static value => new SkillSemanticAbnormalEffectType(value))));
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static FrozenDictionary<string, SkillAbnormalStringDefinition> ReadSkillAbnormalStrings(ResourcePackSection section, string[] strings)
    {
        var cursor = section.Payload.Span;
        var result = new Dictionary<string, SkillAbnormalStringDefinition>(section.Count, StringComparer.Ordinal);
        for (var i = 0; i < section.Count; i++)
        {
            var name = ReadSemanticString(ref cursor, strings);
            result.Add(name, new SkillAbnormalStringDefinition(
                name,
                ReadSemanticString(ref cursor, strings),
                ReadSemanticString(ref cursor, strings),
                ReadSemanticString(ref cursor, strings)));
        }

        RequireFullyRead(cursor);
        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static string ReadSemanticString(ref ReadOnlySpan<byte> cursor, string[] strings)
    {
        var index = ReadInt32(ref cursor);
        if ((uint)index >= (uint)strings.Length)
        {
            throw new InvalidDataException($"Invalid skill semantic string index {index}.");
        }

        return strings[index];
    }

    private static string[] ReadSemanticStrings(ref ReadOnlySpan<byte> cursor, string[] strings)
    {
        var count = ReadCollectionCount(ref cursor, "skill semantic string");
        var result = new string[count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = ReadSemanticString(ref cursor, strings);
        }

        return result;
    }

    private static T[] ReadSemanticValues<T>(
        ref ReadOnlySpan<byte> cursor,
        string[] strings,
        Func<string, T> factory)
    {
        var count = ReadCollectionCount(ref cursor, "skill semantic value");
        var result = new T[count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = factory(ReadSemanticString(ref cursor, strings));
        }

        return result;
    }

    private static int ReadCollectionCount(ref ReadOnlySpan<byte> cursor, string kind)
    {
        var count = ReadInt32(ref cursor);
        if (count < 0)
        {
            throw new InvalidDataException($"Invalid negative {kind} count {count}.");
        }

        return count;
    }
}
