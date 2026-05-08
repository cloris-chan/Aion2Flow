using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet2C38Observation(
    int SourceId,
    int Mode,
    int StateCode,
    int SequenceId,
    int ResultCode,
    int TailLength,
    int TailSourceId,
    int TailSkillCodeRaw);

internal static class Packet2C38Parser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet2C38Observation result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x2c || packet[reader.Offset + 1] != 0x38) return false;
        reader.TryAdvance(2);

        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (!reader.TryReadVarInt(out var mode)) return false;
        if (!reader.TryReadVarInt(out var stateCode)) return false;
        if (!reader.TryReadVarInt(out var sequenceId)) return false;
        if (!reader.TryReadVarInt(out var resultCode)) return false;

        var tailSourceId = 0;
        var tailSkillCodeRaw = 0;
        var tailLength = reader.Remaining;
        if (tailLength >= 5)
        {
            var tailReader = reader;
            if (tailReader.TryReadVarInt(out var parsedTailSourceId) &&
                tailReader.Remaining >= 4 &&
                tailReader.TryReadUInt32Le(out var parsedTailSkillCodeRaw))
            {
                tailSourceId = parsedTailSourceId;
                tailSkillCodeRaw = unchecked((int)parsedTailSkillCodeRaw);
            }
        }

        result = new Packet2C38Observation(
            sourceId,
            mode,
            stateCode,
            sequenceId,
            resultCode,
            tailLength,
            tailSourceId,
            tailSkillCodeRaw);
        return true;
    }
}
