using System.Buffers;
using Cloris.Aion2Flow.Protocol.Packets;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Runtime;

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
            var source = context.CreateObservationSource(0x4036, packet.Length);
            if (parsed.NpcCode.HasValue)
            {
                context.Writer.ApplyNpcCatalog(in source, parsed.SummonId, parsed.NpcCode.Value);
            }

            context.Sink.AppendSummon(in source, parsed.OwnerId, parsed.SummonId);
            return context.MarkParsed();
        }

        if (Packet4036CreateParser.TryParseNpcSpawn(packet, out var spawn))
        {
            var source = context.CreateObservationSource(0x4036, packet.Length);
            if (spawn.NpcCode.HasValue)
            {
                context.Writer.ApplyNpcCatalog(in source, spawn.EntityId, spawn.NpcCode.Value);
            }

            if (spawn.CurrentHp is int currentHp && spawn.MaxHp is int maxHp)
            {
                context.Sink.AppendNpcHp(in source, spawn.EntityId, currentHp, maxHp);
            }
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

        context.Sink.RegisterObservation2B38(context.CreateObservationSource(0x2B38, packet.Length), parsed.SourceId, parsed.SourceIdCopy, parsed.Phase, parsed.InstanceSequenceId, parsed.ActionResourceEffectRef, parsed.SequenceValue, parsed.StateValue, parsed.DetailValue, parsed.TailLength);
        return context.MarkParsed();
    }

    public static bool ParseAux2A38Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet2A38Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.RegisterObservation2A38(context.CreateObservationSource(0x2A38, packet.Length), parsed.EntityId, parsed.Mode, parsed.GroupCode, parsed.InstanceSequenceId, parsed.HeadCode, parsed.HeadValue, parsed.HeadMiddleRaw, parsed.TimelineValue, parsed.StableValue, parsed.EchoSourceId, parsed.StackValue, parsed.BuffResourceEffectRef, parsed.TailLength, parsed.TailLow64, parsed.TailHigh64);

        return context.MarkParsed();
    }

    public static bool ParseAux2C38Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet2C38Parser.TryParse(packet, out var batch))
        {
            return false;
        }

        AuraResultRecord[]? rented = null;
        Span<AuraResultRecord> results = batch.ResultCount <= 64
            ? stackalloc AuraResultRecord[batch.ResultCount]
            : (rented = ArrayPool<AuraResultRecord>.Shared.Rent(batch.ResultCount)).AsSpan(0, batch.ResultCount);
        try
        {
            for (var resultIndex = 0; resultIndex < results.Length; resultIndex++)
            {
                if (!batch.TryRead(out var result))
                    return false;
                results[resultIndex] = new AuraResultRecord(result.StateCode, result.InstanceSequenceId, result.ResultCode, result.DetailEntityId, result.DetailValue0, result.DetailValue1);
            }

            var source = context.CreateObservationSource(0x2C38, packet.Length);
            context.Sink.RegisterObservation2C38(in source, batch.EntityId, results);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<AuraResultRecord>.Shared.Return(rented);
        }

        return context.MarkParsed();
    }

    public static bool ParseState1D37Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet1D37Parser.TryParse(packet, out _))
        {
            return false;
        }

        return context.MarkParsed();
    }

    public static bool ParseMapScopeSignalPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context, ushort opcode)
    {
        if (!PacketScopeSignalParser.TryParse(packet, opcode, out _))
        {
            return false;
        }

        return context.MarkParsed();
    }

    public static bool ParseState4136Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet4136Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.RememberNpcObservationSource(parsed.EntityId);
        var source = context.CreateObservationSource(0x4136, packet.Length);
        if (parsed.NpcCode is int npcCode)
        {
            context.Writer.ApplyNpcCatalog(in source, parsed.EntityId, npcCode, requireCatalogEntry: true);
        }

        if (parsed.OwnerId is int ownerId)
        {
            context.Sink.AppendSummon(in source, ownerId, parsed.EntityId);
        }

        if (parsed.CurrentHp is int currentHp && parsed.MaxHp is int maxHp)
        {
            context.Sink.AppendNpcHp(in source, parsed.EntityId, currentHp, maxHp);
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

    public static bool ParseMapPacket(ReadOnlySpan<byte> packet, ref PacketParseContext context, ushort opcode)
        => PurplePacketMapParser.TryParse(packet, out var mapEvent) &&
           PacketMapHandler.Apply(in mapEvent, opcode, packet.Length, ref context);

    public static bool ParseState4636Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet4636Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        context.Sink.AppendNpc4636State(context.CreateObservationSource(0x4636, packet.Length), parsed.SourceId, parsed.State0, parsed.State1);
        context.Sink.RememberNpcObservationSource(parsed.SourceId);

        return context.MarkParsed();
    }

    public static bool ParseState4536Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (PacketIdentityHandler.ParseNearbyPcProfilePacket(packet, ref context))
            return true;

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
            context.Sink.AppendNpcHp(context.CreateObservationSource(0x008D, packet.Length), parsed.NpcId, parsed.Hp);
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
            context.Sink.SetNpcBattle(context.CreateObservationSource(0x218D, packet.Length), parsed.NpcId, isActive);
        }
        else
        {
            context.Sink.ToggleNpcBattle(context.CreateObservationSource(0x218D, packet.Length), parsed.NpcId);
        }

        return context.MarkParsed();
    }

    private static bool Parse4036StatePacket(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (Packet4036CreateParser.TryParseNpcSpawn(packet, out var spawn) && spawn.NpcCode.HasValue)
        {
            var source = context.CreateObservationSource(0x4036, packet.Length);
            context.Writer.ApplyNpcCatalog(in source, spawn.EntityId, spawn.NpcCode.Value, requireCatalogEntry: true);
            if (spawn.CurrentHp is int currentHp && spawn.MaxHp is int maxHp)
            {
                context.Sink.AppendNpcHp(in source, spawn.EntityId, currentHp, maxHp);
            }
        }

        if (Packet4036CreateParser.TryParseOwner(packet, out var entityId, out var ownerId))
        {
            context.Sink.AppendSummon(context.CreateObservationSource(0x4036, packet.Length), ownerId, entityId);
        }

        if (!Packet4036Parser.TryParse(packet, out _))
        {
            return false;
        }

        return context.MarkParsed();
    }
}
