namespace Cloris.Aion2Flow.Resources.Catalog;

public readonly record struct SkillSemanticRuntimeSlot(
    int SkillId,
    int Slot,
    SkillSemanticValue DirectSemantics,
    SkillSemanticValue Semantics);

internal readonly record struct SkillSemanticRuntimeNode(
    SkillSemanticResourceNodeKind Kind,
    int Id,
    SkillSemanticValue DirectSemantics,
    SkillSemanticValue Semantics,
    int CandidateSlotStart,
    int CandidateSlotCount);

internal sealed record SkillSemanticRuntimeIndexData(
    int[] SkillIds,
    SkillSemanticRuntimeSlot[] Slots,
    SkillSemanticRuntimeNode[] Nodes,
    int[] NodeSlotIndexes);

public sealed class SkillSemanticRuntimeIndex
{
    private enum ResourceReferenceDomain : byte
    {
        Direct = 1,
        Periodic = 2,
        Aura = 3,
    }

    private readonly int[] _skillIds;
    private readonly SkillSemanticRuntimeSlot[] _slots;
    private readonly SkillSemanticRuntimeNode[] _nodes;
    private readonly int[] _nodeSlotIndexes;

    internal SkillSemanticRuntimeIndex(SkillSemanticRuntimeIndexData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Validate(data);
        _skillIds = data.SkillIds;
        _slots = data.Slots;
        _nodes = data.Nodes;
        _nodeSlotIndexes = data.NodeSlotIndexes;
    }

    public static SkillSemanticRuntimeIndex Empty { get; } = new(
        new SkillSemanticRuntimeIndexData([], [], [], []));

    public int SkillCount => _skillIds.Length;

    public int SlotCount => _slots.Length;

    public int NodeCount => _nodes.Length;

    public int NodeSlotReferenceCount => _nodeSlotIndexes.Length;

    public bool ContainsSkill(int skillId) => skillId > 0 && Array.BinarySearch(_skillIds, skillId) >= 0;

    public bool TryResolveEffect(int effectId, out SkillSemanticEffectResolution resolution)
    {
        if (TryGetNode(SkillSemanticResourceNodeKind.SkillEffect, effectId, out var node))
        {
            resolution = new SkillSemanticEffectResolution(effectId, node.DirectSemantics, node.Semantics);
            return true;
        }

        resolution = default;
        return false;
    }

    public bool TryResolveDirectResourceReference(uint rawId, int preferredSkillId, out SkillSemanticResourceResolution resolution)
        => TryResolveResourceReference(rawId, preferredSkillId, ResourceReferenceDomain.Direct, out resolution);

    public bool TryResolvePeriodicResourceReference(uint rawId, int preferredSkillId, out SkillSemanticResourceResolution resolution)
        => TryResolveResourceReference(rawId, preferredSkillId, ResourceReferenceDomain.Periodic, out resolution);

    public bool TryResolveAuraResourceReference(uint rawId, out SkillSemanticResourceResolution resolution)
        => TryResolveResourceReference(rawId, 0, ResourceReferenceDomain.Aura, out resolution);

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
        if (TryResolveResourceNode(rawId, candidateId, preferredSkillId, domain, includeAbnormalNodes: domain is ResourceReferenceDomain.Periodic or ResourceReferenceDomain.Aura, out resolution))
            return true;

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

