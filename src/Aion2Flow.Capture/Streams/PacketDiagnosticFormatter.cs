using System.Text;
using Cloris.Aion2Flow.Protocol.Packets;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketDiagnosticFormatter
{
    public static string SkillHint(uint rawSkillCode)
    {
        if (rawSkillCode == 0 || rawSkillCode > int.MaxValue)
        {
            return string.Empty;
        }

        var variant = CombatResourceRegistry.ParseSkillVariant((int)rawSkillCode);
        var variantHint = SkillVariantHint(variant);
        var normalized = CombatResourceRegistry.InferOriginalSkillCode((int)rawSkillCode);
        if (!normalized.HasValue)
        {
            return $"|skillRaw={rawSkillCode}{variantHint}";
        }

        if (CombatResourceRegistry.SkillMap.TryGetValue(normalized.Value, out var skill))
        {
            return $"|skill={normalized.Value}{variantHint}|skillName={skill.Name}";
        }

        return $"|skill={normalized.Value}{variantHint}";
    }

    public static string ResolvedSkillHint(int skillCode)
    {
        if (skillCode <= 0)
        {
            return string.Empty;
        }

        var variant = CombatResourceRegistry.ParseSkillVariant(skillCode);
        var variantHint = SkillVariantHint(variant);
        var normalized = CombatResourceRegistry.InferOriginalSkillCode(skillCode) ?? skillCode;
        if (CombatResourceRegistry.SkillMap.TryGetValue(normalized, out var skill))
        {
            return $"|skill={normalized}{variantHint}|skillName={skill.Name}";
        }

        return $"|skill={normalized}{variantHint}";
    }

    public static string ResolvedReferenceHint(string prefix, int rawSkillCode)
    {
        if (rawSkillCode <= 0)
        {
            return string.Empty;
        }

        var normalized = CombatResourceRegistry.InferOriginalSkillCode(rawSkillCode) ?? rawSkillCode;
        if (CombatResourceRegistry.SkillMap.TryGetValue(normalized, out var skill))
        {
            return $"|{prefix}={normalized}|{prefix}Name={skill.Name}";
        }

        return $"|{prefix}={normalized}";
    }

    public static string OriginServerHint(int? originServerId)
        => originServerId is > 0 ? $"|originServer={originServerId.Value}" : string.Empty;

    public static string ActiveHint(bool? isActive)
        => isActive.HasValue ? $"|active={isActive.Value}" : string.Empty;

    public static string NpcCodeHint(int? npcCode)
        => npcCode is > 0 ? $"|npcCode={npcCode.Value}" : string.Empty;

    public static string NpcHpHint(int? currentHp, int? maxHp)
        => currentHp is int hp && maxHp is int hpMax ? $"|currentHp={hp}|maxHp={hpMax}" : string.Empty;

    public static string PeriodicTailHint(Packet0538PeriodicValue parsed)
        => parsed.TailLength > 0
            ? $"|tailLen={parsed.TailLength}|tailRaw={parsed.TailRaw}|tailSkillRaw={parsed.TailSkillCodeRaw}|tailPrefix={parsed.TailPrefixValue}{ResolvedReferenceHint("tailSkill", parsed.TailSkillCodeRaw)}"
            : string.Empty;

    public static string ResolvedCombatHint(ParsedCombatPacket packet)
    {
        var observation = packet.ToObservation();
        return ResolvedCombatHint(packet.SourceId, packet.TargetId, in observation);
    }

    public static string ResolvedCombatHint(int sourceId, int targetId, in CombatObservation observation)
    {
        var skillCode = observation.SkillCode > 0 ? observation.SkillCode : observation.OriginalSkillCode;
        if (skillCode <= 0)
        {
            return string.Empty;
        }

        var normalized = CombatResourceRegistry.InferOriginalSkillCode(skillCode) ?? skillCode;
        var classificationObservation = observation with
        {
            SkillCode = normalized,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Unknown
        };
        var valueKind = CombatEventClassifier.Classify(sourceId, targetId, in classificationObservation).ValueKind;
        var variant = CombatResourceRegistry.ParseSkillVariant(observation.OriginalSkillCode != 0 ? observation.OriginalSkillCode : observation.SkillCode);
        var variantHint = SkillVariantHint(variant);

        if (CombatResourceRegistry.SkillMap.TryGetValue(normalized, out var skill))
        {
            return $"|skill={normalized}{variantHint}|skillName={skill.Name}|valueKind={valueKind}";
        }

        return $"|skill={normalized}{variantHint}|valueKind={valueKind}";
    }

    public static string EffectHint(ParsedCombatPacket packet)
    {
        var observation = packet.ToObservation();
        return EffectHint(in observation);
    }

    public static string EffectHint(in CombatObservation observation)
    {
        var effectLabel = CombatObservationTraits.FormatEffectLabel(in observation);
        return string.IsNullOrEmpty(effectLabel)
            ? string.Empty
            : $"|effect={effectLabel}";
    }

    private static string SkillVariantHint(SkillVariantInfo variant)
    {
        if (variant.OriginalSkillCode <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        if (variant.BaseSkillCode > 0)
        {
            builder.Append("|baseSkill=").Append(variant.BaseSkillCode);
        }

        builder.Append("|charge=").Append(variant.ChargeStage);

        if (variant.SpecializationMask != 0)
        {
            builder.Append("|specs=").Append(FormatSpecializationMask(variant.SpecializationMask));
        }

        return builder.ToString();
    }

    private static string FormatSpecializationMask(int mask)
    {
        if (mask == 0)
        {
            return "-";
        }

        Span<int> digits = stackalloc int[5];
        var count = 0;
        for (var i = 0; i < 5; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                digits[count++] = i + 1;
            }
        }

        if (count == 0)
        {
            return "-";
        }

        var builder = new StringBuilder();
        builder.Append(digits[0]);
        for (var i = 1; i < count; i++)
        {
            builder.Append('+').Append(digits[i]);
        }

        return builder.ToString();
    }
}
