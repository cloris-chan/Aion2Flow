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
        var payloadLength = length - 3 - reader.Offset;
        if (payloadLength < 2 || payloadLength > reader.Remaining) return false;
        return TryParsePayload(packet.Slice(reader.Offset, payloadLength), out result, out _);
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

        if (unknown == 0 && damage == 10000)
        {
            damage = loop;
            loop = 0;
        }

        var multiHitCount = 0;
        var drainHealAmount = ParseTail(ref reader, layoutTag, loop, out var multiHitAmounts);
        if (Packet0438Layout.HasMultiHitData(layoutTag))
        {
            multiHitCount = Math.Max(1, multiHitAmounts);
        }

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

    private static int ParseTail(ref PacketSpanReader reader, int layoutTag, int loop, out int multiHitAmounts)
    {
        multiHitAmounts = 0;
        var origin = reader.RemainingSpan;

        if (!Packet0438Layout.HasMultiHitData(layoutTag))
        {
            return TryConsumeDrainSubTail(ref reader, origin, 0, out _);
        }

        var bestDrain = 0;
        var bestAmounts = 0;
        var bestConsumed = 0;
        var bestRank = -1;

        TryMultiHitInterpretation(origin, hasLeadingByte: true, leadingCount: -1, ref bestRank, ref bestDrain, ref bestAmounts, ref bestConsumed);
        TryMultiHitInterpretation(origin, hasLeadingByte: false, leadingCount: loop, ref bestRank, ref bestDrain, ref bestAmounts, ref bestConsumed);

        if (bestRank < 0)
        {
            return 0;
        }

        reader.TryAdvance(bestConsumed);
        multiHitAmounts = bestAmounts;
        return bestDrain;
    }

    private static void TryMultiHitInterpretation(
        ReadOnlySpan<byte> tail,
        bool hasLeadingByte,
        int leadingCount,
        ref int bestRank,
        ref int bestDrain,
        ref int bestAmounts,
        ref int bestConsumed)
    {
        if (tail.IsEmpty)
        {
            return;
        }

        var probe = new PacketSpanReader(tail);
        int amountCount;
        if (hasLeadingByte)
        {
            if (!probe.TryReadByte(out var raw)) return;
            amountCount = raw;
        }
        else
        {
            amountCount = leadingCount;
        }

        if (amountCount < 0 || amountCount > 16) return;

        for (var i = 0; i < amountCount; i++)
        {
            if (!probe.TryReadVarInt(out var amount) || amount <= 0) return;
        }

        var subTailDrain = TryConsumeDrainSubTailFromSpan(tail, probe.Offset, out var subTailConsumed);
        var consumed = probe.Offset + subTailConsumed;
        var rank = (subTailDrain > 0 ? 2 : 0) + (consumed == tail.Length ? 1 : 0);
        if (rank <= bestRank) return;

        bestRank = rank;
        bestDrain = subTailDrain;
        bestAmounts = amountCount;
        bestConsumed = consumed;
    }

    private static int TryConsumeDrainSubTail(ref PacketSpanReader reader, ReadOnlySpan<byte> origin, int offset, out int consumed)
    {
        var drain = TryConsumeDrainSubTailFromSpan(origin, offset, out consumed);
        if (drain > 0)
        {
            reader.TryAdvance(consumed);
        }
        return drain;
    }

    private static int TryConsumeDrainSubTailFromSpan(ReadOnlySpan<byte> origin, int offset, out int consumed)
    {
        consumed = 0;
        if (offset > origin.Length) return 0;
        var span = origin[offset..];
        if (span.IsEmpty) return 0;

        var reader = new PacketSpanReader(span);
        if (!reader.TryReadVarInt(out var count) || count < 0) return 0;

        if (count > 0)
        {
            if (!reader.TryReadVarInt(out _)) return 0;
        }

        if (!reader.TryReadVarInt(out var drain) || drain <= 0) return 0;

        if (!IsPayloadBoundary(span[reader.Offset..])) return 0;

        consumed = reader.Offset;
        return drain;
    }

    private static bool IsPayloadBoundary(ReadOnlySpan<byte> remaining)
    {
        if (remaining.IsEmpty)
        {
            return true;
        }

        var reader = new PacketSpanReader(remaining);
        if (!reader.TryReadVarInt(out var length) || length <= 3 || length > remaining.Length + 3)
        {
            return false;
        }

        return reader.Remaining >= 2;
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
        if ((flagByte & 0x01) != 0) modifiers |= DamageModifiers.Back;
        if ((flagByte & 0x02) != 0) modifiers |= DamageModifiers.Block;
        if ((flagByte & 0x04) != 0) modifiers |= DamageModifiers.Parry;
        if ((flagByte & 0x08) != 0) modifiers |= DamageModifiers.Perfect;
        if ((flagByte & 0x10) != 0) modifiers |= DamageModifiers.Smite;
        if ((flagByte & 0x20) != 0) modifiers |= DamageModifiers.Endurance;
        if ((flagByte & 0x40) != 0) modifiers |= DamageModifiers.Regeneration;
        if ((flagByte & 0x80) != 0) modifiers |= DamageModifiers.DefensivePerfect;
        return modifiers;
    }
}
