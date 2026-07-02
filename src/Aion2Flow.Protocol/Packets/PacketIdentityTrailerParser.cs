namespace Cloris.Aion2Flow.Protocol.Packets;

internal static class PacketIdentityTrailerParser
{
    public static bool TryReadNicknameAdjacentLegionBlock(ReadOnlySpan<byte> payload, int offset, out string legionName, out byte factionCode, out int tailOffset)
    {
        legionName = string.Empty;
        factionCode = 0;
        tailOffset = 0;

        if (!NicknameParserUtil.TryReadLengthPrefixedIdentityText(payload, offset, out var parsedLegionName, out _, out var legionTail) ||
            legionTail + 1 >= payload.Length ||
            payload[legionTail + 1] != 0x00)
        {
            return false;
        }

        var parsedFactionCode = NicknameParserUtil.TryReadFactionCode(payload, legionTail);
        if (parsedFactionCode == 0)
        {
            return false;
        }

        legionName = parsedLegionName;
        factionCode = parsedFactionCode;
        tailOffset = legionTail + 2;
        return true;
    }
}
