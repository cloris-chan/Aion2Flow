using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public static class CombatResourceRegistry
{
    private static SkillDisplayCatalog _skillMap = [];
    private static IReadOnlyDictionary<int, SkillBaseProjection> _skillBaseProjections = new Dictionary<int, SkillBaseProjection>();
    private static IReadOnlyDictionary<uint, int> _effectSkillIds = new Dictionary<uint, int>();
    private static SkillSemanticRuntimeIndex _skillSemanticIndex = SkillSemanticRuntimeIndex.Empty;

    public static SkillDisplayCatalog SkillMap => _skillMap;

    public static SkillDisplayCatalog SkillDisplayMap { get; private set; } = [];
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

    public static void SetGameResources(ResourceCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _skillMap = snapshot.Skills;
        _skillBaseProjections = snapshot.SkillBaseProjections;
        _effectSkillIds = snapshot.EffectSkillIds;
        _skillSemanticIndex = snapshot.SkillSemanticRuntimeIndex;
        SkillDisplayMap = snapshot.Skills;
        NpcCatalog = snapshot.NpcCatalog;
        SkillMapRevision++;
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

    private static bool TryResolveDirectResourceSemantics(
        ResourceEffectRef effectRef,
        in CombatWireObservation observation,
        out SkillSemanticResourceResolution resolution)
    {
        if (effectRef.IsEmpty)
        {
            resolution = default;
            return false;
        }

        var preferredSkillId = ResolveSemanticSkillId(in observation);
        return _skillSemanticIndex.TryResolveDirectResourceReference(effectRef.RawId, preferredSkillId, out resolution);
    }

    private static bool TryResolvePeriodicResourceSemantics(
        ResourceEffectRef effectRef,
        in CombatWireObservation observation,
        out SkillSemanticResourceResolution resolution)
    {
        if (effectRef.IsEmpty)
        {
            resolution = default;
            return false;
        }

        var preferredSkillId = ResolveSemanticSkillId(in observation);
        return _skillSemanticIndex.TryResolvePeriodicResourceReference(effectRef.RawId, preferredSkillId, out resolution);
    }

    public static bool TryResolveDirectCombatResourceSemantics(in CombatWireObservation observation, out SkillSemanticResourceResolution resolution)
        => TryResolveDirectResourceSemantics(observation.DetailResourceEffectRef, in observation, out resolution);

    public static bool TryResolvePeriodicCombatResourceSemantics(in CombatWireObservation observation, out SkillSemanticResourceResolution resolution)
    {
        if (TryResolvePeriodicResourceSemantics(observation.BodyResourceEffectRef, in observation, out resolution))
        {
            return true;
        }

        return TryResolvePeriodicResourceSemantics(observation.DetailResourceEffectRef, in observation, out resolution);
    }

    public static bool TryResolveAuraResourceSemantics(ResourceEffectRef resourceEffectRef, out SkillSemanticResourceResolution resolution)
    {
        if (resourceEffectRef.IsEmpty)
        {
            resolution = default;
            return false;
        }

        return _skillSemanticIndex.TryResolveAuraResourceReference(resourceEffectRef.RawId, out resolution);
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

    private static int ResolveSemanticSkillId(in CombatWireObservation observation)
    {
        var skillId = observation.SkillCode > 0
            ? observation.SkillCode
            : observation.BodySkillVariantRaw > 0
                ? observation.BodySkillVariantRaw
                : observation.PeriodicTailSkillCodeRaw;
        if (skillId <= 0)
        {
            return 0;
        }

        if (_skillSemanticIndex.ContainsSkill(skillId))
        {
            return skillId;
        }

        var baseSkillId = ResolveBaseSkillIdForCode(skillId);
        return _skillSemanticIndex.ContainsSkill(baseSkillId) ? baseSkillId : 0;
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

}
