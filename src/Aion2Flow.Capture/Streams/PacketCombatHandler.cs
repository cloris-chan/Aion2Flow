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
            var combatPacket = new ParsedCombatPacket
            {
                TargetId = parsed.TargetId,
                LayoutTag = parsed.LayoutTag,
                Flag = parsed.Flag,
                SourceId = parsed.SourceId,
                OriginalSkillCode = parsed.SkillCodeRaw,
                SkillCode = parsed.SkillCodeRaw,
                Marker = parsed.Marker,
                Type = parsed.Type,
                Modifiers = parsed.Modifiers,
                Unknown = parsed.Unknown,
                Damage = parsed.Damage,
                Loop = parsed.Loop,
                DrainHealAmount = parsed.DrainHealAmount,
                RegenerationAmount = parsed.RegenerationAmount,
                DetailRaw = parsed.DetailRaw,
                ResourceKind = parsed.ResourceKind,
                Timestamp = context.TimestampMilliseconds,
                FrameOrdinal = frameOrdinal,
                BatchOrdinal = batchOrdinal
            };

            if (parsed.TailMultiHitCount > 0)
            {
                combatPacket.MultiHitCount = parsed.TailMultiHitCount;
                combatPacket.Modifiers |= DamageModifiers.MultiHit;
            }

            context.Sink.AppendCombatPacket(combatPacket);

            if (parsed.RegenerationAmount > 0 && ShouldStoreRegenerationHealing(parsed.TargetId, context.Sink))
            {
                var regenPacket = new ParsedCombatPacket
                {
                    TargetId = parsed.TargetId,
                    SourceId = parsed.TargetId,
                    OriginalSkillCode = parsed.SkillCodeRaw,
                    SkillCode = parsed.SkillCodeRaw,
                    Damage = parsed.RegenerationAmount,
                    EventKind = CombatEventKind.Healing,
                    ValueKind = CombatValueKind.Healing,
                    Timestamp = context.TimestampMilliseconds,
                    FrameOrdinal = frameOrdinal,
                    BatchOrdinal = batchOrdinal
                };
                regenPacket.SetEffectTag(PacketEffectTag.RegenerationHealing);
                context.Sink.AppendCombatPacket(regenPacket);
            }

            if (ShouldStoreDrainHealing(parsed))
            {
                context.Sink.AppendCombatPacket(new ParsedCombatPacket
                {
                    TargetId = parsed.SourceId,
                    SourceId = parsed.SourceId,
                    OriginalSkillCode = parsed.SkillCodeRaw,
                    SkillCode = parsed.SkillCodeRaw,
                    Damage = parsed.DrainHealAmount,
                    DrainHealAmount = parsed.DrainHealAmount,
                    Timestamp = context.TimestampMilliseconds,
                    FrameOrdinal = frameOrdinal,
                    BatchOrdinal = batchOrdinal
                });
            }

            RawPacketDump.AppendFrameEvent("damage", context.Connection, $"target={parsed.TargetId}|source={parsed.SourceId}|skillRaw={parsed.SkillCodeRaw}|damage={parsed.Damage}{PacketDiagnosticFormatter.ResolvedCombatHint(combatPacket)}", packet[..(packet.Length - parsed.TailLength)]);
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
            RawPacketDump.AppendFrameEvent(
                "compact-value",
                context.Connection,
                $"target={compact.TargetId}|source={compact.SourceId}|switch={compact.LayoutTag}|flag={compact.Flag}|marker={compact.Marker}|type={compact.Type}|skillRaw={compact.SkillCodeRaw}|unknown={compact.Unknown}|value={compact.Value}|loop={compact.Loop}|tailLen={compact.TailLength}|tailRaw={compact.TailRaw}{PacketDiagnosticFormatter.ResolvedSkillHint(compact.SkillCodeRaw)}{PacketDiagnosticFormatter.ResolvedReferenceHint("tailSkill", compact.TailRaw)}",
                packet[..(packet.Length - compact.TailLength)]);
            return context.MarkParsed();
        }

        if (!Packet0438CompactOutcomeParser.TryParse(packet, out var compactOutcome))
        {
            return false;
        }

        context.Sink.RegisterCompactValue0438(
            compactOutcome.TargetId,
            compactOutcome.SourceId,
            compactOutcome.SkillCodeRaw,
            compactOutcome.Marker,
            compactOutcome.LayoutTag,
            compactOutcome.Type,
            context.TimestampMilliseconds,
            frameOrdinal,
            batchOrdinal);
        RawPacketDump.AppendFrameEvent(
            "compact-outcome",
            context.Connection,
            $"target={compactOutcome.TargetId}|source={compactOutcome.SourceId}|layout={compactOutcome.LayoutTag}|flag={compactOutcome.Flag}|marker={compactOutcome.Marker}|type={compactOutcome.Type}|skillRaw={compactOutcome.SkillCodeRaw}|tailLen={compactOutcome.TailLength}{PacketDiagnosticFormatter.SkillHint((uint)compactOutcome.SkillCodeRaw)}",
            packet);
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
            context.Sink.RegisterPeriodicLink0538(
                parsed.TargetId,
                parsed.SourceId,
                parsed.LinkId,
                parsed.Unknown,
                parsed.TailRaw,
                context.TimestampMilliseconds,
                frameOrdinal,
                batchOrdinal);

            RawPacketDump.AppendFrameEvent(
                "periodic-link",
                context.Connection,
                $"target={parsed.TargetId}|source={parsed.SourceId}|mode={parsed.Mode}|skillRaw={parsed.SkillCodeRaw}|linkId={parsed.LinkId}|unknown={parsed.Unknown}|tailRaw={parsed.TailRaw}|effect={Packet0538PeriodicValueParser.FormatEffectLabel(parsed.TargetId, parsed.SourceId, parsed.Mode)}{PacketDiagnosticFormatter.ResolvedReferenceHint("tailSkill", parsed.TailRaw)}",
                packet);
            return context.MarkParsed();
        }

        var combatPacket = new ParsedCombatPacket
        {
            TargetId = parsed.TargetId,
            SourceId = parsed.SourceId,
            OriginalSkillCode = parsed.SkillCodeRaw,
            SkillCode = parsed.NormalizedSkillCode,
            Unknown = parsed.Unknown,
            Damage = parsed.Damage,
            Timestamp = context.TimestampMilliseconds,
            FrameOrdinal = frameOrdinal,
            BatchOrdinal = batchOrdinal
        };
        combatPacket.SetPeriodicEffect(
            parsed.TargetId == parsed.SourceId ? PeriodicEffectRelation.Self : PeriodicEffectRelation.Target,
            parsed.Mode);

        context.Sink.AppendCombatPacket(combatPacket);
        RawPacketDump.AppendFrameEvent("periodic", context.Connection, $"target={parsed.TargetId}|source={parsed.SourceId}|mode={parsed.Mode}|skillRaw={parsed.SkillCodeRaw}|unknown={parsed.Unknown}|damage={parsed.Damage}{PacketDiagnosticFormatter.PeriodicTailHint(parsed)}{PacketDiagnosticFormatter.EffectHint(combatPacket)}{PacketDiagnosticFormatter.ResolvedCombatHint(combatPacket)}", packet);
        return context.MarkParsed();
    }

    public static bool ParseCompactControl0238Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet0238CompactControlParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.RegisterCompactControl0238(parsed.SourceId, parsed.SkillCodeRaw, parsed.Marker, context.BatchOrdinal);
        RawPacketDump.AppendFrameEvent(
            "compact-0238",
            context.Connection,
            $"source={parsed.SourceId}|mode={parsed.Mode}|skillRaw={parsed.SkillCodeRaw}|marker={parsed.Marker}|flag={parsed.Flag}|echoSource={parsed.EchoSourceId}|zero=0x{parsed.ZeroValue:x8}|tailValue=0x{parsed.TailValue:x8}{PacketDiagnosticFormatter.SkillHint((uint)parsed.SkillCodeRaw)}",
            packet);
        return context.MarkParsed();
    }

    public static bool ParseCompactControl0638Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet0638CompactControlParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.RegisterCompactControl0638(parsed.SourceId, parsed.SkillCodeRaw, parsed.Marker, context.TimestampMilliseconds, context.FrameOrdinal, context.BatchOrdinal);
        RawPacketDump.AppendFrameEvent(
            "compact-0638",
            context.Connection,
            $"source={parsed.SourceId}|skillRaw={parsed.SkillCodeRaw}|marker={parsed.Marker}|flag={parsed.Flag}{PacketDiagnosticFormatter.SkillHint((uint)parsed.SkillCodeRaw)}",
            packet);
        return context.MarkParsed();
    }

    public static bool Parse3538SidecarPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet3538SidecarParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        RawPacketDump.AppendFrameEvent(
            "sidecar-3538",
            context.Connection,
            $"target={parsed.TargetId}|state={parsed.State}|source={parsed.SourceId}",
            packet);
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

        var combatPacket = new ParsedCombatPacket
        {
            TargetId = parsed.TargetId,
            LayoutTag = parsed.LayoutTag,
            Flag = parsed.Flag,
            SourceId = parsed.SourceId,
            OriginalSkillCode = parsed.SkillCodeRaw,
            SkillCode = resolvedSkillCode,
            Marker = parsed.Marker,
            Type = parsed.Type,
            Modifiers = parsed.TailMultiHitCount > 0
                ? parsed.Modifiers | DamageModifiers.MultiHit
                : parsed.Modifiers,
            Unknown = parsed.Unknown,
            Damage = parsed.Damage,
            Loop = parsed.Loop,
            MultiHitCount = parsed.TailMultiHitCount,
            DrainHealAmount = parsed.DrainHealAmount,
            RegenerationAmount = parsed.RegenerationAmount,
            DetailRaw = parsed.DetailRaw,
            ResourceKind = parsed.ResourceKind,
            Timestamp = context.TimestampMilliseconds,
            FrameOrdinal = frameOrdinal,
            BatchOrdinal = batchOrdinal
        };

        context.Sink.AppendCombatPacket(combatPacket);

        if (parsed.RegenerationAmount > 0 && ShouldStoreRegenerationHealing(parsed.TargetId, context.Sink))
        {
            var regenPacket = new ParsedCombatPacket
            {
                TargetId = parsed.TargetId,
                SourceId = parsed.TargetId,
                OriginalSkillCode = parsed.SkillCodeRaw,
                SkillCode = resolvedSkillCode,
                Damage = parsed.RegenerationAmount,
                EventKind = CombatEventKind.Healing,
                ValueKind = CombatValueKind.Healing,
                Timestamp = context.TimestampMilliseconds,
                FrameOrdinal = frameOrdinal,
                BatchOrdinal = batchOrdinal
            };
            regenPacket.SetEffectTag(PacketEffectTag.RegenerationHealing);
            context.Sink.AppendCombatPacket(regenPacket);
        }

        if (ShouldStoreDrainHealing(parsed))
        {
            context.Sink.AppendCombatPacket(new ParsedCombatPacket
            {
                TargetId = parsed.SourceId,
                SourceId = parsed.SourceId,
                OriginalSkillCode = parsed.SkillCodeRaw,
                SkillCode = resolvedSkillCode,
                Damage = parsed.DrainHealAmount,
                DrainHealAmount = parsed.DrainHealAmount,
                Timestamp = context.TimestampMilliseconds,
                FrameOrdinal = frameOrdinal,
                BatchOrdinal = batchOrdinal
            });
        }

        RawPacketDump.AppendFrameEvent("damage", context.Connection, $"target={parsed.TargetId}|source={parsed.SourceId}|skillRaw={parsed.SkillCodeRaw}|damage={parsed.Damage}{PacketDiagnosticFormatter.ResolvedCombatHint(combatPacket)}", payload[..consumed]);
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
        if (sourceId == targetId)
        {
            return false;
        }
        if (!reader.TryReadVarInt(out var unknownInfo)) return false;
        if (!reader.TryReadUInt32Le(out var skillRaw)) return false;

        var resolvedSkillCode = ResolveSkillCode(skillRaw) ?? ResolveSkillCode(skillRaw / 100);
        if (resolvedSkillCode is null) return false;

        if (!reader.TryReadVarInt(out var damage)) return false;
        if (damage <= 0) return false;

        if (!context.Sink.IsKnownEntity(sourceId) && !context.Sink.IsKnownEntity(targetId))
        {
            return false;
        }

        var combatPacket = new ParsedCombatPacket
        {
            TargetId = targetId,
            SourceId = sourceId,
            OriginalSkillCode = skillRaw,
            SkillCode = resolvedSkillCode.Value,
            Unknown = unknownInfo,
            Damage = damage,
            Timestamp = context.TimestampMilliseconds,
            FrameOrdinal = frameOrdinal,
            BatchOrdinal = batchOrdinal
        };
        combatPacket.SetPeriodicEffect(PeriodicEffectRelation.Target, mode);

        context.Sink.AppendCombatPacket(combatPacket);

        consumed = reader.Offset;
        RawPacketDump.AppendFrameEvent("periodic", context.Connection, $"target={targetId}|source={sourceId}|skill={resolvedSkillCode.Value}|damage={damage}{PacketDiagnosticFormatter.EffectHint(combatPacket)}", payload[..consumed]);
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
}
