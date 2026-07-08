using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public enum CombatPacketEvidenceKind : byte
{
    MissingPacketContext,
    Control0238,
    Control0638,
    Value0438,
    Effect0538,
    OtherCombatOpcode
}

public static class CombatPacketContextReader
{
    internal static CombatPacketEvidenceKind ClassifyPacketEvidence(in RawPacketReference raw)
    {
        if (!HasPacketContext(in raw))
            return CombatPacketEvidenceKind.MissingPacketContext;

        return raw.Opcode switch
        {
            0x0238 => CombatPacketEvidenceKind.Control0238,
            0x0638 => CombatPacketEvidenceKind.Control0638,
            0x0438 => CombatPacketEvidenceKind.Value0438,
            0x0538 => CombatPacketEvidenceKind.Effect0538,
            _ => CombatPacketEvidenceKind.OtherCombatOpcode
        };
    }

    internal static bool HasPacketContext(in RawPacketReference raw) =>
        raw.Opcode != 0 ||
        raw.PayloadLength != 0 ||
        raw.CaptureSequence != 0 ||
        !raw.StructurePath.IsEmpty;
}
