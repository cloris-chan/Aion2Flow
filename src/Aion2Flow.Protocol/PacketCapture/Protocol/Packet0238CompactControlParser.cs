using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet0238CompactControl(
    int SourceId,
    int Mode,
    int SkillCodeRaw,
    int Marker,
    int Flag,
    int EchoSourceId,
    int ZeroValue,
    int TailValue);

internal static class Packet0238CompactControlParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet0238CompactControl result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out var length)) return false;
        if (length <= 3 || length != packet.Length + 3) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x02 || packet[reader.Offset + 1] != 0x38) return false;
        if (!reader.TryAdvance(2)) return false;

        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (!reader.TryReadVarInt(out var mode)) return false;
        if (!reader.TryReadUInt32Le(out var skillCodeRaw)) return false;
        if (!reader.TryReadByte(out var marker)) return false;
        if (!reader.TryReadByte(out var flag)) return false;
        if (!reader.TryReadVarInt(out var echoSourceId)) return false;
        if (!reader.TryReadUInt32Le(out var zeroValue)) return false;
        if (!reader.TryReadUInt32Le(out var tailValue)) return false;
        if (reader.Remaining != 0) return false;

        result = new Packet0238CompactControl(
            sourceId,
            mode,
            skillCodeRaw,
            marker,
            flag,
            echoSourceId,
            zeroValue,
            tailValue);
        return true;
    }
}
