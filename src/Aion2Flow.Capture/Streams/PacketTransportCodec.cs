using System.Buffers;
using System.Buffers.Binary;
using Cloris.Aion2Flow.Protocol.Readers;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketTransportCodec
{
    public const int LengthPrefixedHeaderLength = sizeof(int);
    public const int MaximumEnvelopeBodyLength =
        CaptureBufferLimits.StreamTailBufferSize - LengthPrefixedHeaderLength;
    public static ReadOnlySpan<byte> Pattern => [0x06, 0x00, 0x36];

    public static bool TryWriteVarInt(int value, Span<byte> destination, out int written)
    {
        written = 0;
        var num = value;
        while ((uint)num > 0x7fu)
        {
            if (written >= destination.Length) return false;
            destination[written++] = (byte)((num & 0x7f) | 0x80);
            num >>= 7;
        }

        if (written >= destination.Length) return false;
        destination[written++] = (byte)num;
        return true;
    }

    public static bool TryReadVarInt(ReadOnlySpan<byte> bytes, int offset, out PacketVarIntReadResult result)
    {
        var value = 0;
        var shift = 0;
        var count = 0;

        while (true)
        {
            if (offset + count >= bytes.Length)
            {
                result = default;
                return false;
            }

            var byteVal = bytes[offset + count] & 0xff;
            count++;

            value |= (byteVal & 0x7f) << shift;

            if ((byteVal & 0x80) == 0)
            {
                result = new PacketVarIntReadResult(value, count);
                return true;
            }

            shift += 7;
            if (shift >= 32 || count > 5)
            {
                result = default;
                return false;
            }
        }
    }

    public static bool TryReadTransportLength(ReadOnlySpan<byte> bytes, int offset, out int packetLength)
    {
        packetLength = 0;
        if ((uint)offset > (uint)bytes.Length)
        {
            return false;
        }

        var prefix = ReadCanonicalLengthPrefix(bytes[offset..]);
        if (prefix.Kind != PacketCanonicalFrameProbeKind.Complete)
        {
            return false;
        }

        packetLength = prefix.FrameLength;
        return true;
    }

    public static PacketCanonicalFrameProbe ProbeCanonicalFrame(ReadOnlySpan<byte> payload, int maximumFrameLength)
    {
        var prefix = ReadCanonicalLengthPrefix(payload);
        if (prefix.Kind == PacketCanonicalFrameProbeKind.NeedMore)
        {
            return PacketCanonicalFrameProbe.NeedMore;
        }

        if (prefix.Kind == PacketCanonicalFrameProbeKind.Invalid ||
            prefix.FrameLength < prefix.PrefixLength + sizeof(ushort) ||
            prefix.FrameLength > maximumFrameLength)
        {
            return PacketCanonicalFrameProbe.Invalid;
        }

        return payload.Length < prefix.FrameLength
            ? new PacketCanonicalFrameProbe(
                PacketCanonicalFrameProbeKind.NeedMore,
                prefix.FrameLength,
                prefix.PrefixLength)
            : new PacketCanonicalFrameProbe(
                PacketCanonicalFrameProbeKind.Complete,
                prefix.FrameLength,
                prefix.PrefixLength);
    }

    public static PacketLengthPrefixedProbe ProbeLengthPrefixedStream(
        ReadOnlySpan<byte> payload,
        int maximumEnvelopeLength)
    {
        if (payload.Length < LengthPrefixedHeaderLength)
        {
            var partialValue = 0u;
            for (var index = 0; index < payload.Length; index++)
            {
                partialValue |= (uint)payload[index] << (index * 8);
            }

            return partialValue > maximumEnvelopeLength
                ? PacketLengthPrefixedProbe.Invalid
                : PacketLengthPrefixedProbe.NeedMore;
        }

        var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(payload);
        if (bodyLength <= 0 || bodyLength > maximumEnvelopeLength)
        {
            return PacketLengthPrefixedProbe.Invalid;
        }

        var nextHeaderOffset = checked(LengthPrefixedHeaderLength + bodyLength);
        if (payload.Length < nextHeaderOffset)
        {
            return PacketLengthPrefixedProbe.NeedMore;
        }

        var body = payload.Slice(LengthPrefixedHeaderLength, bodyLength);
        if (IsCompleteRecognizedCanonicalSequence(body, CaptureBufferLimits.StreamTailBufferSize))
        {
            return new PacketLengthPrefixedProbe(
                PacketLengthPrefixedProbeKind.Complete,
                0,
                -1);
        }

        if (!HasRecognizedCanonicalStart(body, CaptureBufferLimits.StreamTailBufferSize))
        {
            return ProbeUnalignedLengthPrefixedStream(payload, maximumEnvelopeLength);
        }

        var directTickProbe = ProbeDirectTickAfterEnvelopeBoundary(
            payload,
            nextHeaderOffset);
        if (directTickProbe.Kind == DirectTickProbeKind.Complete)
        {
            return new PacketLengthPrefixedProbe(
                PacketLengthPrefixedProbeKind.Ambiguous,
                0,
                directTickProbe.Offset);
        }

        if (directTickProbe.Kind == DirectTickProbeKind.NeedMore)
        {
            return PacketLengthPrefixedProbe.NeedMore;
        }

        if (payload.Length < nextHeaderOffset + LengthPrefixedHeaderLength)
        {
            return PacketLengthPrefixedProbe.NeedMore;
        }

        var nextBodyLength = BinaryPrimitives.ReadInt32LittleEndian(payload[nextHeaderOffset..]);
        if (nextBodyLength <= 0 || nextBodyLength > maximumEnvelopeLength)
        {
            return PacketLengthPrefixedProbe.Invalid;
        }

        return new PacketLengthPrefixedProbe(PacketLengthPrefixedProbeKind.Complete, 0, -1);
    }

    public static PacketLengthPrefixedBoundaryProbe ProbeLengthPrefixedStreamBoundary(
        ReadOnlySpan<byte> payload,
        int maximumEnvelopeLength)
    {
        var pending = PacketLengthPrefixedBoundaryProbe.None;
        var pendingNextHeaderOffset = -1;
        for (var candidateOffset = 0;
             candidateOffset + LengthPrefixedHeaderLength <= payload.Length;
             candidateOffset++)
        {
            var candidate = payload[candidateOffset..];
            var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(candidate);
            if (bodyLength <= 0 || bodyLength > maximumEnvelopeLength)
            {
                continue;
            }

            var probe = ProbeLengthPrefixedStream(candidate, maximumEnvelopeLength);
            if (probe.Kind == PacketLengthPrefixedProbeKind.Complete)
            {
                if (candidateOffset == pendingNextHeaderOffset)
                {
                    continue;
                }

                return new PacketLengthPrefixedBoundaryProbe(
                    PacketLengthPrefixedBoundaryProbeKind.Complete,
                    candidateOffset,
                    probe.CanonicalPrefixLength,
                    -1);
            }

            if (probe.Kind == PacketLengthPrefixedProbeKind.Ambiguous &&
                pending.Kind == PacketLengthPrefixedBoundaryProbeKind.None)
            {
                pending = new PacketLengthPrefixedBoundaryProbe(
                    PacketLengthPrefixedBoundaryProbeKind.Ambiguous,
                    candidateOffset,
                    0,
                    checked(candidateOffset + probe.DirectRecoveryTickOffset));
                continue;
            }

            if (probe.Kind == PacketLengthPrefixedProbeKind.NeedMore &&
                pending.Kind == PacketLengthPrefixedBoundaryProbeKind.None &&
                TryGetPotentialNextEnvelopeOffset(
                    candidate,
                    bodyLength,
                    maximumEnvelopeLength,
                    out var nextHeaderOffset))
            {
                pending = new PacketLengthPrefixedBoundaryProbe(
                    PacketLengthPrefixedBoundaryProbeKind.Pending,
                    candidateOffset,
                    0,
                    -1);
                pendingNextHeaderOffset = checked(candidateOffset + nextHeaderOffset);
            }

        }

        return pending;
    }

    public static bool HasIncompleteLengthPrefixedEnvelopeContainingRange(
        ReadOnlySpan<byte> payload,
        int rangeOffset,
        int rangeLength,
        int maximumEnvelopeLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rangeOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(rangeLength);
        if (rangeOffset > payload.Length - rangeLength)
        {
            return false;
        }

        var rangeEnd = checked(rangeOffset + rangeLength);
        for (var candidateOffset = 0;
             candidateOffset + LengthPrefixedHeaderLength <= rangeOffset;
             candidateOffset++)
        {
            var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(payload[candidateOffset..]);
            if (bodyLength <= 0 || bodyLength > maximumEnvelopeLength)
            {
                continue;
            }

            var bodyOffset = checked(candidateOffset + LengthPrefixedHeaderLength);
            var bodyEnd = (long)bodyOffset + bodyLength;
            if (bodyOffset <= rangeOffset && rangeEnd <= bodyEnd && bodyEnd > payload.Length)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsKnownGameplayOpcode(byte opcode0, byte opcode1)
    {
        return (opcode0, opcode1) switch
        {
            (0x29, 0x33) or
            (0x00, 0x36) or (0x03, 0x36) or (0x11, 0x36) or (0x15, 0x36) or
            (0x21, 0x36) or (0x23, 0x36) or (0x33, 0x36) or (0x40, 0x36) or
            (0x41, 0x36) or (0x45, 0x36) or (0x46, 0x36) or (0x49, 0x36) or
            (0x4a, 0x36) or (0x4b, 0x36) or
            (0x1d, 0x37) or
            (0x02, 0x38) or (0x03, 0x38) or (0x04, 0x38) or (0x05, 0x38) or
            (0x06, 0x38) or (0x2a, 0x38) or (0x2b, 0x38) or (0x2c, 0x38) or
            (0x35, 0x38) or
            (0x01, 0x40) or (0x02, 0x40) or
            (0x84, 0x56) or
            (0x00, 0x61) or (0x01, 0x61) or
            (0x00, 0x8d) or (0x04, 0x8d) or (0x21, 0x8d) or
            (0x00, 0x92) or (0x0d, 0x92) or (0x1b, 0x92) or (0x2e, 0x92) or
            (0x2f, 0x92) or
            (0x09, 0x94) or (0x0b, 0x94) or
            (0x02, 0x96) or (0x0a, 0x96) or (0x1b, 0x96) or (0x1d, 0x96) or
            (0x1e, 0x96) or (0x2b, 0x96) => true,
            _ => false
        };
    }

    public static bool HasRecognizedCanonicalFrameStart(
        ReadOnlySpan<byte> payload,
        in PacketCanonicalFrameProbe frame)
    {
        if (frame.Kind == PacketCanonicalFrameProbeKind.Invalid ||
            frame.PrefixLength == 0 ||
            payload.Length < frame.PrefixLength + sizeof(ushort))
        {
            return false;
        }

        var opcode0 = payload[frame.PrefixLength];
        var opcode1 = payload[frame.PrefixLength + 1];
        return (opcode0 == 0xff && opcode1 == 0xff) ||
               opcode1 == 0x39 ||
               IsKnownGameplayOpcode(opcode0, opcode1);
    }

    private static PacketCanonicalFrameProbe ReadCanonicalLengthPrefix(ReadOnlySpan<byte> payload)
    {
        ulong value = 0;
        for (var index = 0; index < 5; index++)
        {
            if (index >= payload.Length)
            {
                return PacketCanonicalFrameProbe.NeedMore;
            }

            var current = payload[index];
            if (index == 4 && (current & 0xf0) != 0)
            {
                return PacketCanonicalFrameProbe.Invalid;
            }

            value |= (ulong)(current & 0x7f) << (index * 7);
            if ((current & 0x80) != 0)
            {
                continue;
            }

            var prefixLength = index + 1;
            if (prefixLength > 1 && value < (1UL << ((prefixLength - 1) * 7)))
            {
                return PacketCanonicalFrameProbe.Invalid;
            }

            var frameLength = (long)value + prefixLength - 4;
            if (frameLength <= 0 || frameLength > int.MaxValue)
            {
                return PacketCanonicalFrameProbe.Invalid;
            }

            return new PacketCanonicalFrameProbe(
                PacketCanonicalFrameProbeKind.Complete,
                (int)frameLength,
                prefixLength);
        }

        return PacketCanonicalFrameProbe.Invalid;
    }

    private static bool IsCompleteRecognizedCanonicalSequence(
        ReadOnlySpan<byte> body,
        int maximumFrameLength)
    {
        var firstFrame = ProbeCanonicalFrame(body, maximumFrameLength);
        if (firstFrame.Kind != PacketCanonicalFrameProbeKind.Complete ||
            !HasRecognizedCanonicalFrameStart(body, in firstFrame))
        {
            return false;
        }

        var consumed = firstFrame.FrameLength;
        while (consumed < body.Length)
        {
            var frame = ProbeCanonicalFrame(body[consumed..], maximumFrameLength);
            if (frame.Kind != PacketCanonicalFrameProbeKind.Complete)
            {
                return false;
            }

            consumed += frame.FrameLength;
        }

        return consumed == body.Length;
    }

    private static bool HasRecognizedCanonicalStart(
        ReadOnlySpan<byte> body,
        int maximumFrameLength)
    {
        var frame = ProbeCanonicalFrame(body, maximumFrameLength);
        return HasRecognizedCanonicalFrameStart(body, in frame);
    }

    private static DirectTickProbe ProbeDirectTickAfterEnvelopeBoundary(
        ReadOnlySpan<byte> payload,
        int envelopeEndOffset)
    {
        const int canonicalStartOffset = LengthPrefixedHeaderLength;
        var canonical = payload[canonicalStartOffset..];
        var offset = 0;
        var crossedEnvelopeBoundary = false;
        while (offset < canonical.Length)
        {
            var frame = ProbeCanonicalFrame(canonical[offset..], CaptureBufferLimits.StreamTailBufferSize);
            if (frame.Kind == PacketCanonicalFrameProbeKind.Invalid ||
                frame.PrefixLength == 0 ||
                canonical.Length < offset + frame.PrefixLength + sizeof(ushort))
            {
                return DirectTickProbe.None;
            }

            var frameStart = canonicalStartOffset + offset;
            var frameEnd = checked(frameStart + frame.FrameLength);
            var crossesEnvelopeBoundary =
                frameStart < envelopeEndOffset && envelopeEndOffset < frameEnd;
            if (!HasRecognizedCanonicalFrameStart(canonical[offset..], in frame))
            {
                return DirectTickProbe.None;
            }

            if (frame.Kind != PacketCanonicalFrameProbeKind.Complete)
            {
                return crossesEnvelopeBoundary || crossedEnvelopeBoundary
                    ? DirectTickProbe.NeedMore
                    : DirectTickProbe.None;
            }

            var opcodeOffset = offset + frame.PrefixLength;
            var isTick = canonical[opcodeOffset] == 0x00 &&
                         canonical[opcodeOffset + 1] == 0x36 &&
                         frame.FrameLength == 11;
            if (crossesEnvelopeBoundary)
            {
                if (isTick)
                {
                    return new DirectTickProbe(DirectTickProbeKind.Complete, frameStart);
                }

                crossedEnvelopeBoundary = true;
            }
            else if (crossedEnvelopeBoundary && frameStart >= envelopeEndOffset && isTick)
            {
                return new DirectTickProbe(DirectTickProbeKind.Complete, frameStart);
            }

            offset = frameEnd - canonicalStartOffset;
        }

        return crossedEnvelopeBoundary
            ? DirectTickProbe.NeedMore
            : DirectTickProbe.None;
    }

    private static PacketLengthPrefixedProbe ProbeUnalignedLengthPrefixedStream(
        ReadOnlySpan<byte> payload,
        int maximumEnvelopeLength)
    {
        const int minimumEnvelopeCount = 3;
        var rawOffset = 0;
        var canonicalLength = 0;
        var envelopeCount = 0;
        while (rawOffset < payload.Length)
        {
            if (payload.Length - rawOffset < LengthPrefixedHeaderLength)
            {
                break;
            }

            var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(payload[rawOffset..]);
            if (bodyLength <= 0 || bodyLength > maximumEnvelopeLength)
            {
                return PacketLengthPrefixedProbe.Invalid;
            }

            var bodyOffset = checked(rawOffset + LengthPrefixedHeaderLength);
            if (payload.Length - bodyOffset < bodyLength)
            {
                break;
            }

            canonicalLength = checked(canonicalLength + bodyLength);
            envelopeCount++;
            rawOffset = checked(bodyOffset + bodyLength);
        }

        if (envelopeCount < minimumEnvelopeCount)
        {
            return PacketLengthPrefixedProbe.NeedMore;
        }

        var canonicalOwner = ArrayPool<byte>.Shared.Rent(canonicalLength);
        try
        {
            var canonical = canonicalOwner.AsSpan(0, canonicalLength);
            var canonicalOffset = 0;
            rawOffset = 0;
            for (var envelopeIndex = 0; envelopeIndex < envelopeCount; envelopeIndex++)
            {
                var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(payload[rawOffset..]);
                var bodyOffset = checked(rawOffset + LengthPrefixedHeaderLength);
                payload.Slice(bodyOffset, bodyLength).CopyTo(canonical[canonicalOffset..]);
                canonicalOffset += bodyLength;
                rawOffset = checked(bodyOffset + bodyLength);
            }

            return TryFindRecognizedCanonicalPair(canonical, out var prefixLength)
                ? new PacketLengthPrefixedProbe(
                    PacketLengthPrefixedProbeKind.Complete,
                    prefixLength,
                    -1)
                : PacketLengthPrefixedProbe.NeedMore;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(canonicalOwner);
        }
    }

    private static bool TryFindRecognizedCanonicalPair(
        ReadOnlySpan<byte> canonical,
        out int prefixLength)
    {
        prefixLength = 0;
        for (var offset = 0; offset < canonical.Length; offset++)
        {
            var first = ProbeCanonicalFrame(
                canonical[offset..],
                CaptureBufferLimits.StreamTailBufferSize);
            if (first.Kind != PacketCanonicalFrameProbeKind.Complete ||
                !HasRecognizedCanonicalFrameStart(canonical[offset..], in first))
            {
                continue;
            }

            var secondOffset = checked(offset + first.FrameLength);
            if (secondOffset >= canonical.Length)
            {
                continue;
            }

            var second = ProbeCanonicalFrame(
                canonical[secondOffset..],
                CaptureBufferLimits.StreamTailBufferSize);
            if (second.Kind != PacketCanonicalFrameProbeKind.Complete ||
                !HasRecognizedCanonicalFrameStart(canonical[secondOffset..], in second))
            {
                continue;
            }

            prefixLength = offset;
            return true;
        }

        return false;
    }

    private static bool TryGetPotentialNextEnvelopeOffset(
        ReadOnlySpan<byte> payload,
        int bodyLength,
        int maximumEnvelopeLength,
        out int nextHeaderOffset)
    {
        nextHeaderOffset = checked(LengthPrefixedHeaderLength + bodyLength);
        if (payload.Length < nextHeaderOffset)
        {
            return false;
        }

        if (payload.Length - nextHeaderOffset < LengthPrefixedHeaderLength)
        {
            return true;
        }

        var nextBodyLength = BinaryPrimitives.ReadInt32LittleEndian(payload[nextHeaderOffset..]);
        return nextBodyLength > 0 && nextBodyLength <= maximumEnvelopeLength;
    }

}

internal enum PacketCanonicalFrameProbeKind : byte
{
    NeedMore,
    Invalid,
    Complete
}

internal readonly record struct PacketCanonicalFrameProbe(
    PacketCanonicalFrameProbeKind Kind,
    int FrameLength,
    int PrefixLength)
{
    public static PacketCanonicalFrameProbe NeedMore { get; } = new(
        PacketCanonicalFrameProbeKind.NeedMore,
        0,
        0);

    public static PacketCanonicalFrameProbe Invalid { get; } = new(
        PacketCanonicalFrameProbeKind.Invalid,
        0,
        0);
}

internal enum PacketLengthPrefixedProbeKind : byte
{
    NeedMore,
    Invalid,
    Ambiguous,
    Complete
}

internal readonly record struct PacketLengthPrefixedProbe(
    PacketLengthPrefixedProbeKind Kind,
    int CanonicalPrefixLength,
    int DirectRecoveryTickOffset)
{
    public static PacketLengthPrefixedProbe NeedMore { get; } = new(
        PacketLengthPrefixedProbeKind.NeedMore,
        0,
        -1);

    public static PacketLengthPrefixedProbe Invalid { get; } = new(
        PacketLengthPrefixedProbeKind.Invalid,
        0,
        -1);
}

internal enum DirectTickProbeKind : byte
{
    None,
    NeedMore,
    Complete
}

internal readonly record struct DirectTickProbe(DirectTickProbeKind Kind, int Offset)
{
    public static DirectTickProbe None { get; } = new(DirectTickProbeKind.None, -1);
    public static DirectTickProbe NeedMore { get; } = new(DirectTickProbeKind.NeedMore, -1);
}

internal enum PacketLengthPrefixedBoundaryProbeKind : byte
{
    None,
    Pending,
    Ambiguous,
    Complete
}

internal readonly record struct PacketLengthPrefixedBoundaryProbe(
    PacketLengthPrefixedBoundaryProbeKind Kind,
    int RawOffset,
    int CanonicalPrefixLength,
    int DirectRecoveryTickOffset)
{
    public static PacketLengthPrefixedBoundaryProbe None { get; } = new(
        PacketLengthPrefixedBoundaryProbeKind.None,
        0,
        0,
        -1);
}
