using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet4036Create(Packet4036Kind Kind, int OwnerId, int SummonId, int? NpcCode, int TailOffset);

internal readonly record struct Packet4036NpcSpawn(Packet4036Kind Kind, int EntityId, int? NpcCode, int? CurrentHp, int? MaxHp);

internal static class Packet4036CreateParser
{
    private const int SpawnHpPairOffsetFromNpcCodeEnd = 21;

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

        var kind = Packet4036Descriptors.ClassifyKind(packet.Length);
        if (!Packet4036Descriptors.IsCreateKind(kind))
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

        result = new Packet4036Create(kind, ownerId, summonId, npcCode, packet.Length);
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

        var kind = Packet4036Descriptors.ClassifyKind(packet.Length);
        if (kind != Packet4036Kind.State152)
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

        var kind = Packet4036Descriptors.ClassifyKind(packet.Length);

        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var entityId)) return false;
        if (entityId <= 0) return false;
        if (reader.Remaining < 3) return false;
        var spawnTag1 = packet[reader.Offset + 1];
        var spawnTag2 = packet[reader.Offset + 2];
        var spawnTagLikelyCarriesNpcCode = (spawnTag1 == 0x20 || spawnTag1 == 0x21 || spawnTag1 == 0x22 || spawnTag1 == 0x30) && spawnTag2 == 0x00;
        if (!reader.TryAdvance(3)) return false;

        int? npcCode = null;
        int? currentHp = null;
        int? maxHp = null;
        if (spawnTagLikelyCarriesNpcCode &&
            reader.TryReadUInt32Le(out var npcValue) &&
            npcValue is >= 2_000_000 and <= 2_999_999)
        {
            npcCode = npcValue;
            if (TryReadSpawnHpPair(packet, reader.Offset + SpawnHpPairOffsetFromNpcCodeEnd, out var parsedCurrentHp, out var parsedMaxHp))
            {
                currentHp = parsedCurrentHp;
                maxHp = parsedMaxHp;
            }
        }

        result = new Packet4036NpcSpawn(kind, entityId, npcCode, currentHp, maxHp);
        return true;
    }

    private static bool TryReadSpawnHpPair(ReadOnlySpan<byte> packet, int offset, out int currentHp, out int maxHp)
    {
        currentHp = 0;
        maxHp = 0;

        if (!TryReadVarInt(packet, offset, out currentHp, out var currentLength))
        {
            return false;
        }

        if (!TryReadVarInt(packet, offset + currentLength, out maxHp, out var maxLength))
        {
            return false;
        }

        if (currentHp < 0 || maxHp <= 0 || currentHp > maxHp)
        {
            return false;
        }

        var afterHpOffset = offset + currentLength + maxLength;
        return HasPercentGaugePair(packet, afterHpOffset);
    }

    private static bool HasPercentGaugePair(ReadOnlySpan<byte> packet, int offset)
    {
        if ((uint)offset > packet.Length - 8u)
        {
            return false;
        }

        return packet[offset] == 0x64 &&
               packet[offset + 1] == 0x00 &&
               packet[offset + 2] == 0x00 &&
               packet[offset + 3] == 0x00 &&
               packet[offset + 4] == 0x64 &&
               packet[offset + 5] == 0x00 &&
               packet[offset + 6] == 0x00 &&
               packet[offset + 7] == 0x00;
    }

    private static bool TryReadVarInt(ReadOnlySpan<byte> buffer, int offset, out int value, out int consumed)
    {
        value = 0;
        consumed = 0;
        var shift = 0;

        for (var i = 0; i < 5; i++)
        {
            var index = offset + i;
            if ((uint)index >= (uint)buffer.Length)
            {
                return false;
            }

            var current = buffer[index];
            value |= (current & 0x7f) << shift;
            consumed = i + 1;
            if ((current & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }

        value = 0;
        consumed = 0;
        return false;
    }
}