    private bool TryResolveResourceNode(
        uint rawId,
        int nodeId,
        int preferredSkillId,
        ResourceReferenceDomain domain,
        bool includeAbnormalNodes,
        out SkillSemanticResourceResolution resolution)
    {
        if (!TryGetNodeRange(nodeId, out var nodeStart, out var nodeEnd))
        {
            resolution = default;
            return false;
        }

        if (domain == ResourceReferenceDomain.Aura &&
            includeAbnormalNodes &&
            TryResolveAuraResourceNode(rawId, nodeStart, nodeEnd, preferredSkillId, out resolution))
        {
            return true;
        }

        if (domain == ResourceReferenceDomain.Periodic &&
            includeAbnormalNodes &&
            TryResolveAbnormalResourceNode(rawId, nodeStart, nodeEnd, preferredSkillId, out resolution))
        {
            return true;
        }

        if (TryResolveNode(rawId, nodeStart, nodeEnd, SkillSemanticResourceNodeKind.SkillEffect, preferredSkillId, out resolution) ||
            TryResolveNode(rawId, nodeStart, nodeEnd, SkillSemanticResourceNodeKind.SkillEffectGroup, preferredSkillId, out resolution) ||
            TryResolveNode(rawId, nodeStart, nodeEnd, SkillSemanticResourceNodeKind.SkillProjectile, preferredSkillId, out resolution))
        {
            return true;
        }

        if (domain == ResourceReferenceDomain.Direct &&
            includeAbnormalNodes &&
            TryResolveAbnormalResourceNode(rawId, nodeStart, nodeEnd, preferredSkillId, out resolution))
        {
            return true;
        }

        return TryResolveNode(rawId, nodeStart, nodeEnd, SkillSemanticResourceNodeKind.SkillEffectFilter, preferredSkillId, out resolution);
    }

    private bool TryResolveAbnormalResourceNode(
        uint rawId,
        int nodeStart,
        int nodeEnd,
        int preferredSkillId,
        out SkillSemanticResourceResolution resolution)
        => TryResolveNode(rawId, nodeStart, nodeEnd, SkillSemanticResourceNodeKind.SkillAbnormalEffect, preferredSkillId, out resolution) ||
           TryResolveNode(rawId, nodeStart, nodeEnd, SkillSemanticResourceNodeKind.SkillAbnormal, preferredSkillId, out resolution);

    private bool TryResolveAuraResourceNode(
        uint rawId,
        int nodeStart,
        int nodeEnd,
        int preferredSkillId,
        out SkillSemanticResourceResolution resolution)
        => TryResolveNode(rawId, nodeStart, nodeEnd, SkillSemanticResourceNodeKind.SkillAbnormal, preferredSkillId, out resolution) ||
           TryResolveNode(rawId, nodeStart, nodeEnd, SkillSemanticResourceNodeKind.SkillAbnormalEffect, preferredSkillId, out resolution);

    private bool TryResolveNode(
        uint rawId,
        int nodeStart,
        int nodeEnd,
        SkillSemanticResourceNodeKind kind,
        int preferredSkillId,
        out SkillSemanticResourceResolution resolution)
    {
        SkillSemanticRuntimeNode node = default;
        var found = false;
        for (var i = nodeStart; i < nodeEnd; i++)
        {
            if (_nodes[i].Kind != kind)
                continue;

            node = _nodes[i];
            found = true;
            break;
        }

        if (!found)
        {
            resolution = default;
            return false;
        }

        var slot = SelectUnambiguousSlot(in node, preferredSkillId, out var candidateSlotCount);
        var directSemantics = node.DirectSemantics;
        var semantics = node.Semantics;
        if (slot.HasValue && semantics.IsEmpty)
        {
            directSemantics = slot.Value.DirectSemantics;
            semantics = slot.Value.Semantics;
        }

        resolution = new SkillSemanticResourceResolution(
            rawId,
            node.Kind,
            node.Id,
            directSemantics,
            semantics,
            slot,
            candidateSlotCount);
        return true;
    }

