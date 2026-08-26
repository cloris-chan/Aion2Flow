using Cloris.Aion2Flow.Protocol.Packets;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketCooldownHandler
{
    public static bool Parse2238Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet2238CooldownChargeParser.TryParse(packet, out var parsed))
            return false;

        context.Sink.RegisterCooldownCharge2238(
            context.CreateObservationSource(0x2238, packet.Length),
            parsed.State,
            parsed.PacketSkillCode,
            parsed.AvailableCount,
            parsed.NextChargeRemainingMilliseconds);
        return context.MarkParsed();
    }

    public static bool Parse4738Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet4738CooldownParser.TryParse(packet, out var batch))
            return false;

        var source = context.CreateObservationSource(0x4738, packet.Length);
        for (var entryIndex = 0; entryIndex < batch.Count; entryIndex++)
        {
            if (!batch.TryRead(out var parsed))
                return false;

            context.Sink.RegisterCooldown4738(
                in source,
                parsed.RowBaseSkillId,
                parsed.RemainingMilliseconds);
        }

        return context.MarkParsed();
    }
}
