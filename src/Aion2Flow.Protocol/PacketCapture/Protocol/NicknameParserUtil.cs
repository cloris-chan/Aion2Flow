using System.Text;

namespace Cloris.Aion2Flow.PacketCapture.Protocol;

internal static class NicknameParserUtil
{
    public const int MaxNicknameLength = 72;
    private const int MaxOriginServerId = 10000;

    public static bool TryReadLengthPrefixedNickname(
        ReadOnlySpan<byte> packet,
        int lengthOffset,
        bool strict,
        out string nickname,
        out int nicknameLength,
        out int tailOffset)
    {
        nickname = string.Empty;
        nicknameLength = 0;
        tailOffset = 0;

        if ((uint)lengthOffset >= (uint)packet.Length)
        {
            return false;
        }

        nicknameLength = packet[lengthOffset];
        if (nicknameLength is < 1 or > MaxNicknameLength)
        {
            return false;
        }

        var nicknameOffset = lengthOffset + 1;
        if (nicknameOffset + nicknameLength > packet.Length)
        {
            return false;
        }

        var decoded = Encoding.UTF8.GetString(packet.Slice(nicknameOffset, nicknameLength));
        var sanitized = strict
            ? NicknameSanitizer.SanitizeStrict(decoded)
            : NicknameSanitizer.Sanitize(decoded);
        if (sanitized is null)
        {
            return false;
        }

        nickname = sanitized;
        tailOffset = nicknameOffset + nicknameLength;
        return true;
    }

    public static int? TryReadPossibleOriginServerBefore(ReadOnlySpan<byte> packet, int endOffset)
    {
        if (endOffset < 2)
        {
            return null;
        }

        var startOffset = endOffset - 2;
        if ((packet[startOffset] & 0x80) == 0 ||
            (packet[startOffset + 1] & 0x80) != 0)
        {
            return null;
        }

        return TryReadVarInt(packet, startOffset, out var value, out var byteCount) &&
               startOffset + byteCount == endOffset &&
               IsPossibleOriginServerId(value)
            ? value
            : null;
    }

    public static int? TryReadPossibleOriginServerAt(ReadOnlySpan<byte> packet, int offset)
        => TryReadPossibleOriginServerAt(packet, offset, out var originServerId, out _)
            ? originServerId
            : null;

    public static bool TryReadPossibleOriginServerAt(
        ReadOnlySpan<byte> packet,
        int offset,
        out int originServerId,
        out int byteCount)
    {
        originServerId = 0;
        byteCount = 0;

        if (offset < 0 || offset + 1 >= packet.Length)
        {
            return false;
        }

        if ((packet[offset] & 0x80) == 0 ||
            (packet[offset + 1] & 0x80) != 0)
        {
            return false;
        }

        if (TryReadVarInt(packet, offset, out var value, out byteCount) &&
            byteCount == 2 &&
            IsPossibleOriginServerId(value))
        {
            originServerId = value;
            return true;
        }

        byteCount = 0;
        return false;
    }

    private static bool TryReadVarInt(ReadOnlySpan<byte> bytes, int offset, out int value, out int byteCount)
    {
        value = 0;
        byteCount = 0;
        var shift = 0;

        while (true)
        {
            if (offset + byteCount >= bytes.Length)
            {
                value = 0;
                byteCount = 0;
                return false;
            }

            var byteVal = bytes[offset + byteCount] & 0xff;
            byteCount++;

            value |= (byteVal & 0x7f) << shift;
            if ((byteVal & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
            if (shift >= 32 || byteCount > 5)
            {
                value = 0;
                byteCount = 0;
                return false;
            }
        }
    }

    private static bool IsPossibleOriginServerId(int value)
        => value is > 0 and <= MaxOriginServerId;
}
