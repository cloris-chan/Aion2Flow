using System.Buffers;
using System.Globalization;
using System.Text;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal static class NicknameSanitizer
{
    public static string? SanitizeExact(string nickname)
    {
        if (string.IsNullOrEmpty(nickname))
            return null;

        var text = nickname.AsSpan();
        for (var offset = 0; offset < text.Length;)
        {
            if (Rune.DecodeFromUtf16(text[offset..], out var rune, out var charsConsumed) != OperationStatus.Done)
                return null;

            if (!IsIdentityRune(rune))
                return null;

            offset += charsConsumed;
        }

        return nickname;
    }

    private static bool IsIdentityRune(Rune rune)
        => rune.Value != 0xfffd &&
           Rune.GetUnicodeCategory(rune) is
               UnicodeCategory.UppercaseLetter or
               UnicodeCategory.LowercaseLetter or
               UnicodeCategory.TitlecaseLetter or
               UnicodeCategory.ModifierLetter or
               UnicodeCategory.OtherLetter or
               UnicodeCategory.DecimalDigitNumber or
               UnicodeCategory.LetterNumber or
               UnicodeCategory.OtherNumber;
}
