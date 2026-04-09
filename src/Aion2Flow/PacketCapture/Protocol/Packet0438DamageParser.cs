using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet0438Damage(
    int TargetId,
    int LayoutTag,
    int Flag,
    int SourceId,
    int SkillCodeRaw,
    int Marker,
    int Type,
    DamageModifiers Modifiers,
    int Unknown,
    int Damage,
    int Loop,
    int TailLength,
    int TailMultiHitCount);

internal static class Packet0438DamageParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet0438Damage result)
    {
        result = default;

        if (packet.IsEmpty || packet[0] == 0x20)
        {
            return false;
        }

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out var length)) return false;
        if (length <= 3 || length > packet.Length + 3) return false;
        if (reader.Remaining < 2) return false;
        return TryParsePayload(packet[reader.Offset..], out result, out _);
    }

    public static bool TryParsePayload(ReadOnlySpan<byte> payload, out Packet0438Damage result, out int consumed)
    {
        result = default;
        consumed = 0;

        if (payload.Length < 2 || payload[0] != 0x04 || payload[1] != 0x38)
        {
            return false;
        }

        var reader = new PacketSpanReader(payload);
        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var targetId)) return false;
        if (!reader.TryReadVarInt(out var layoutTag)) return false;
        if (!reader.TryReadVarInt(out var flag)) return false;
        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (targetId == 0 || sourceId == 0) return false;
        if (reader.Remaining < 5) return false;
        if (!reader.TryReadUInt32Le(out var skillCodeRaw)) return false;
        if (!reader.TryReadByte(out var marker)) return false;
        if (!reader.TryReadVarInt(out var type)) return false;

        if (!Packet0438Layout.TryGetSpecialsLength(layoutTag, out var specialsLength) || reader.Remaining < specialsLength)
        {
            return false;
        }

        var specials = ParseDamageModifiers(payload.Slice(reader.Offset, specialsLength), type);
        if (!reader.TryAdvance(specialsLength)) return false;

        if (!reader.TryReadVarInt(out var unknown)) return false;
        if (!reader.TryReadVarInt(out var damage)) return false;
        if (!reader.TryReadVarInt(out var loop)) return false;

        consumed = reader.Offset;
        var tail = payload[consumed..];
        result = new Packet0438Damage(
            targetId,
            layoutTag,
            flag,
            sourceId,
            skillCodeRaw,
            marker,
            type,
            specials,
            unknown,
            damage,
            loop,
            payload.Length - consumed,
            ParseTailMultiHitCount(tail));
        return true;
    }

    private static int ParseTailMultiHitCount(ReadOnlySpan<byte> tail)
    {
        if (tail.Length < 5)
        {
            return 0;
        }

        var count = tail[0];
        if (count <= 0 || count > 16)
        {
            return 0;
        }

        var expectedLength = (count * 2) + 3;
        if (tail.Length != expectedLength || tail[^2] != 0x01 || tail[^1] != 0x00)
        {
            return 0;
        }

        for (var i = 0; i < count; i++)
        {
            var amountOffset = 1 + (i * 2);
            var amount = tail[amountOffset] | (tail[amountOffset + 1] << 8);
            if (amount <= 0)
            {
                return 0;
            }
        }

        return count;
    }

    private static DamageModifiers ParseDamageModifiers(ReadOnlySpan<byte> packet, int type)
    {
        DamageModifiers modifiers = DamageModifiers.None;
        if (type == 3)
        {
            modifiers |= DamageModifiers.Critical;
        }

        if (packet.Length == 8) return modifiers;
        if (packet.Length < 10) return modifiers;

        var flagByte = packet[0];
        var hasBlock = (flagByte & 0x02) != 0;
        var hasParry = (flagByte & 0x04) != 0;
        var hasPerfect = (flagByte & 0x08) != 0;
        var hasDefensivePerfect = (flagByte & 0x80) != 0 && (hasBlock || hasParry);

        if ((flagByte & 0x01) != 0) modifiers |= DamageModifiers.Back;
        if (hasBlock) modifiers |= DamageModifiers.Block;
        if (hasParry) modifiers |= DamageModifiers.Parry;
        if (hasPerfect || hasDefensivePerfect) modifiers |= DamageModifiers.Perfect;
        if ((flagByte & 0x10) != 0) modifiers |= DamageModifiers.Smite;
        if ((flagByte & 0x20) != 0) modifiers |= DamageModifiers.Endurance;
        if ((flagByte & 0x40) != 0) modifiers |= DamageModifiers.Regeneration;
        if ((flagByte & 0x80) != 0 && !hasDefensivePerfect) modifiers |= DamageModifiers.DefensivePerfect;
        return modifiers;
    }
}
