using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal enum PacketScopeSignal : byte
{
    Current,
    Transition
}

internal static class PacketScopeSignalParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, ushort opcode, out PacketScopeSignal signal)
    {
        signal = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _) || reader.Remaining < 2)
        {
            return false;
        }

        if (packet[reader.Offset] != (byte)(opcode >> 8) ||
            packet[reader.Offset + 1] != (byte)opcode)
        {
            return false;
        }

        reader.TryAdvance(2);
        var body = packet[reader.Offset..];
        switch (opcode)
        {
            case 0x0061:
                if (body.Length != 21)
                {
                    return false;
                }

                signal = body[4] switch
                {
                    0x02 => PacketScopeSignal.Current,
                    0x03 or 0x04 => PacketScopeSignal.Transition,
                    _ => (PacketScopeSignal)0xff
                };
                return signal != (PacketScopeSignal)0xff;

            case 0x0161:
                if (body.Length != 7 ||
                    body[0] != 0 ||
                    body[5] != 1 ||
                    body[6] != 0)
                {
                    return false;
                }

                signal = PacketScopeSignal.Transition;
                return true;

            default:
                return false;
        }
    }
}
