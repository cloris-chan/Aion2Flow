using System.Buffers.Binary;
using System.Text;
using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet2E92MapInstance(uint InstanceId, string ContentKey);

internal static class Packet2E92Parser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet2E92MapInstance result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x2E || packet[reader.Offset + 1] != 0x92) return false;
        reader.TryAdvance(2);

        var body = packet[reader.Offset..];
        if (body.Length < 5) return false;

        var instanceId = BinaryPrimitives.ReadUInt32LittleEndian(body[..4]);
        int strLength = body[4];
        if (strLength <= 0 || strLength > 64 || 5 + strLength > body.Length) return false;

        var keySpan = body.Slice(5, strLength);
        for (var i = 0; i < keySpan.Length; i++)
        {
            var b = keySpan[i];
            if (b < 0x20 || b > 0x7E) return false;
        }

        result = new Packet2E92MapInstance(instanceId, Encoding.ASCII.GetString(keySpan));
        return true;
    }
}
