using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Protocol.Packets;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.Capture.Streams;

internal sealed class PacketStateHandler
{
    public static bool ParseSummonPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        var kind = Packet4036Descriptors.ClassifyKind(packet.Length);
        if (!Packet4036Descriptors.IsCreateKind(kind))
        {
            return Parse4036StatePacket(packet, ref context);
        }

        if (Packet4036CreateParser.TryParse(packet, out var parsed))
        {
            if (parsed.NpcCode.HasValue)
            {
                context.Writer.ApplyNpcCatalog(parsed.SummonId, parsed.NpcCode.Value);
            }

            context.Sink.AppendNpcKind(parsed.SummonId, NpcKind.Summon);
            context.Sink.AppendSummon(parsed.OwnerId, parsed.SummonId);
            RawPacketDump.AppendFrameEvent("summon", context.Connection, $"kind={Packet4036Descriptors.FormatKind(parsed.Kind, parsed.TailOffset)}|owner={parsed.OwnerId}|summon={parsed.SummonId}{PacketDiagnosticFormatter.NpcCodeHint(parsed.NpcCode)}", packet[..Math.Min(parsed.TailOffset, packet.Length)]);
            return context.MarkParsed();
        }

        if (Packet4036CreateParser.TryParseNpcSpawn(packet, out var spawn))
        {
            if (spawn.NpcCode.HasValue)
            {
                context.Writer.ApplyNpcCatalog(spawn.EntityId, spawn.NpcCode.Value);
            }

            if (spawn.CurrentHp is int currentHp && spawn.MaxHp is int maxHp)
            {
                context.Sink.AppendNpcHp(spawn.EntityId, currentHp, maxHp, context.TimestampMilliseconds);
            }

            RawPacketDump.AppendFrameEvent("npc-spawn", context.Connection, $"kind={Packet4036Descriptors.FormatKind(spawn.Kind, packet.Length)}|entity={spawn.EntityId}{PacketDiagnosticFormatter.NpcCodeHint(spawn.NpcCode)}{PacketDiagnosticFormatter.NpcHpHint(spawn.CurrentHp, spawn.MaxHp)}", packet);
            return context.MarkParsed();
        }

