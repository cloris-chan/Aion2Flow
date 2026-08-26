using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet2238CooldownCharge(
    byte State,
    int PacketSkillCode,
    int AvailableCount,
    int NextChargeRemainingMilliseconds);

internal static class Packet2238CooldownChargeParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet2238CooldownCharge result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!TryReadCanonicalVarInt(ref reader, out var declaredLength) || declaredLength < 12)
            return false;

        if (reader.Remaining < 2 || packet[reader.Offset] != 0x22 || packet[reader.Offset + 1] != 0x38)
            return false;

        if (!reader.TryAdvance(2) || reader.Remaining != declaredLength - 6 ||
            !reader.TryReadByte(out var state) ||
            !reader.TryReadUInt32Le(out var packetSkillCode) || packetSkillCode <= 0 ||
            !TryReadCanonicalVarInt(ref reader, out var availableCount))
        {
            return false;
        }

        var nextChargeRemainingMilliseconds = 0;
        if (state == 3)
        {
            if (!TryReadCanonicalVarInt(ref reader, out nextChargeRemainingMilliseconds) || nextChargeRemainingMilliseconds <= 0)
                return false;
        }
        else if (state != 1)
        {
            return false;
        }

        if (reader.Remaining != 0)
            return false;

        result = new Packet2238CooldownCharge(
            state,
            packetSkillCode,
            availableCount,
            nextChargeRemainingMilliseconds);
        return true;
    }

    private static bool TryReadCanonicalVarInt(ref PacketSpanReader reader, out int value)
    {
        var valueOffset = reader.Offset;
        if (!reader.TryReadVarInt(out value) || value < 0)
            return false;

        var encodedLength = reader.Offset - valueOffset;
        var remaining = (uint)value;
        var canonicalLength = 1;
        while (remaining >= 0x80)
        {
            remaining >>= 7;
            canonicalLength++;
        }

        return encodedLength == canonicalLength;
    }
}
