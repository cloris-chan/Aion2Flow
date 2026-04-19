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
    int TailMultiHitCount,
    int DrainHealAmount = 0,
    int RegenerationAmount = 0,
    long DetailRaw = 0,
    CombatResourceKind ResourceKind = CombatResourceKind.Unknown);

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

        if (!Packet0438Layout.TryGetDetailLength(layoutTag, out var detailLength) || reader.Remaining < detailLength)
        {
            return false;
        }

        var detailSlice = payload.Slice(reader.Offset, detailLength);
        var modifiers = ParseDamageModifiers(detailSlice, type);
        var (regenAmount, detailRaw) = ExtractRegenerationAmount(detailSlice, modifiers);
        var resourceKind = ExtractResourceKind(detailSlice);
        if (!reader.TryAdvance(detailLength)) return false;

        if (!reader.TryReadVarInt(out var unknown)) return false;
        if (!reader.TryReadVarInt(out var damage)) return false;
        if (!reader.TryReadVarInt(out var loop)) return false;

        var multiHitCount = 0;
        if (Packet0438Layout.HasMultiHitData(layoutTag))
        {
            var segmentCount = TryParseMultiHitSegment(ref reader);
            multiHitCount = Math.Max(1, segmentCount);
        }

        var drainHealAmount = TryParseDrainHealTail(ref reader);

        consumed = reader.Offset;
        result = new Packet0438Damage(
            targetId,
            layoutTag,
            flag,
            sourceId,
            skillCodeRaw,
            marker,
            type,
            modifiers,
            unknown,
            damage,
            loop,
            payload.Length - consumed,
            multiHitCount,
            drainHealAmount,
            regenAmount,
            detailRaw,
            resourceKind);
        return true;
    }

    private static int TryParseMultiHitSegment(ref PacketSpanReader reader)
    {
        var remaining = reader.RemainingSpan;
        if (remaining.Length < 3)
        {
            return 0;
        }

        var count = remaining[0];
        if (count == 0 || count > 16)
        {
            return 0;
        }

        var dataLength = 1 + (count * 2);
        if (remaining.Length < dataLength)
        {
            return 0;
        }

        for (var i = 0; i < count; i++)
        {
            var offset = 1 + (i * 2);
            var amount = remaining[offset] | (remaining[offset + 1] << 8);
            if (amount <= 0)
            {
                return 0;
            }
        }

        reader.TryAdvance(dataLength);
        return count;
    }

    private static int TryParseDrainHealTail(ref PacketSpanReader reader)
    {
        var remaining = reader.RemainingSpan;
        if (remaining.Length < 3)
        {
            return 0;
        }

        var tailReader = new PacketSpanReader(remaining);
        if (!tailReader.TryReadVarInt(out var count) || count <= 0)
        {
            return 0;
        }

        if (!tailReader.TryReadVarInt(out _))
        {
            return 0;
        }

        if (!tailReader.TryReadVarInt(out var drainValue) || drainValue <= 0)
        {
            return 0;
        }

        reader.TryAdvance(tailReader.Offset);
        return drainValue;
    }

    private static (int RegenAmount, long DetailRaw) ExtractRegenerationAmount(ReadOnlySpan<byte> detail, DamageModifiers modifiers)
    {
        long raw = 0;
        for (var i = 0; i < Math.Min(detail.Length, 8); i++)
            raw |= (long)detail[i] << (i * 8);

        if ((modifiers & DamageModifiers.Regeneration) == 0)
            return (0, raw);

        if (detail.Length > 1)
        {
            var reader = new PacketSpanReader(detail[1..]);
            if (reader.TryReadVarInt(out var val) && val > 0)
                return (val, raw);
        }

        return (0, raw);
    }

    private static CombatResourceKind ExtractResourceKind(ReadOnlySpan<byte> detail)
    {
        if (detail.Length != 8)
            return CombatResourceKind.Unknown;

        return detail[0] switch
        {
            0x57 => CombatResourceKind.Health,
            0x58 => CombatResourceKind.Mana,
            _ => CombatResourceKind.Unknown
        };
    }

    private static DamageModifiers ParseDamageModifiers(ReadOnlySpan<byte> detail, int type)
    {
        DamageModifiers modifiers = DamageModifiers.None;
        if (type == 3)
        {
            modifiers |= DamageModifiers.Critical;
        }

        if (detail.Length == 8) return modifiers;
        if (detail.Length < 10) return modifiers;

        var flagByte = detail[0];
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
