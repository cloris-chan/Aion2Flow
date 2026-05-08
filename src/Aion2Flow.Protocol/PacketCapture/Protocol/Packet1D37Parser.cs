using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet1D37State(
    int SourceId,
    int GroupCode,
    int StateCode,
    int TailLength,
    string TailSignature);

internal static class Packet1D37Parser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet1D37State result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x1d || packet[reader.Offset + 1] != 0x37) return false;
        reader.TryAdvance(2);

        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (!reader.TryReadVarInt(out var groupCode)) return false;
        if (!reader.TryReadVarInt(out var stateCode)) return false;

        var tail = reader.RemainingSpan;
        result = new Packet1D37State(
            sourceId,
            groupCode,
            stateCode,
            tail.Length,
            BuildTailSignature(tail));
        return true;
    }

    private static string BuildTailSignature(ReadOnlySpan<byte> tail)
    {
        if (tail.IsEmpty)
        {
            return "empty";
        }

        return Convert.ToHexString(tail[..Math.Min(8, tail.Length)]);
    }
}
