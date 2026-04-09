using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet1D37State(
    int SourceId,
    int GroupCode,
    int StateCode,
    int TailLength,
    string Family,
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
            ClassifyFamily(groupCode, stateCode),
            BuildTailSignature(tail));
        return true;
    }

    private static string ClassifyFamily(int groupCode, int stateCode)
    {
        return (groupCode, stateCode) switch
        {
            (39, 3) => "abnormal-apply-primary",
            (47, 3) => "abnormal-apply-secondary",
            (38, 3) => "abnormal-apply-hidden",
            (46, 3) => "abnormal-sync-active",
            (46, 4) => "abnormal-sync-refresh",
            (46, 9) => "abnormal-sync-remove",
            (3, _) => "system-state-low",
            (7, _) => "system-state-mid",
            (_, 3) => $"group-{groupCode}-apply",
            (_, 4) => $"group-{groupCode}-refresh",
            (_, 9) => $"group-{groupCode}-remove",
            _ => $"group-{groupCode}-state-{stateCode}"
        };
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
