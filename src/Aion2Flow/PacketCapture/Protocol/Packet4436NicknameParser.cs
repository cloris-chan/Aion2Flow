using Cloris.Aion2Flow.PacketCapture.Readers;
using System.Text;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal readonly record struct Packet4436Nickname(
    int PlayerId,
    string Nickname,
    int NicknameLength,
    int Delta);

internal static class Packet4436NicknameParser
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out Packet4436Nickname result)
    {
        result = default;

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;
        if (packet[reader.Offset] != 0x44 || packet[reader.Offset + 1] != 0x36) return false;
        reader.TryAdvance(2);

        if (!reader.TryReadVarInt(out var playerId)) return false;
        if (!reader.TryReadVarInt(out _)) return false;
        if (!reader.TryReadVarInt(out _)) return false;
        if (!reader.TryAdvance(1)) return false;

        var baseOffset = reader.Offset;
        string? bestName = null;
        var bestScore = int.MinValue;
        var bestLength = -1;
        var bestDelta = -1;

        for (var i = 0; i < 6 && baseOffset + i < packet.Length; i++)
        {
            var candidateReader = new PacketSpanReader(packet);
            if (!candidateReader.TryAdvance(baseOffset + i)) continue;
            if (!candidateReader.TryReadVarInt(out var nicknameLength)) continue;
            if (nicknameLength is < 1 or > 71) continue;
            if (candidateReader.Remaining < nicknameLength) continue;

            var nicknameSpan = packet.Slice(candidateReader.Offset, nicknameLength);
            var sanitizedName = NicknameSanitizer.SanitizeStrict(Encoding.UTF8.GetString(nicknameSpan));
            if (sanitizedName is null) continue;

            var score = ScoreNicknameCandidate(sanitizedName, nicknameLength, i, candidateReader.Offset, packet.Length);
            if (score > bestScore)
            {
                bestName = sanitizedName;
                bestScore = score;
                bestLength = nicknameLength;
                bestDelta = i;
            }
        }

        if (bestName is null)
        {
            return false;
        }

        result = new Packet4436Nickname(playerId, bestName, bestLength, bestDelta);
        return true;
    }

    private static int ScoreNicknameCandidate(string nickname, int nicknameLength, int delta, int offset, int packetLength)
    {
        var score = 0;

        if (nickname.Length == nicknameLength)
        {
            score += 120;
        }
        else
        {
            score -= 40;
        }

        if (nicknameLength <= 24)
        {
            score += 40;
        }
        else if (nicknameLength <= 40)
        {
            score += 10;
        }
        else
        {
            score -= (nicknameLength - 40) * 8;
        }

        score -= delta * 25;
        score -= offset / 16;
        score -= Math.Max(packetLength - offset - nicknameLength - 160, 0) / 8;

        var hasAsciiLetter = false;
        var hasLowerAscii = false;
        var hasUpperAscii = false;
        foreach (var ch in nickname)
        {
            if (ch is >= 'a' and <= 'z')
            {
                hasAsciiLetter = true;
                hasLowerAscii = true;
            }
            else if (ch is >= 'A' and <= 'Z')
            {
                hasAsciiLetter = true;
                hasUpperAscii = true;
            }
        }

        if (hasAsciiLetter)
        {
            score += 20;
        }

        if (hasLowerAscii && hasUpperAscii)
        {
            score += 10;
        }

        return score;
    }
}
