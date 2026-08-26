using System.Buffers.Binary;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet2A38Observation(int EntityId, int Mode, int GroupCode, int InstanceSequenceId, uint HeadCode, ushort HeadValue, ulong HeadMiddleRaw, uint TimelineValue, uint StableValue, int EchoSourceId, int StackValue, ResourceEffectRef BuffResourceEffectRef, int TailLength, ulong TailLow64, ulong TailHigh64)
{
    public long? PacketDurationMilliseconds =>
        PacketDurationDecoder.TryDecodeMilliseconds(HeadValue, HeadMiddleRaw, out var durationMilliseconds)
            ? durationMilliseconds
            : null;
}

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
        var headMiddleRaw = ReadWord(body.Slice(6, 6));
        var timelineValue = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(12, 4));
        var stableValue = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(16, 4));

        var tailReader = new PacketSpanReader(body[20..]);
        if (!tailReader.TryReadVarInt(out var echoSourceId)) return false;
        if (!tailReader.TryReadVarInt(out var stackValue)) return false;
        if (tailReader.Remaining < 4) return false;
        var buffResourceEffectRef = ResourceEffectRef.FromRaw(BinaryPrimitives.ReadUInt32LittleEndian(tailReader.RemainingSpan[..4]));
        tailReader.TryAdvance(4);

        var tail = tailReader.RemainingSpan;
        result = new Packet2A38Observation(sourceId, mode, groupCode, sequenceId, headCode, headValue, headMiddleRaw, timelineValue, stableValue, echoSourceId, stackValue, buffResourceEffectRef, tail.Length, ReadWord(tail[..Math.Min(8, tail.Length)]), tail.Length > 8 ? ReadWord(tail.Slice(8, Math.Min(8, tail.Length - 8))) : 0);
        return true;
    }

    private static ulong ReadWord(ReadOnlySpan<byte> bytes)
    {
        ulong value = 0;
        for (var index = 0; index < bytes.Length; index++)
            value |= (ulong)bytes[index] << (index * 8);
        return value;
    }
}
