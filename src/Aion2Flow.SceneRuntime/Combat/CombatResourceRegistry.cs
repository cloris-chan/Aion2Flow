using System.Collections.Concurrent;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public static class CombatResourceRegistry
{
    private static SkillCollection _skillMap = [];
    private static readonly ConcurrentDictionary<int, int> _resolvedSkillCodeCache = [];

    public static SkillCollection SkillMap
    {
        get => _skillMap;
        set
        {
            _skillMap = value;
            SkillDisplayMap = _skillMap;
            SkillCodes = [.. _skillMap.Select(static x => x.Id).OrderBy(static x => x)];
            _resolvedSkillCodeCache.Clear();
        }
    }

    public static SkillCollection SkillDisplayMap { get; private set; } = [];
    public static int[] SkillCodes { get; private set; } = [];
    public static IReadOnlyDictionary<int, NpcCatalogEntry> NpcCatalog { get; private set; } = new Dictionary<int, NpcCatalogEntry>();

    public static void EnsureCombatResources()
    {
        if (SkillMap.Count != 0)
            return;

        SkillMap = ResourceDatabase.LoadCombatSkills();
    }

    public static void LoadSkillMap(string lang)
    {
        SkillMap = ResourceDatabase.LoadCombatSkills();
        UpdateDisplayResources(ResourceDatabase.LoadSkills(lang), ResourceDatabase.LoadNpcCatalog(lang));
    }

    public static void SetGameResources(SkillCollection skillMap, IReadOnlyDictionary<int, NpcCatalogEntry> npcCatalog)
    {
        SkillMap = skillMap;
        SkillDisplayMap = skillMap;
        NpcCatalog = npcCatalog;
    }

    public static void UpdateDisplayResources(SkillCollection skillMap, IReadOnlyDictionary<int, NpcCatalogEntry> npcCatalog)
    {
        EnsureCombatResources();
        SkillDisplayMap = skillMap;
        NpcCatalog = npcCatalog;
    }

    public static bool TryResolveNpcCatalogEntry(int npcCode, out NpcCatalogEntry entry)
    {
        if (NpcCatalog.TryGetValue(npcCode, out entry))
            return true;

        entry = default;
        return false;
    }

    public static NpcKind ResolveNpcKind(NpcCatalogKind kind) =>
        kind switch
        {
            NpcCatalogKind.Monster => NpcKind.Monster,
            NpcCatalogKind.Boss => NpcKind.Boss,
            NpcCatalogKind.Summon => NpcKind.Summon,
            NpcCatalogKind.Friendly => NpcKind.Friendly,
            _ => NpcKind.Unknown
        };

    public static int? InferOriginalSkillCode(int skillCode)
    {
        if (skillCode <= 0)
            return null;

        Span<int> candidates = stackalloc int[12];
        var count = 0;

        static bool TryPush(Span<int> span, ref int count, int value)
        {
            if (value <= 0) return false;
            for (var i = 0; i < count; i++)
            {
                if (span[i] == value) return false;
            }

            if (count >= span.Length) return false;
            span[count++] = value;
            return true;
        }

        if (TryPush(candidates, ref count, skillCode) && Array.BinarySearch(SkillCodes, skillCode) >= 0)
            return skillCode;

        var variant = ParseSkillVariant(skillCode);

        var specializationWithoutCharge = variant.BaseSkillCode + EncodeVariantSuffix(variant.SpecializationMask, 0);
        if (TryPush(candidates, ref count, specializationWithoutCharge) && Array.BinarySearch(SkillCodes, specializationWithoutCharge) >= 0)
            return specializationWithoutCharge;

        if (TryPush(candidates, ref count, variant.BaseSkillCode) && Array.BinarySearch(SkillCodes, variant.BaseSkillCode) >= 0)
            return variant.BaseSkillCode;

        var baseWithCharge = variant.BaseSkillCode + EncodeVariantSuffix(0, variant.ChargeStage);
        if (TryPush(candidates, ref count, baseWithCharge) && Array.BinarySearch(SkillCodes, baseWithCharge) >= 0)
            return baseWithCharge;

        var byHundred = skillCode / 100;
        if (TryPush(candidates, ref count, byHundred) && Array.BinarySearch(SkillCodes, byHundred) >= 0)
            return byHundred;

        var byHundredVariant = ParseSkillVariant(byHundred);
        var byHundredSpecializationWithoutCharge = byHundredVariant.BaseSkillCode + EncodeVariantSuffix(byHundredVariant.SpecializationMask, 0);
        if (TryPush(candidates, ref count, byHundredSpecializationWithoutCharge) && Array.BinarySearch(SkillCodes, byHundredSpecializationWithoutCharge) >= 0)
            return byHundredSpecializationWithoutCharge;

        if (byHundredVariant.BaseSkillCode >= 100000)
        {
            if (TryPush(candidates, ref count, byHundredVariant.BaseSkillCode) && Array.BinarySearch(SkillCodes, byHundredVariant.BaseSkillCode) >= 0)
                return byHundredVariant.BaseSkillCode;

            var byHundredBaseWithCharge = byHundredVariant.BaseSkillCode + EncodeVariantSuffix(0, byHundredVariant.ChargeStage);
            if (TryPush(candidates, ref count, byHundredBaseWithCharge) && Array.BinarySearch(SkillCodes, byHundredBaseWithCharge) >= 0)
                return byHundredBaseWithCharge;
        }

        var byThousand = skillCode - (skillCode % 1000);
        if (TryPush(candidates, ref count, byThousand) && Array.BinarySearch(SkillCodes, byThousand) >= 0)
            return byThousand;

        return null;
    }

    public static void NormalizePacketForStorage(ParsedCombatPacket packet)
    {
        if (packet.IsNormalized)
            return;

        var originalSkillCode = packet.OriginalSkillCode != 0 ? packet.OriginalSkillCode : packet.SkillCode;
        var variant = ParseSkillVariant(originalSkillCode);
        packet.OriginalSkillCode = variant.OriginalSkillCode;
        packet.BaseSkillCode = variant.BaseSkillCode;
        packet.ChargeStage = variant.ChargeStage;
        packet.SpecializationMask = variant.SpecializationMask;
        if (packet.Type == 3)
            packet.Modifiers |= DamageModifiers.Critical;

        packet.SkillCode = ResolveSkillCode(packet.SkillCode, originalSkillCode, variant);
        if (packet.ValueKind == CombatValueKind.Unknown)
        {
            packet.ValueKind = CombatEventClassifier.ClassifyValueKind(packet);
            packet.EventKind = CombatEventClassifier.Classify(packet);
        }

        if (packet.ValueKind is CombatValueKind.PeriodicDamage or CombatValueKind.PeriodicHealing && (packet.Modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) == 0)
        {
            packet.HitContribution = 0;
            packet.AttemptContribution = 0;
        }

        packet.IsNormalized = true;
    }

    public static SkillVariantInfo ParseSkillVariant(int originalSkillCode)
    {
        if (originalSkillCode <= 0)
            return new SkillVariantInfo(0, 0, 0, 0, 0);

        var chargeStage = originalSkillCode % 10;
        var specializationDigits = (originalSkillCode / 10) % 1000;
        var specializationMask = 0;
        var specializationAccumulator = specializationDigits;

        while (specializationAccumulator > 0)
        {
            var digit = specializationAccumulator % 10;
            specializationAccumulator /= 10;
            if (digit is >= 1 and <= 5)
                specializationMask |= 1 << (digit - 1);
        }

        var baseSkillCode = originalSkillCode - (originalSkillCode % 10000);
        var normalizedSkillCode = baseSkillCode + chargeStage;
        return new SkillVariantInfo(originalSkillCode, normalizedSkillCode, baseSkillCode, chargeStage, specializationMask);
    }

    private static int ResolveSkillCode(int packetSkillCode, int originalSkillCode, SkillVariantInfo variant)
    {
        if (SkillMap.Count == 0)
        {
            if (packetSkillCode > 0)
                return packetSkillCode;

            if (originalSkillCode > 0)
                return originalSkillCode;

            return variant.NormalizedSkillCode;
        }

        if (originalSkillCode <= 0)
            return variant.NormalizedSkillCode;

        if (_resolvedSkillCodeCache.TryGetValue(originalSkillCode, out var cached))
            return cached;

        var inferredSkillCode = InferOriginalSkillCode(originalSkillCode) ?? variant.NormalizedSkillCode;
        var resolvedSkillCode = ResolveTriggeredSiblingSkillCode(originalSkillCode, inferredSkillCode);
        resolvedSkillCode = ResolveSameNameVariantGroupSkillCode(resolvedSkillCode, variant);
        _resolvedSkillCodeCache[originalSkillCode] = resolvedSkillCode;
        return resolvedSkillCode;
    }

    private static int ResolveTriggeredSiblingSkillCode(int originalSkillCode, int inferredSkillCode)
    {
        if (originalSkillCode <= 0 || inferredSkillCode <= 0 || SkillMap.Count == 0 || Array.BinarySearch(SkillCodes, originalSkillCode) >= 0 || !SkillMap.TryGetValue(inferredSkillCode, out var inferredSkill))
            return inferredSkillCode;

        var variantSuffix = inferredSkillCode % 10000;
        if (variantSuffix == 0)
            return inferredSkillCode;

        foreach (var triggeredSkillId in inferredSkill.EnumerateTriggeredSkillIds())
        {
            if (triggeredSkillId <= 0)
                continue;

            if (!SkillMap.TryGetValue(triggeredSkillId, out var candidate))
                continue;

            if (candidate.Id == inferredSkillCode || candidate.Id % 10000 != variantSuffix || candidate.Category != inferredSkill.Category || candidate.SourceType != inferredSkill.SourceType)
                continue;

            return candidate.Id;
        }

        return inferredSkillCode;
    }

    private static int ResolveSameNameVariantGroupSkillCode(int resolvedSkillCode, SkillVariantInfo variant)
    {
        if (resolvedSkillCode <= 0 || variant.BaseSkillCode <= 0 || resolvedSkillCode == variant.BaseSkillCode || !ShouldCollapseSameNameVariantGroup(variant.BaseSkillCode) || SkillMap.Count == 0)
            return resolvedSkillCode;

        if (!SkillMap.TryGetValue(resolvedSkillCode, out var resolvedSkill) || !SkillMap.TryGetValue(variant.BaseSkillCode, out var baseSkill))
            return resolvedSkillCode;

        if (resolvedSkill.SourceType != SkillSourceType.PcSkill || baseSkill.SourceType != resolvedSkill.SourceType || baseSkill.Category != resolvedSkill.Category || !string.Equals(baseSkill.Name, resolvedSkill.Name, StringComparison.Ordinal))
            return resolvedSkillCode;

        if (BaseSkillGroupHasTriggeredSiblings(variant.BaseSkillCode))
            return resolvedSkillCode;

        return variant.BaseSkillCode;
    }

    private static bool ShouldCollapseSameNameVariantGroup(int baseSkillCode) => baseSkillCode == 12240000;

    private static bool BaseSkillGroupHasTriggeredSiblings(int baseSkillCode)
    {
        foreach (var skill in SkillMap)
        {
            if (skill.Id <= 0 || ParseSkillVariant(skill.Id).BaseSkillCode != baseSkillCode)
                continue;

            if (skill.EnumerateTriggeredSkillIds().Any())
                return true;
        }

        return false;
    }

    private static int EncodeVariantSuffix(int specializationMask, int chargeStage)
    {
        var suffix = 0;
        for (var specialization = 1; specialization <= 5; specialization++)
        {
            var bit = 1 << (specialization - 1);
            if ((specializationMask & bit) != 0)
                suffix = (suffix * 10) + specialization;
        }

        return (suffix * 10) + chargeStage;
    }
}
