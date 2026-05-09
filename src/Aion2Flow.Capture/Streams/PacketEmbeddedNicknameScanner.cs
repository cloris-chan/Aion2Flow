using System.Globalization;
using System.Text;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketEmbeddedNicknameScanner
{
    public static void Scan(ReadOnlySpan<byte> packet, ref PacketParseContext context)
    {
        var originOffset = 0;
        while (originOffset < packet.Length)
        {
            if (!PacketTransportCodec.TryReadVarInt(packet, originOffset, out var info))
            {
                originOffset++;
                continue;
            }

            var innerOffset = originOffset + info.ByteCount;

            if (innerOffset + 6 >= packet.Length)
            {
                originOffset++;
                continue;
            }

            if (packet[innerOffset + 3] == 0x01 && packet[innerOffset + 4] == 0x07)
            {
                var possibleNameLength = packet[innerOffset + 5] & 0xff;
                if (innerOffset + 6 + possibleNameLength <= packet.Length)
                {
                    var possibleNameBytes = packet[(innerOffset + 6)..(innerOffset + 6 + possibleNameLength)];
                    var possibleName = Encoding.UTF8.GetString(possibleNameBytes);
                    var sanitizedName = SanitizeNickname(possibleName);
                    if (sanitizedName != null)
                    {
                        context.Sink.AppendNickname(info.Value, sanitizedName);
                    }
                }
            }

            if (packet.Length > innerOffset + 5)
            {
                if (packet[innerOffset + 3] == 0x00 && packet[innerOffset + 4] == 0x07)
                {
                    var possibleNameLength = packet[innerOffset + 5] & 0xff;
                    if (packet.Length > innerOffset + possibleNameLength + 6)
                    {
                        var possibleNameBytes = packet[(innerOffset + 6)..(innerOffset + possibleNameLength + 6)];
                        var possibleName = Encoding.UTF8.GetString(possibleNameBytes);
                        var sanitizedName = SanitizeNickname(possibleName);
                        if (sanitizedName != null)
                        {
                            context.Sink.AppendNickname(info.Value, sanitizedName);
                        }
                    }
                }
            }

            originOffset++;
        }
    }

    private static string? SanitizeNickname(string nickname)
    {
        var sanitized = nickname.Split('\0')[0].Trim();
        if (string.IsNullOrEmpty(sanitized)) return null;

        var nicknameBuilder = new StringBuilder();
        var onlyNumbers = true;
        var hasHan = false;

        foreach (var ch in sanitized)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                if (nicknameBuilder.Length == 0) return null;
                break;
            }
            if (ch == '\uFFFD')
            {
                if (nicknameBuilder.Length == 0) return null;
                break;
            }
            if (char.IsControl(ch))
            {
                if (nicknameBuilder.Length == 0) return null;
                break;
            }
            nicknameBuilder.Append(ch);
            if (char.IsLetter(ch)) onlyNumbers = false;
            if (char.GetUnicodeCategory(ch) == UnicodeCategory.OtherLetter)
            {
                hasHan = true;
            }
        }

        var trimmed = nicknameBuilder.ToString();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length < 3 && !hasHan) return null;
        if (onlyNumbers) return null;
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]) && !hasHan) return null;

        return trimmed;
    }
}
