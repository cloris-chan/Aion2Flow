using System.Text;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct SteamPacket4136State(
    int EntityId, int Prefix, int NameByteLength, int NpcCode, int? CurrentHp, int? MaxHp, int? OwnerId, int TailLength);

internal static class SteamPacket4136Parser
{
    private static readonly UTF8Encoding NameEncoding = new(false, true);
    private static ReadOnlySpan<byte> OwnerSectionPrefix =>
        [0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x80, 0x75, 0xd5, 0x2a, 0xbb, 0x03, 0x00, 0x00];

    public static bool TryParse(ReadOnlySpan<byte> packet, out SteamPacket4136State result)
    {
        result = default;
        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out var declaredLength) ||
            (long)declaredLength + reader.Offset - sizeof(int) != packet.Length ||
            !reader.TryReadUInt16Le(out var opcode) || opcode != 0x3641 ||
            !reader.TryReadVarInt(out var entityId) || entityId <= 0 ||
            !reader.TryReadUInt16Le(out var prefix) || !reader.TryReadByte(out var named))
            return false;

        // Observed layout selectors, not a decoded bit-field contract. PURPLE uses different offsets.
        if (named == 0)
        {
            if (prefix is not (0x001c or 0x2004 or 0x2005 or 0x2007 or 0x200c or 0x200d or 0x200f or
                0x2185 or 0x2c04 or 0x2c0c or 0x3004 or 0x3007 or 0x300c or 0x300d or 0x3c0c))
                return false;
        }
        else if (named != 1 || prefix != 0x001f)
        {
            return false;
        }

        var nameByteLength = 0;
        if (named == 1)
        {
            if (!reader.TryReadVarInt(out nameByteLength) || nameByteLength is < 1 or > 72 ||
                reader.Remaining < nameByteLength)
                return false;
            try
            {
                _ = NameEncoding.GetCharCount(reader.RemainingSpan[..nameByteLength]);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
            reader.TryAdvance(nameByteLength);
        }

        var npcCodeOffset = reader.Offset;
        if (!reader.TryReadUInt32Le(out var npcCode) || !PacketNpcStateFields.IsNpcCatalogCode(npcCode))
            return false;

        int? currentHp = null, maxHp = null, ownerId = null;
        var hpOffset = prefix == 0x2185
            ? PacketNpcStateFields.ExtendedStateHpPairOffsetFromNpcCodeStart
            : PacketNpcStateFields.StateHpPairOffsetFromNpcCodeStart;
        if (named == 0 && PacketNpcStateFields.TryReadStateHpPair(packet, npcCodeOffset + hpOffset, out var hp))
        {
            currentHp = hp.CurrentHp;
            maxHp = hp.MaxHp;
        }

        if (named == 1 && TryReadOwner(reader.RemainingSpan, entityId, out var owner))
            ownerId = owner;

        result = new(entityId, prefix, nameByteLength, npcCode, currentHp, maxHp, ownerId, reader.Remaining);
        return true;
    }

    private static bool TryReadOwner(ReadOnlySpan<byte> tail, int entityId, out int ownerId)
    {
        ownerId = 0;
        var sectionOffset = tail.IndexOf(OwnerSectionPrefix);
        if (sectionOffset < 0)
            return false;

        var reader = new PacketSpanReader(tail[(sectionOffset + OwnerSectionPrefix.Length)..]);
        return reader.TryReadVarInt(out var repeatedEntityId) && repeatedEntityId == entityId &&
               reader.TryReadByte(out var kind) && kind == 1 &&
               reader.TryReadByte(out var state) && state == 2 && reader.TryAdvance(12) &&
               reader.TryReadByte(out var marker0) && marker0 == 7 &&
               reader.TryReadByte(out var marker1) && marker1 == 2 &&
               reader.TryReadByte(out var marker2) && marker2 == 6 &&
               reader.TryReadUInt32Le(out ownerId) && ownerId > 0 && ownerId != entityId;
    }
}
