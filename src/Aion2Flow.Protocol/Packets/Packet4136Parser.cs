using System.Buffers.Binary;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet4136State(int EntityId, byte Mode0, byte Mode1, byte Mode2, int? NpcCode, int? CurrentHp, int? MaxHp, int? OwnerId, int TailLength);

internal static class Packet4136Parser
{
    private const int NpcCodeOffsetFromModes = 3;
    private const int SummonCreateNpcCodeOffsetFromModes = 16;
    private const int ExtendedStateBodyLength = 104;
    private const int ExtendedNpcStateBodyLength = 114;

    private static ReadOnlySpan<byte> OwnerSectionSentinel => [0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff];

    private static ReadOnlySpan<byte> OwnerHeaderMarker => [0x80, 0x75, 0xd5, 0x2a, 0xbb, 0x03, 0x00, 0x00];

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
        if (TryReadOwnedNpc(packet, tailStart, entityId, mode0, mode1, mode2, out var summonNpcCode, out var parsedOwnerId))
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
                var hpOffset = npcCodeOffset + ResolveHpPairOffsetFromNpcCodeStart(packet.Length - tailStart, mode0, mode1, mode2);
                if (PacketNpcStateFields.TryReadStateHpPair(packet, hpOffset, out var hp))
                {
                    currentHp = hp.CurrentHp;
                    maxHp = hp.MaxHp;
                }
            }
        }

        result = new Packet4136State(entityId, mode0, mode1, mode2, npcCode, currentHp, maxHp, ownerId, reader.Remaining);
        return true;
    }

    private static int ResolveHpPairOffsetFromNpcCodeStart(int bodyLength, byte mode0, byte mode1, byte mode2)
    {
        if (mode2 == 0x00 &&
            ((mode0 == 0x85 && mode1 == 0x21 && bodyLength == ExtendedNpcStateBodyLength) ||
             (mode0 == 0x05 && mode1 == 0x20 && bodyLength == ExtendedStateBodyLength)))
        {
            return PacketNpcStateFields.ExtendedStateHpPairOffsetFromNpcCodeStart;
        }

        return PacketNpcStateFields.StateHpPairOffsetFromNpcCodeStart;
    }

    private static bool TryReadOwnedNpc(
        ReadOnlySpan<byte> packet,
        int tailStart,
        int entityId,
        byte mode0,
        byte mode1,
        byte mode2,
        out int npcCode,
        out int ownerId)
    {
        npcCode = 0;
        ownerId = 0;

        if (mode2 == 0x00 && IsDirectNpcCodeLayout(mode0, mode1))
        {
            return TryReadOwnedNpcAt(packet, entityId, tailStart + NpcCodeOffsetFromModes, out npcCode, out ownerId);
        }

        if (mode1 != 0x00 || mode2 != 0x01)
        {
            return false;
        }

        if (mode0 == 0x5f)
        {
            return TryReadOwnedNpcAt(packet, entityId, tailStart + SummonCreateNpcCodeOffsetFromModes, out npcCode, out ownerId) ||
                   TryReadNamePrefixedOwnedNpc(packet, tailStart, entityId, out npcCode, out ownerId);
        }

        if (mode0 == 0x1f)
        {
            return TryReadNamePrefixedOwnedNpc(packet, tailStart, entityId, out npcCode, out ownerId);
        }

        return false;
    }

    private static bool IsDirectNpcCodeLayout(byte mode0, byte mode1) =>
        (mode0, mode1) is
            (0x17, 0x00) or
            (0x1d, 0x10) or
            (0x1f, 0x00) or
            (0x1f, 0x10) or
            (0x5d, 0x10) or
            (0x5f, 0x00) or
            (0x5f, 0x10);

    private static bool TryReadNamePrefixedOwnedNpc(ReadOnlySpan<byte> packet, int tailStart, int entityId, out int npcCode, out int ownerId)
    {
        npcCode = 0;
        ownerId = 0;
        var nameOffset = tailStart + NpcCodeOffsetFromModes;
        if ((uint)nameOffset > (uint)packet.Length)
        {
            return false;
        }

        var nameReader = new PacketSpanReader(packet[nameOffset..]);
        if (!nameReader.TryReadVarInt(out var nameByteLength) ||
            nameByteLength <= 0 ||
            !nameReader.TryAdvance(nameByteLength))
        {
            return false;
        }

        return TryReadOwnedNpcAt(packet, entityId, nameOffset + nameReader.Offset, out npcCode, out ownerId);
    }

    private static bool TryReadOwnedNpcAt(ReadOnlySpan<byte> packet, int entityId, int npcCodeOffset, out int npcCode, out int ownerId)
    {
        npcCode = 0;
        ownerId = 0;
        return PacketNpcStateFields.TryReadNpcCatalogCode(packet, npcCodeOffset, out npcCode) &&
               TryExtractSummonOwnerId(packet, entityId, npcCodeOffset + sizeof(int), out ownerId);
    }

    private static bool TryExtractSummonOwnerId(ReadOnlySpan<byte> packet, int entityId, int ownerSearchOffset, out int ownerId)
    {
        ownerId = 0;
        if ((uint)ownerSearchOffset > (uint)packet.Length)
        {
            return false;
        }

        var ownerTail = packet[ownerSearchOffset..];
        var sentinelOffset = ownerTail.IndexOf(OwnerSectionSentinel);
        ReadOnlySpan<byte> ownerSection;
        if (sentinelOffset >= 0)
        {
            ownerSection = ownerTail[(sentinelOffset + OwnerSectionSentinel.Length)..];
            if (TryExtractOwnerIdFromHeader(ownerSection, entityId, out ownerId))
            {
                return true;
            }
        }
        else
        {
            ownerSection = ownerTail;
        }

        var ownerMarkerOffset = ownerSection.LastIndexOf(OwnerOpcodeMarker);
        if (ownerMarkerOffset < 0)
        {
            ownerMarkerOffset = ownerSection.LastIndexOf(OwnerOpcodeMarkerAlt);
        }

        if (ownerMarkerOffset < 0)
        {
            return false;
        }

        var ownerOffset = ownerMarkerOffset + OwnerOpcodeMarker.Length;
        if (!BinaryPrimitives.TryReadInt32LittleEndian(ownerSection[ownerOffset..], out ownerId))
        {
            return false;
        }

        return ownerId > 0 && ownerId != entityId;
    }

    private static bool TryExtractOwnerIdFromHeader(ReadOnlySpan<byte> afterSentinel, int entityId, out int ownerId)
    {
        ownerId = 0;
        if (!afterSentinel.StartsWith(OwnerHeaderMarker))
        {
            return false;
        }

        var reader = new PacketSpanReader(afterSentinel[OwnerHeaderMarker.Length..]);
        if (!reader.TryReadVarInt(out var candidate) || candidate <= 0 || candidate == entityId)
        {
            return false;
        }

        var tail = afterSentinel[(OwnerHeaderMarker.Length + reader.Offset)..];
        if (tail.Length < 2 || tail[1] != 0x02 || tail[0] is < 0x0e or > 0x11)
        {
            return false;
        }

        ownerId = candidate;
        return true;
    }
}
