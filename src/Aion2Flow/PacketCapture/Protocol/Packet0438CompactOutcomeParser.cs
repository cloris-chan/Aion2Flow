using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet0438CompactOutcome(
    int TargetId,
    int LayoutTag,
    int Flag,
    int SourceId,
    int SkillCodeRaw,
    int Marker,
    int Type,
    int TailLength);

internal static class Packet0438CompactOutcomeParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet0438CompactOutcome result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out var length)) return false;
        if (length <= 3 || length != packet.Length + 3) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x04 || packet[reader.Offset + 1] != 0x38) return false;
        if (!reader.TryAdvance(2)) return false;

        if (!reader.TryReadVarInt(out var targetId)) return false;
        if (!reader.TryReadVarInt(out var layoutTag)) return false;
        if (!reader.TryReadVarInt(out var flag)) return false;
        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (targetId <= 0 || sourceId <= 0) return false;
        if (layoutTag != 2 || reader.Remaining < 5) return false;

        if (!reader.TryReadUInt32Le(out var skillCodeRaw)) return false;
        if (!reader.TryReadByte(out var marker)) return false;
        if (!reader.TryReadVarInt(out var type)) return false;
        if (reader.Remaining == 0) return false;

        result = new Packet0438CompactOutcome(
            targetId,
            layoutTag,
            flag,
            sourceId,
            skillCodeRaw,
            marker,
            type,
            reader.Remaining);
        return true;
    }
}
