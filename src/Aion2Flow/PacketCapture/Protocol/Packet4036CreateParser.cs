using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet4036Create(string Family, int OwnerId, int SummonId, int? NpcCode, int TailOffset);

internal readonly record struct Packet4036NpcSpawn(string Family, int EntityId, int? NpcCode);

internal static class Packet4036CreateParser
{
    private static readonly byte[] EightByteMarker = [0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff];

    private static readonly byte[] OwnerOpcodeMarker = [0x07, 0x02, 0x06];

    private static readonly byte[] OwnerOpcodeMarkerAlt = [0x07, 0x02, 0x01];

    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet4036Create result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x40 || packet[reader.Offset + 1] != 0x36) return false;

        var family = ClassifyFamily(packet.Length);
        if (family is not "create-198" and not "create-177")
        {
            return false;
        }

        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var summonId)) return false;
        if (!reader.TryAdvance(3)) return false;

        int? npcCode = null;
        if (reader.TryReadUInt32Le(out var npcValue) && npcValue is >= 2_000_000 and <= 2_999_999)
        {
            npcCode = npcValue;
        }

        if (!TryExtractOwnerId(packet, out var ownerId))
        {
            return false;
        }

        result = new Packet4036Create(family, ownerId, summonId, npcCode, packet.Length);
        return true;
    }

    public static bool TryParseOwner(ReadOnlySpan<byte> packet, out int entityId, out int ownerId)
    {
        entityId = 0;
        ownerId = 0;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x40 || packet[reader.Offset + 1] != 0x36) return false;

        var family = ClassifyFamily(packet.Length);
        if (family is not "state-152")
        {
            return false;
        }

        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var parsedEntityId)) return false;
        if (parsedEntityId <= 0) return false;

        if (!TryExtractOwnerId(packet, out var parsedOwnerId))
        {
            return false;
        }

        if (parsedOwnerId == parsedEntityId) return false;

        entityId = parsedEntityId;
        ownerId = parsedOwnerId;
        return true;
    }

    private static bool TryExtractOwnerId(ReadOnlySpan<byte> packet, out int ownerId)
    {
        ownerId = 0;

        var keyIndex = FindArrayIndex(packet, EightByteMarker);
        if (keyIndex < 0) return false;

        var afterMarker = packet[(keyIndex + EightByteMarker.Length)..];
        var ownerOpcodeIndex = FindLastArrayIndex(afterMarker, OwnerOpcodeMarker);
        if (ownerOpcodeIndex < 0)
            ownerOpcodeIndex = FindLastArrayIndex(afterMarker, OwnerOpcodeMarkerAlt);
        if (ownerOpcodeIndex < 0) return false;

        var ownerOffset = keyIndex + ownerOpcodeIndex + 11;
        if (ownerOffset + 2 > packet.Length) return false;

        ownerId = packet[ownerOffset] | (packet[ownerOffset + 1] << 8);
        return ownerId != 0;
    }

    private static string ClassifyFamily(int payloadLength)
    {
        return payloadLength switch
        {
            >= 190 => "create-198",
            >= 175 => "create-177",
            >= 150 => "state-152",
            >= 135 => "state-137",
            >= 118 => "state-120",
            >= 110 => "state-113",
            >= 95 => "state-97",
            _ => $"state-{payloadLength}"
        };
    }

    private static int FindArrayIndex(ReadOnlySpan<byte> data, ReadOnlySpan<byte> needle)
    {
        if (needle.Length == 0) return 0;
        if (data.Length < needle.Length) return -1;

        var index = data.IndexOf(needle[0]);
        while (index >= 0 && index <= data.Length - needle.Length)
        {
            if (data.Slice(index, needle.Length).SequenceEqual(needle))
            {
                return index;
            }

            var next = data[(index + 1)..].IndexOf(needle[0]);
            if (next < 0)
            {
                return -1;
            }

            index += next + 1;
        }

        return -1;
    }

    private static int FindLastArrayIndex(ReadOnlySpan<byte> data, ReadOnlySpan<byte> needle)
    {
        if (needle.Length == 0) return data.Length;
        if (data.Length < needle.Length) return -1;

        for (var index = data.Length - needle.Length; index >= 0; index--)
        {
            if (data.Slice(index, needle.Length).SequenceEqual(needle))
            {
                return index;
            }
        }

        return -1;
    }

    public static bool TryParseNpcSpawn(ReadOnlySpan<byte> packet, out Packet4036NpcSpawn result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x40 || packet[reader.Offset + 1] != 0x36) return false;

        var family = ClassifyFamily(packet.Length);

        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var entityId)) return false;
        if (entityId <= 0) return false;
        if (!reader.TryAdvance(3)) return false;

        int? npcCode = null;
        if (reader.TryReadUInt32Le(out var npcValue) && npcValue is >= 2_000_000 and <= 2_999_999)
        {
            npcCode = npcValue;
        }

        result = new Packet4036NpcSpawn(family, entityId, npcCode);
        return true;
    }
}
