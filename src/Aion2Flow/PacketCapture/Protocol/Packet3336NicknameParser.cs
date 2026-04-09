using Cloris.Aion2Flow.PacketCapture.Readers;
using System.Text;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet3336Nickname(
    int PlayerId,
    string Nickname,
    int NicknameLength,
    int TailOffset);

internal static class Packet3336NicknameParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet3336Nickname result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        return TryParsePayload(packet[reader.Offset..], out result);
    }

    public static bool TryParsePayload(ReadOnlySpan<byte> payload, out Packet3336Nickname result)
    {
        result = default;

        var reader = new PacketSpanReader(payload);
        if (reader.Remaining < 2) return false;
        if (payload[reader.Offset] != 0x33 || payload[reader.Offset + 1] != 0x36) return false;
        reader.TryAdvance(2);

        if (!reader.TryReadVarInt(out var playerId)) return false;
        var scan = payload[reader.Offset..Math.Min(reader.Offset + 10, payload.Length)];
        var splitIndex = scan.IndexOf((byte)0x07);
        if (splitIndex < 0) return false;

        if (!reader.TryAdvance(splitIndex + 1)) return false;
        if (!reader.TryReadVarInt(out var nicknameLength)) return false;
        if (nicknameLength is < 1 or > 71) return false;
        if (reader.Remaining < nicknameLength) return false;

        var nicknameSpan = payload.Slice(reader.Offset, nicknameLength);
        var sanitizedName = NicknameSanitizer.SanitizeStrict(Encoding.UTF8.GetString(nicknameSpan));
        if (sanitizedName is null) return false;
        if (!reader.TryAdvance(nicknameLength)) return false;

        result = new Packet3336Nickname(playerId, sanitizedName, nicknameLength, reader.Offset);
        return true;
    }
}
