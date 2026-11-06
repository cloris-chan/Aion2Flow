using System.Buffers.Binary;
using System.Text;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct SteamMapDestination(uint Sequence, uint MapId, ulong PositionLow, uint PositionHigh, int NameByteLength);
internal readonly record struct SteamMapArrival(ulong PositionLow, uint PositionHigh, uint Value, int TailValue);

internal static class SteamPacketMapParser
{
    private static readonly UTF8Encoding NameEncoding = new(false, true);

    public static bool TryParse(ReadOnlySpan<byte> packet, out PacketMapEvent result)
    {
        result = default;
        if (!TryReadBody(packet, out var opcode, out var body))
            return false;

        switch (opcode)
        {
            case 0x2136:
                if (!TryReadDestinationBody(body, out var destination)) return false;
                result = new(PacketMapEventKind.Candidate, destination.MapId);
                return true;
            case 0x2336:
                if (!TryReadArrivalBody(body, out _)) return false;
                result = new(PacketMapEventKind.Arrival, 0);
                return true;
            case 0x0140:
                // Phase zero also occurs during same-map progress; only phase one confirms a map.
                if (body.Length != 15 || body[13] != 1 || body[14] != 0) return false;
                break;
            case 0x0240:
                if (body.Length != 6 || body[4] != 0 || body[5] != 1) return false;
                break;
            case 0x2e92:
                if (!Packet2E92Parser.TryParse(packet, out var registration) || registration.InstanceId == 0) return false;
                result = new(PacketMapEventKind.InstanceRegistered, registration.InstanceId);
                return true;
            case 0x2f92:
                if (!Packet2F92Parser.TryParse(packet, out var instanceId)) return false;
                result = new(PacketMapEventKind.InstanceUnregistered, instanceId);
                return true;
            default:
                return false;
        }

        var mapId = BinaryPrimitives.ReadUInt32LittleEndian(body);
        if (mapId == 0) return false;
        result = new(PacketMapEventKind.Candidate, mapId);
        return true;
    }

    public static bool TryReadDestination(ReadOnlySpan<byte> packet, out SteamMapDestination result)
    {
        result = default;
        return TryReadBody(packet, out var opcode, out var body) && opcode == 0x2136 && TryReadDestinationBody(body, out result);
    }

    public static bool TryReadArrival(ReadOnlySpan<byte> packet, out SteamMapArrival result)
    {
        result = default;
        return TryReadBody(packet, out var opcode, out var body) && opcode == 0x2336 && TryReadArrivalBody(body, out result);
    }

    private static bool TryReadDestinationBody(ReadOnlySpan<byte> body, out SteamMapDestination result)
    {
        result = default;
        if (body.Length < 46) return false;
        var sequence = BinaryPrimitives.ReadUInt32LittleEndian(body);
        var mapId = BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
        if (sequence == 0 || mapId == 0) return false;

        var tail = new PacketSpanReader(body[44..]);
        if (!tail.TryReadVarInt(out var nameBytes) || nameBytes < 0 || nameBytes != tail.Remaining - 1 || body[^1] != 0)
            return false;
        try
        {
            _ = NameEncoding.GetCharCount(tail.RemainingSpan[..nameBytes]);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        result = new(sequence, mapId, BinaryPrimitives.ReadUInt64LittleEndian(body[16..]),
            BinaryPrimitives.ReadUInt32LittleEndian(body[24..]), nameBytes);
        return true;
    }

    private static bool TryReadArrivalBody(ReadOnlySpan<byte> body, out SteamMapArrival result)
    {
        result = default;
        if (body.Length < 23 || body[..6].IndexOfAnyExcept((byte)0) >= 0) return false;
        var tail = new PacketSpanReader(body[22..]);
        if (!tail.TryReadVarInt(out var tailValue) || tailValue < 0 || tail.Remaining != 0) return false;
        result = new(BinaryPrimitives.ReadUInt64LittleEndian(body[6..]), BinaryPrimitives.ReadUInt32LittleEndian(body[14..]),
            BinaryPrimitives.ReadUInt32LittleEndian(body[18..]), tailValue);
        return true;
    }

    private static bool TryReadBody(ReadOnlySpan<byte> packet, out ushort opcode, out ReadOnlySpan<byte> body)
    {
        opcode = 0;
        body = default;
        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out var length) || (long)length + reader.Offset - sizeof(int) != packet.Length ||
            !reader.TryReadByte(out var opcode0) || !reader.TryReadByte(out var opcode1))
            return false;
        opcode = (ushort)((opcode0 << 8) | opcode1);
        body = reader.RemainingSpan;
        return true;
    }
}
