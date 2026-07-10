using System.Collections.Frozen;

namespace Cloris.Aion2Flow.Resources.Catalog;

public sealed record SkillSemanticEffectSlot(
    int SkillId,
    int Slot,
    SkillSemanticFacet DirectFacets,
    SkillSemanticFacet Facets,
    IReadOnlyList<int> EffectFilterIds,
    IReadOnlyList<int> EffectGroupIds,
    IReadOnlyList<int> EffectIds,
    IReadOnlyList<int> ProjectileIds,
    IReadOnlyList<int> AbnormalIds,
    IReadOnlyList<int> AbnormalEffectIds,
    bool HasUnresolvedReferences);

public enum SkillSemanticResourceNodeKind : byte
{
    SkillEffect = 1,
    SkillEffectGroup = 2,
    SkillEffectFilter = 3,
    SkillProjectile = 4,
    SkillAbnormal = 5,
    SkillAbnormalEffect = 6
}

public readonly record struct SkillSemanticResourceResolution(
    uint RawId,
    SkillSemanticResourceNodeKind NodeKind,
    int NodeId,
    SkillSemanticFacet DirectFacets,
    SkillSemanticFacet Facets,
    SkillSemanticEffectSlot? Slot,
    int CandidateSlotCount)
{
    public bool HasUnambiguousSlot => Slot is not null;
}

internal sealed class SkillSemanticEffectSlotIndex
{
    private SkillSemanticEffectSlotIndex(
        IReadOnlyList<SkillSemanticEffectSlot> slots,
        IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> slotsBySkillId,
        IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> slotsByEffectFilterId,
        IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> slotsByEffectGroupId,
        IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> slotsByEffectId,
        IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> slotsByProjectileId,
        IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> slotsByAbnormalId,
        IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> slotsByAbnormalEffectId,
        IReadOnlyDictionary<int, SkillSemanticFacet> directFacetsByEffectGroupId,
        IReadOnlyDictionary<int, SkillSemanticFacet> directFacetsByProjectileId)
    {
        Slots = slots;
        SlotsBySkillId = slotsBySkillId;
        SlotsByEffectFilterId = slotsByEffectFilterId;
        SlotsByEffectGroupId = slotsByEffectGroupId;
        SlotsByEffectId = slotsByEffectId;
        SlotsByProjectileId = slotsByProjectileId;
        SlotsByAbnormalId = slotsByAbnormalId;
        SlotsByAbnormalEffectId = slotsByAbnormalEffectId;
        DirectFacetsByEffectGroupId = directFacetsByEffectGroupId;
        DirectFacetsByProjectileId = directFacetsByProjectileId;
    }

