using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet3538Sidecar(
    int TargetId,
    int State,
    int SourceId);

internal static class Packet3538SidecarParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet3538Sidecar result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out var length)) return false;
        if (length <= 3 || length != packet.Length + 3) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x35 || packet[reader.Offset + 1] != 0x38) return false;
        if (!reader.TryAdvance(2)) return false;

        if (!reader.TryReadVarInt(out var targetId)) return false;
        if (!reader.TryReadVarInt(out var state)) return false;
        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (reader.Remaining != 0) return false;

        result = new Packet3538Sidecar(targetId, state, sourceId);
        return true;
    }
}
