using System.Buffers.Binary;
using System.Text;

namespace Cloris.Aion2Flow.Protocol.Packets;

internal static class NicknameParserUtil
{
    public const int MaxNicknameLength = 72;

    public static bool TryReadLengthPrefixedNickname(ReadOnlySpan<byte> packet, int lengthOffset, out string nickname, out int nicknameLength, out int tailOffset)
        => TryReadLengthPrefixedIdentityText(packet, lengthOffset, out nickname, out nicknameLength, out tailOffset);

    public static bool TryReadLengthPrefixedIdentityText(ReadOnlySpan<byte> packet, int lengthOffset, out string text, out int textLength, out int tailOffset)
    {
        text = string.Empty;
        textLength = 0;
        tailOffset = 0;

        if ((uint)lengthOffset >= (uint)packet.Length)
        {
            return false;
        }

        textLength = packet[lengthOffset];
        if (textLength is < 1 or > MaxNicknameLength)
        {
            return false;
        }

        var textOffset = lengthOffset + 1;
        if (textOffset + textLength > packet.Length)
        {
            return false;
        }

        var decoded = Encoding.UTF8.GetString(packet.Slice(textOffset, textLength));
        var sanitized = NicknameSanitizer.SanitizeExact(decoded);
        if (sanitized is null)
        {
            return false;
        }

        text = sanitized;
        tailOffset = textOffset + textLength;
        return true;
    }

    public static bool TryReadOriginServerIdLe16(ReadOnlySpan<byte> packet, int offset, out int originServerId)
    {
        originServerId = 0;

        if (offset < 0 || offset + sizeof(ushort) > packet.Length)
        {
            return false;
        }

        var value = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(offset, sizeof(ushort)));
        if (value is < 1000 or >= 3000)
            return false;

        originServerId = value;
        return true;
    }

    public static byte SelectFactionCode(byte directFactionCode, int? originServerId)
        => directFactionCode is 1 or 2 ? directFactionCode : TryInferFactionCodeFromOriginServerId(originServerId);

    public static byte TryInferFactionCodeFromOriginServerId(int? originServerId)
        => originServerId switch
        {
            >= 1000 and < 2000 => 1,
            >= 2000 and < 3000 => 2,
            _ => 0
        };

    public static byte TryReadFactionCode(ReadOnlySpan<byte> packet, int offset)
    {
        if ((uint)offset >= (uint)packet.Length)
        {
            return 0;
        }

        return packet[offset] is 1 or 2 ? packet[offset] : (byte)0;
    }

    public static int? TryReadClassCode(ReadOnlySpan<byte> packet, int offset)
    {
        if (offset < 0 || offset + sizeof(int) > packet.Length)
            return null;

        return BinaryPrimitives.TryReadInt32LittleEndian(packet.Slice(offset, sizeof(int)), out var value) && IsCurrentClassCode(value) ? value : null;
    }

    private static bool IsCurrentClassCode(int value) => value is >= 5 and <= 36 or >= 45 and <= 48;
}
