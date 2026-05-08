using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet0438CompactValue(
    int TargetId,
    int LayoutTag,
    int Flag,
    int SourceId,
    int SkillCodeRaw,
    int Marker,
    int Type,
    int Unknown,
    int Value,
    int Loop,
    int TailLength,
    int TailRaw);

internal static class Packet0438CompactValueParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet0438CompactValue result)
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
        if (layoutTag != 0 || reader.Remaining < 5) return false;

        if (!reader.TryReadUInt32Le(out var skillCodeRaw)) return false;
        if (!reader.TryReadByte(out var marker)) return false;
        if (!reader.TryReadVarInt(out var type)) return false;
        if (!reader.TryReadVarInt(out var unknown)) return false;
        if (!reader.TryReadVarInt(out var value)) return false;
        if (!reader.TryReadVarInt(out var loop)) return false;

        var tailLength = reader.Remaining;
        if (tailLength is <= 0 or > 16)
        {
            return false;
        }

        var tailRaw = 0;
        if (tailLength >= 4)
        {
            var tail = packet[reader.Offset..];
            tailRaw = tail[0]
                | (tail[1] << 8)
                | (tail[2] << 16)
                | (tail[3] << 24);
        }

        result = new Packet0438CompactValue(
            targetId,
            layoutTag,
            flag,
            sourceId,
            skillCodeRaw,
            marker,
            type,
            unknown,
            value,
            loop,
            tailLength,
            tailRaw);
        return true;
    }
}
