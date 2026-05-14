using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Protocol.Packets;
using Cloris.Aion2Flow.Protocol.Readers;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketEmbeddedPacketScanner
{
    public static bool ScanForKnownPackets(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        var parsed = false;

        for (var offset = 0; offset + 1 < packet.Length; offset++)
        {
            int consumed;
            if (packet[offset] == 0x04 && packet[offset + 1] == 0x38)
            {
                if (PacketCombatHandler.TryParseDamageAt(packet, offset, ref context, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
            else if (packet[offset] == 0x05 && packet[offset + 1] == 0x38)
            {
                if (PacketCombatHandler.TryParsePeriodicValuePacketAt(packet, offset, ref context, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
            else if (packet[offset] == 0x40 && packet[offset + 1] == 0x36)
            {
                if (TryParseSummonPacketAt(packet, offset, ref context, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
            else if (packet[offset] == 0x33 && packet[offset + 1] == 0x36)
            {
                if (TryParseOwnNicknameAt(packet, offset, ref context, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
            else if (packet[offset] == 0x04 && packet[offset + 1] == 0x8d)
            {
                if (TryParseNicknameAt(packet, offset, ref context, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
        }

        PacketEmbeddedNicknameScanner.Scan(packet, ref context);

        return parsed;
    }

    public static bool TryParseRemainHpAt(ReadOnlySpan<byte> packet, int opcodeOffset, ref PacketParseContext context, out int consumed)
    {
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (payload.Length < 8 || payload[0] != 0x00 || payload[1] != 0x8d)
        {
            return false;
        }

        var reader = new PacketSpanReader(payload);
        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var npcId)) return false;
        if (npcId == 0) return false;

        if (!reader.TryReadVarInt(out var value0)) return false;
        if (!reader.TryReadVarInt(out var value1)) return false;
        if (!reader.TryReadVarInt(out var value2)) return false;
        if (!reader.TryReadUInt32Le(out var npcHp)) return false;

        if (value0 == 2 && value1 == 1 && value2 == 0)
        {
            context.Sink.AppendNpcHp(npcId, checked(npcHp), context.TimestampMilliseconds);
        }
        consumed = reader.Offset;
        var eventName = value0 == 2 && value1 == 1 && value2 == 0 ? "remain-hp" : "entity-value-008d";
        RawPacketDump.AppendFrameEvent(eventName, context.Connection, $"npcId={npcId}|value0={value0}|value1={value1}|value2={value2}|value={npcHp}", payload[..consumed]);
        return context.MarkParsed();
    }

    public static bool TryParseBattleToggleAt(ReadOnlySpan<byte> packet, int opcodeOffset, ref PacketParseContext context, out int consumed)
    {
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (payload.Length < 3 || payload[0] != 0x21 || payload[1] != 0x8d)
        {
            return false;
        }

        var reader = new PacketSpanReader(payload);
        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var npcId)) return false;
        if (npcId == 0) return false;

        bool? isActive = null;
        if (reader.Remaining >= 2 &&
            payload[reader.Offset] == 0x00 &&
            payload[reader.Offset + 1] is 0x00 or 0x01)
        {
            isActive = payload[reader.Offset + 1] == 0x01;
            if (!reader.TryAdvance(2)) return false;
        }

        if (isActive is bool active)
        {
            context.Sink.SetNpcBattle(npcId, active, context.TimestampMilliseconds);
        }
        else
        {
            context.Sink.ToggleNpcBattle(npcId);
        }
        consumed = reader.Offset;
        RawPacketDump.AppendFrameEvent("battle-toggle", context.Connection, $"npcId={npcId}{PacketDiagnosticFormatter.ActiveHint(isActive)}", payload[..consumed]);
        return context.MarkParsed();
    }

    private static bool TryParseNicknameAt(ReadOnlySpan<byte> packet, int opcodeOffset, ref PacketParseContext context, out int consumed)
    {
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (payload.Length < 11 || payload[0] != 0x04 || payload[1] != 0x8d)
        {
            return false;
        }

        if (!Packet048DNicknameParser.TryParsePayload(payload, out var parsed))
        {
            return false;
        }

        consumed = parsed.TailOffset;
        context.Sink.AppendNickname(parsed.PlayerId, parsed.Nickname, parsed.OriginServerId);
        RawPacketDump.AppendFrameEvent("nickname", context.Connection, $"playerId={parsed.PlayerId}|len={parsed.NicknameLength}{PacketDiagnosticFormatter.OriginServerHint(parsed.OriginServerId)}", payload[..consumed]);
        return context.MarkParsed();
    }

    private static bool TryParseOwnNicknameAt(ReadOnlySpan<byte> packet, int opcodeOffset, ref PacketParseContext context, out int consumed)
    {
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (!Packet3336NicknameParser.TryParsePayload(payload, out var parsed))
        {
            return false;
        }

        context.Sink.AppendNickname(parsed.PlayerId, parsed.Nickname, parsed.OriginServerId);
        context.Sink.MarkSceneArrival();
        consumed = parsed.TailOffset;
        RawPacketDump.AppendFrameEvent("nickname", context.Connection, $"playerId={parsed.PlayerId}|kind=own|len={parsed.NicknameLength}{PacketDiagnosticFormatter.OriginServerHint(parsed.OriginServerId)}|embedded=true", payload[..consumed]);
        return context.MarkParsed();
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

        int? npcCode = null;
        if (reader.TryReadUInt32Le(out var npcValue) && npcValue is >= 2_000_000 and <= 2_999_999)
        {
            npcCode = npcValue;
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
        var payloadLength = consumed > 0 ? consumed : payload.Length;
        var kind = Packet4036Descriptors.ClassifyKind(payloadLength);
        RawPacketDump.AppendFrameEvent("summon", context.Connection, $"kind={Packet4036Descriptors.FormatKind(kind, payloadLength)}|owner={realSourceId}|summon={summonId}{PacketDiagnosticFormatter.NpcCodeHint(npcCode)}", payload[..consumed]);
        return context.MarkParsed();
    }
}
