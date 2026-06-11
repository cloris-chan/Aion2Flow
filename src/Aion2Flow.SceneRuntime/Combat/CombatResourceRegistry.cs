using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public static class CombatResourceRegistry
{
    private static SkillCollection _skillMap = [];

    public static SkillCollection SkillMap
    {
        get => _skillMap;
        set
        {
            _skillMap = value;
            SkillDisplayMap = _skillMap;
            SkillMapRevision++;
        }
    }

    public static SkillCollection SkillDisplayMap { get; private set; } = [];
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

        if (SkillMap.TryGetValue(skillCode, out var skill) && !string.IsNullOrWhiteSpace(skill.Name))
            return skill.Name;

        var variant = SkillVariantInfo.Parse(skillCode);
        Span<int> fallbackCodes = stackalloc int[3];
        fallbackCodes[0] = variant.BaseSkillCode + EncodeVariantSuffix(variant.SpecializationMask, 0);
        fallbackCodes[1] = variant.BaseSkillCode;
        fallbackCodes[2] = variant.BaseSkillCode + variant.VariantState;

        foreach (var fallbackCode in fallbackCodes)
        {
            if (fallbackCode <= 0 || fallbackCode == skillCode)
                continue;

            if (SkillDisplayMap.TryGetValue(fallbackCode, out displaySkill) && !string.IsNullOrWhiteSpace(displaySkill.Name))
                return displaySkill.Name;

            if (SkillMap.TryGetValue(fallbackCode, out skill) && !string.IsNullOrWhiteSpace(skill.Name))
                return skill.Name;
        }

        return string.Empty;
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
        packet.BodySkillVariantRaw = normalized.BodySkillVariantRaw;
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
            SkillCode = Math.Max(0, observation.SkillCode),
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

    private static int EncodeVariantSuffix(int specializationMask, int variantState)
    {
        var suffix = 0;
        for (var specialization = 1; specialization <= 5; specialization++)
        {
            var bit = 1 << (specialization - 1);
            if ((specializationMask & bit) != 0)
                suffix = (suffix * 10) + specialization;
        }

        return (suffix * 10) + variantState;
    }
}