    private SkillSemanticRuntimeSlot? SelectUnambiguousSlot(
        in SkillSemanticRuntimeNode node,
        int preferredSkillId,
        out int candidateSlotCount)
    {
        if (node.CandidateSlotCount == 0)
        {
            candidateSlotCount = 0;
            return null;
        }

        var indexes = _nodeSlotIndexes.AsSpan(node.CandidateSlotStart, node.CandidateSlotCount);
        if (preferredSkillId > 0)
        {
            SkillSemanticRuntimeSlot? match = null;
            candidateSlotCount = 0;
            for (var i = 0; i < indexes.Length; i++)
            {
                var candidate = _slots[indexes[i]];
                if (candidate.SkillId != preferredSkillId)
                    continue;

                match = candidate;
                candidateSlotCount++;
            }

            if (candidateSlotCount > 0)
                return candidateSlotCount == 1 ? match : null;
        }

        candidateSlotCount = indexes.Length;
        return indexes.Length == 1 ? _slots[indexes[0]] : null;
    }

    private bool TryGetNode(SkillSemanticResourceNodeKind kind, int id, out SkillSemanticRuntimeNode node)
    {
        if (!TryGetNodeRange(id, out var start, out var end))
        {
            node = default;
            return false;
        }

        for (var i = start; i < end; i++)
        {
            if (_nodes[i].Kind == kind)
            {
                node = _nodes[i];
                return true;
            }
        }

        node = default;
        return false;
    }

    private bool TryGetNodeRange(int id, out int start, out int end)
    {
        if (id <= 0)
        {
            start = 0;
            end = 0;
            return false;
        }

        var low = 0;
        var high = _nodes.Length;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (_nodes[middle].Id < id)
                low = middle + 1;
            else
                high = middle;
        }

        if (low >= _nodes.Length || _nodes[low].Id != id)
        {
            start = 0;
            end = 0;
            return false;
        }

