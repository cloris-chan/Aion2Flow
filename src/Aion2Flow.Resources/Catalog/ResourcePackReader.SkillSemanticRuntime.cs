namespace Cloris.Aion2Flow.Resources.Catalog;

internal static partial class ResourcePackReader
{
    private static SkillSemanticRuntimeIndex ReadSkillSemanticRuntimeIndex(
        IReadOnlyDictionary<SectionId, ResourcePackSection> sections)
    {
        var skillIds = ReadRuntimeSkillIds(RequireSection(sections, SectionId.SkillSemanticRuntimeSkillIds));
        var slots = ReadRuntimeSlots(RequireSection(sections, SectionId.SkillSemanticRuntimeSlots));
        var nodes = ReadRuntimeNodes(RequireSection(sections, SectionId.SkillSemanticRuntimeNodes));
        var nodeSlotIndexes = ReadRuntimeNodeSlotIndexes(RequireSection(sections, SectionId.SkillSemanticRuntimeNodeSlots));
        return new SkillSemanticRuntimeIndex(new SkillSemanticRuntimeIndexData(skillIds, slots, nodes, nodeSlotIndexes));
    }

    private static int[] ReadRuntimeSkillIds(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var result = new int[section.Count];
        for (var i = 0; i < result.Length; i++)
            result[i] = ReadInt32(ref cursor);
        RequireFullyRead(cursor);
        return result;
    }

    private static SkillSemanticRuntimeSlot[] ReadRuntimeSlots(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var result = new SkillSemanticRuntimeSlot[section.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new SkillSemanticRuntimeSlot(
                ReadInt32(ref cursor),
                ReadInt32(ref cursor),
                (SkillSemanticFacet)ReadUInt16(ref cursor),
                (SkillSemanticFacet)ReadUInt16(ref cursor));
        }

        RequireFullyRead(cursor);
        return result;
    }

    private static SkillSemanticRuntimeNode[] ReadRuntimeNodes(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var result = new SkillSemanticRuntimeNode[section.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new SkillSemanticRuntimeNode(
                (SkillSemanticResourceNodeKind)ReadByte(ref cursor),
                ReadInt32(ref cursor),
                (SkillSemanticFacet)ReadUInt16(ref cursor),
                (SkillSemanticFacet)ReadUInt16(ref cursor),
                ReadInt32(ref cursor),
                ReadInt32(ref cursor));
        }

        RequireFullyRead(cursor);
        return result;
    }

    private static int[] ReadRuntimeNodeSlotIndexes(ResourcePackSection section)
    {
        var cursor = section.Payload.Span;
        var result = new int[section.Count];
        for (var i = 0; i < result.Length; i++)
            result[i] = ReadInt32(ref cursor);
        RequireFullyRead(cursor);
        return result;
    }
}
