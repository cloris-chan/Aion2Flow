using Cloris.Aion2Flow.PacketCapture.Readers;
using System.Buffers.Binary;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet0240State(
    uint Value0,
    ushort Value1,
    int TailLength,
    string TailSignature);

internal static class Packet0240Parser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet0240State result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x02 || packet[reader.Offset + 1] != 0x40) return false;
        reader.TryAdvance(2);

        var body = packet[reader.Offset..];
        if (body.Length < 6) return false;

        var value0 = BinaryPrimitives.ReadUInt32LittleEndian(body[..4]);
        var value1 = BinaryPrimitives.ReadUInt16LittleEndian(body[^2..]);
        var tailLength = body.Length - 6;
        var tailSignature = Convert.ToHexString(body[4..Math.Min(body.Length - 2, 12)]);

        result = new Packet0240State(value0, value1, tailLength, tailSignature);
        return true;
    }
}
