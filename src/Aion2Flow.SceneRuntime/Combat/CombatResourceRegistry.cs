using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public static class CombatResourceRegistry
{
    private static SkillDisplayCatalog _skillMap = [];
    private static IReadOnlyDictionary<int, SkillClientMetadata> _skillClientMetadata = new Dictionary<int, SkillClientMetadata>();
    private static IReadOnlyDictionary<int, SkillBaseProjection> _skillBaseProjections = new Dictionary<int, SkillBaseProjection>();
    private static IReadOnlyDictionary<uint, int> _effectSkillIds = new Dictionary<uint, int>();
    private static SkillSemanticOwnerGraph? _skillSemanticGraph;

    public static SkillDisplayCatalog SkillMap
    {
        get => _skillMap;
        set
        {
            _skillMap = value;
            SetSkillResourceMetadata(new Dictionary<int, SkillClientMetadata>(), new Dictionary<int, SkillBaseProjection>(), [], null);
            SkillDisplayMap = _skillMap;
            SkillMapRevision++;
        }
    }

    public static SkillDisplayCatalog SkillDisplayMap { get; private set; } = [];
    public static IReadOnlyDictionary<int, SkillClientMetadata> SkillClientMetadata => _skillClientMetadata;
    public static IReadOnlyDictionary<int, SkillBaseProjection> SkillBaseProjections => _skillBaseProjections;
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
        SetSkillResourceMetadata(new Dictionary<int, SkillClientMetadata>(), new Dictionary<int, SkillBaseProjection>(), [], null);
        SkillDisplayMap = skillMap;
        NpcCatalog = npcCatalog;
    }

    public static void SetGameResources(ResourceCatalogSnapshot snapshot)
    {
        SkillMap = snapshot.Skills;
        SetSkillResourceMetadata(snapshot.SkillClientMetadata, snapshot.SkillBaseProjections, snapshot.SkillEffectReferences, snapshot.SkillSemanticOwnerGraph);
        SkillDisplayMap = snapshot.Skills;
        NpcCatalog = snapshot.NpcCatalog;
    }

    public static void SetGameResources(
        SkillDisplayCatalog skillMap,
        IReadOnlyDictionary<int, NpcDisplayEntry> npcCatalog,
        IReadOnlyDictionary<int, SkillClientMetadata> skillClientMetadata,
        IReadOnlyDictionary<int, SkillBaseProjection> skillBaseProjections,
        IReadOnlyList<SkillEffectReference> skillEffectReferences)
    {
        SkillMap = skillMap;
        SetSkillResourceMetadata(skillClientMetadata, skillBaseProjections, skillEffectReferences, null);
        SkillDisplayMap = skillMap;
        NpcCatalog = npcCatalog;
    }

    public static void SetGameResources(
        SkillDisplayCatalog skillMap,
        IReadOnlyDictionary<int, NpcDisplayEntry> npcCatalog,
        IReadOnlyDictionary<int, SkillBaseProjection> skillBaseProjections)
    {
        SkillMap = skillMap;
        SetSkillResourceMetadata(new Dictionary<int, SkillClientMetadata>(), skillBaseProjections, [], null);
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

    public static bool TryResolveSkillEffectSemantics(ResourceEffectRef effectRef, out SkillSemanticEffectResolution resolution)
    {
        if (_skillSemanticGraph is not null &&
            !effectRef.IsEmpty &&
            effectRef.RawId <= int.MaxValue &&
            _skillSemanticGraph.TryResolveEffect(unchecked((int)effectRef.RawId), out resolution))
        {
            return true;
        }

        resolution = default;
        return false;
    }

    public static bool TryResolveDirectCombatEffectSemantics(in CombatObservation observation, out SkillSemanticEffectResolution resolution)
        => TryResolveSkillEffectSemantics(observation.DetailResourceEffectRef, out resolution);

    public static bool TryResolveCombatResourceSemantics(
        ResourceEffectRef effectRef,
        in CombatObservation observation,
        out SkillSemanticResourceResolution resolution)
    {
        if (_skillSemanticGraph is null || effectRef.IsEmpty)
        {
            resolution = default;
            return false;
        }

        var preferredSkillId = ResolveSemanticSkillId(in observation);
        return _skillSemanticGraph.TryResolveResourceReference(effectRef.RawId, preferredSkillId, out resolution);
    }

    public static bool TryResolveDirectCombatResourceSemantics(in CombatObservation observation, out SkillSemanticResourceResolution resolution)
        => TryResolveCombatResourceSemantics(observation.DetailResourceEffectRef, in observation, out resolution);

    public static bool TryResolvePeriodicCombatResourceSemantics(in CombatObservation observation, out SkillSemanticResourceResolution resolution)
    {
        if (TryResolveCombatResourceSemantics(observation.BodyResourceEffectRef, in observation, out resolution))
        {
            return true;
        }

        return TryResolveCombatResourceSemantics(observation.DetailResourceEffectRef, in observation, out resolution);
    }

    public static int ResolveBaseSkillIdForCode(int skillCode)
    {
        if (skillCode <= 0)
        {
            return 0;
        }

        return _skillBaseProjections.TryGetValue(skillCode, out var projection) && projection.BaseSkillId > 0
            ? projection.BaseSkillId
            : skillCode;
    }

    private static int ResolveSemanticSkillId(in CombatObservation observation)
    {
        var skillId = observation.SkillCode > 0
            ? observation.SkillCode
            : observation.BodySkillVariantRaw > 0
                ? observation.BodySkillVariantRaw
                : observation.PeriodicTailSkillCodeRaw;
        if (skillId <= 0 || _skillSemanticGraph is null)
        {
            return 0;
        }

        if (_skillSemanticGraph.Profiles.ContainsKey(skillId))
        {
            return skillId;
        }

        var baseSkillId = ResolveBaseSkillIdForCode(skillId);
        return _skillSemanticGraph.Profiles.ContainsKey(baseSkillId) ? baseSkillId : 0;
    }

    public static bool TryResolveBaseSkillIdForEffectRef(ResourceEffectRef effectRef, out int baseSkillId)
    {
        if (!TryResolveSkillIdByEffectRef(effectRef, out var skillId))
        {
            baseSkillId = 0;
            return false;
        }

        baseSkillId = ResolveBaseSkillIdForCode(skillId);
        return baseSkillId > 0;
    }

    public static bool TryResolveBaseSkillIdForEventKey(CombatEventKey eventKey, out int baseSkillId)
    {
        if (eventKey.SkillCode > 0)
        {
            baseSkillId = ResolveBaseSkillIdForCode(eventKey.SkillCode);
            return baseSkillId > 0;
        }

        var hasBodyBase = TryResolveBaseSkillIdForEffectRef(eventKey.BodyResourceEffectRef, out var bodyBaseSkillId);
        var hasDetailBase = TryResolveBaseSkillIdForEffectRef(eventKey.DetailResourceEffectRef, out var detailBaseSkillId);
        if (hasBodyBase && (!hasDetailBase || bodyBaseSkillId == detailBaseSkillId))
        {
            baseSkillId = bodyBaseSkillId;
            return true;
        }

        if (hasDetailBase && !hasBodyBase)
        {
            baseSkillId = detailBaseSkillId;
            return true;
        }

        baseSkillId = 0;
        return false;
    }

    public static bool TryResolveSkillByEffectRef(ResourceEffectRef effectRef, out SkillDisplayEntry skillDisplayEntry)
    {
        if (!TryResolveSkillIdByEffectRef(effectRef, out var skillId))
        {
            skillDisplayEntry = default;
            return false;
        }

        if (TryResolveSkillDisplayEntry(skillId, out skillDisplayEntry))
        {
            return true;
        }

        var baseSkillId = ResolveBaseSkillIdForCode(skillId);
        if (baseSkillId != skillId && TryResolveSkillDisplayEntry(baseSkillId, out skillDisplayEntry))
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

        var baseSkillId = ResolveBaseSkillIdForCode(skillCode);
        if (baseSkillId != skillCode && TryResolveSkillDisplayEntry(baseSkillId, out skillDisplayEntry))
            return skillDisplayEntry.Name;

        return string.Empty;
    }

    private static bool TryResolveSkillDisplayEntry(int skillCode, out SkillDisplayEntry skillDisplayEntry)
    {
        if (SkillDisplayMap.TryGetValue(skillCode, out skillDisplayEntry) && !string.IsNullOrWhiteSpace(skillDisplayEntry.Name))
            return true;

        if (SkillMap.TryGetValue(skillCode, out skillDisplayEntry) && !string.IsNullOrWhiteSpace(skillDisplayEntry.Name))
            return true;

        skillDisplayEntry = default;
        return false;
    }

    private static void SetSkillResourceMetadata(
        IReadOnlyDictionary<int, SkillClientMetadata> skillClientMetadata,
        IReadOnlyDictionary<int, SkillBaseProjection> skillBaseProjections,
        IReadOnlyList<SkillEffectReference> skillEffectReferences,
        SkillSemanticOwnerGraph? skillSemanticGraph)
    {
        _skillClientMetadata = skillClientMetadata;
        _skillBaseProjections = skillBaseProjections;
        _effectSkillIds = SkillEffectReferenceIndex.BuildUnambiguousSkillIdsByEffectCode(skillEffectReferences);
        _skillSemanticGraph = skillSemanticGraph;
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
