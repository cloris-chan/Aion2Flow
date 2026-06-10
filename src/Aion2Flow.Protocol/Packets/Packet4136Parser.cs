using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet4136State(int EntityId, byte Mode0, byte Mode1, byte Mode2, int? NpcCode, int? CurrentHp, int? MaxHp, int TailLength);

internal static class Packet4136Parser
{
    private const int NpcCodeOffsetFromModes = 3;

    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet4136State result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x41 || packet[reader.Offset + 1] != 0x36) return false;
        reader.TryAdvance(2);

        if (!reader.TryReadVarInt(out var entityId)) return false;
        var tailStart = reader.Offset;
        if (!reader.TryReadByte(out var mode0)) return false;
        if (!reader.TryReadByte(out var mode1)) return false;
        if (!reader.TryReadByte(out var mode2)) return false;

        int? npcCode = null;
        int? currentHp = null;
        int? maxHp = null;
        var npcCodeOffset = tailStart + NpcCodeOffsetFromModes;
        if (PacketNpcStateFields.TryReadNpcCatalogCode(packet, npcCodeOffset, out var parsedNpcCode))
        {
            npcCode = parsedNpcCode;
            var hpOffset = npcCodeOffset + sizeof(int) + PacketNpcStateFields.HpPairOffsetFromNpcCodeEnd;
            if (PacketNpcStateFields.TryReadPositiveHpPair(packet, hpOffset, out var hp))
            {
                currentHp = hp.CurrentHp;
                maxHp = hp.MaxHp;
            }
        }

        result = new Packet4136State(entityId, mode0, mode1, mode2, npcCode, currentHp, maxHp, reader.Remaining);
        return true;
    }
}
