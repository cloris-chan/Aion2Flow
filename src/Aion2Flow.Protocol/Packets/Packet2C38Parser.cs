using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet2C38ResultObservation(int ResultIndex, int StateCode, int InstanceSequenceId, int ResultCode, int DetailEntityId, uint DetailValue0, uint DetailValue1);

internal ref struct Packet2C38BatchReader
{
    private PacketSpanReader reader;
    private int nextResultIndex;

    internal Packet2C38BatchReader(int entityId, int resultCount, PacketSpanReader reader)
    {
        EntityId = entityId;
        ResultCount = resultCount;
        this.reader = reader;
        nextResultIndex = 0;
    }

    public int EntityId { get; }

    public int ResultCount { get; }

    public bool TryRead(out Packet2C38ResultObservation result)
    {
        if (nextResultIndex >= ResultCount)
        {
            result = default;
            return false;
        }

        var resultIndex = nextResultIndex;
        if (!Packet2C38Parser.TryReadResult(ref reader, resultIndex, out result))
            return false;

        nextResultIndex++;
        return true;
    }
}

internal static class Packet2C38Parser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet2C38BatchReader batch)
    {
        batch = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x2c || packet[reader.Offset + 1] != 0x38) return false;
        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var entityId)) return false;
        if (!reader.TryReadVarInt(out var resultCount) || resultCount <= 0) return false;

        var validationReader = reader;
        for (var resultIndex = 0; resultIndex < resultCount; resultIndex++)
        {
            if (!TryReadResult(ref validationReader, resultIndex, out _))
                return false;
        }

        if (validationReader.Remaining != 0)
            return false;

        batch = new Packet2C38BatchReader(entityId, resultCount, reader);
        return true;
    }

    internal static bool TryReadResult(ref PacketSpanReader reader, int resultIndex, out Packet2C38ResultObservation result)
    {
        result = default;
        if (!reader.TryReadVarInt(out var stateCode)) return false;
        if (!reader.TryReadVarInt(out var instanceSequenceId)) return false;
        if (!reader.TryReadVarInt(out var resultCode)) return false;

        var detailEntityId = 0;
        var detailValue0 = 0u;
        var detailValue1 = 0u;
        if (stateCode == 7 && resultCode == 11)
        {
            if (!reader.TryReadVarInt(out detailEntityId)) return false;
            if (!reader.TryReadUInt32Le(out var rawDetailValue0)) return false;
            if (!reader.TryReadUInt32Le(out var rawDetailValue1)) return false;
            detailValue0 = unchecked((uint)rawDetailValue0);
            detailValue1 = unchecked((uint)rawDetailValue1);
        }

        result = new Packet2C38ResultObservation(resultIndex, stateCode, instanceSequenceId, resultCode, detailEntityId, detailValue0, detailValue1);
        return true;
    }
}