        start = low;
        end = low + 1;
        while (end < _nodes.Length && _nodes[end].Id == id)
            end++;
        return true;
    }

    private static void Validate(SkillSemanticRuntimeIndexData data)
    {
        var previousSkillId = 0;
        for (var i = 0; i < data.SkillIds.Length; i++)
        {
            var skillId = data.SkillIds[i];
            if (skillId <= previousSkillId)
                throw new InvalidDataException("Runtime semantic skill ids must be positive and strictly increasing.");
            previousSkillId = skillId;
        }

        var previousSlot = default(SkillSemanticRuntimeSlot);
        for (var i = 0; i < data.Slots.Length; i++)
        {
            var slot = data.Slots[i];
            if (slot.SkillId <= 0 || slot.Slot < -1 ||
                !IsValidSemanticValue(slot.DirectSemantics) ||
                !IsValidSemanticValue(slot.Semantics) ||
                !IsSemanticSubset(slot.DirectSemantics, slot.Semantics) ||
                i > 0 && CompareSlotKey(previousSlot, slot) >= 0)
            {
                throw new InvalidDataException("Runtime semantic slots must be valid and strictly ordered by skill and slot.");
            }

            previousSlot = slot;
        }

        var expectedCandidateStart = 0;
        var previousNode = default(SkillSemanticRuntimeNode);
        for (var i = 0; i < data.Nodes.Length; i++)
        {
            var node = data.Nodes[i];
            if (!Enum.IsDefined(node.Kind) || node.Id <= 0 || node.CandidateSlotStart != expectedCandidateStart || node.CandidateSlotCount < 0 ||
                !IsValidSemanticValue(node.DirectSemantics) ||
                !IsValidSemanticValue(node.Semantics) ||
                !IsSemanticSubset(node.DirectSemantics, node.Semantics) ||
                node.CandidateSlotCount > data.NodeSlotIndexes.Length - node.CandidateSlotStart ||
                i > 0 && CompareNodeKey(previousNode.Kind, previousNode.Id, node.Kind, node.Id) >= 0)
            {
                throw new InvalidDataException("Runtime semantic nodes are invalid or out of order.");
            }

            SkillSemanticRuntimeSlot? previousCandidate = null;
            var candidateEnd = node.CandidateSlotStart + node.CandidateSlotCount;
            for (var candidateIndex = node.CandidateSlotStart; candidateIndex < candidateEnd; candidateIndex++)
            {
                var slotIndex = data.NodeSlotIndexes[candidateIndex];
                if ((uint)slotIndex >= (uint)data.Slots.Length)
                    throw new InvalidDataException("Runtime semantic node references an invalid slot.");

                var slot = data.Slots[slotIndex];
                if (previousCandidate.HasValue && CompareSlotKey(previousCandidate.Value, slot) >= 0)
                    throw new InvalidDataException("Runtime semantic node slots must be strictly ordered.");
                previousCandidate = slot;
            }

            expectedCandidateStart = candidateEnd;
            previousNode = node;
        }

        if (expectedCandidateStart != data.NodeSlotIndexes.Length)
            throw new InvalidDataException("Runtime semantic node slot references contain trailing values.");
    }

    private static bool IsValidSemanticValue(SkillSemanticValue value)
    {
        const SkillQuantifiedFacet quantifiedMask =
            SkillQuantifiedFacet.DirectDamage |
            SkillQuantifiedFacet.DirectHealing |
            SkillQuantifiedFacet.PeriodicDamage |
            SkillQuantifiedFacet.PeriodicHealing |
            SkillQuantifiedFacet.Shield;
        const SkillAuraFacet auraMask = SkillAuraFacet.Buff | SkillAuraFacet.Debuff;
        const SkillSemanticKnowledge knowledgeMask =
            SkillSemanticKnowledge.Classified |
            SkillSemanticKnowledge.KnownNonQuantified |
            SkillSemanticKnowledge.Unclassified;

        if ((value.QuantifiedFacets & ~quantifiedMask) != 0 ||
            (value.AuraFacets & ~auraMask) != 0 ||
            (value.Knowledge & ~knowledgeMask) != 0)
        {
            return false;
        }

        var hasFacets = value.QuantifiedFacets != SkillQuantifiedFacet.None || value.AuraFacets != SkillAuraFacet.None;
        var isClassified = (value.Knowledge & SkillSemanticKnowledge.Classified) != 0;
        return hasFacets == isClassified;
    }

    private static bool IsSemanticSubset(SkillSemanticValue direct, SkillSemanticValue transitive) =>
        (direct.QuantifiedFacets & ~transitive.QuantifiedFacets) == 0 &&
        (direct.AuraFacets & ~transitive.AuraFacets) == 0 &&
        (direct.Knowledge & ~transitive.Knowledge) == 0;

    private static int CompareNodeKey(
        SkillSemanticResourceNodeKind leftKind,
        int leftId,
        SkillSemanticResourceNodeKind rightKind,
        int rightId)
    {
        var comparison = leftId.CompareTo(rightId);
        return comparison != 0 ? comparison : ((byte)leftKind).CompareTo((byte)rightKind);
    }

    private static int CompareSlotKey(in SkillSemanticRuntimeSlot left, in SkillSemanticRuntimeSlot right)
    {
        var comparison = left.SkillId.CompareTo(right.SkillId);
        return comparison != 0 ? comparison : left.Slot.CompareTo(right.Slot);
    }
}

internal static class SkillSemanticRuntimeIndexCompiler
{
    public static SkillSemanticRuntimeIndexData Compile(
        SkillSemanticCatalog semantics,
        IReadOnlyList<SkillEffectReference> references)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        ArgumentNullException.ThrowIfNull(references);