        return false;
    }

    public static bool ParseAux2B38Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet2B38Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        RawPacketDump.AppendFrameEvent(
            "aux-2b38",
            context.Connection,
            $"source={parsed.SourceId}|source2={parsed.SourceIdCopy}|phase={parsed.Phase}|marker={parsed.Marker}|action=0x{parsed.ActionCode:x8}|seq={parsed.Sequence}|state={parsed.StateValue}|detail={parsed.DetailValue}|tailLen={parsed.TailLength}",
            packet);

        return context.MarkParsed();
    }

    public static bool ParseAux2A38Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        var frameOrdinal = context.FrameOrdinal;
        var batchOrdinal = context.BatchOrdinal;

        if (!Packet2A38Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.RegisterObservation2A38(
            parsed.SourceId,
            parsed.Mode,
            parsed.GroupCode,
            parsed.SequenceId,
            parsed.HeadValue,
            parsed.BuffCodeRaw,
            context.TimestampMilliseconds,
            frameOrdinal,
            batchOrdinal);

        RawPacketDump.AppendFrameEvent(
            "aux-2a38",
            context.Connection,
            $"source={parsed.SourceId}|mode={parsed.Mode}|group={parsed.GroupCode}|seq={parsed.SequenceId}|head=0x{parsed.HeadCode:x8}|headValue=0x{parsed.HeadValue:x4}|timeline=0x{parsed.TimelineValue:x8}|stable=0x{parsed.StableValue:x8}|echoSource={parsed.EchoSourceId}|stack={parsed.StackValue}|buff=0x{parsed.BuffCodeRaw:x8}{PacketDiagnosticFormatter.SkillHint(parsed.BuffCodeRaw)}|tailSig={parsed.TailSignature}|tailLen={parsed.TailLength}",
            packet);

        return context.MarkParsed();
    }

    public static bool ParseAux2C38Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        var frameOrdinal = context.FrameOrdinal;
        var batchOrdinal = context.BatchOrdinal;

        if (!Packet2C38Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.RegisterObservation2C38(
            parsed.SourceId,
            parsed.Mode,
            parsed.SequenceId,
            parsed.ResultCode,
            parsed.TailSourceId,
            parsed.TailSkillCodeRaw,
            context.TimestampMilliseconds,
            frameOrdinal,
            batchOrdinal);

        RawPacketDump.AppendFrameEvent(
            "aux-2c38",
            context.Connection,
            $"source={parsed.SourceId}|mode={parsed.Mode}|state={parsed.StateCode}|seq={parsed.SequenceId}|result={parsed.ResultCode}|tailLen={parsed.TailLength}|tailSource={parsed.TailSourceId}|tailSkillRaw={parsed.TailSkillCodeRaw}{PacketDiagnosticFormatter.SkillHint((uint)parsed.TailSkillCodeRaw)}",
            packet);

        return context.MarkParsed();
    }

    public static bool ParseState1D37Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet1D37Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        RawPacketDump.AppendFrameEvent(
            "state-1d37",
            context.Connection,
            $"source={parsed.SourceId}|group={parsed.GroupCode}|state={parsed.StateCode}|tailSig={parsed.TailSignature}|tailLen={parsed.TailLength}",
            packet);

        return context.MarkParsed();
    }

    public static bool ParseState4136Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet4136Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        RawPacketDump.AppendFrameEvent(
            "state-4136",
            context.Connection,
            $"source={parsed.SourceId}|state0={parsed.State0}|state1={parsed.State1}|tailLen={parsed.TailLength}",
            packet);

        return context.MarkParsed();
    }

    public static bool ParseWrapped8456Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet8456EnvelopeParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        RawPacketDump.AppendFrameEvent(
            "wrapped-8456",
            context.Connection,
            $"p0=0x{parsed.Prefix0:x2}|p1=0x{parsed.Prefix1:x2}|p2=0x{parsed.Prefix2:x2}|innerOpcode=0x{parsed.InnerOpcode:x4}|innerValue={parsed.InnerValue}|stamp=0x{parsed.Stamp:x16}|trailer=0x{parsed.Trailer:x2}|tailLen={parsed.TailLength}",
            packet);

        return context.MarkParsed();
    }

    public static bool ParseState0140Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet0140Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        var sceneMap = context.Writer.StageDestinationMapFromSceneState(parsed.Value0);
        var targetId = context.Sink.ResolveNpcObservationSource();
        if (targetId > 0)
        {
            context.Sink.AppendNpc0140Value(targetId, parsed.Value0);
            if (parsed.Value0 <= int.MaxValue)
            {
                context.Writer.ApplyNpcCatalog(targetId, (int)parsed.Value0, requireCatalogEntry: true);
            }
        }

        RawPacketDump.AppendFrameEvent(
            "state-0140",
            context.Connection,
            $"target={targetId}|value0={parsed.Value0}|value1={parsed.Value1}|sceneMap={sceneMap}|tailSig={parsed.TailSignature}|tailLen={parsed.TailLength}",
            packet);

        return context.MarkParsed();
    }

    public static bool ParseState2136Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet2136Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        var sceneMap = context.Writer.StageDestinationMapFromSceneState(parsed.Value0);

        var targetId = context.Sink.ResolveNpcObservationSource();
        if (targetId > 0)
        {
            context.Sink.AppendNpc2136State(targetId, parsed.Sequence, parsed.Value0);
            if (parsed.Value0 <= int.MaxValue)
            {
                context.Writer.ApplyNpcCatalog(targetId, (int)parsed.Value0, requireCatalogEntry: true);
            }
        }

        RawPacketDump.AppendFrameEvent(
            "state-2136",
            context.Connection,
            $"target={targetId}|seq={parsed.Sequence}|value0={parsed.Value0}|value1={parsed.Value1}|value2={parsed.Value2}|sceneMap={sceneMap}|value3=0x{parsed.Value3:x8}|value4=0x{parsed.Value4:x8}|value5=0x{parsed.Value5:x8}|value6=0x{parsed.Value6:x8}|value7={parsed.Value7}|tailMarker=0x{parsed.TailMarker:x4}|tailLen={parsed.TailLength}",
            packet);

        return context.MarkParsed();
    }

    public static bool ParseMap2E92Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet2E92Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.StageDestinationMapInstance(parsed.InstanceId);

        RawPacketDump.AppendFrameEvent(
            "map-2e92",
            context.Connection,
            $"instance={parsed.InstanceId}|contentKey={parsed.ContentKey}",
            packet);

        return context.MarkParsed();
    }

    public static bool ParseState0240Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet0240Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        var sceneMap = context.Writer.StageDestinationMapFromSceneState(parsed.Value0);
        var targetId = context.Sink.ResolveNpcObservationSource();
        if (targetId > 0)
        {
            context.Sink.AppendNpc0240Value(targetId, parsed.Value0);
            if (parsed.Value0 <= int.MaxValue)
            {
                context.Writer.ApplyNpcCatalog(targetId, (int)parsed.Value0, requireCatalogEntry: true);
            }
        }

        RawPacketDump.AppendFrameEvent(
            "state-0240",
            context.Connection,
            $"target={targetId}|value0={parsed.Value0}|value1={parsed.Value1}|sceneMap={sceneMap}|tailSig={parsed.TailSignature}|tailLen={parsed.TailLength}",
            packet);

        return context.MarkParsed();
    }

    public static bool ParseState4636Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet4636Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.AppendNpc4636State(parsed.SourceId, parsed.State0, parsed.State1);
        context.Sink.RememberNpcObservationSource(parsed.SourceId);

        RawPacketDump.AppendFrameEvent(
            "state-4636",
            context.Connection,
            $"source={parsed.SourceId}|state0={parsed.State0}|state1={parsed.State1}|tailLen={parsed.TailLength}",
            packet);

        return context.MarkParsed();
    }

    public static bool ParseState4536Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet4536Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.RememberNpcObservationSource(parsed.SourceId);

        RawPacketDump.AppendFrameEvent(
            "state-4536",
            context.Connection,
            $"source={parsed.SourceId}|value0={parsed.Value0}|tailLen={parsed.TailLength}",
            packet);

        return context.MarkParsed();
    }

    public static bool ParseState4936Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet4936Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        RawPacketDump.AppendFrameEvent(
            "state-4936",
            context.Connection,
            $"source={parsed.SourceId}|mode={parsed.Mode}|group={parsed.GroupCode}|flag={parsed.Flag}|value0=0x{parsed.Value0:x8}|marker=0x{parsed.Marker:x4}|value1=0x{parsed.Value1:x8}|tailSig={parsed.TailSignature}|tailLen={parsed.TailLength}",
            packet);

        return context.MarkParsed();
    }

    public static bool ParseRemainHpPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet008DRemainHpParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        var isHealth = Packet008DRemainHpParser.IsHealthValue(parsed);
        if (isHealth)
        {
            context.Sink.AppendNpcHp(parsed.NpcId, checked((int)parsed.Hp), context.TimestampMilliseconds);
        }

        var eventName = isHealth ? "remain-hp" : "entity-value-008d";
        RawPacketDump.AppendFrameEvent(eventName, context.Connection, $"npcId={parsed.NpcId}|value0={parsed.Value0}|value1={parsed.Value1}|value2={parsed.Value2}|value={parsed.Hp}", packet[..(packet.Length - parsed.TailLength)]);
        return context.MarkParsed();
    }

    public static bool ParseBattleTogglePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet218DBattleToggleParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        if (parsed.IsActive is bool isActive)
        {
            context.Sink.SetNpcBattle(parsed.NpcId, isActive, context.TimestampMilliseconds);
        }
        else
        {
            context.Sink.ToggleNpcBattle(parsed.NpcId);
        }

        RawPacketDump.AppendFrameEvent("battle-toggle", context.Connection, $"npcId={parsed.NpcId}{PacketDiagnosticFormatter.ActiveHint(parsed.IsActive)}|tailLen={parsed.TailLength}", packet);
        return context.MarkParsed();
    }

    private static bool Parse4036StatePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (Packet4036CreateParser.TryParseNpcSpawn(packet, out var spawn) && spawn.NpcCode.HasValue)
        {
            context.Writer.ApplyNpcCatalog(spawn.EntityId, spawn.NpcCode.Value, requireCatalogEntry: true);
            if (spawn.CurrentHp is int currentHp && spawn.MaxHp is int maxHp)
            {
                context.Sink.AppendNpcHp(spawn.EntityId, currentHp, maxHp, context.TimestampMilliseconds);
            }
        }

        if (Packet4036CreateParser.TryParseOwner(packet, out var entityId, out var ownerId))
        {
            context.Sink.AppendSummon(ownerId, entityId);
        }

        if (!Packet4036Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        RawPacketDump.AppendFrameEvent(
            "state-4036",
            context.Connection,
            $"kind={Packet4036Descriptors.FormatKind(parsed.Kind, parsed.PayloadLength)}|layout={Packet4036Descriptors.FormatLayout(parsed.Kind, parsed.LayoutKind, parsed.PayloadLength, parsed.BodyLength, parsed.Mode0, parsed.Mode1, parsed.Mode2)}|source={parsed.SourceId}|mode={parsed.Mode0:x2}{parsed.Mode1:x2}{parsed.Mode2:x2}|seed=0x{parsed.Seed:x8}|tag=0x{parsed.Tag:x4}|p0=0x{parsed.P0:x8}|p1=0x{parsed.P1:x8}|p2=0x{parsed.P2:x8}|marker=0x{parsed.Marker:x8}|repeat0={parsed.Repeat0}|repeat1={parsed.Repeat1}|linked={parsed.LinkedValue}|gauge0={parsed.Gauge0}|gauge1={parsed.Gauge1}|tailMode={parsed.TailMode}|tailState={parsed.TailState}|tailFlags={parsed.TailFlag0}/{parsed.TailFlag1}|tailValue={parsed.TailValue}|tailHash=0x{parsed.TailHash:x8}|tailTerm={parsed.TailTerminator}|sharedTag=0x{parsed.SharedTag:x8}|sharedGauge={parsed.SharedGauge0}/{parsed.SharedGauge1}/{parsed.SharedGauge2}/{parsed.SharedGauge3}|sharedFlag={parsed.SharedFlag}|sharedMini0=0x{parsed.SharedMini0:x8}|sharedMini1=0x{parsed.SharedMini1:x8}|heavyGauge={parsed.HeavyGauge0}/{parsed.HeavyGauge1}|heavyValue={parsed.HeavyValue0}/{parsed.HeavyValue1}|heavyFlag={parsed.HeavyFlag}|heavyMini0=0x{parsed.HeavyMini0:x8}|heavySentinel=0x{parsed.HeavySentinel0:x8}/0x{parsed.HeavySentinel1:x8}|heavyTrailer=0x{parsed.HeavyTrailer0:x8}/0x{parsed.HeavyTrailer1:x8}|tail0=0x{parsed.Tail0:x8}|tail1=0x{parsed.Tail1:x8}|bodyLen={parsed.BodyLength}",
            packet);

        return context.MarkParsed();
    }
}
