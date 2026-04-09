using Cloris.Aion2Flow.PacketCapture.Readers;
using System.Buffers.Binary;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet008DRemainHp(
    int MobId,
    int Value0,
    int Value1,
    int Value2,
    uint Hp,
    int TailLength);

internal static class Packet008DRemainHpParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet008DRemainHp result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x00 || packet[reader.Offset + 1] != 0x8d) return false;
        if (!reader.TryAdvance(2)) return false;

        if (!reader.TryReadVarInt(out var mobId)) return false;
        if (mobId == 0) return false;
        if (!reader.TryReadVarInt(out var value0)) return false;
        if (!reader.TryReadVarInt(out var value1)) return false;
        if (!reader.TryReadVarInt(out var value2)) return false;
        if (reader.Remaining < 4) return false;

        var hp = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(reader.Offset, 4));
        if (!reader.TryAdvance(4)) return false;

        result = new Packet008DRemainHp(mobId, value0, value1, value2, hp, reader.Remaining);
        return true;
    }
}
