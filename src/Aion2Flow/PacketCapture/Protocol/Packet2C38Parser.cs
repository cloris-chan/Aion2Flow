using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet2C38Observation(
    int SourceId,
    int Mode,
    int StateCode,
    int SequenceId,
    int ResultCode,
    string Family,
    int TailLength);

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

        result = new Packet2C38Observation(
            sourceId,
            mode,
            stateCode,
            sequenceId,
            resultCode,
            ClassifyFamily(mode, stateCode, resultCode),
            reader.Remaining);
        return true;
    }

    private static string ClassifyFamily(int mode, int stateCode, int resultCode)
    {
        return (mode, stateCode, resultCode) switch
        {
            (1, 0, 1) => "natural-expire-observed",
            (1, 0, 12) => "manual-remove-observed",
            (1, 0, 19) => "summon-transition-observed",
            _ => $"mode-{mode}-state-{stateCode}-result-{resultCode}"
        };
    }
}
