using System.Buffers.Binary;
using System.Text;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal static class PurplePacketMapParser
{
    private static readonly UTF8Encoding NameEncoding = new(false, true);

    public static bool TryParse(ReadOnlySpan<byte> packet, out PacketMapEvent result)
    {
        result = default;
        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out var length) || (long)length + reader.Offset - sizeof(int) != packet.Length ||
            !reader.TryReadByte(out var opcode0) || !reader.TryReadByte(out var opcode1))
            return false;

        var body = reader.RemainingSpan;
        var opcode = (ushort)((opcode0 << 8) | opcode1);
        switch (opcode)
        {
            case 0x2136:
                if (!IsDestinationBody(body)) return false;
                result = new(PacketMapEventKind.Candidate, BinaryPrimitives.ReadUInt32LittleEndian(body[4..]));
                return true;
            case 0x2336:
                if (body.Length < 23 || body[..6].IndexOfAnyExcept((byte)0) >= 0) return false;
                var tail = new PacketSpanReader(body[22..]);
                if (!tail.TryReadVarInt(out var value) || value < 0 || tail.Remaining != 0) return false;
                result = new(PacketMapEventKind.Arrival, 0);
                return true;
            case 0x0140:
                // The extended status also occurs during same-map progress, before arrival.
                if (body.Length == 24 && body[4] <= 1 && body[14] == 1 && body[15] == 2 &&
                    (body[4] != 0 || body[5..13].IndexOfAnyExcept((byte)0) < 0))
                {
                    var candidate = BinaryPrimitives.ReadUInt32LittleEndian(body);
                    if (candidate == 0) return false;
                    result = new(PacketMapEventKind.Candidate, candidate);
                    return true;
                }
                if (body.Length != 15 || body[4..13].IndexOfAnyExcept((byte)0) >= 0 ||
                    body[13] != 1 || body[14] != 0)
                    return false;
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
        result = new(PacketMapEventKind.CurrentMap, mapId);
        return true;
    }

    private static bool IsDestinationBody(ReadOnlySpan<byte> body)
    {
        if (body.Length < 46 || BinaryPrimitives.ReadUInt32LittleEndian(body) == 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(body[4..]) == 0)
            return false;

        var name = new PacketSpanReader(body[44..]);
        if (!name.TryReadVarInt(out var nameLength) || nameLength < 0 ||
            nameLength != name.Remaining - 1 || body[^1] > 1)
            return false;
        try
        {
            _ = NameEncoding.GetCharCount(name.RemainingSpan[..nameLength]);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
