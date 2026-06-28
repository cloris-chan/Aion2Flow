using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public static class CombatResourceRegistry
{
    private static SkillCollection _skillMap = [];
    private static IReadOnlyDictionary<int, SkillAnalysis> _skillAnalysis = new Dictionary<int, SkillAnalysis>();
    private static IReadOnlyDictionary<int, SkillPresentation> _skillPresentations = new Dictionary<int, SkillPresentation>();
    private static IReadOnlyDictionary<uint, int> _effectSkillIds = new Dictionary<uint, int>();

    public static SkillCollection SkillMap
    {
        get => _skillMap;
        set
        {
            _skillMap = value;
            SetSkillDatResources(new Dictionary<int, SkillAnalysis>(), new Dictionary<int, SkillPresentation>(), []);
            SkillDisplayMap = _skillMap;
            SkillMapRevision++;
        }
    }

    public static SkillCollection SkillDisplayMap { get; private set; } = [];
    public static IReadOnlyDictionary<int, SkillAnalysis> SkillAnalysis => _skillAnalysis;
    public static IReadOnlyDictionary<int, SkillPresentation> SkillPresentations => _skillPresentations;
    public static long SkillMapRevision { get; private set; }
    public static IReadOnlyDictionary<int, NpcCatalogEntry> NpcCatalog { get; private set; } = new Dictionary<int, NpcCatalogEntry>();

    public static void EnsureCombatResources()
    {
        if (SkillMap.Count != 0)
            return;

        SkillMap = ResourceDatabase.LoadCombatSkills();
        SetSkillDatResources(ResourceDatabase.LoadSkillAnalysis(), ResourceDatabase.LoadSkillPresentations(), ResourceDatabase.LoadSkillEffectRelations());
    }

    public static void LoadSkillMap(string lang)
    {
        SkillMap = ResourceDatabase.LoadCombatSkills();
        SetSkillDatResources(ResourceDatabase.LoadSkillAnalysis(), ResourceDatabase.LoadSkillPresentations(), ResourceDatabase.LoadSkillEffectRelations());
        UpdateDisplayResources(ResourceDatabase.LoadSkills(lang), ResourceDatabase.LoadNpcCatalog(lang));
    }

    public static void SetGameResources(SkillCollection skillMap, IReadOnlyDictionary<int, NpcCatalogEntry> npcCatalog)
    {
        SkillMap = skillMap;
        SetSkillDatResources(new Dictionary<int, SkillAnalysis>(), new Dictionary<int, SkillPresentation>(), []);
        SkillDisplayMap = skillMap;
        NpcCatalog = npcCatalog;
    }

    public static void SetGameResources(
        SkillCollection skillMap,
        IReadOnlyDictionary<int, NpcCatalogEntry> npcCatalog,
        IReadOnlyDictionary<int, SkillAnalysis> skillAnalysis,
        IReadOnlyDictionary<int, SkillPresentation> skillPresentations,
        IReadOnlyList<SkillEffectRelation> skillEffectRelations)
    {
        SkillMap = skillMap;
        SetSkillDatResources(skillAnalysis, skillPresentations, skillEffectRelations);
        SkillDisplayMap = skillMap;
        NpcCatalog = npcCatalog;
    }

    public static void SetGameResources(
        SkillCollection skillMap,
        IReadOnlyDictionary<int, NpcCatalogEntry> npcCatalog,
        IReadOnlyDictionary<int, SkillPresentation> skillPresentations)
    {
        SkillMap = skillMap;
        SetSkillDatResources(new Dictionary<int, SkillAnalysis>(), skillPresentations, []);
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

    public static bool TryResolveSkillIdByEffectRef(ResourceEffectRef effectRef, out int skillId)
    {
        if (effectRef.IsEmpty || !_effectSkillIds.TryGetValue(effectRef.RawId, out skillId))
        {
            skillId = 0;
            return false;
        }

        return true;
    }

    public static int ResolveDisplaySkillIdForCode(int skillCode)
    {
        if (skillCode <= 0)
        {
            return 0;
        }

        return TryResolveDisplaySkillIdForCode(skillCode, out var displaySkillId) ? displaySkillId : skillCode;
    }

    public static int ResolvePresentationSkillIdForCode(int skillCode)
    {
        if (skillCode <= 0)
        {
            return 0;
        }

        return TryResolvePresentationSkillIdForCode(skillCode, out var presentationSkillId) ? presentationSkillId : skillCode;
    }

    private static bool TryResolveDisplaySkillIdForCode(int skillCode, out int displaySkillId)
    {
        if (_skillPresentations.TryGetValue(skillCode, out var presentation))
        {
            displaySkillId = presentation.DisplaySkillId > 0 ? presentation.DisplaySkillId : skillCode;
            return true;
        }

        displaySkillId = 0;
        return false;
    }

    private static bool TryResolvePresentationSkillIdForCode(int skillCode, out int presentationSkillId)
    {
        if (_skillPresentations.TryGetValue(skillCode, out var presentation))
        {
            presentationSkillId = presentation.PresentationSkillId > 0 ? presentation.PresentationSkillId : skillCode;
            return true;
        }

        presentationSkillId = 0;
        return false;
    }

    public static bool TryResolveSkillByEffectRef(ResourceEffectRef effectRef, out Skill skill)
    {
        if (TryResolveSkillIdByEffectRef(effectRef, out var skillId) &&
            SkillDisplayMap.TryGetValue(skillId, out skill) &&
            !string.IsNullOrWhiteSpace(skill.Name))
        {
            return true;
        }

        if (TryResolveSkillIdByEffectRef(effectRef, out skillId) &&
            SkillMap.TryGetValue(skillId, out skill) &&
            !string.IsNullOrWhiteSpace(skill.Name))
        {
            return true;
        }

        skill = default;
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

        if (TryResolveSkillByEffectRef(ResourceEffectRef.FromRaw(unchecked((uint)skillCode)), out var effectSkill))
            return effectSkill.Name;

        var displaySkillId = ResolveDisplaySkillIdForCode(skillCode);
        if (displaySkillId != skillCode)
        {
            if (SkillDisplayMap.TryGetValue(displaySkillId, out displaySkill) && !string.IsNullOrWhiteSpace(displaySkill.Name))
                return displaySkill.Name;

            if (SkillMap.TryGetValue(displaySkillId, out skill) && !string.IsNullOrWhiteSpace(skill.Name))
                return skill.Name;
        }

        return string.Empty;
    }

    private static void SetSkillDatResources(
        IReadOnlyDictionary<int, SkillAnalysis> skillAnalysis,
        IReadOnlyDictionary<int, SkillPresentation> skillPresentations,
        IReadOnlyList<SkillEffectRelation> skillEffectRelations)
    {
        _skillAnalysis = skillAnalysis;
        _skillPresentations = skillPresentations;
        _effectSkillIds = SkillEffectRelationIndex.Build(skillEffectRelations);
    }

    public static NpcKind ResolveNpcKind(NpcCatalogKind kind) =>
        kind switch
        {
            NpcCatalogKind.Monster => NpcKind.Monster,
            NpcCatalogKind.Boss => NpcKind.Boss,
            NpcCatalogKind.Summon => NpcKind.Summon,
            NpcCatalogKind.Friendly => NpcKind.Friendly,
            NpcCatalogKind.TrainingDummy => NpcKind.TrainingDummy,
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
}