        var referencesBySkillId = references
            .GroupBy(static reference => reference.SkillId)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        var semanticValues = SkillSemanticValueIndex.Build(semantics, referencesBySkillId);
        var richSlots = SkillSemanticEffectSlotIndex.Build(semantics, references, semanticValues);
        var slots = new SkillSemanticRuntimeSlot[richSlots.Slots.Count];
        var slotIndexes = new Dictionary<(int SkillId, int Slot), int>(slots.Length);
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = richSlots.Slots[i];
            slots[i] = new SkillSemanticRuntimeSlot(slot.SkillId, slot.Slot, slot.DirectSemantics, slot.Semantics);
            slotIndexes.Add((slot.SkillId, slot.Slot), i);
        }

        var sources = new List<NodeSource>(
            semanticValues.EffectSemantics.Count +
            semanticValues.EffectGroupSemantics.Count +
            semanticValues.ProjectileSemantics.Count +
            semanticValues.AbnormalSemantics.Count +
            semanticValues.AbnormalEffectSemantics.Count +
            richSlots.SlotsByEffectFilterId.Count);
        AddSources(sources, SkillSemanticResourceNodeKind.SkillEffect, semanticValues.EffectSemantics, semanticValues.DirectEffectSemantics, richSlots.SlotsByEffectId);
        AddSources(sources, SkillSemanticResourceNodeKind.SkillEffectGroup, semanticValues.EffectGroupSemantics, richSlots.DirectSemanticsByEffectGroupId, richSlots.SlotsByEffectGroupId);
        AddSources(sources, SkillSemanticResourceNodeKind.SkillEffectFilter, null, null, richSlots.SlotsByEffectFilterId);
        AddSources(sources, SkillSemanticResourceNodeKind.SkillProjectile, semanticValues.ProjectileSemantics, richSlots.DirectSemanticsByProjectileId, richSlots.SlotsByProjectileId);
        AddSources(sources, SkillSemanticResourceNodeKind.SkillAbnormal, semanticValues.AbnormalSemantics, semanticValues.DirectAbnormalSemantics, richSlots.SlotsByAbnormalId);
        AddSources(sources, SkillSemanticResourceNodeKind.SkillAbnormalEffect, semanticValues.AbnormalEffectSemantics, semanticValues.AbnormalEffectSemantics, richSlots.SlotsByAbnormalEffectId);
        sources.Sort(static (left, right) =>
        {
            var comparison = left.Id.CompareTo(right.Id);
            return comparison != 0 ? comparison : ((byte)left.Kind).CompareTo((byte)right.Kind);
        });

        var nodes = new SkillSemanticRuntimeNode[sources.Count];
        var nodeSlotIndexes = new List<int>();
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var candidateStart = nodeSlotIndexes.Count;
            if (source.Slots is not null)
            {
                for (var slotIndex = 0; slotIndex < source.Slots.Count; slotIndex++)
                {
                    var slot = source.Slots[slotIndex];
                    nodeSlotIndexes.Add(slotIndexes[(slot.SkillId, slot.Slot)]);
                }
            }

            nodes[i] = new SkillSemanticRuntimeNode(
                source.Kind,
                source.Id,
                source.DirectSemantics,
                source.Semantics,
                candidateStart,
                nodeSlotIndexes.Count - candidateStart);
        }

        return new SkillSemanticRuntimeIndexData(
            referencesBySkillId.Keys.Order().ToArray(),
            slots,
            nodes,
            nodeSlotIndexes.ToArray());
    }

    private static void AddSources(
        List<NodeSource> target,
        SkillSemanticResourceNodeKind kind,
        IReadOnlyDictionary<int, SkillSemanticValue>? semantics,
        IReadOnlyDictionary<int, SkillSemanticValue>? directSemantics,
        IReadOnlyDictionary<int, IReadOnlyList<SkillSemanticEffectSlot>> slots)
    {
        if (semantics is not null)
        {
            foreach (var (id, value) in semantics)
            {
                slots.TryGetValue(id, out var candidates);
                target.Add(new NodeSource(kind, id, directSemantics?.GetValueOrDefault(id) ?? SkillSemanticValue.Empty, value, candidates));
            }

            return;
        }

        foreach (var (id, candidates) in slots)
            target.Add(new NodeSource(kind, id, SkillSemanticValue.Empty, SkillSemanticValue.Empty, candidates));
    }

    private readonly record struct NodeSource(
        SkillSemanticResourceNodeKind Kind,
        int Id,
        SkillSemanticValue DirectSemantics,
        SkillSemanticValue Semantics,
        IReadOnlyList<SkillSemanticEffectSlot>? Slots);
}