    public IReadOnlyList<SkillSemanticEffectSlot> Slots { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> SlotsBySkillId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> SlotsByEffectFilterId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> SlotsByEffectGroupId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> SlotsByEffectId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> SlotsByProjectileId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> SlotsByAbnormalId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> SlotsByAbnormalEffectId { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> DirectFacetsByEffectGroupId { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> DirectFacetsByProjectileId { get; }

    public static SkillSemanticEffectSlotIndex Build(
        SkillSemanticCatalog semantics,
        IReadOnlyList<SkillEffectReference> references,
        SkillSemanticFacetIndex facets)
    {
        var accumulators = new Dictionary<(int SkillId, int Slot), SlotAccumulator>();
        foreach (var reference in references)
        {
            var key = (reference.SkillId, reference.Slot);
            if (!accumulators.TryGetValue(key, out var accumulator))
            {
                accumulator = new SlotAccumulator(reference.SkillId, reference.Slot);
                accumulators.Add(key, accumulator);
            }

            accumulator.Add(reference);
        }

        var slots = new SkillSemanticEffectSlot[accumulators.Count];
        var index = 0;
        foreach (var accumulator in accumulators.Values.OrderBy(static value => value.SkillId).ThenBy(static value => value.Slot))
        {
            slots[index++] = accumulator.Build(semantics, facets);
        }

        return new SkillSemanticEffectSlotIndex(
            slots,
            BuildLookup(slots, static slot => [slot.SkillId]),
            BuildLookup(slots, static slot => slot.EffectFilterIds),
            BuildLookup(slots, static slot => slot.EffectGroupIds),
            BuildLookup(slots, static slot => slot.EffectIds),
            BuildLookup(slots, static slot => slot.ProjectileIds),
            BuildLookup(slots, static slot => slot.AbnormalIds),
            BuildLookup(slots, static slot => slot.AbnormalEffectIds),
            BuildDirectEffectGroupFacets(semantics, facets),
            BuildDirectProjectileFacets(semantics, facets));
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> BuildLookup(
        IReadOnlyList<SkillSemanticEffectSlot> slots,
        Func<SkillSemanticEffectSlot, IReadOnlyList<int>> idsSelector)
    {
        var values = new Dictionary<int, List<SkillSemanticEffectSlot>>();
        foreach (var slot in slots)
        {
            foreach (var id in idsSelector(slot))
            {
                if (!values.TryGetValue(id, out var entries))
                {
                    entries = [];
                    values.Add(id, entries);
                }

                entries.Add(slot);
            }
        }

        return values.ToFrozenDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<SkillSemanticEffectSlot>)pair.Value.ToArray());
    }

    private static IReadOnlyDictionary<int, SkillSemanticFacet> BuildDirectEffectGroupFacets(
        SkillSemanticCatalog semantics,
        SkillSemanticFacetIndex facets)
    {
        var result = new Dictionary<int, SkillSemanticFacet>(semantics.EffectsByGroupId.Count);
        foreach (var (groupId, effects) in semantics.EffectsByGroupId)
        {
            var directFacets = SkillSemanticFacet.None;
            foreach (var effect in effects)
            {
                directFacets |= facets.DirectEffectFacets.GetValueOrDefault(effect.Id);
            }

            result.Add(groupId, directFacets);
        }

        return result.ToFrozenDictionary();
    }

    private static IReadOnlyDictionary<int, SkillSemanticFacet> BuildDirectProjectileFacets(
        SkillSemanticCatalog semantics,
        SkillSemanticFacetIndex facets)
    {
        var result = new Dictionary<int, SkillSemanticFacet>(semantics.Projectiles.Count);
        foreach (var projectileId in semantics.Projectiles.Keys)
        {
            var visitor = new SlotNodeVisitor(semantics, facets);
            visitor.TraverseProjectile(projectileId);
            result.Add(projectileId, visitor.DirectFacets);
        }

        return result.ToFrozenDictionary();
    }

    private sealed class SlotAccumulator(int skillId, int slot)
    {
        private int _effectFilterId;
        private int _effectGroupId;
        private int _projectileId;
        private int _toggleAbnormalId;

        public int SkillId { get; } = skillId;
        public int Slot { get; } = slot;

        public void Add(SkillEffectReference reference)
        {
            switch (reference.Kind)
            {
                case SkillEffectReferenceKind.SkillEffectFilterId:
                    SetUnique(ref _effectFilterId, reference.EffectCode, reference.Kind);
                    break;
                case SkillEffectReferenceKind.SkillEffectGroupId:
                    SetUnique(ref _effectGroupId, reference.EffectCode, reference.Kind);
                    break;
                case SkillEffectReferenceKind.ProjectileId:
                    SetUnique(ref _projectileId, reference.EffectCode, reference.Kind);
                    break;
                case SkillEffectReferenceKind.ToggleOnAbnormalId:
                    SetUnique(ref _toggleAbnormalId, reference.EffectCode, reference.Kind);
                    break;
                default:
                    throw new InvalidDataException($"Unsupported skill effect reference kind {reference.Kind}.");
            }
        }

        public SkillSemanticEffectSlot Build(SkillSemanticCatalog semantics, SkillSemanticFacetIndex facets)
        {
            var visitor = new SlotNodeVisitor(semantics, facets);
            visitor.AddEffectFilter(_effectFilterId);
            visitor.TraverseEffectGroup(_effectGroupId);
            visitor.TraverseProjectile(_projectileId);
            visitor.TraverseAbnormal(_toggleAbnormalId);
            return visitor.Build(SkillId, Slot);
        }

        private void SetUnique(ref int target, int value, SkillEffectReferenceKind kind)
        {
            if (target == 0 || target == value)
            {
                target = value;
                return;
            }

            throw new InvalidDataException($"Skill {SkillId} slot {Slot} has conflicting {kind} values {target} and {value}.");
        }
    }

    private sealed class SlotNodeVisitor(SkillSemanticCatalog semantics, SkillSemanticFacetIndex facets)
    {
        private readonly SortedSet<int> _effectFilterIds = [];
        private readonly SortedSet<int> _effectGroupIds = [];
        private readonly SortedSet<int> _effectIds = [];
        private readonly SortedSet<int> _projectileIds = [];
        private readonly SortedSet<int> _abnormalIds = [];
        private readonly SortedSet<int> _abnormalEffectIds = [];
        private readonly HashSet<int> _visitedProjectiles = [];
        private readonly HashSet<int> _visitedAbnormals = [];
        private bool _hasUnresolvedReferences;

        public SkillSemanticFacet DirectFacets { get; private set; }
        public SkillSemanticFacet Facets { get; private set; }

        public void AddEffectFilter(int filterId)
        {
            if (filterId <= 0)
            {
                return;
            }

            _effectFilterIds.Add(filterId);
            _hasUnresolvedReferences |= !semantics.EffectFilters.ContainsKey(filterId);
        }

        public void TraverseEffectGroup(int groupId)
        {
            if (groupId <= 0 || !_effectGroupIds.Add(groupId))
            {
                return;
            }

            if (!semantics.EffectsByGroupId.TryGetValue(groupId, out var effects))
            {
                _hasUnresolvedReferences = true;
                return;
            }

            Facets |= facets.EffectGroupFacets.GetValueOrDefault(groupId);
            foreach (var effect in effects)
            {
                _effectIds.Add(effect.Id);
                DirectFacets |= facets.DirectEffectFacets.GetValueOrDefault(effect.Id);
                Facets |= facets.EffectFacets.GetValueOrDefault(effect.Id);
                TraverseAbnormal(effect.Links.AppliedAbnormalId);
            }
        }

        public void TraverseProjectile(int projectileId)
        {
            if (projectileId <= 0 || !_visitedProjectiles.Add(projectileId))
            {
                return;
            }

            _projectileIds.Add(projectileId);
            if (!semantics.Projectiles.TryGetValue(projectileId, out var projectile))
            {
                _hasUnresolvedReferences = true;
                return;
            }

            Facets |= facets.ProjectileFacets.GetValueOrDefault(projectileId);
            TraverseProjectile(projectile.ChainProjectileId);
            AddEffectFilter(projectile.ChainSkillEffectFilterId);
            TraverseEffectGroup(projectile.ChainSkillEffectGroupId);
        }

        public void TraverseAbnormal(int abnormalId)
        {
            if (abnormalId <= 0 || !_visitedAbnormals.Add(abnormalId))
            {
                return;
            }

            _abnormalIds.Add(abnormalId);
            if (!semantics.Abnormals.ContainsKey(abnormalId))
            {
                _hasUnresolvedReferences = true;
                return;
            }

            Facets |= facets.AbnormalFacets.GetValueOrDefault(abnormalId);
            if (!semantics.AbnormalEffectsByAbnormalId.TryGetValue(abnormalId, out var effects))
            {
                return;
            }

            foreach (var effect in effects)
            {
                _abnormalEffectIds.Add(effect.Id);
                Facets |= facets.AbnormalEffectFacets.GetValueOrDefault(effect.Id);
                TraverseAbnormal(effect.Links.LinkedAbnormalId);
            }
        }

        public SkillSemanticEffectSlot Build(int skillId, int slot)
        {
            if (Facets == SkillSemanticFacet.None &&
                (_effectFilterIds.Count > 0 || _effectGroupIds.Count > 0 || _projectileIds.Count > 0 || _abnormalIds.Count > 0))
            {
                Facets = SkillSemanticFacet.Support;
            }

            return new SkillSemanticEffectSlot(
                skillId,
                slot,
                DirectFacets,
                Facets,
                _effectFilterIds.ToArray(),
                _effectGroupIds.ToArray(),
                _effectIds.ToArray(),
                _projectileIds.ToArray(),
                _abnormalIds.ToArray(),
                _abnormalEffectIds.ToArray(),
                _hasUnresolvedReferences);
        }
    }
}
