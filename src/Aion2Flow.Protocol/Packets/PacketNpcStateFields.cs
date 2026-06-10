using System.Buffers.Binary;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct PacketNpcHpPair(int CurrentHp, int MaxHp);

internal static class PacketNpcStateFields
{
    public const int HpPairOffsetFromNpcCodeEnd = 21;

    public static bool IsNpcCatalogCode(int value) => value is >= 2_000_000 and <= 2_999_999;

    public static bool TryReadNpcCatalogCode(ReadOnlySpan<byte> packet, int offset, out int npcCode)
    {
        npcCode = 0;
        if (offset < 0 || offset + sizeof(int) > packet.Length)
        {
            return false;
        }

        if (!BinaryPrimitives.TryReadInt32LittleEndian(packet.Slice(offset, sizeof(int)), out var value) ||
            !IsNpcCatalogCode(value))
        {
            return false;
        }

        npcCode = value;
        return true;
    }

    public static bool TryReadPositiveHpPair(ReadOnlySpan<byte> packet, int offset, out PacketNpcHpPair hp) => TryReadHpPair(packet, offset, allowZeroCurrentHp: false, requireCurrentWithinMax: false, requirePercentGaugePair: false, out hp);

    public static bool TryReadSpawnHpPair(ReadOnlySpan<byte> packet, int offset, out PacketNpcHpPair hp) => TryReadHpPair(packet, offset, allowZeroCurrentHp: true, requireCurrentWithinMax: true, requirePercentGaugePair: true, out hp);

    private static bool TryReadHpPair(ReadOnlySpan<byte> packet, int offset, bool allowZeroCurrentHp, bool requireCurrentWithinMax, bool requirePercentGaugePair, out PacketNpcHpPair hp)
    {
        hp = default;
        if ((uint)offset >= (uint)packet.Length)
        {
            return false;
        }

        var reader = new PacketSpanReader(packet[offset..]);
        if (!reader.TryReadVarInt(out var currentHp) ||
            currentHp < 0 ||
            (!allowZeroCurrentHp && currentHp == 0))
        {
            return false;
        }

        if (!reader.TryReadVarInt(out var maxHp) || maxHp <= 0)
        {
            return false;
        }

        if (requireCurrentWithinMax && currentHp > maxHp)
        {
            return false;
        }

        if (requirePercentGaugePair && !HasPercentGaugePair(packet, offset + reader.Offset))
        {
            return false;
        }

        hp = new PacketNpcHpPair(currentHp, maxHp);
        return true;
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
}
