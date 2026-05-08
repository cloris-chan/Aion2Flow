using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet218DBattleToggle(int NpcId, bool? IsActive, int TailLength);

internal static class Packet218DBattleToggleParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet218DBattleToggle result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x21 || packet[reader.Offset + 1] != 0x8d) return false;
        if (!reader.TryAdvance(2)) return false;

        if (!reader.TryReadVarInt(out var npcId)) return false;
        if (npcId == 0) return false;

        bool? isActive = null;
        if (reader.Remaining >= 2 &&
            packet[reader.Offset] == 0x00 &&
            packet[reader.Offset + 1] is 0x00 or 0x01)
        {
            isActive = packet[reader.Offset + 1] == 0x01;
        }

        result = new Packet218DBattleToggle(npcId, isActive, reader.Remaining);
        return true;
    }
}
