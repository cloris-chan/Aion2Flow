using System.Buffers.Binary;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal enum PacketMapEventSignal : byte
{
    Current = 0x02,
    TransitionAnnounced = 0x03,
    TransitionCountdown = 0x04
}

internal readonly record struct PacketMapEventState(uint MapId, PacketMapEventSignal Signal);

internal static class PacketMapEventParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out PacketMapEventState result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;

        var opcode0 = packet[reader.Offset];
        var opcode1 = packet[reader.Offset + 1];
        reader.TryAdvance(2);

        var body = packet[reader.Offset..];
        uint mapId;
        PacketMapEventSignal signal;
        switch (opcode0, opcode1)
        {
            case (0x00, 0x61):
                if (body.Length != 21)
                {
                    return false;
                }

                mapId = BinaryPrimitives.ReadUInt32LittleEndian(body[..4]);
                switch (body[4])
                {
                    case 0x02:
                        signal = PacketMapEventSignal.Current;
                        break;
                    case 0x03:
                        signal = PacketMapEventSignal.TransitionAnnounced;
                        break;
                    case 0x04:
                        signal = PacketMapEventSignal.TransitionCountdown;
                        break;
                    default:
                        return false;
                }

                break;
            case (0x01, 0x61):
                if (body.Length != 7 || body[0] != 0 || BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(5, 2)) != 1)
                {
                    return false;
                }

                mapId = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(1, 4));
                signal = PacketMapEventSignal.TransitionAnnounced;
                break;
            default:
                return false;
        }

        if (mapId == 0)
        {
            return false;
        }

        result = new PacketMapEventState(mapId, signal);
        return true;
    }
}
