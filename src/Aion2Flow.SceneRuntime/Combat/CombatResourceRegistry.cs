using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public sealed class CombatResourceRegistry
{
    private readonly SkillDisplayCatalog _skillMap;
    private readonly IReadOnlyDictionary<int, SkillBaseProjection> _skillBaseProjections;
    private readonly IReadOnlyDictionary<uint, int> _effectSkillIds;
    private readonly SkillSemanticRuntimeIndex _skillSemanticIndex;

    public SkillDisplayCatalog SkillMap => _skillMap;

    public IReadOnlyDictionary<int, SkillBaseProjection> SkillBaseProjections => _skillBaseProjections;
    public IReadOnlyDictionary<int, NpcDisplayEntry> NpcCatalog { get; }

    public CombatResourceRegistry(ResourceCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _skillMap = snapshot.Skills;
        _skillBaseProjections = snapshot.SkillBaseProjections;
        _effectSkillIds = snapshot.EffectSkillIds;
        _skillSemanticIndex = snapshot.SkillSemanticRuntimeIndex;
        NpcCatalog = snapshot.NpcCatalog;
    }

    public bool TryResolveNpcCatalogEntry(int npcCode, out NpcDisplayEntry entry)
    {
        if (NpcCatalog.TryGetValue(npcCode, out entry))
            return true;

        entry = default;
        return false;
    }

    public bool TryResolveSkillIdByEffectRef(ResourceEffectRef effectRef, out int skillId)
    {
        if (effectRef.IsEmpty || !_effectSkillIds.TryGetValue(effectRef.RawId, out skillId))
        {
            skillId = 0;
            return false;
        }

        return true;
    }

    private bool TryResolveDirectResourceSemantics(
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

    private bool TryResolvePeriodicResourceSemantics(
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

    public bool TryResolveDirectCombatResourceSemantics(in CombatWireObservation observation, out SkillSemanticResourceResolution resolution)
        => TryResolveDirectResourceSemantics(observation.DetailResourceEffectRef, in observation, out resolution);

    public bool TryResolvePeriodicCombatResourceSemantics(in CombatWireObservation observation, out SkillSemanticResourceResolution resolution)
    {
        if (TryResolvePeriodicResourceSemantics(observation.BodyResourceEffectRef, in observation, out resolution))
        {
            return true;
        }

        return TryResolvePeriodicResourceSemantics(observation.DetailResourceEffectRef, in observation, out resolution);
    }

    public bool TryResolveAuraResourceSemantics(ResourceEffectRef resourceEffectRef, out SkillSemanticResourceResolution resolution)
    {
        if (resourceEffectRef.IsEmpty)
        {
            resolution = default;
            return false;
        }

        return _skillSemanticIndex.TryResolveAuraResourceReference(resourceEffectRef.RawId, out resolution);
    }

    public int ResolveBaseSkillIdForCode(int skillCode)
    {
        if (skillCode <= 0)
        {
            return 0;
        }

        return _skillBaseProjections.TryGetValue(skillCode, out var projection) && projection.BaseSkillId > 0
            ? projection.BaseSkillId
            : skillCode;
    }

    private int ResolveSemanticSkillId(in CombatWireObservation observation)
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

    public bool TryResolveBaseSkillIdForEffectRef(ResourceEffectRef effectRef, out int baseSkillId)
    {
        if (!TryResolveSkillIdByEffectRef(effectRef, out var skillId))
        {
            baseSkillId = 0;
            return false;
        }

        baseSkillId = ResolveBaseSkillIdForCode(skillId);
        return baseSkillId > 0;
    }

    public bool TryResolveBaseSkillIdForEventKey(CombatEventKey eventKey, out int baseSkillId)
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

    public bool TryResolveSkillByEffectRef(ResourceEffectRef effectRef, out SkillDisplayEntry skillDisplayEntry)
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

    public string DisplaySkillNameFor(int skillCode)
    {
        if (skillCode <= 0)
            return string.Empty;

        if (SkillMap.TryGetValue(skillCode, out var skillDisplayEntry) && !string.IsNullOrWhiteSpace(skillDisplayEntry.Name))
            return skillDisplayEntry.Name;

        if (TryResolveSkillByEffectRef(ResourceEffectRef.FromRaw(unchecked((uint)skillCode)), out var effectSkill))
            return effectSkill.Name;

        var baseSkillId = ResolveBaseSkillIdForCode(skillCode);
        if (baseSkillId != skillCode && TryResolveSkillDisplayEntry(baseSkillId, out skillDisplayEntry))
            return skillDisplayEntry.Name;

        return string.Empty;
    }

    private bool TryResolveSkillDisplayEntry(int skillCode, out SkillDisplayEntry skillDisplayEntry)
    {
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
