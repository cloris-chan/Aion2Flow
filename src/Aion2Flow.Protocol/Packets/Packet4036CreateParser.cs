using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet4036Create(Packet4036Kind Kind, int OwnerId, int SummonId, int? NpcCode, int TailOffset);

internal readonly record struct Packet4036NpcSpawn(Packet4036Kind Kind, int EntityId, int? NpcCode, int? CurrentHp, int? MaxHp);

internal static class Packet4036CreateParser
{
    private static ReadOnlySpan<byte> OwnerSectionSentinel => [0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff];

    private static ReadOnlySpan<byte> OwnerHeaderMarker => [0x80, 0x75, 0xd5, 0x2a, 0xbb, 0x03, 0x00, 0x00];

    private static ReadOnlySpan<byte> OwnerOpcodeMarker => [0x07, 0x02, 0x06];

    private static ReadOnlySpan<byte> OwnerOpcodeMarkerAlt => [0x07, 0x02, 0x01];

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
        if (reader.TryReadUInt32Le(out var npcValue) && PacketNpcStateFields.IsNpcCatalogCode(npcValue))
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

    internal static bool TryExtractOwnerId(ReadOnlySpan<byte> packet, out int ownerId)
        => TryExtractOwnerId(packet, out ownerId, out _);

    internal static bool TryExtractOwnerId(ReadOnlySpan<byte> packet, out int ownerId, out int tailOffset)
    {
        ownerId = 0;
        tailOffset = 0;

        var keyIndex = packet.IndexOf(OwnerSectionSentinel);
        if (keyIndex < 0) return false;

        var afterMarker = packet[(keyIndex + OwnerSectionSentinel.Length)..];
        if (TryExtractOwnerIdFromHeader(afterMarker, out ownerId, out var headerTailOffset))
        {
            tailOffset = keyIndex + OwnerSectionSentinel.Length + headerTailOffset;
            return true;
        }

        var ownerOpcodeIndex = afterMarker.LastIndexOf(OwnerOpcodeMarker);
        if (ownerOpcodeIndex < 0)
            ownerOpcodeIndex = afterMarker.LastIndexOf(OwnerOpcodeMarkerAlt);
        if (ownerOpcodeIndex < 0) return false;

        var ownerOffset = keyIndex + ownerOpcodeIndex + 11;
        if (ownerOffset + 2 > packet.Length) return false;

        ownerId = packet[ownerOffset] | (packet[ownerOffset + 1] << 8);
        tailOffset = ownerOffset + 2;
        return ownerId != 0;
    }

    private static bool TryExtractOwnerIdFromHeader(ReadOnlySpan<byte> afterMarker, out int ownerId, out int tailOffset)
    {
        ownerId = 0;
        tailOffset = 0;

        if (!afterMarker.StartsWith(OwnerHeaderMarker))
            return false;

        if (!TryReadVarInt(afterMarker, OwnerHeaderMarker.Length, out var candidate, out var consumed))
            return false;

        var offset = OwnerHeaderMarker.Length + consumed;
        if (candidate <= 0 || afterMarker.Length - offset < 14)
            return false;

        return afterMarker[offset] == 0x10 &&
               afterMarker[offset + 1] == 0x02 &&
               afterMarker[offset + 11] == 0x60 &&
               afterMarker[offset + 12] == 0xfc &&
               afterMarker[offset + 13] == 0x44 &&
               TryConfirmOwnerLe32(afterMarker, offset + 14, candidate, out ownerId, out tailOffset);
    }

    private static bool TryConfirmOwnerLe32(ReadOnlySpan<byte> buffer, int offset, int candidate, out int ownerId, out int tailOffset)
    {
        ownerId = 0;
        tailOffset = 0;
        var end = Math.Min(buffer.Length - 4, offset + 8);
        for (var i = offset; i <= end; i++)
        {
            if (buffer[i] == (byte)candidate &&
                buffer[i + 1] == (byte)(candidate >> 8) &&
                buffer[i + 2] == (byte)(candidate >> 16) &&
                buffer[i + 3] == (byte)(candidate >> 24))
            {
                ownerId = candidate;
                tailOffset = i + 4;
                return true;
            }
        }

        return false;
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
        var spawnTag0 = packet[reader.Offset];
        var spawnTag1 = packet[reader.Offset + 1];
        var spawnTag2 = packet[reader.Offset + 2];
        var spawnTagLikelyCarriesNpcCode =
            ((spawnTag1 == 0x10 || spawnTag1 == 0x20 || spawnTag1 == 0x21 || spawnTag1 == 0x22 || spawnTag1 == 0x30 || spawnTag1 == 0x32) && spawnTag2 == 0x00)
            || (spawnTag0 == 0x1C && spawnTag1 == 0x00 && spawnTag2 == 0x00);
        if (!reader.TryAdvance(3)) return false;

        int? npcCode = null;
        int? currentHp = null;
        int? maxHp = null;
        if (spawnTagLikelyCarriesNpcCode &&
            reader.TryReadUInt32Le(out var npcValue) &&
            PacketNpcStateFields.IsNpcCatalogCode(npcValue))
        {
            npcCode = npcValue;
            if (PacketNpcStateFields.TryReadSpawnHpPair(packet, reader.Offset + PacketNpcStateFields.HpPairOffsetFromNpcCodeEnd, out var hp))
            {
                currentHp = hp.CurrentHp;
                maxHp = hp.MaxHp;
            }
        }

        result = new Packet4036NpcSpawn(kind, entityId, npcCode, currentHp, maxHp);
        return true;
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
