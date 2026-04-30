using System.Globalization;
using System.Text;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal static class NicknameSanitizer
{
    public static string? Sanitize(string nickname)
    {
        var sanitized = nickname.Split('\0')[0].Trim();
        if (string.IsNullOrEmpty(sanitized))
        {
            return null;
        }

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

    public static string? SanitizeStrict(string nickname)
    {
        var sanitized = Sanitize(nickname);
        if (sanitized is null)
        {
            return null;
        }

        var rawSource = nickname.Split('\0')[0];
        if (rawSource.Length == 0)
        {
            return null;
        }

        if (rawSource.Contains('\uFFFD'))
        {
            return null;
        }

        if (!string.Equals(sanitized, rawSource, StringComparison.Ordinal))
        {
            return null;
        }

        return sanitized;
    }
}
