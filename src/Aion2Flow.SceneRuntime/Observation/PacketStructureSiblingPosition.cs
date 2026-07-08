namespace Cloris.Aion2Flow.SceneRuntime.Observation;

public readonly record struct PacketStructureSiblingPosition(int AssociationScopeId, int SiblingIndex);

public static class PacketStructureSiblingPositionResolver
{
    public static bool TryResolve(in PacketStructurePath structurePath, out PacketStructureSiblingPosition position)
    {
        position = default;
        if (structurePath.IsEmpty)
            return false;

        var parent = structurePath.Parent;
        if (parent.ScopeId > 0)
        {
            position = new PacketStructureSiblingPosition(parent.ScopeId, structurePath.Leaf.SiblingIndex);
            return true;
        }

        if (structurePath.Leaf.ParentScopeId > 0)
        {
            position = new PacketStructureSiblingPosition(structurePath.Leaf.ParentScopeId, structurePath.Leaf.SiblingIndex);
            return true;
        }

        if (structurePath.Leaf.ScopeId <= 0)
            return false;

        position = new PacketStructureSiblingPosition(structurePath.Leaf.ScopeId, structurePath.Leaf.SiblingIndex);
        return true;
    }
}
