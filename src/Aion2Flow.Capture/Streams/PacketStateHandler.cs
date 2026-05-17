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
            return context.MarkParsed();
        }

        return false;
    }

    public static bool ParseAux2B38Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet2B38Parser.TryParse(packet, out _))
        {
            return false;
        }

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

        RawPacketDump.ObserveParsedPacket("aux-2a38", context.Connection);
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

        RawPacketDump.ObserveParsedPacket("aux-2c38", context.Connection);
        return context.MarkParsed();
    }

    public static bool ParseState1D37Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet1D37Parser.TryParse(packet, out _))
        {
            return false;
        }

        RawPacketDump.ObserveParsedPacket("state-1d37", context.Connection);
        return context.MarkParsed();
    }

    public static bool ParseState4136Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet4136Parser.TryParse(packet, out _))
        {
            return false;
        }

        return context.MarkParsed();
    }

    public static bool ParseWrapped8456Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet8456EnvelopeParser.TryParse(packet, out _))
        {
            return false;
        }

        return context.MarkParsed();
    }

    public static bool ParseState0140Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet0140Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Writer.ConfirmDestinationMapFromSceneState(parsed.Value0);
        var targetId = context.Sink.ResolveNpcObservationSource();
        if (targetId > 0)
        {
            context.Sink.AppendNpc0140Value(targetId, parsed.Value0);
            if (parsed.Value0 <= int.MaxValue)
            {
                context.Writer.ApplyNpcCatalog(targetId, (int)parsed.Value0, requireCatalogEntry: true);
            }
        }

        return context.MarkParsed();
    }

    public static bool ParseState2136Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet2136Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Writer.StagePendingDestinationMapFromSceneState(parsed.Value0);

        var targetId = context.Sink.ResolveNpcObservationSource();
        if (targetId > 0)
        {
            context.Sink.AppendNpc2136State(targetId, parsed.Sequence, parsed.Value0);
            if (parsed.Value0 <= int.MaxValue)
            {
                context.Writer.ApplyNpcCatalog(targetId, (int)parsed.Value0, requireCatalogEntry: true);
            }
        }

        return context.MarkParsed();
    }

    public static bool ParseMap2E92Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet2E92Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.ConfirmDestinationMapInstance(parsed.InstanceId);

        return context.MarkParsed();
    }

    public static bool ParseState0240Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet0240Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Writer.ConfirmDestinationMapFromSceneState(parsed.Value0);
        var targetId = context.Sink.ResolveNpcObservationSource();
        if (targetId > 0)
        {
            context.Sink.AppendNpc0240Value(targetId, parsed.Value0);
            if (parsed.Value0 <= int.MaxValue)
            {
                context.Writer.ApplyNpcCatalog(targetId, (int)parsed.Value0, requireCatalogEntry: true);
            }
        }

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

        return context.MarkParsed();
    }

    public static bool ParseState4536Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet4536Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.RememberNpcObservationSource(parsed.SourceId);

        return context.MarkParsed();
    }

    public static bool ParseState4936Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet4936Parser.TryParse(packet, out _))
        {
            return false;
        }

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

        if (isHealth)
        {
            RawPacketDump.ObserveParsedPacket("remain-hp", context.Connection);
        }

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

        if (!Packet4036Parser.TryParse(packet, out _))
        {
            return false;
        }

        return context.MarkParsed();
    }
}
