using System.Text;
using Cloris.Aion2Flow.PacketCapture.Readers;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet0994Nickname(
    int PlayerId,
    string Nickname,
    int NicknameLength,
    int TailOffset);

internal static class Packet0994NicknameParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet0994Nickname result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;

        var opcode0 = packet[reader.Offset];
        var opcode1 = packet[reader.Offset + 1];
        if (opcode1 != 0x94) return false;

        if (!reader.TryAdvance(2)) return false;

        return opcode0 switch
        {
            0x09 => TryParse0994Body(packet, reader.Offset, out result),
            0x0b => TryParse0B94Body(packet, reader.Offset, out result),
            _ => false
        };
    }

    private static bool TryParse0994Body(ReadOnlySpan<byte> packet, int bodyOffset, out Packet0994Nickname result)
    {
        result = default;

        var searchEnd = Math.Min(packet.Length - 1, bodyOffset + 16);
        for (var kindOffset = bodyOffset + 4; kindOffset < searchEnd; kindOffset++)
        {
            if (packet[kindOffset] > 0x10) continue;
            if (!TryReadPositiveInt32Le(packet, kindOffset - 4, out var playerId)) continue;

            var reader = new PacketSpanReader(packet);
            if (!reader.TryAdvance(kindOffset + 1)) continue;
            if (!reader.TryReadVarInt(out var varintPlayerId)) continue;
            if (varintPlayerId != playerId) continue;

            if (!TryReadStrictNickname(packet, reader.Offset, out var nickname, out var nicknameLength, out var tailOffset))
            {
                continue;
            }

            result = new Packet0994Nickname(playerId, nickname, nicknameLength, tailOffset);
            return true;
        }

        return false;
    }

    private static bool TryParse0B94Body(ReadOnlySpan<byte> packet, int bodyOffset, out Packet0994Nickname result)
    {
        result = default;

        if (packet.Length < bodyOffset + 17) return false;
        if (packet[bodyOffset + 4] > 0x10) return false;
        if (!TryReadPositiveInt32Le(packet, bodyOffset, out var playerId)) return false;
        if (!TryReadPositiveInt32Le(packet, bodyOffset + 5, out var repeatedPlayerId)) return false;
        if (repeatedPlayerId != playerId) return false;

        if (!TryReadStrictNickname(packet, bodyOffset + 13, out var nickname, out var nicknameLength, out var tailOffset))
        {
            return false;
        }

        result = new Packet0994Nickname(playerId, nickname, nicknameLength, tailOffset);
        return true;
    }

    private static bool TryReadPositiveInt32Le(ReadOnlySpan<byte> packet, int offset, out int value)
    {
        value = 0;
        if (offset < 0 || offset + 4 > packet.Length) return false;

        value = packet[offset]
            | (packet[offset + 1] << 8)
            | (packet[offset + 2] << 16)
            | (packet[offset + 3] << 24);

        return value > 0;
    }

    private static bool TryReadStrictNickname(
        ReadOnlySpan<byte> packet,
        int lengthOffset,
        out string nickname,
        out int nicknameLength,
        out int tailOffset)
    {
        nickname = string.Empty;
        nicknameLength = 0;
        tailOffset = 0;

        if ((uint)lengthOffset >= (uint)packet.Length) return false;

        nicknameLength = packet[lengthOffset];
        if (nicknameLength is < 1 or > 72) return false;

        var nameOffset = lengthOffset + 1;
        if (nameOffset + nicknameLength > packet.Length) return false;

        var nicknameSpan = packet.Slice(nameOffset, nicknameLength);
        var sanitizedName = NicknameSanitizer.SanitizeStrict(Encoding.UTF8.GetString(nicknameSpan));
        if (sanitizedName is null) return false;

        nickname = sanitizedName;
        tailOffset = nameOffset + nicknameLength;
        return true;
    }
}
