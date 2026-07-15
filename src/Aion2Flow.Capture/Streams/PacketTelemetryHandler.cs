using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketTelemetryHandler
{
    public static bool ParseRoundTripEcho0336Packet(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        if (!Packet0336RoundTripParser.TryParse(packet, out var parsed) ||
            !Packet0336RoundTripParser.IsPlausibleClientEcho(parsed.ClientSentUnixMilliseconds, context.TimestampMilliseconds))
        {
            return false;
        }

        context.ObserveProtocolRoundTrip(parsed.ClientSentUnixMilliseconds, parsed.ServerUnixMilliseconds);
        return context.MarkParsed();
    }
}
