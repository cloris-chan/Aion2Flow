using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet4738Cooldown(int RowBaseSkillId, int RemainingMilliseconds);

internal ref struct Packet4738CooldownBatchReader
{
    private PacketSpanReader reader;
    private int nextEntryIndex;

    internal Packet4738CooldownBatchReader(int count, PacketSpanReader reader)
    {
        Count = count;
        this.reader = reader;
        nextEntryIndex = 0;
    }

    public int Count { get; }

    public bool TryRead(out Packet4738Cooldown result)
    {
        result = default;
        if (nextEntryIndex >= Count || !Packet4738CooldownParser.TryReadEntry(ref reader, out result))
            return false;

        nextEntryIndex++;
        return true;
    }
}

internal static class Packet4738CooldownParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet4738CooldownBatchReader batch)
    {
        batch = default;

        var reader = new PacketSpanReader(packet);
        if (!TryReadCanonicalVarInt(ref reader, out var declaredLength) || declaredLength < 6)
            return false;

        if (reader.Remaining < 2 || packet[reader.Offset] != 0x47 || packet[reader.Offset + 1] != 0x38)
            return false;

        if (!reader.TryAdvance(2) || reader.Remaining != declaredLength - 6)
            return false;

        if (!reader.TryReadByte(out var count) || count == 0)
            return false;

        var validationReader = reader;
        for (var entryIndex = 0; entryIndex < count; entryIndex++)
        {
            if (!TryReadEntry(ref validationReader, out _))
                return false;
        }

        if (validationReader.Remaining != 0)
            return false;

        batch = new Packet4738CooldownBatchReader(count, reader);
        return true;
    }

    internal static bool TryReadEntry(ref PacketSpanReader reader, out Packet4738Cooldown result)
    {
        result = default;
        if (!reader.TryReadUInt32Le(out var rowBaseSkillId) || rowBaseSkillId <= 0 ||
            !TryReadCanonicalVarInt(ref reader, out var remainingMilliseconds))
        {
            return false;
        }

        result = new Packet4738Cooldown(rowBaseSkillId, remainingMilliseconds);
        return true;
    }

    private static bool TryReadCanonicalVarInt(ref PacketSpanReader reader, out int value)
    {
        var valueOffset = reader.Offset;
        return reader.TryReadVarInt(out value) &&
               value >= 0 &&
               reader.Offset - valueOffset == GetCanonicalVarIntLength(value);
    }

    private static int GetCanonicalVarIntLength(int value)
    {
        var remaining = (uint)value;
        var length = 1;
        while (remaining >= 0x80)
        {
            remaining >>= 7;
            length++;
        }

        return length;
    }
}
