using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public static class CombatResourceRegistry
{
    private static SkillDisplayCatalog _skillMap = [];
    private static IReadOnlyDictionary<int, SkillClientMetadata> _skillClientMetadata = new Dictionary<int, SkillClientMetadata>();
    private static IReadOnlyDictionary<int, SkillDisplayProjection> _skillDisplayProjections = new Dictionary<int, SkillDisplayProjection>();
    private static IReadOnlyDictionary<uint, int> _effectSkillIds = new Dictionary<uint, int>();

    public static SkillDisplayCatalog SkillMap
    {
        get => _skillMap;
        set
        {
            _skillMap = value;
            SetSkillDisplayResources(new Dictionary<int, SkillClientMetadata>(), new Dictionary<int, SkillDisplayProjection>(), []);
            SkillDisplayMap = _skillMap;
            SkillMapRevision++;
        }
    }

    public static SkillDisplayCatalog SkillDisplayMap { get; private set; } = [];
    public static IReadOnlyDictionary<int, SkillClientMetadata> SkillClientMetadata => _skillClientMetadata;
    public static IReadOnlyDictionary<int, SkillDisplayProjection> SkillDisplayProjections => _skillDisplayProjections;
    public static long SkillMapRevision { get; private set; }
    public static IReadOnlyDictionary<int, NpcDisplayEntry> NpcCatalog { get; private set; } = new Dictionary<int, NpcDisplayEntry>();

    public static void EnsureCombatResources()
    {
        if (SkillMap.Count != 0)
            return;

        SetGameResources(ResourceCatalog.Load(ResourceLanguage.English));
    }

    public static void LoadSkillMap(string lang)
    {
        SetGameResources(ResourceCatalog.Load(lang));
    }

    public static void SetGameResources(SkillDisplayCatalog skillMap, IReadOnlyDictionary<int, NpcDisplayEntry> npcCatalog)
    {
        SkillMap = skillMap;
        SetSkillDisplayResources(new Dictionary<int, SkillClientMetadata>(), new Dictionary<int, SkillDisplayProjection>(), []);
        SkillDisplayMap = skillMap;
        NpcCatalog = npcCatalog;
    }

    public static void SetGameResources(ResourceCatalogSnapshot snapshot)
    {
        SkillMap = snapshot.Skills;
        SetSkillDisplayResources(snapshot.SkillClientMetadata, snapshot.SkillDisplayProjections, snapshot.SkillEffectReferences);
        SkillDisplayMap = snapshot.Skills;
        NpcCatalog = snapshot.NpcCatalog;
    }

    public static void SetGameResources(
        SkillDisplayCatalog skillMap,
        IReadOnlyDictionary<int, NpcDisplayEntry> npcCatalog,
        IReadOnlyDictionary<int, SkillClientMetadata> skillClientMetadata,
        IReadOnlyDictionary<int, SkillDisplayProjection> skillDisplayProjections,
        IReadOnlyList<SkillEffectReference> skillEffectReferences)
    {
        SkillMap = skillMap;
        SetSkillDisplayResources(skillClientMetadata, skillDisplayProjections, skillEffectReferences);
        SkillDisplayMap = skillMap;
        NpcCatalog = npcCatalog;
    }

    public static void SetGameResources(
        SkillDisplayCatalog skillMap,
        IReadOnlyDictionary<int, NpcDisplayEntry> npcCatalog,
        IReadOnlyDictionary<int, SkillDisplayProjection> skillDisplayProjections)
    {
        SkillMap = skillMap;
        SetSkillDisplayResources(new Dictionary<int, SkillClientMetadata>(), skillDisplayProjections, []);
        SkillDisplayMap = skillMap;
        NpcCatalog = npcCatalog;
    }

    public static void UpdateDisplayResources(SkillDisplayCatalog skillMap, IReadOnlyDictionary<int, NpcDisplayEntry> npcCatalog)
    {
        EnsureCombatResources();
        SkillDisplayMap = skillMap;
        NpcCatalog = npcCatalog;
    }

    public static bool TryResolveNpcCatalogEntry(int npcCode, out NpcDisplayEntry entry)
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
        if (_skillDisplayProjections.TryGetValue(skillCode, out var presentation))
        {
            displaySkillId = presentation.DisplaySkillId > 0 ? presentation.DisplaySkillId : skillCode;
            return true;
        }

        displaySkillId = 0;
        return false;
    }

    private static bool TryResolvePresentationSkillIdForCode(int skillCode, out int presentationSkillId)
    {
        if (_skillDisplayProjections.TryGetValue(skillCode, out var presentation))
        {
            presentationSkillId = presentation.PresentationSkillId > 0 ? presentation.PresentationSkillId : skillCode;
            return true;
        }

        presentationSkillId = 0;
        return false;
    }

    public static bool TryResolveSkillByEffectRef(ResourceEffectRef effectRef, out SkillDisplayEntry skillDisplayEntry)
    {
        if (TryResolveSkillIdByEffectRef(effectRef, out var skillId) &&
            SkillDisplayMap.TryGetValue(skillId, out skillDisplayEntry) &&
            !string.IsNullOrWhiteSpace(skillDisplayEntry.Name))
        {
            return true;
        }

        if (TryResolveSkillIdByEffectRef(effectRef, out skillId) &&
            SkillMap.TryGetValue(skillId, out skillDisplayEntry) &&
            !string.IsNullOrWhiteSpace(skillDisplayEntry.Name))
        {
            return true;
        }

        skillDisplayEntry = default;
        return false;
    }

    public static string DisplaySkillNameFor(int skillCode)
    {
        if (skillCode <= 0)
            return string.Empty;

        if (SkillDisplayMap.TryGetValue(skillCode, out var displaySkill) && !string.IsNullOrWhiteSpace(displaySkill.Name))
            return displaySkill.Name;

        if (SkillMap.TryGetValue(skillCode, out var skillDisplayEntry) && !string.IsNullOrWhiteSpace(skillDisplayEntry.Name))
            return skillDisplayEntry.Name;

        if (TryResolveSkillByEffectRef(ResourceEffectRef.FromRaw(unchecked((uint)skillCode)), out var effectSkill))
            return effectSkill.Name;

        var displaySkillId = ResolveDisplaySkillIdForCode(skillCode);
        if (displaySkillId != skillCode)
        {
            if (SkillDisplayMap.TryGetValue(displaySkillId, out displaySkill) && !string.IsNullOrWhiteSpace(displaySkill.Name))
                return displaySkill.Name;

            if (SkillMap.TryGetValue(displaySkillId, out skillDisplayEntry) && !string.IsNullOrWhiteSpace(skillDisplayEntry.Name))
                return skillDisplayEntry.Name;
        }

        return string.Empty;
    }

    private static void SetSkillDisplayResources(
        IReadOnlyDictionary<int, SkillClientMetadata> skillClientMetadata,
        IReadOnlyDictionary<int, SkillDisplayProjection> skillDisplayProjections,
        IReadOnlyList<SkillEffectReference> skillEffectReferences)
    {
        _skillClientMetadata = skillClientMetadata;
        _skillDisplayProjections = skillDisplayProjections;
        _effectSkillIds = SkillEffectReferenceIndex.Build(skillEffectReferences);
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
