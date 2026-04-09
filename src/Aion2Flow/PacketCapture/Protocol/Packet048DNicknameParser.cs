using Cloris.Aion2Flow.PacketCapture.Readers;
using System.Text;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet048DNickname(
    int PlayerId,
    string Nickname,
    int NicknameLength,
    int TailOffset);

internal static class Packet048DNicknameParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet048DNickname result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x04 || packet[reader.Offset + 1] != 0x8d) return false;

        reader = new PacketSpanReader(packet);
        if (!reader.TryAdvance(10)) return false;
        if (!reader.TryReadVarInt(out var playerId)) return false;
        if (!reader.TryReadByte(out var nicknameLengthByte)) return false;

        var nicknameLength = nicknameLengthByte;
        if (nicknameLength is <= 0 or > 72) return false;
        if (reader.Remaining < nicknameLength) return false;

        var nicknameSpan = packet.Slice(reader.Offset, nicknameLength);
        var sanitizedName = NicknameSanitizer.Sanitize(Encoding.UTF8.GetString(nicknameSpan));
        if (sanitizedName is null) return false;

        result = new Packet048DNickname(playerId, sanitizedName, nicknameLength, Math.Min(reader.Offset + nicknameLength, packet.Length));
        return true;
    }
}
