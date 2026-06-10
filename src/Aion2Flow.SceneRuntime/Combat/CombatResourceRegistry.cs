using System.Collections.Concurrent;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public static class CombatResourceRegistry
{
    private static SkillCollection _skillMap = [];
    private static readonly ConcurrentDictionary<int, int> _skillCodeNormalizationCache = [];

    public static SkillCollection SkillMap
    {
        get => _skillMap;
        set
        {
            _skillMap = value;
            SkillDisplayMap = _skillMap;
            SkillCodes = [.. _skillMap.Select(static x => x.Id).OrderBy(static x => x)];
            _skillCodeNormalizationCache.Clear();
            SkillMapRevision++;
        }
    }

    public static SkillCollection SkillDisplayMap { get; private set; } = [];
    public static int[] SkillCodes { get; private set; } = [];
    public static long SkillMapRevision { get; private set; }
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

    public static string DisplaySkillNameFor(int skillCode)
    {
        if (skillCode <= 0)
            return string.Empty;

        if (SkillDisplayMap.TryGetValue(skillCode, out var displaySkill) && !string.IsNullOrWhiteSpace(displaySkill.Name))
            return displaySkill.Name;

        return SkillMap.TryGetValue(skillCode, out var skill) && !string.IsNullOrWhiteSpace(skill.Name)
            ? skill.Name
            : string.Empty;
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

    public static void NormalizePacketForStorage(ref ParsedCombatPacket packet)
    {
        if (packet.IsNormalized)
            return;

        var observation = packet.ToObservation();
        var normalized = NormalizeObservationForStorage(packet.SourceId, packet.TargetId, in observation);
        packet.SkillCode = normalized.SkillCode;
        packet.BodyResourceEffectRef = normalized.BodyResourceEffectRef;
        packet.Damage = checked((int)normalized.Damage);
        packet.HitContribution = normalized.HitCount;
        packet.AttemptContribution = normalized.AttemptCount;
        packet.DetailRaw = normalized.DetailRaw;
        packet.DetailResourceEffectRef = normalized.DetailResourceEffectRef;
        packet.Marker = normalized.Marker;
        packet.Type = normalized.Type;
        packet.Flag = normalized.Flag;
        packet.LayoutTag = normalized.LayoutTag;
        packet.Loop = normalized.Loop;
        packet.MultiHitCount = normalized.MultiHitCount;
        packet.DrainHealAmount = normalized.DrainHealAmount;
        packet.RegenerationAmount = normalized.RegenerationAmount;
        packet.Modifiers = normalized.Modifiers;
        packet.ResourceKind = normalized.ResourceKind;
        packet.EventKind = normalized.EventKind;
        packet.ValueKind = normalized.ValueKind;
        packet.PeriodicTailSkillCodeRaw = normalized.PeriodicTailSkillCodeRaw;
        packet.PeriodicTailPrefixValue = normalized.PeriodicTailPrefixValue;
        packet.PeriodicTailLength = normalized.PeriodicTailLength;
        packet.SetPeriodicEffect(normalized.PeriodicRelation, normalized.PeriodicMode);
        packet.SetEffectTag(normalized.EffectTag);
        packet.IsNormalized = true;
    }

    public static CombatObservation NormalizeObservationForStorage(int sourceId, int targetId, in CombatObservation observation)
    {
        var modifiers = observation.Type == 3 ? observation.Modifiers | DamageModifiers.Critical : observation.Modifiers;
        var normalized = observation with
        {
            SkillCode = NormalizeConfirmedSkillCode(observation.SkillCode),
            Modifiers = modifiers
        };

        if (normalized.ValueKind == CombatValueKind.Unknown)
        {
            var (EventKind, ValueKind) = CombatEventClassifier.Classify(sourceId, targetId, in normalized);
            normalized = normalized with
            {
                ValueKind = ValueKind,
                EventKind = EventKind
            };
        }

        if (normalized.ValueKind is CombatValueKind.PeriodicDamage or CombatValueKind.PeriodicHealing && (normalized.Modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) == 0)
        {
            normalized = normalized with
            {
                HitCount = 0,
                AttemptCount = 0
            };
        }

        return normalized;
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

    private static int NormalizeConfirmedSkillCode(int skillCode)
    {
        if (skillCode <= 0 || SkillMap.Count == 0)
            return Math.Max(0, skillCode);

        if (_skillCodeNormalizationCache.TryGetValue(skillCode, out var cached))
            return cached;

        var variant = ParseSkillVariant(skillCode);
        var resolvedSkillCode = Array.BinarySearch(SkillCodes, skillCode) >= 0
            ? skillCode
            : ResolveKnownVariantSkillCode(skillCode, variant);
        resolvedSkillCode = ResolveSameNameVariantGroupSkillCode(resolvedSkillCode, variant);
        _skillCodeNormalizationCache[skillCode] = resolvedSkillCode;
        return resolvedSkillCode;
    }

    private static int ResolveKnownVariantSkillCode(int skillCode, SkillVariantInfo variant)
    {
        if (variant.BaseSkillCode <= 0)
            return skillCode;

        Span<int> candidates = stackalloc int[3];
        var count = 0;
        var specializationWithoutCharge = variant.BaseSkillCode + EncodeVariantSuffix(variant.SpecializationMask, 0);
        if (specializationWithoutCharge > 0)
            candidates[count++] = specializationWithoutCharge;
        candidates[count++] = variant.BaseSkillCode;
        candidates[count++] = variant.BaseSkillCode + EncodeVariantSuffix(0, variant.ChargeStage);

        for (var i = 0; i < count; i++)
        {
            var candidate = candidates[i];
            if (candidate > 0 && Array.BinarySearch(SkillCodes, candidate) >= 0)
                return candidate;
        }

        return skillCode;
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
