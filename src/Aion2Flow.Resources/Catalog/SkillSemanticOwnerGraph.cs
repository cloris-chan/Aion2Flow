using System.Collections.Frozen;

namespace Cloris.Aion2Flow.Resources.Catalog;

[Flags]
public enum SkillSemanticFacet : ushort
{
    None = 0,
    Damage = 1 << 0,
    Healing = 1 << 1,
    DamageOverTime = 1 << 2,
    HealingOverTime = 1 << 3,
    Shield = 1 << 4,
    Buff = 1 << 5,
    Debuff = 1 << 6,
    Support = 1 << 7
}

public enum SkillSemanticOwnerEdgeKind : byte
{
    SkillEffectFilter = 1,
    SkillEffectGroup = 2,
    SkillProjectile = 3,
    SkillToggleAbnormal = 4,
    EffectGroupEffect = 5,
    EffectLevel = 6,
    EffectAbnormal = 7,
    EffectTriggeredSkill = 8,
    FilterTargetAbnormalCriterion = 9,
    FilterTargetAbnormalGroupCriterion = 10,
    ProjectileChain = 11,
    ProjectileEffectFilter = 12,
    ProjectileEffectGroup = 13,
    ProjectileTargetFilter = 14,
    AbnormalEffect = 15,
    AbnormalEffectLevel = 16,
    AbnormalTriggeredSkill = 17,
    AbnormalLinkedAbnormal = 18
}

public readonly record struct SkillSemanticOwnerEdge(
    int OwnerSkillId,
    SkillSemanticOwnerEdgeKind Kind,
    int SourceId,
    int TargetId,
    bool IsResolved);

public readonly record struct SkillSemanticEffectResolution(
    int EffectId,
    SkillSemanticFacet DirectFacets,
    SkillSemanticFacet Facets);

public sealed record SkillSemanticProfile(
    int SkillId,
    SkillSemanticFacet Facets,
    IReadOnlyList<int> EffectGroupIds,
    IReadOnlyList<int> EffectIds,
    IReadOnlyList<int> EffectLevelIds,
    IReadOnlyList<int> EffectFilterIds,
    IReadOnlyList<int> ProjectileIds,
    IReadOnlyList<int> AbnormalIds,
    IReadOnlyList<int> AbnormalEffectIds,
    IReadOnlyList<int> AbnormalEffectLevelIds,
    IReadOnlyList<int> TriggeredSkillIds,
    bool HasUnresolvedReferences);

public sealed class SkillSemanticOwnerGraph
{
    private SkillSemanticOwnerGraph(
        IReadOnlyDictionary<int, SkillSemanticProfile> profiles,
        IReadOnlyList<SkillSemanticOwnerEdge> edges,
        SkillSemanticFacetIndex facets,
        SkillSemanticEffectSlotIndex slots)
    {
        Profiles = profiles;
        Edges = edges;
        EdgesByOwnerSkillId = BuildEdgeLookup(edges);
        FacetsBySkillId = facets.SkillFacets;
        FacetsByEffectGroupId = facets.EffectGroupFacets;
        DirectFacetsByEffectId = facets.DirectEffectFacets;
        FacetsByEffectId = facets.EffectFacets;
        FacetsByProjectileId = facets.ProjectileFacets;
        FacetsByAbnormalId = facets.AbnormalFacets;
        FacetsByAbnormalEffectId = facets.AbnormalEffectFacets;
        OwnerSkillIdsByEffectGroupId = BuildOwnerLookup(profiles.Values, static profile => profile.EffectGroupIds);
        OwnerSkillIdsByEffectId = BuildOwnerLookup(profiles.Values, static profile => profile.EffectIds);
        OwnerSkillIdsByEffectFilterId = BuildOwnerLookup(profiles.Values, static profile => profile.EffectFilterIds);
        OwnerSkillIdsByProjectileId = BuildOwnerLookup(profiles.Values, static profile => profile.ProjectileIds);
        OwnerSkillIdsByAbnormalId = BuildOwnerLookup(profiles.Values, static profile => profile.AbnormalIds);
        OwnerSkillIdsByAbnormalEffectId = BuildOwnerLookup(profiles.Values, static profile => profile.AbnormalEffectIds);
        EffectSlots = slots.Slots;
        EffectSlotsBySkillId = slots.SlotsBySkillId;
        EffectSlotsByEffectFilterId = slots.SlotsByEffectFilterId;
        EffectSlotsByEffectGroupId = slots.SlotsByEffectGroupId;
        EffectSlotsByEffectId = slots.SlotsByEffectId;
        EffectSlotsByProjectileId = slots.SlotsByProjectileId;
        EffectSlotsByAbnormalId = slots.SlotsByAbnormalId;
        EffectSlotsByAbnormalEffectId = slots.SlotsByAbnormalEffectId;
        DirectFacetsByEffectGroupId = slots.DirectFacetsByEffectGroupId;
        DirectFacetsByProjectileId = slots.DirectFacetsByProjectileId;
    }

