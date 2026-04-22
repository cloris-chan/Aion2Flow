using System.Buffers.Binary;
using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet4936State(
    int SourceId,
    int Mode,
    int GroupCode,
    int Flag,
    uint Value0,
    ushort Marker,
    uint Value1,
    string TailSignature,
    int TailLength);

internal static class Packet4936Parser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet4936State result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x49 || packet[reader.Offset + 1] != 0x36) return false;
        reader.TryAdvance(2);

        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (!reader.TryReadVarInt(out var mode)) return false;
        if (!reader.TryReadVarInt(out var groupCode)) return false;
        if (!reader.TryReadVarInt(out var flag)) return false;
        if (reader.Remaining < 4) return false;

        var tail = reader.RemainingSpan;
        var value0 = tail.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(tail[..4]) : 0u;
        var marker = tail.Length >= 6 ? BinaryPrimitives.ReadUInt16LittleEndian(tail.Slice(4, 2)) : (ushort)0;
        var value1 = tail.Length >= 10 ? BinaryPrimitives.ReadUInt32LittleEndian(tail.Slice(6, 4)) : 0u;
        var tailSignature = Convert.ToHexString(tail[..Math.Min(12, tail.Length)]);

        result = new Packet4936State(
            sourceId,
            mode,
            groupCode,
            flag,
            value0,
            marker,
            value1,
            tailSignature,
            tail.Length);
        return true;
    }
}
