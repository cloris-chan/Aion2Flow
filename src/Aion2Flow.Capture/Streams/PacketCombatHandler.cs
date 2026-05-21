using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Protocol.Packets;
using Cloris.Aion2Flow.Protocol.Readers;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketCombatHandler
{
    public static bool Parse0438ValuePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        var frameOrdinal = context.FrameOrdinal;
        var batchOrdinal = context.BatchOrdinal;

        if (Packet0438DamageParser.TryParse(packet, out var parsed))
        {
            var observation = new CombatObservation
            {
                LayoutTag = parsed.LayoutTag,
                Flag = parsed.Flag,
                OriginalSkillCode = parsed.SkillCodeRaw,
                SkillCode = parsed.SkillCodeRaw,
                Marker = parsed.Marker,
                Type = parsed.Type,
                Modifiers = parsed.Modifiers,
                Damage = parsed.Damage,
                HitCount = 1,
                AttemptCount = 1,
                Loop = parsed.Loop,
                DrainHealAmount = parsed.DrainHealAmount,
                RegenerationAmount = parsed.RegenerationAmount,
                DetailRaw = parsed.DetailRaw,
                ResourceKind = parsed.ResourceKind,
                ChainId = parsed.Unknown
            };

            if (parsed.TailMultiHitCount > 0)
            {
                observation = observation with
                {
                    MultiHitCount = parsed.TailMultiHitCount,
                    Modifiers = observation.Modifiers | DamageModifiers.MultiHit
                };
            }

            context.Sink.AppendCombatObservation(parsed.SourceId, parsed.TargetId, context.TimestampMilliseconds, frameOrdinal, batchOrdinal, in observation, 0x0438, packet.Length);

            if (parsed.RegenerationAmount > 0 && ShouldStoreRegenerationHealing(parsed.TargetId, context.Sink))
            {
                var regenObservation = new CombatObservation
                {
                    OriginalSkillCode = parsed.SkillCodeRaw,
                    SkillCode = parsed.SkillCodeRaw,
                    Damage = parsed.RegenerationAmount,
                    HitCount = 1,
                    AttemptCount = 1,
                    EventKind = CombatEventKind.Healing,
                    ValueKind = CombatValueKind.Healing,
                    EffectTag = PacketEffectTag.RegenerationHealing
                };
                context.Sink.AppendCombatObservation(parsed.TargetId, parsed.TargetId, context.TimestampMilliseconds, frameOrdinal, batchOrdinal, in regenObservation, 0x0438, packet.Length);
            }

            if (ShouldStoreDrainHealing(parsed))
            {
                var drainObservation = new CombatObservation
                {
                    OriginalSkillCode = parsed.SkillCodeRaw,
                    SkillCode = parsed.SkillCodeRaw,
                    Damage = parsed.DrainHealAmount,
                    HitCount = 1,
                    AttemptCount = 1,
                    DrainHealAmount = parsed.DrainHealAmount
                };
                context.Sink.AppendCombatObservation(parsed.SourceId, parsed.SourceId, context.TimestampMilliseconds, frameOrdinal, batchOrdinal, in drainObservation, 0x0438, packet.Length);
            }

            return context.MarkParsed();
        }

        if (Packet0438CompactValueParser.TryParse(packet, out var compact))
        {
            context.Sink.RegisterCompactValue0438(
                compact.TargetId,
                compact.SourceId,
                compact.SkillCodeRaw,
                compact.Marker,
                compact.LayoutTag,
                compact.Type,
                compact.Value,
                context.TimestampMilliseconds,
                frameOrdinal,
                batchOrdinal);
            RawPacketDump.ObserveParsedPacket("compact-value", context.Connection);
            return context.MarkParsed();
        }

        if (!Packet0438CompactSignalParser.TryParse(packet, out var compactSignal))
        {
            return false;
        }

        context.Sink.RegisterCompactValue0438(
            compactSignal.TargetId,
            compactSignal.SourceId,
            compactSignal.SkillCodeRaw,
            compactSignal.Marker,
            compactSignal.LayoutTag,
            compactSignal.Type,
            context.TimestampMilliseconds,
            frameOrdinal,
            batchOrdinal);
        return context.MarkParsed();
    }

    public static bool ParsePeriodicValuePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        var frameOrdinal = context.FrameOrdinal;
        var batchOrdinal = context.BatchOrdinal;

        if (!Packet0538PeriodicValueParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        if (parsed.IsLinkRecord)
        {
            if (parsed.LinkId > 0 && parsed.TargetId > 0 && parsed.LinkId != parsed.TargetId)
            {
                context.Sink.RememberNpcObservationSource(parsed.TargetId);
                var invincibleObservation = new CombatObservation
                {
                    OriginalSkillCode = parsed.TailRaw,
                    SkillCode = parsed.TailRaw,
                    Damage = 0,
                    HitCount = 0,
                    AttemptCount = 1,
                    DetailRaw = parsed.LinkId,
                    Marker = parsed.Unknown,
                    Type = 48,
                    Modifiers = DamageModifiers.Invincible,
                    EventKind = CombatEventKind.Damage,
                    ValueKind = CombatValueKind.Damage,
                    EffectTag = PacketEffectTag.PeriodicLinkInvincible
                };
                context.Sink.AppendCombatObservation(parsed.LinkId, parsed.TargetId, context.TimestampMilliseconds, frameOrdinal, batchOrdinal, in invincibleObservation, 0x0538, packet.Length);
            }

            return context.MarkParsed();
        }

        if (IsActiveSkillInvincible(parsed.Mode, parsed.TargetId, parsed.SourceId, parsed.NormalizedSkillCode, parsed.Damage))
        {
            var invincibleObservation = new CombatObservation
            {
                OriginalSkillCode = parsed.SkillCodeRaw,
                SkillCode = parsed.NormalizedSkillCode,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 1,
                DetailRaw = parsed.Damage,
                Marker = parsed.Unknown,
                Type = parsed.Mode,
                Modifiers = DamageModifiers.Invincible,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage,
                EffectTag = PacketEffectTag.ActiveSkillInvincible
            };
            context.Sink.AppendCombatObservation(parsed.SourceId, parsed.TargetId, context.TimestampMilliseconds, frameOrdinal, batchOrdinal, in invincibleObservation, 0x0538, packet.Length);
            return context.MarkParsed();
        }

        var observation = new CombatObservation
        {
            OriginalSkillCode = parsed.SkillCodeRaw,
            SkillCode = parsed.NormalizedSkillCode,
            ChainId = parsed.Unknown,
            Damage = parsed.Damage,
            HitCount = 1,
            AttemptCount = 1,
            PeriodicRelation = parsed.TargetId == parsed.SourceId ? PeriodicEffectRelation.Self : PeriodicEffectRelation.Target,
            PeriodicMode = parsed.Mode
        };

        context.Sink.AppendCombatObservation(parsed.SourceId, parsed.TargetId, context.TimestampMilliseconds, frameOrdinal, batchOrdinal, in observation, 0x0538, packet.Length);
        return context.MarkParsed();
    }

    public static bool ParseCompactControl0238Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet0238CompactControlParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.RegisterCompactControl0238(parsed.SourceId, parsed.SkillCodeRaw, parsed.Marker, context.BatchOrdinal);
        RawPacketDump.ObserveParsedPacket("compact-0238", context.Connection);
        return context.MarkParsed();
    }

    public static bool ParseCompactControl0638Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet0638CompactControlParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.RegisterCompactControl0638(parsed.SourceId, parsed.SkillCodeRaw, parsed.Marker, context.TimestampMilliseconds, context.FrameOrdinal, context.BatchOrdinal);
        RawPacketDump.ObserveParsedPacket("compact-0638", context.Connection);
        return context.MarkParsed();
    }

    public static bool Parse3538SidecarPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet3538SidecarParser.TryParse(packet, out _))
        {
            return false;
        }

        return context.MarkParsed();
    }

    public static bool TryParseDamageAt(ReadOnlySpan<byte> packet, int opcodeOffset, ref PacketParseContext context, out int consumed)
    {
        var frameOrdinal = context.FrameOrdinal;
        var batchOrdinal = context.BatchOrdinal;
        var payload = packet[opcodeOffset..];
        if (!Packet0438DamageParser.TryParsePayload(payload, out var parsed, out consumed))
        {
            return false;
        }

        var resolvedSkillCode = ResolveSkillCode(parsed.SkillCodeRaw) ?? parsed.SkillCodeRaw;

        if (parsed.Damage <= 0) return false;

        if (!context.Sink.IsKnownEntity(parsed.SourceId) && !context.Sink.IsKnownEntity(parsed.TargetId))
        {
            return false;
        }

        var observation = new CombatObservation
        {
            LayoutTag = parsed.LayoutTag,
            Flag = parsed.Flag,
            OriginalSkillCode = parsed.SkillCodeRaw,
            SkillCode = resolvedSkillCode,
            Marker = parsed.Marker,
            Type = parsed.Type,
            Modifiers = parsed.TailMultiHitCount > 0
                ? parsed.Modifiers | DamageModifiers.MultiHit
                : parsed.Modifiers,
            ChainId = parsed.Unknown,
            Damage = parsed.Damage,
            HitCount = 1,
            AttemptCount = 1,
            Loop = parsed.Loop,
            MultiHitCount = parsed.TailMultiHitCount,
            DrainHealAmount = parsed.DrainHealAmount,
            RegenerationAmount = parsed.RegenerationAmount,
            DetailRaw = parsed.DetailRaw,
            ResourceKind = parsed.ResourceKind
        };

        context.Sink.AppendCombatObservation(parsed.SourceId, parsed.TargetId, context.TimestampMilliseconds, frameOrdinal, batchOrdinal, in observation, 0x0438, consumed);

        if (parsed.RegenerationAmount > 0 && ShouldStoreRegenerationHealing(parsed.TargetId, context.Sink))
        {
            var regenObservation = new CombatObservation
            {
                OriginalSkillCode = parsed.SkillCodeRaw,
                SkillCode = resolvedSkillCode,
                Damage = parsed.RegenerationAmount,
                HitCount = 1,
                AttemptCount = 1,
                EventKind = CombatEventKind.Healing,
                ValueKind = CombatValueKind.Healing,
                EffectTag = PacketEffectTag.RegenerationHealing
            };
            context.Sink.AppendCombatObservation(parsed.TargetId, parsed.TargetId, context.TimestampMilliseconds, frameOrdinal, batchOrdinal, in regenObservation, 0x0438, consumed);
        }

        if (ShouldStoreDrainHealing(parsed))
        {
            var drainObservation = new CombatObservation
            {
                OriginalSkillCode = parsed.SkillCodeRaw,
                SkillCode = resolvedSkillCode,
                Damage = parsed.DrainHealAmount,
                HitCount = 1,
                AttemptCount = 1,
                DrainHealAmount = parsed.DrainHealAmount
            };
            context.Sink.AppendCombatObservation(parsed.SourceId, parsed.SourceId, context.TimestampMilliseconds, frameOrdinal, batchOrdinal, in drainObservation, 0x0438, consumed);
        }

        return context.MarkParsed();
    }

    public static bool TryParsePeriodicValuePacketAt(ReadOnlySpan<byte> packet, int opcodeOffset, ref PacketParseContext context, out int consumed)
    {
        var frameOrdinal = context.FrameOrdinal;
        var batchOrdinal = context.BatchOrdinal;
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (payload.Length < 2 || payload[0] != 0x05 || payload[1] != 0x38)
        {
            return false;
        }

        var reader = new PacketSpanReader(payload);
        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var targetId)) return false;
        if (reader.Remaining < 1) return false;
        var mode = payload[reader.Offset];
        if (!reader.TryAdvance(1)) return false;
        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (sourceId == 0 || targetId == 0) return false;
        if (!reader.TryReadVarInt(out var unknownInfo)) return false;
        if (!reader.TryReadUInt32Le(out var skillRaw)) return false;

        var resolvedSkillCode = ResolveSkillCode(skillRaw) ?? ResolveSkillCode(skillRaw / 100);
        if (resolvedSkillCode is null) return false;

        if (!reader.TryReadVarInt(out var damage)) return false;
        if (damage <= 0) return false;

        if (sourceId == targetId && !IsActiveSkillInvincible(mode, targetId, sourceId, resolvedSkillCode.Value, damage))
        {
            return false;
        }

        if (!context.Sink.IsKnownEntity(sourceId) && !context.Sink.IsKnownEntity(targetId))
        {
            return false;
        }

        if (IsActiveSkillInvincible(mode, targetId, sourceId, resolvedSkillCode.Value, damage))
        {
            var invincibleObservation = new CombatObservation
            {
                OriginalSkillCode = skillRaw,
                SkillCode = resolvedSkillCode.Value,
                ChainId = unknownInfo,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 1,
                DetailRaw = damage,
                Type = mode,
                Modifiers = DamageModifiers.Invincible,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage,
                EffectTag = PacketEffectTag.ActiveSkillInvincible
            };
            consumed = reader.Offset;
            context.Sink.AppendCombatObservation(sourceId, targetId, context.TimestampMilliseconds, frameOrdinal, batchOrdinal, in invincibleObservation, 0x0538, consumed);
            return context.MarkParsed();
        }

        var observation = new CombatObservation
        {
            OriginalSkillCode = skillRaw,
            SkillCode = resolvedSkillCode.Value,
            ChainId = unknownInfo,
            Damage = damage,
            HitCount = 1,
            AttemptCount = 1,
            PeriodicRelation = PeriodicEffectRelation.Target,
            PeriodicMode = mode
        };

        consumed = reader.Offset;
        context.Sink.AppendCombatObservation(sourceId, targetId, context.TimestampMilliseconds, frameOrdinal, batchOrdinal, in observation, 0x0538, consumed);
        return context.MarkParsed();
    }

    private static int? ResolveSkillCode(int skillCode)
    {
        if (skillCode <= 0)
        {
            return null;
        }

        return CombatResourceRegistry.InferOriginalSkillCode(skillCode);
    }

    private static bool ShouldStoreRegenerationHealing(int targetId, IRuntimeObservationSink sink)
    {
        if (targetId <= 0)
        {
            return false;
        }

        if (sink.HasSummonOwner(targetId))
        {
            return false;
        }

        return !sink.TryGetNpcRuntimeState(targetId, out var state) || state.Kind != NpcKind.Summon;
    }

    private static bool ShouldStoreDrainHealing(Packet0438Damage parsed)
    {
        if (parsed.DrainHealAmount <= 0 || parsed.SourceId == parsed.TargetId)
        {
            return false;
        }

        return true;
    }

    private static bool IsActiveSkillInvincible(int mode, int targetId, int sourceId, int skillCode, int packetValue)
    {
        if (mode != 56 || targetId <= 0 || targetId != sourceId || packetValue <= 0)
        {
            return false;
        }

        return NormalizeSkillBase(skillCode) is 11380000 or 15410000 or 17270000;
    }

    private static int NormalizeSkillBase(int skillCode)
        => skillCode <= 0 ? 0 : skillCode - skillCode % 10000;
}