    public IReadOnlyDictionary<int, SkillSemanticProfile> Profiles { get; }
    public IReadOnlyList<SkillSemanticOwnerEdge> Edges { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticOwnerEdge>> EdgesByOwnerSkillId { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> FacetsBySkillId { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> FacetsByEffectGroupId { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> DirectFacetsByEffectId { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> FacetsByEffectId { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> FacetsByProjectileId { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> FacetsByAbnormalId { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> FacetsByAbnormalEffectId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<int>> OwnerSkillIdsByEffectGroupId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<int>> OwnerSkillIdsByEffectId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<int>> OwnerSkillIdsByEffectFilterId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<int>> OwnerSkillIdsByProjectileId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<int>> OwnerSkillIdsByAbnormalId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<int>> OwnerSkillIdsByAbnormalEffectId { get; }
    public IReadOnlyList<SkillSemanticEffectSlot> EffectSlots { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> EffectSlotsBySkillId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> EffectSlotsByEffectFilterId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> EffectSlotsByEffectGroupId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> EffectSlotsByEffectId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> EffectSlotsByProjectileId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> EffectSlotsByAbnormalId { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> EffectSlotsByAbnormalEffectId { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> DirectFacetsByEffectGroupId { get; }
    public IReadOnlyDictionary<int, SkillSemanticFacet> DirectFacetsByProjectileId { get; }

    public bool TryResolveEffect(int effectId, out SkillSemanticEffectResolution resolution)
    {
        if (effectId > 0 &&
            DirectFacetsByEffectId.TryGetValue(effectId, out var directFacets) &&
            FacetsByEffectId.TryGetValue(effectId, out var facets))
        {
            resolution = new SkillSemanticEffectResolution(effectId, directFacets, facets);
            return true;
        }

        resolution = default;
        return false;
    }

    public bool TryResolveDirectResourceReference(uint rawId, int preferredSkillId, out SkillSemanticResourceResolution resolution)
        => TryResolveResourceReference(rawId, preferredSkillId, ResourceReferenceDomain.Direct, out resolution);

    public bool TryResolvePeriodicResourceReference(uint rawId, int preferredSkillId, out SkillSemanticResourceResolution resolution)
        => TryResolveResourceReference(rawId, preferredSkillId, ResourceReferenceDomain.Periodic, out resolution);

    private bool TryResolveResourceReference(
        uint rawId,
        int preferredSkillId,
        ResourceReferenceDomain domain,
        out SkillSemanticResourceResolution resolution)
    {
        if (rawId is 0 or > int.MaxValue)
        {
            resolution = default;
            return false;
        }

        var candidateId = unchecked((int)rawId);
        if (TryResolveResourceNode(rawId, candidateId, preferredSkillId, domain, includeAbnormalNodes: domain == ResourceReferenceDomain.Periodic, out resolution))
        {
            return true;
        }

        candidateId = unchecked((int)(rawId / 10));
        if (candidateId > 0 &&
            TryResolveResourceNode(rawId, candidateId, preferredSkillId, domain, includeAbnormalNodes: true, out resolution))
        {
            return true;
        }

        if (domain == ResourceReferenceDomain.Direct &&
            TryResolveResourceNode(rawId, unchecked((int)rawId), preferredSkillId, domain, includeAbnormalNodes: true, out resolution))
        {
            return true;
        }

        resolution = default;
        return false;
    }

    internal static SkillSemanticOwnerGraph Build(
        SkillSemanticCatalog semantics,
        IReadOnlyList<SkillEffectReference> references)
    {
        var referencesBySkillId = references
            .GroupBy(static reference => reference.SkillId)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        var facets = SkillSemanticFacetIndex.Build(semantics, referencesBySkillId);
        var slots = SkillSemanticEffectSlotIndex.Build(semantics, references, facets);
        var profiles = new Dictionary<int, SkillSemanticProfile>(referencesBySkillId.Count);
        var allEdges = new List<SkillSemanticOwnerEdge>();
        foreach (var skillId in referencesBySkillId.Keys.Order())
        {
            var builder = new ProfileBuilder(skillId, semantics, referencesBySkillId, facets);
            builder.TraverseSkill(skillId, triggered: false);
            var profile = builder.Build();
            profiles.Add(skillId, profile);
            allEdges.AddRange(builder.Edges);
        }

        var orderedEdges = allEdges
            .Distinct()
            .OrderBy(static edge => edge.OwnerSkillId)
            .ThenBy(static edge => edge.Kind)
            .ThenBy(static edge => edge.SourceId)
            .ThenBy(static edge => edge.TargetId)
            .ToArray();
        return new SkillSemanticOwnerGraph(profiles.ToFrozenDictionary(), orderedEdges, facets, slots);
    }

    private bool TryResolveResourceNode(
        uint rawId,
        int nodeId,
        int preferredSkillId,
        ResourceReferenceDomain domain,
        bool includeAbnormalNodes,
        out SkillSemanticResourceResolution resolution)
    {
        if (domain == ResourceReferenceDomain.Periodic &&
            includeAbnormalNodes &&
            TryResolveAbnormalResourceNode(rawId, nodeId, preferredSkillId, out resolution))
        {
            return true;
        }

        if (DirectFacetsByEffectId.TryGetValue(nodeId, out var directEffectFacets) && FacetsByEffectId.TryGetValue(nodeId, out var effectFacets))
        {
            resolution = CreateResourceResolution(rawId, SkillSemanticResourceNodeKind.SkillEffect, nodeId, directEffectFacets, effectFacets, EffectSlotsByEffectId, preferredSkillId);
            return true;
        }

        if (FacetsByEffectGroupId.TryGetValue(nodeId, out var groupFacets))
        {
            resolution = CreateResourceResolution(rawId, SkillSemanticResourceNodeKind.SkillEffectGroup, nodeId, DirectFacetsByEffectGroupId.GetValueOrDefault(nodeId), groupFacets, EffectSlotsByEffectGroupId, preferredSkillId);
            return true;
        }

        if (FacetsByProjectileId.TryGetValue(nodeId, out var projectileFacets))
        {
            resolution = CreateResourceResolution(rawId, SkillSemanticResourceNodeKind.SkillProjectile, nodeId, DirectFacetsByProjectileId.GetValueOrDefault(nodeId), projectileFacets, EffectSlotsByProjectileId, preferredSkillId);
            return true;
        }

        if (domain == ResourceReferenceDomain.Direct &&
            includeAbnormalNodes &&
            TryResolveAbnormalResourceNode(rawId, nodeId, preferredSkillId, out resolution))
        {
            return true;
        }

        if (EffectSlotsByEffectFilterId.ContainsKey(nodeId))
        {
            resolution = CreateResourceResolution(rawId, SkillSemanticResourceNodeKind.SkillEffectFilter, nodeId, SkillSemanticFacet.None, SkillSemanticFacet.None, EffectSlotsByEffectFilterId, preferredSkillId);
            return true;
        }

        resolution = default;
        return false;
    }

    private bool TryResolveAbnormalResourceNode(uint rawId, int nodeId, int preferredSkillId, out SkillSemanticResourceResolution resolution)
    {
        if (FacetsByAbnormalEffectId.TryGetValue(nodeId, out var abnormalEffectFacets))
        {
            resolution = CreateResourceResolution(rawId, SkillSemanticResourceNodeKind.SkillAbnormalEffect, nodeId, SkillSemanticFacet.None, abnormalEffectFacets, EffectSlotsByAbnormalEffectId, preferredSkillId);
            return true;
        }

        if (FacetsByAbnormalId.TryGetValue(nodeId, out var abnormalFacets))
        {
            resolution = CreateResourceResolution(rawId, SkillSemanticResourceNodeKind.SkillAbnormal, nodeId, SkillSemanticFacet.None, abnormalFacets, EffectSlotsByAbnormalId, preferredSkillId);
            return true;
        }

        resolution = default;
        return false;
    }

    private static SkillSemanticResourceResolution CreateResourceResolution(
        uint rawId,
        SkillSemanticResourceNodeKind nodeKind,
        int nodeId,
        SkillSemanticFacet directFacets,
        SkillSemanticFacet facets,
        IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> slotsByNodeId,
        int preferredSkillId)
    {
        slotsByNodeId.TryGetValue(nodeId, out var slots);
        var slot = SelectUnambiguousSlot(slots, preferredSkillId, out var candidateSlotCount);
        if (slot is not null && facets == SkillSemanticFacet.None)
        {
            directFacets = slot.DirectFacets;
            facets = slot.Facets;
        }

        return new SkillSemanticResourceResolution(rawId, nodeKind, nodeId, directFacets, facets, slot, candidateSlotCount);
    }

    private static SkillSemanticEffectSlot? SelectUnambiguousSlot(
        IReadOnlyList<SkillSemanticEffectSlot>? slots,
        int preferredSkillId,
        out int candidateSlotCount)
    {
        if (slots is null || slots.Count == 0)
        {
            candidateSlotCount = 0;
            return null;
        }

        if (preferredSkillId > 0)
        {
            SkillSemanticEffectSlot? match = null;
            candidateSlotCount = 0;
            for (var i = 0; i < slots.Count; i++)
            {
                var candidate = slots[i];
                if (candidate.SkillId != preferredSkillId)
                {
                    continue;
                }

                match = candidate;
                candidateSlotCount++;
            }

            if (candidateSlotCount > 0)
            {
                return candidateSlotCount == 1 ? match : null;
            }
        }

        candidateSlotCount = slots.Count;
        return slots.Count == 1 ? slots[0] : null;
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticOwnerEdge>> BuildEdgeLookup(IEnumerable<SkillSemanticOwnerEdge> edges)
        => edges
            .GroupBy(static edge => edge.OwnerSkillId)
            .ToFrozenDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<SkillSemanticOwnerEdge>)group.ToArray());

    private static IReadOnlyDictionary<int, IReadOnlyList<int>> BuildOwnerLookup(
        IEnumerable<SkillSemanticProfile> profiles,
        Func<SkillSemanticProfile, IReadOnlyList<int>> valuesSelector)
    {
        var owners = new Dictionary<int, HashSet<int>>();
        foreach (var profile in profiles)
        {
            foreach (var value in valuesSelector(profile))
            {
                if (!owners.TryGetValue(value, out var skillIds))
                {
                    skillIds = [];
                    owners.Add(value, skillIds);
                }

                skillIds.Add(profile.SkillId);
            }
        }

        return owners.ToFrozenDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<int>)pair.Value.Order().ToArray());
    }

    private enum ResourceReferenceDomain : byte
    {
        Direct = 1,
        Periodic = 2
    }

    private sealed class ProfileBuilder(
        int ownerSkillId,
        SkillSemanticCatalog semantics,
        IReadOnlyDictionary<int, SkillEffectReference[]> referencesBySkillId,
        SkillSemanticFacetIndex facets)
    {
        private readonly HashSet<int> _visitedSkills = [];
        private readonly HashSet<int> _visitedEffectGroups = [];
        private readonly HashSet<int> _visitedProjectiles = [];
        private readonly HashSet<int> _visitedAbnormals = [];
        private readonly SortedSet<int> _effectGroupIds = [];
        private readonly SortedSet<int> _effectIds = [];
        private readonly SortedSet<int> _effectLevelIds = [];
        private readonly SortedSet<int> _effectFilterIds = [];
        private readonly SortedSet<int> _projectileIds = [];
        private readonly SortedSet<int> _abnormalIds = [];
        private readonly SortedSet<int> _abnormalEffectIds = [];
        private readonly SortedSet<int> _abnormalEffectLevelIds = [];
        private readonly SortedSet<int> _triggeredSkillIds = [];
        private readonly List<SkillSemanticOwnerEdge> _edges = [];
        private SkillSemanticFacet _facets = facets.SkillFacets.GetValueOrDefault(ownerSkillId);
        private bool _hasUnresolvedReferences;

        public IReadOnlyList<SkillSemanticOwnerEdge> Edges => _edges;

        public void TraverseSkill(int skillId, bool triggered)
        {
            if (!_visitedSkills.Add(skillId) || !referencesBySkillId.TryGetValue(skillId, out var references))
            {
                if (triggered && !referencesBySkillId.ContainsKey(skillId))
                {
                    _hasUnresolvedReferences = true;
                }

                return;
            }

            foreach (var reference in references)
            {
                switch (reference.Kind)
                {
                    case SkillEffectReferenceKind.SkillEffectFilterId:
                        AddEffectFilter(reference.SkillId, reference.EffectCode, SkillSemanticOwnerEdgeKind.SkillEffectFilter);
                        break;
                    case SkillEffectReferenceKind.SkillEffectGroupId:
                        AddEdge(SkillSemanticOwnerEdgeKind.SkillEffectGroup, reference.SkillId, reference.EffectCode, semantics.EffectsByGroupId.ContainsKey(reference.EffectCode));
                        TraverseEffectGroup(reference.EffectCode);
                        break;
                    case SkillEffectReferenceKind.ProjectileId:
                        AddEdge(SkillSemanticOwnerEdgeKind.SkillProjectile, reference.SkillId, reference.EffectCode, semantics.Projectiles.ContainsKey(reference.EffectCode));
                        TraverseProjectile(reference.EffectCode);
                        break;
                    case SkillEffectReferenceKind.ToggleOnAbnormalId:
                        AddEdge(SkillSemanticOwnerEdgeKind.SkillToggleAbnormal, reference.SkillId, reference.EffectCode, semantics.Abnormals.ContainsKey(reference.EffectCode));
                        TraverseAbnormal(reference.EffectCode);
                        break;
                }
            }
        }

        public SkillSemanticProfile Build()
        {
            if (_facets == SkillSemanticFacet.None &&
                (_effectIds.Count > 0 || _effectFilterIds.Count > 0 || _projectileIds.Count > 0 || _abnormalIds.Count > 0))
            {
                _facets = SkillSemanticFacet.Support;
            }

            return new SkillSemanticProfile(
                ownerSkillId,
                _facets,
                _effectGroupIds.ToArray(),
                _effectIds.ToArray(),
                _effectLevelIds.ToArray(),
                _effectFilterIds.ToArray(),
                _projectileIds.ToArray(),
                _abnormalIds.ToArray(),
                _abnormalEffectIds.ToArray(),
                _abnormalEffectLevelIds.ToArray(),
                _triggeredSkillIds.ToArray(),
                _hasUnresolvedReferences);
        }

        private void TraverseEffectGroup(int groupId)
        {
            if (!_visitedEffectGroups.Add(groupId))
            {
                return;
            }

            if (!semantics.EffectsByGroupId.TryGetValue(groupId, out var effects))
            {
                _hasUnresolvedReferences = true;
                return;
            }

            _effectGroupIds.Add(groupId);
            _facets |= facets.EffectGroupFacets.GetValueOrDefault(groupId);
            foreach (var effect in effects)
            {
                _effectIds.Add(effect.Id);
                AddEdge(SkillSemanticOwnerEdgeKind.EffectGroupEffect, groupId, effect.Id, true);
                _facets |= facets.EffectFacets.GetValueOrDefault(effect.Id);
                if (!string.Equals(effect.LevelGroupId, "None", StringComparison.Ordinal) && effect.LevelGroupId.Length > 0)
                {
                    if (semantics.EffectLevelsByGroupId.TryGetValue(effect.LevelGroupId, out var levels))
                    {
                        foreach (var level in levels)
                        {
                            _effectLevelIds.Add(level.Id);
                            AddEdge(SkillSemanticOwnerEdgeKind.EffectLevel, effect.Id, level.Id, true);
                        }
                    }
                    else
                    {
                        _hasUnresolvedReferences = true;
                    }
                }

                if (effect.Links.AppliedAbnormalId is var abnormalId and > 0)
                {
                    AddEdge(SkillSemanticOwnerEdgeKind.EffectAbnormal, effect.Id, abnormalId, semantics.Abnormals.ContainsKey(abnormalId));
                    TraverseAbnormal(abnormalId);
                }

                if (effect.Links.TriggeredSkillId is var triggeredSkillId and > 0)
                {
                    _triggeredSkillIds.Add(triggeredSkillId);
                    var resolved = referencesBySkillId.ContainsKey(triggeredSkillId);
                    AddEdge(SkillSemanticOwnerEdgeKind.EffectTriggeredSkill, effect.Id, triggeredSkillId, resolved);
                    TraverseSkill(triggeredSkillId, triggered: true);
                }
            }
        }

        private void TraverseProjectile(int projectileId)
        {
            if (!_visitedProjectiles.Add(projectileId))
            {
                return;
            }

            if (!semantics.Projectiles.TryGetValue(projectileId, out var projectile))
            {
                _hasUnresolvedReferences = true;
                return;
            }

            _projectileIds.Add(projectileId);
            _facets |= facets.ProjectileFacets.GetValueOrDefault(projectileId);
            if (projectile.ChainProjectileId > 0)
            {
                AddEdge(SkillSemanticOwnerEdgeKind.ProjectileChain, projectileId, projectile.ChainProjectileId, semantics.Projectiles.ContainsKey(projectile.ChainProjectileId));
                TraverseProjectile(projectile.ChainProjectileId);
            }

            if (projectile.ChainSkillEffectFilterId > 0)
            {
                AddEffectFilter(projectileId, projectile.ChainSkillEffectFilterId, SkillSemanticOwnerEdgeKind.ProjectileEffectFilter);
            }

            if (projectile.ChainSkillEffectGroupId > 0)
            {
                AddEdge(SkillSemanticOwnerEdgeKind.ProjectileEffectGroup, projectileId, projectile.ChainSkillEffectGroupId, semantics.EffectsByGroupId.ContainsKey(projectile.ChainSkillEffectGroupId));
                TraverseEffectGroup(projectile.ChainSkillEffectGroupId);
            }

            if (projectile.ChainTargetFilterId > 0)
            {
                AddEdge(SkillSemanticOwnerEdgeKind.ProjectileTargetFilter, projectileId, projectile.ChainTargetFilterId, false);
            }
        }

        private void AddEffectFilter(int sourceId, int filterId, SkillSemanticOwnerEdgeKind edgeKind)
        {
            var resolved = semantics.EffectFilters.TryGetValue(filterId, out var filter);
            AddEdge(edgeKind, sourceId, filterId, resolved);
            if (!resolved || filter is null)
            {
                return;
            }

            _effectFilterIds.Add(filterId);
            foreach (var abnormalId in filter.TargetAbnormalIds)
            {
                AddEdge(SkillSemanticOwnerEdgeKind.FilterTargetAbnormalCriterion, filterId, abnormalId, semantics.Abnormals.ContainsKey(abnormalId));
            }

            foreach (var abnormalGroupId in filter.TargetAbnormalGroupIds)
            {
                AddEdge(SkillSemanticOwnerEdgeKind.FilterTargetAbnormalGroupCriterion, filterId, abnormalGroupId, semantics.AbnormalsByGroupId.ContainsKey(unchecked((uint)abnormalGroupId)));
            }
        }

        private void TraverseAbnormal(int abnormalId)
        {
            if (!_visitedAbnormals.Add(abnormalId))
            {
                return;
            }

            if (!semantics.Abnormals.TryGetValue(abnormalId, out var abnormal))
            {
                _hasUnresolvedReferences = true;
                return;
            }

            _abnormalIds.Add(abnormalId);
            _facets |= facets.AbnormalFacets.GetValueOrDefault(abnormalId);
            if (!semantics.AbnormalEffectsByAbnormalId.TryGetValue(abnormalId, out var effects))
            {
                return;
            }

            foreach (var effect in effects)
            {
                _abnormalEffectIds.Add(effect.Id);
                AddEdge(SkillSemanticOwnerEdgeKind.AbnormalEffect, abnormalId, effect.Id, true);
                _facets |= facets.AbnormalEffectFacets.GetValueOrDefault(effect.Id);
                if (!string.Equals(effect.LevelGroupId, "None", StringComparison.Ordinal) && effect.LevelGroupId.Length > 0)
                {
                    if (semantics.AbnormalEffectLevelsByGroupId.TryGetValue(effect.LevelGroupId, out var levels))
                    {
                        foreach (var level in levels)
                        {
                            _abnormalEffectLevelIds.Add(level.Id);
                            AddEdge(SkillSemanticOwnerEdgeKind.AbnormalEffectLevel, effect.Id, level.Id, true);
                        }
                    }
                    else
                    {
                        _hasUnresolvedReferences = true;
                    }
                }

                if (effect.Links.LinkedAbnormalId is var linkedAbnormalId and > 0)
                {
                    AddEdge(SkillSemanticOwnerEdgeKind.AbnormalLinkedAbnormal, effect.Id, linkedAbnormalId, semantics.Abnormals.ContainsKey(linkedAbnormalId));
                    TraverseAbnormal(linkedAbnormalId);
                }

                if (effect.Links.TriggeredSkillId is var triggeredSkillId and > 0)
                {
                    _triggeredSkillIds.Add(triggeredSkillId);
                    var resolved = referencesBySkillId.ContainsKey(triggeredSkillId);
                    AddEdge(SkillSemanticOwnerEdgeKind.AbnormalTriggeredSkill, effect.Id, triggeredSkillId, resolved);
                    TraverseSkill(triggeredSkillId, triggered: true);
                }
            }
        }

        private void AddEdge(SkillSemanticOwnerEdgeKind kind, int sourceId, int targetId, bool resolved)
        {
            _edges.Add(new SkillSemanticOwnerEdge(ownerSkillId, kind, sourceId, targetId, resolved));
            if (!resolved)
            {
                _hasUnresolvedReferences = true;
            }
        }
    }

}
