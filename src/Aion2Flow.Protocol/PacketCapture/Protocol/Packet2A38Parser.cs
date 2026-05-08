using Cloris.Aion2Flow.PacketCapture.Readers;
using System.Buffers.Binary;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet2A38Observation(
    int SourceId,
    int Mode,
    int GroupCode,
    int SequenceId,
    uint HeadCode,
    ushort HeadValue,
    uint TimelineValue,
    uint StableValue,
    int EchoSourceId,
    int StackValue,
    uint BuffCodeRaw,
    string TailSignature,
    int TailLength);

internal static class Packet2A38Parser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet2A38Observation result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x2a || packet[reader.Offset + 1] != 0x38) return false;
        reader.TryAdvance(2);

        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (!reader.TryReadVarInt(out var mode)) return false;
        if (!reader.TryReadVarInt(out var groupCode)) return false;
        if (!reader.TryReadVarInt(out var sequenceId)) return false;
        if (reader.Remaining < 23) return false;

        var body = reader.RemainingSpan;
        var headCode = BinaryPrimitives.ReadUInt32LittleEndian(body[..4]);
        var headValue = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(4, 2));
        var timelineValue = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(12, 4));
        var stableValue = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(16, 4));

        var tailReader = new PacketSpanReader(body[20..]);
        if (!tailReader.TryReadVarInt(out var echoSourceId)) return false;
        if (!tailReader.TryReadVarInt(out var stackValue)) return false;
        if (tailReader.Remaining < 4) return false;
        var buffCodeRaw = BinaryPrimitives.ReadUInt32LittleEndian(tailReader.RemainingSpan[..4]);
        tailReader.TryAdvance(4);

        result = new Packet2A38Observation(
            sourceId,
            mode,
            groupCode,
            sequenceId,
            headCode,
            headValue,
            timelineValue,
            stableValue,
            echoSourceId,
            stackValue,
            buffCodeRaw,
            Convert.ToHexString(tailReader.RemainingSpan[..Math.Min(8, tailReader.Remaining)]),
            tailReader.Remaining);
        return true;
    }
}
