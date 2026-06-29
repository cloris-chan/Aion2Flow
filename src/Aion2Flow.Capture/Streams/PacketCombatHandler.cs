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
        if (Packet0438DamageParser.TryParse(packet, out var parsed))
        {
            var observation = new CombatObservation
            {
                SkillCode = parsed.BodySkillVariantRaw,
                BodySkillVariantRaw = parsed.BodySkillVariantRaw,
                LayoutTag = parsed.LayoutTag,
                Flag = parsed.Flag,
                Marker = parsed.Marker,
                Type = parsed.Type,
                Modifiers = parsed.Modifiers,
                Damage = parsed.Damage,
                HitCount = 1,
                AttemptCount = 1,
                Loop = parsed.Loop,
                MultiHitCount = parsed.MultiHitCount,
                DrainHealAmount = parsed.DrainHealAmount,
                RegenerationAmount = parsed.RegenerationAmount,
                DetailRaw = parsed.DetailRaw,
                DetailResourceEffectRef = parsed.DetailResourceEffectRef,
                ResourceKind = parsed.ResourceKind,
                ChainId = parsed.Unknown
            };

            context.Sink.AppendCombatObservation(context.CreateObservationSource(0x0438, packet.Length), parsed.SourceId, parsed.TargetId, in observation);

            if (parsed.RegenerationAmount > 0 && ShouldStoreRegenerationHealing(parsed.TargetId, context.Sink))
            {
                var regenObservation = new CombatObservation
                {
                    SkillCode = parsed.BodySkillVariantRaw,
                    BodySkillVariantRaw = parsed.BodySkillVariantRaw,
                    Damage = parsed.RegenerationAmount,
                    HitCount = 1,
                    AttemptCount = 1,
                    DetailResourceEffectRef = parsed.DetailResourceEffectRef,
                    EventKind = CombatEventKind.Healing,
                    ValueKind = CombatValueKind.Healing,
                    EffectTag = PacketEffectTag.RegenerationHealing
                };
                context.Sink.AppendCombatObservation(context.CreateObservationSource(0x0438, packet.Length), parsed.TargetId, parsed.TargetId, in regenObservation);
            }

            if (ShouldStoreDrainHealing(parsed))
            {
                var drainObservation = new CombatObservation
                {
                    SkillCode = parsed.BodySkillVariantRaw,
                    BodySkillVariantRaw = parsed.BodySkillVariantRaw,
                    Damage = parsed.DrainHealAmount,
                    HitCount = 1,
                    AttemptCount = 1,
                    DrainHealAmount = parsed.DrainHealAmount,
                    DetailResourceEffectRef = parsed.DetailResourceEffectRef,
                    EventKind = CombatEventKind.Healing,
                    ValueKind = CombatValueKind.DrainHealing
                };
                context.Sink.AppendCombatObservation(context.CreateObservationSource(0x0438, packet.Length), parsed.SourceId, parsed.SourceId, in drainObservation);
            }

            return context.MarkParsed();
        }

        if (Packet0438ImplicitDetailSidecarParser.TryParse(packet, out var implicitDetailSidecar))
        {
            context.Sink.RegisterCompactValue0438(context.CreateObservationSource(0x0438, packet.Length), implicitDetailSidecar.TargetId, implicitDetailSidecar.SourceId, implicitDetailSidecar.BodySkillVariantRaw, implicitDetailSidecar.Marker, implicitDetailSidecar.LayoutTag, implicitDetailSidecar.Type);
            RawPacketDump.ObserveParsedPacket("implicit-detail-sidecar", context.Connection);
            return context.MarkParsed();
        }

        if (Packet0438CompactValueParser.TryParse(packet, out var compact))
        {
            context.Sink.RegisterCompactValue0438(context.CreateObservationSource(0x0438, packet.Length), compact.TargetId, compact.SourceId, compact.BodySkillVariantRaw, compact.Marker, compact.LayoutTag, compact.Type, compact.Value);
            RawPacketDump.ObserveParsedPacket("compact-value", context.Connection);
            return context.MarkParsed();
        }

        if (!Packet0438CompactSignalParser.TryParse(packet, out var compactSignal))
        {
            return false;
        }

        context.Sink.RegisterCompactValue0438(context.CreateObservationSource(0x0438, packet.Length), compactSignal.TargetId, compactSignal.SourceId, compactSignal.BodySkillVariantRaw, compactSignal.Marker, compactSignal.LayoutTag, compactSignal.Type);
        return context.MarkParsed();
    }

    public static bool ParsePeriodicValuePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
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
                    SkillCode = parsed.TailSkillCodeRaw,
                    BodyResourceEffectRef = parsed.BodyResourceEffectRef,
                    Damage = 0,
                    HitCount = 0,
                    AttemptCount = 1,
                    DetailRaw = parsed.LinkId,
                    Marker = parsed.Unknown,
                    Type = 48,
                    PeriodicTailSkillCodeRaw = parsed.TailSkillCodeRaw,
                    PeriodicTailPrefixValue = parsed.TailPrefixValue,
                    PeriodicTailLength = parsed.TailLength,
                    Modifiers = DamageModifiers.Invincible,
                    EventKind = CombatEventKind.Damage,
                    ValueKind = CombatValueKind.Damage,
                    EffectTag = PacketEffectTag.PeriodicLinkInvincible
                };
                context.Sink.AppendCombatObservation(context.CreateObservationSource(0x0538, packet.Length), parsed.LinkId, parsed.TargetId, in invincibleObservation);
            }

            return context.MarkParsed();
        }

        if (IsActiveSkillInvincible(parsed.Mode, parsed.TargetId, parsed.SourceId, parsed.Damage))
        {
            var invincibleObservation = new CombatObservation
            {
                SkillCode = parsed.TailSkillCodeRaw,
                BodyResourceEffectRef = parsed.BodyResourceEffectRef,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 1,
                DetailRaw = parsed.Damage,
                Marker = parsed.Unknown,
                Type = parsed.Mode,
                PeriodicTailSkillCodeRaw = parsed.TailSkillCodeRaw,
                PeriodicTailPrefixValue = parsed.TailPrefixValue,
                PeriodicTailLength = parsed.TailLength,
                Modifiers = DamageModifiers.Invincible,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage,
                EffectTag = PacketEffectTag.ActiveSkillInvincible
            };
            context.Sink.AppendCombatObservation(context.CreateObservationSource(0x0538, packet.Length), parsed.SourceId, parsed.TargetId, in invincibleObservation);
            return context.MarkParsed();
        }

        var observation = new CombatObservation
        {
            SkillCode = parsed.TailSkillCodeRaw,
            BodyResourceEffectRef = parsed.BodyResourceEffectRef,
            ChainId = parsed.Unknown,
            Damage = parsed.Damage,
            HitCount = 1,
            AttemptCount = 1,
            PeriodicRelation = parsed.TargetId == parsed.SourceId ? PeriodicEffectRelation.Self : PeriodicEffectRelation.Target,
            PeriodicMode = parsed.Mode,
            PeriodicTailSkillCodeRaw = parsed.TailSkillCodeRaw,
            PeriodicTailPrefixValue = parsed.TailPrefixValue,
            PeriodicTailLength = parsed.TailLength
        };

        context.Sink.AppendCombatObservation(context.CreateObservationSource(0x0538, packet.Length), parsed.SourceId, parsed.TargetId, in observation);
        return context.MarkParsed();
    }

    public static bool ParseCompactControl0238Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet0238CompactControlParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.RegisterCompactControl0238(context.CreateObservationSource(0x0238, packet.Length), parsed.SourceId, parsed.Mode, parsed.BodyCodeRaw, parsed.Marker, parsed.Flag, parsed.EchoSourceId);
        RawPacketDump.ObserveParsedPacket("compact-0238", context.Connection);
        return context.MarkParsed();
    }

    public static bool ParseCompactControl0638Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet0638CompactControlParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.RegisterCompactControl0638(context.CreateObservationSource(0x0638, packet.Length), parsed.SourceId, parsed.BodyResourceEffectRef, parsed.Marker, parsed.Flag);
        RawPacketDump.ObserveParsedPacket("compact-0638", context.Connection);
        return context.MarkParsed();
    }

    public static bool TryParseCompactControl0238At(ReadOnlySpan<byte> packet, int opcodeOffset, ref PacketParseContext context, out int consumed)
    {
        consumed = 0;
        var payload = packet[opcodeOffset..];
        if (payload.Length < 2 || payload[0] != 0x02 || payload[1] != 0x38)
            return false;

        var reader = new PacketSpanReader(payload);
        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (!reader.TryReadVarInt(out var mode)) return false;
        if (!reader.TryReadUInt32Le(out var bodyResourceEffectRefRaw)) return false;
        if (!reader.TryReadByte(out var marker)) return false;
        if (!reader.TryReadByte(out var flag)) return false;
        if (!reader.TryReadVarInt(out var echoSourceId)) return false;
        if (sourceId <= 0 || bodyResourceEffectRefRaw == 0) return false;

        consumed = ResolveEmbedded0238Length(ref reader);
        var previous = context.EnterStructure(PacketStructureKind.EmbeddedFrame, opcodeOffset, consumed, 2, Math.Max(0, consumed - 2), 0);
        try
        {
            context.Sink.RegisterCompactControl0238(context.CreateObservationSource(0x0238, consumed), sourceId, mode, unchecked((uint)bodyResourceEffectRefRaw), marker, flag, echoSourceId);
            return context.MarkParsed();
        }
        finally
        {
            context.RestoreStructure(previous);
        }
    }

    private static int ResolveEmbedded0238Length(ref PacketSpanReader reader)
    {
        var consumed = reader.Offset;
        var tailLength = Packet0238CompactControlParser.ResolveZeroPrefixedTailLength(reader.RemainingSpan);
        if (tailLength > 0)
            return consumed + tailLength;

        return consumed;
    }

    public static bool TryParseCompactControl0638At(ReadOnlySpan<byte> packet, int opcodeOffset, ref PacketParseContext context, out int consumed)
    {
        consumed = 0;
        var payload = packet[opcodeOffset..];
        if (payload.Length < 2 || payload[0] != 0x06 || payload[1] != 0x38)
            return false;

        var reader = new PacketSpanReader(payload);
        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (!reader.TryReadUInt32Le(out var bodyResourceEffectRefRaw)) return false;
        if (!reader.TryReadByte(out var marker)) return false;
        if (!reader.TryReadByte(out var flag)) return false;
        if (sourceId <= 0 || bodyResourceEffectRefRaw == 0) return false;

        consumed = reader.Offset;
        var previous = context.EnterStructure(PacketStructureKind.EmbeddedFrame, opcodeOffset, consumed, 2, Math.Max(0, consumed - 2), 0);
        try
        {
            context.Sink.RegisterCompactControl0638(context.CreateObservationSource(0x0638, consumed), sourceId, ResourceEffectRef.FromRaw(bodyResourceEffectRefRaw), marker, flag);
            return context.MarkParsed();
        }
        finally
        {
            context.RestoreStructure(previous);
        }
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
        var payload = packet[opcodeOffset..];
        if (!Packet0438DamageParser.TryParsePayload(payload, out var parsed, out consumed))
        {
            return false;
        }

        if (parsed.Damage <= 0) return false;

        if (!context.Sink.IsKnownEntity(parsed.SourceId) && !context.Sink.IsKnownEntity(parsed.TargetId))
        {
            return false;
        }

        var previous = context.EnterStructure(PacketStructureKind.EmbeddedFrame, opcodeOffset, consumed, 2, Math.Max(0, consumed - 2), 0);
        try
        {
            var observation = new CombatObservation
            {
                SkillCode = parsed.BodySkillVariantRaw,
                BodySkillVariantRaw = parsed.BodySkillVariantRaw,
                LayoutTag = parsed.LayoutTag,
                Flag = parsed.Flag,
                Marker = parsed.Marker,
                Type = parsed.Type,
                Modifiers = parsed.Modifiers,
                ChainId = parsed.Unknown,
                Damage = parsed.Damage,
                HitCount = 1,
                AttemptCount = 1,
                Loop = parsed.Loop,
                MultiHitCount = parsed.MultiHitCount,
                DrainHealAmount = parsed.DrainHealAmount,
                RegenerationAmount = parsed.RegenerationAmount,
                DetailRaw = parsed.DetailRaw,
                DetailResourceEffectRef = parsed.DetailResourceEffectRef,
                ResourceKind = parsed.ResourceKind
            };

            context.Sink.AppendCombatObservation(context.CreateObservationSource(0x0438, consumed), parsed.SourceId, parsed.TargetId, in observation);

            if (parsed.RegenerationAmount > 0 && ShouldStoreRegenerationHealing(parsed.TargetId, context.Sink))
            {
                var regenObservation = new CombatObservation
                {
                    SkillCode = parsed.BodySkillVariantRaw,
                    BodySkillVariantRaw = parsed.BodySkillVariantRaw,
                    Damage = parsed.RegenerationAmount,
                    HitCount = 1,
                    AttemptCount = 1,
                    DetailResourceEffectRef = parsed.DetailResourceEffectRef,
                    EventKind = CombatEventKind.Healing,
                    ValueKind = CombatValueKind.Healing,
                    EffectTag = PacketEffectTag.RegenerationHealing
                };
                context.Sink.AppendCombatObservation(context.CreateObservationSource(0x0438, consumed), parsed.TargetId, parsed.TargetId, in regenObservation);
            }

            if (ShouldStoreDrainHealing(parsed))
            {
                var drainObservation = new CombatObservation
                {
                    SkillCode = parsed.BodySkillVariantRaw,
                    BodySkillVariantRaw = parsed.BodySkillVariantRaw,
                    Damage = parsed.DrainHealAmount,
                    HitCount = 1,
                    AttemptCount = 1,
                    DrainHealAmount = parsed.DrainHealAmount,
                    DetailResourceEffectRef = parsed.DetailResourceEffectRef,
                    EventKind = CombatEventKind.Healing,
                    ValueKind = CombatValueKind.DrainHealing
                };
                context.Sink.AppendCombatObservation(context.CreateObservationSource(0x0438, consumed), parsed.SourceId, parsed.SourceId, in drainObservation);
            }

            return context.MarkParsed();
        }
        finally
        {
            context.RestoreStructure(previous);
        }
    }

    public static bool TryParsePeriodicValuePacketAt(ReadOnlySpan<byte> packet, int opcodeOffset, ref PacketParseContext context, out int consumed)
    {
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
        if (!reader.TryReadUInt32Le(out var bodyResourceEffectRefRaw)) return false;
        var bodyResourceEffectRef = ResourceEffectRef.FromRaw(bodyResourceEffectRefRaw);

        if (!reader.TryReadVarInt(out var damage)) return false;
        if (damage <= 0) return false;

        if (sourceId == targetId && !IsActiveSkillInvincible(mode, targetId, sourceId, damage))
        {
            return false;
        }

        if (!context.Sink.IsKnownEntity(sourceId) && !context.Sink.IsKnownEntity(targetId))
        {
            return false;
        }

        consumed = reader.Offset;
        var previous = context.EnterStructure(PacketStructureKind.EmbeddedFrame, opcodeOffset, consumed, 2, Math.Max(0, consumed - 2), 0);
        try
        {
            if (IsActiveSkillInvincible(mode, targetId, sourceId, damage))
            {
                var invincibleObservation = new CombatObservation
                {
                    ChainId = unknownInfo,
                    Damage = 0,
                    HitCount = 0,
                    AttemptCount = 1,
                    DetailRaw = damage,
                    Type = mode,
                    BodyResourceEffectRef = bodyResourceEffectRef,
                    Modifiers = DamageModifiers.Invincible,
                    EventKind = CombatEventKind.Damage,
                    ValueKind = CombatValueKind.Damage,
                    EffectTag = PacketEffectTag.ActiveSkillInvincible
                };
                context.Sink.AppendCombatObservation(context.CreateObservationSource(0x0538, consumed), sourceId, targetId, in invincibleObservation);
                return context.MarkParsed();
            }

            var observation = new CombatObservation
            {
                ChainId = unknownInfo,
                BodyResourceEffectRef = bodyResourceEffectRef,
                Damage = damage,
                HitCount = 1,
                AttemptCount = 1,
                PeriodicRelation = PeriodicEffectRelation.Target,
                PeriodicMode = mode
            };

            context.Sink.AppendCombatObservation(context.CreateObservationSource(0x0538, consumed), sourceId, targetId, in observation);
            return context.MarkParsed();
        }
        finally
        {
            context.RestoreStructure(previous);
        }
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

    private static bool IsActiveSkillInvincible(int mode, int targetId, int sourceId, int packetValue) => mode == 56 && targetId > 0 && targetId == sourceId && packetValue > 0;
}
