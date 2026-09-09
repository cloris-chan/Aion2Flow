using System.Buffers.Binary;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal readonly record struct Packet0336RoundTrip(long ClientSentUnixMilliseconds, long ServerUnixMilliseconds);

internal static class Packet0336RoundTripParser
{
    private const long UnixEpochOffsetMilliseconds = 62_135_596_800_000;
    private const long MaximumRoundTripMilliseconds = 10_000;
    private const int FrameLength = 21;
    private const int DeclaredLength = 24;

    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet0336RoundTrip result)
    {
        result = default;

        if (packet.Length != FrameLength)
        {
            return false;
        }

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out var declaredLength) || declaredLength != DeclaredLength)
        {
            return false;
        }

        if (reader.Remaining != 20 ||
            packet[reader.Offset] != 0x03 ||
            packet[reader.Offset + 1] != 0x36 ||
            packet[reader.Offset + 2] != 0 ||
            packet[reader.Offset + 3] != 0)
        {
            return false;
        }

        var clientRawMilliseconds = BinaryPrimitives.ReadUInt64LittleEndian(packet.Slice(reader.Offset + 4, sizeof(ulong)));
        var serverRawMilliseconds = BinaryPrimitives.ReadUInt64LittleEndian(packet.Slice(reader.Offset + 12, sizeof(ulong)));
        if (clientRawMilliseconds < UnixEpochOffsetMilliseconds ||
            clientRawMilliseconds > long.MaxValue ||
            serverRawMilliseconds > long.MaxValue)
        {
            return false;
        }

        result = new Packet0336RoundTrip(
            (long)clientRawMilliseconds - UnixEpochOffsetMilliseconds,
            (long)serverRawMilliseconds);
        return true;
    }

    public static bool IsPlausibleClientEcho(long clientSentUnixMilliseconds, long arrivalUnixMilliseconds)
        => clientSentUnixMilliseconds >= 0 &&
           arrivalUnixMilliseconds >= clientSentUnixMilliseconds &&
           arrivalUnixMilliseconds - clientSentUnixMilliseconds <= MaximumRoundTripMilliseconds;
}
