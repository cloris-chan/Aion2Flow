using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet0538PeriodicValue(int TargetId, int Mode, int SourceId, int Unknown, ResourceEffectRef BodyResourceEffectRef, int Damage, int TailLength, int TailRaw, int TailSkillCodeRaw, int TailPrefixValue)
{
    public bool IsLinkRecord => Mode == 48;

    public int LinkId => IsLinkRecord ? Damage : 0;
}

internal static class Packet0538PeriodicValueParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet0538PeriodicValue result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out var length)) return false;
        if (length <= 3 || length > packet.Length + 3) return false;
        if (reader.Remaining < 2) return false;
        var payloadLength = length - 3 - reader.Offset;
        if (payloadLength < 2 || payloadLength > reader.Remaining) return false;
        return TryParsePayload(packet.Slice(reader.Offset, payloadLength), out result);
    }

    internal static bool TryParsePayload(ReadOnlySpan<byte> payload, out Packet0538PeriodicValue result)
    {
        result = default;

        if (payload.Length < 2 || payload[0] != 0x05 || payload[1] != 0x38)
        {
            return false;
        }

        var reader = new PacketSpanReader(payload);
        if (!reader.TryAdvance(2)) return false;

        if (!reader.TryReadVarInt(out var targetId)) return false;
        if (!reader.TryReadVarInt(out var mode)) return false;
        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (targetId <= 0 || sourceId <= 0) return false;
        if (!reader.TryReadVarInt(out var unknown)) return false;
        if (!reader.TryReadUInt32Le(out var bodyResourceEffectRefRaw)) return false;
        if (!reader.TryReadVarInt(out var damage)) return false;
        var tailLength = reader.Remaining;
        var tailRaw = 0;
        var tailSkillCodeRaw = 0;
        var tailPrefixValue = 0;
        if (tailLength is > 0 and < 4)
        {
            var tailPrefixReader = new PacketSpanReader(reader.RemainingSpan);
            if (tailPrefixReader.TryReadVarInt(out var parsedTailPrefixValue) &&
                tailPrefixReader.Remaining == 0 &&
                parsedTailPrefixValue > 0)
            {
                tailPrefixValue = parsedTailPrefixValue;
            }
        }
        else if (tailLength == 4)
        {
            var tail = reader.RemainingSpan;
            var tailSkillReader = new PacketSpanReader(tail[^4..]);
            if (!tailSkillReader.TryReadUInt32Le(out tailSkillCodeRaw))
            {
                return false;
            }

            tailRaw = tailSkillCodeRaw;
        }
        else if (tailLength > 4)
        {
            var tail = reader.RemainingSpan;
            var tailPrefixReader = new PacketSpanReader(tail[..^4]);
            if (tailPrefixReader.TryReadVarInt(out var parsedTailPrefixValue) &&
                tailPrefixReader.Remaining == 0)
            {
                var tailSkillReader = new PacketSpanReader(tail[^4..]);
                if (!tailSkillReader.TryReadUInt32Le(out tailSkillCodeRaw))
                {
                    return false;
                }

                tailPrefixValue = parsedTailPrefixValue;
            }
        }

        result = new Packet0538PeriodicValue(targetId, mode, sourceId, unknown, ResourceEffectRef.FromRaw(bodyResourceEffectRefRaw), damage, tailLength, tailRaw, tailSkillCodeRaw, tailPrefixValue);
        return true;
    }

    internal static string FormatEffectLabel(int targetId, int sourceId, int mode)
    {
        if (targetId == sourceId)
        {
            return mode switch
            {
                1 => "periodic-self-initial",
                3 => "periodic-self-tick",
                _ => $"periodic-self-mode-{mode}"
            };
        }

        return mode switch
        {
            1 => "periodic-target-initial",
            2 => "periodic-target-tick",
            3 => "periodic-target-tick",
            _ => $"periodic-target-mode-{mode}"
        };
    }
}
