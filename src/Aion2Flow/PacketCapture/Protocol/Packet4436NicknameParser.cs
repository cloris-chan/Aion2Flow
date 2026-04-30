using System.Text;
using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet4436Nickname(
    int PlayerId,
    string Nickname,
    int NicknameLength,
    int Delta);

internal static class Packet4436NicknameParser
{
    private const int MarkerSearchLimit = 8;

    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet4436Nickname result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x44 || packet[reader.Offset + 1] != 0x36) return false;
        reader.TryAdvance(2);

        if (!reader.TryReadVarInt(out var playerId)) return false;

        var searchStart = reader.Offset;
        var searchEnd = Math.Min(packet.Length - 1, searchStart + MarkerSearchLimit);

        for (var markerOffset = searchStart; markerOffset < searchEnd; markerOffset++)
        {
            if (packet[markerOffset] != 0x07) continue;

            var candidateReader = new PacketSpanReader(packet);
            if (!candidateReader.TryAdvance(markerOffset + 1)) continue;
            if (!candidateReader.TryReadVarInt(out var nicknameLength)) continue;
            if (nicknameLength is < 1 or > 71) continue;
            if (candidateReader.Remaining < nicknameLength) continue;

            var nicknameSpan = packet.Slice(candidateReader.Offset, nicknameLength);
            var sanitizedName = NicknameSanitizer.SanitizeStrict(Encoding.UTF8.GetString(nicknameSpan));
            if (sanitizedName is null) continue;

            result = new Packet4436Nickname(playerId, sanitizedName, nicknameLength, markerOffset - searchStart);
            return true;
        }

        return false;
    }
}
