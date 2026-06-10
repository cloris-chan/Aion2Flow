using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet4136State(int EntityId, byte Mode0, byte Mode1, byte Mode2, int? NpcCode, int? CurrentHp, int? MaxHp, int? OwnerId, int TailLength);

internal static class Packet4136Parser
{
    private const int NpcCodeOffsetFromModes = 3;
    private const int SummonCreateNpcCodeOffsetFromModes = 16;

    private static ReadOnlySpan<byte> OwnerSectionSentinel => [0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff];

    private static ReadOnlySpan<byte> OwnerOpcodeMarker => [0x07, 0x02, 0x06];

    private static ReadOnlySpan<byte> OwnerOpcodeMarkerAlt => [0x07, 0x02, 0x01];

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
        int? ownerId = null;
        if (TryReadSummonCreateNpcCode(packet, tailStart, mode0, mode1, mode2, out var summonNpcCode) &&
            TryExtractSummonOwnerId(packet, entityId, out var parsedOwnerId))
        {
            npcCode = summonNpcCode;
            ownerId = parsedOwnerId;
        }
        else
        {
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
        }

        result = new Packet4136State(entityId, mode0, mode1, mode2, npcCode, currentHp, maxHp, ownerId, reader.Remaining);
        return true;
    }

    private static bool TryReadSummonCreateNpcCode(ReadOnlySpan<byte> packet, int tailStart, byte mode0, byte mode1, byte mode2, out int npcCode)
    {
        npcCode = 0;
        if (mode0 != 0x5f || mode1 != 0x00 || mode2 != 0x01)
        {
            return false;
        }

        return PacketNpcStateFields.TryReadNpcCatalogCode(packet, tailStart + SummonCreateNpcCodeOffsetFromModes, out npcCode);
    }

    private static bool TryExtractSummonOwnerId(ReadOnlySpan<byte> packet, int entityId, out int ownerId)
    {
        ownerId = 0;

        var sentinelOffset = packet.IndexOf(OwnerSectionSentinel);
        if (sentinelOffset < 0)
        {
            return false;
        }

        var afterSentinel = packet[(sentinelOffset + OwnerSectionSentinel.Length)..];
        var ownerMarkerOffset = afterSentinel.LastIndexOf(OwnerOpcodeMarker);
        if (ownerMarkerOffset < 0)
        {
            ownerMarkerOffset = afterSentinel.LastIndexOf(OwnerOpcodeMarkerAlt);
        }

        if (ownerMarkerOffset < 0)
        {
            return false;
        }

        var ownerOffset = sentinelOffset + OwnerSectionSentinel.Length + ownerMarkerOffset + OwnerOpcodeMarker.Length;
        if (ownerOffset < 0 || ownerOffset + sizeof(int) > packet.Length)
        {
            return false;
        }

        ownerId = packet[ownerOffset]
            | (packet[ownerOffset + 1] << 8)
            | (packet[ownerOffset + 2] << 16)
            | (packet[ownerOffset + 3] << 24);
        return ownerId > 0 && ownerId != entityId;
    }
}
