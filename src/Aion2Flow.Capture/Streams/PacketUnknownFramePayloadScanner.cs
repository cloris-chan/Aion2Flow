using Cloris.Aion2Flow.Protocol.Readers;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketUnknownFramePayloadScanner
{
    public static bool Scan(ReadOnlySpan<byte> payload, ref PacketParseContext context)
    {
        var parsed = false;

        for (var offset = 0; offset + 1 < payload.Length; offset++)
        {
            int consumed;
            if (payload[offset] == 0x04 && payload[offset + 1] == 0x38)
            {
                if (PacketCombatHandler.TryParseDamageAt(payload, offset, ref context, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
            else if (payload[offset] == 0x05 && payload[offset + 1] == 0x38)
            {
                if (PacketCombatHandler.TryParsePeriodicValuePacketAt(payload, offset, ref context, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
            else if (payload[offset] == 0x40 && payload[offset + 1] == 0x36)
            {
                if (TryParseSummonPacketAt(payload, offset, ref context, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
        }

        return parsed;
    }

    private static bool TryParseSummonPacketAt(ReadOnlySpan<byte> packet, int opcodeOffset, ref PacketParseContext context, out int consumed)
    {
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (payload.Length < 2 || payload[0] != 0x40 || payload[1] != 0x36)
        {
            return false;
        }

        var reader = new PacketSpanReader(payload);
        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var summonId)) return false;
        if (!reader.TryAdvance(3)) return false;

        if (reader.TryReadUInt32Le(out var npcValue) && npcValue is >= 2_000_000 and <= 2_999_999)
        {
            context.Writer.ApplyNpcCatalog(summonId, npcValue);
            context.Sink.AppendNpcKind(summonId, NpcKind.Summon);
        }

        ReadOnlySpan<byte> keyPattern = [0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff];
        var keyIdx = payload.IndexOf(keyPattern);
        if (keyIdx == -1) return false;
        var afterPacket = payload[(keyIdx + 8)..];

        ReadOnlySpan<byte> opcodePattern = [0x07, 0x02, 0x06];
        var opcodeIdx = afterPacket.IndexOf(opcodePattern);
        if (opcodeIdx == -1) return false;

        var offset = keyIdx + opcodeIdx + 11;
        if (offset + 2 > payload.Length) return false;

        var realSourceId = (payload[offset] & 0xff) | ((payload[offset + 1] & 0xff) << 8);
        if (realSourceId == 0) return false;

        context.Sink.AppendSummon(realSourceId, summonId);
        consumed = Math.Max(offset + 2, reader.Offset);
        return context.MarkParsed();
    }
}
