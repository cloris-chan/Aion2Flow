using System.Globalization;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal static class NicknameSanitizer
{
    public static string? Sanitize(string nickname)
    {
        var raw = GetNullTerminatedPrefix(nickname).Trim();
        if (raw.IsEmpty)
        {
            return null;
        }

        var sanitized = raw;
        var onlyNumbers = true;
        var hasHan = false;
        var length = 0;

        foreach (var ch in sanitized)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                if (length == 0) return null;
                break;
            }

            if (ch == '\uFFFD')
            {
                if (length == 0) return null;
                break;
            }

            if (char.IsControl(ch))
            {
                if (length == 0) return null;
                break;
            }

            length++;
            if (char.IsLetter(ch)) onlyNumbers = false;
            if (char.GetUnicodeCategory(ch) == UnicodeCategory.OtherLetter)
            {
                hasHan = true;
            }
        }

        var trimmed = sanitized[..length];
        if (trimmed.Length == 0) return null;
        if (trimmed.Length < 3 && !hasHan) return null;
        if (onlyNumbers) return null;
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]) && !hasHan) return null;

        return new string(trimmed);
    }

    public static string? SanitizeStrict(string nickname)
    {
        var sanitized = Sanitize(nickname);
        if (sanitized is null)
        {
            return null;
        }

        var rawSource = GetNullTerminatedPrefix(nickname);
        if (rawSource.Length == 0)
        {
            return null;
        }

        if (rawSource.Contains('\uFFFD'))
        {
            return null;
        }

        if (!rawSource.SequenceEqual(sanitized))
        {
            return null;
        }

        return sanitized;
    }

    private static ReadOnlySpan<char> GetNullTerminatedPrefix(string nickname)
    {
        var span = nickname.AsSpan();
        var terminator = span.IndexOf('\0');
        return terminator >= 0 ? span[..terminator] : span;
    }
}
