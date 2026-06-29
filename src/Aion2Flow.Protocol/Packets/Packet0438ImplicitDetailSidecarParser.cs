using System.Buffers.Binary;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet0438ImplicitDetailSidecar(int TargetId, int LayoutTag, int Flag, int SourceId, int BodySkillVariantRaw, int Marker, int Type, long DetailRaw, int Unknown, int Value, int Loop);

internal static class Packet0438ImplicitDetailSidecarParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet0438ImplicitDetailSidecar result)
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
        if (layoutTag != 0 || !IsByteControlField(flag) || reader.Remaining < 5) return false;

        if (!reader.TryReadUInt32Le(out var bodySkillVariantRaw)) return false;
        if (bodySkillVariantRaw <= 0) return false;
        if (!reader.TryReadByte(out var marker)) return false;
        if (!reader.TryReadVarInt(out var type)) return false;
        if (!IsByteControlField(type)) return false;

        if (!TryParseRemainder(reader.RemainingSpan, out var detailRaw, out var unknown, out var value, out var loop))
            return false;

        result = new Packet0438ImplicitDetailSidecar(targetId, layoutTag, flag, sourceId, unchecked((int)bodySkillVariantRaw), marker, type, detailRaw, unknown, value, loop);
        return true;
    }

    internal static bool TryParseRemainder(ReadOnlySpan<byte> remainder, out long detailRaw, out int unknown, out int value, out int loop)
    {
        detailRaw = 0;
        unknown = 0;
        value = 0;
        loop = 0;

        if (remainder.Length < 11)
            return false;

        detailRaw = BinaryPrimitives.ReadInt64LittleEndian(remainder[..8]);
        if (detailRaw == 0)
            return false;

        var reader = new PacketSpanReader(remainder[8..]);
        if (!reader.TryReadVarInt(out unknown) || unknown <= 0) return false;
        if (!reader.TryReadVarInt(out value) || value <= 0) return false;
        if (!reader.TryReadVarInt(out loop) || loop != 0) return false;
        return reader.Remaining == 0;
    }

    private static bool IsByteControlField(int value) => value is >= 0 and <= byte.MaxValue;
}
