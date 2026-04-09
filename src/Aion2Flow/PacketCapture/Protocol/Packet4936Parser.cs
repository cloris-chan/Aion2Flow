using Cloris.Aion2Flow.PacketCapture.Readers;
using System.Buffers.Binary;

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
    int TailLength,
    string Family);

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
            0,
            ClassifyFamily(mode, groupCode, flag, value0, marker, value1, tail.Length));
        return true;
    }

    private static string ClassifyFamily(int mode, int groupCode, int flag, uint value0, ushort marker, uint value1, int tailLength)
    {
        return (mode, groupCode, flag, value0, marker, value1, tailLength) switch
        {
            (2, 19, 0, 0x000001EA, 0x0092, 0x00000A69, 10) => "buff-apply-observed",
            (2, 19, 0, 0x00000185, 0x0092, 0x00000681, 10) => "buff-remove-observed",
            (3, 70, 0, 0x00001237, 0x01A9, 0x00001176, 16) => "hot-buff-apply-observed",
            (3, 70, 0, 0x00000A67, 0x01A9, 0x0000104A, 16) => "hot-buff-remove-observed",
            (1, _, _, _, _, _, 6) => "state-short-4936",
            (2, _, _, _, _, _, 10) => "state-mid-4936",
            (3, _, _, _, _, _, 16) => "state-long-4936",
            (5, _, _, _, _, _, 32) => "state-xl-4936",
            _ => $"state-{mode}-{groupCode}-{flag}-{tailLength}"
        };
    }
}
