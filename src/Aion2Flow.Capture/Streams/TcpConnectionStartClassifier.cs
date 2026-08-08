using System.Buffers;

namespace Cloris.Aion2Flow.Capture.Streams;

internal sealed class TcpConnectionStartClassifier : IDisposable
{
    private readonly PacketTailBuffer _pending = new(CaptureBufferLimits.TransportRawBufferSize);
    private int _canonicalPrefixLength;
    private PacketTransportFraming _framing;
    private TcpStreamKind _kind;

    public TcpConnectionStartResult Classify(ReadOnlySpan<byte> payload)
    {
        if (_kind == TcpStreamKind.NonGame)
        {
            return TcpConnectionStartResult.NonGame;
        }

        if (_kind == TcpStreamKind.Game)
        {
            return TcpConnectionStartResult.Game(_framing, _canonicalPrefixLength);
        }

        if (_pending.Length == 0)
        {
            var initialLengthPrefixed = PacketTransportCodec.ProbeLengthPrefixedStream(
                payload,
                PacketTransportCodec.MaximumEnvelopeBodyLength);
            if (initialLengthPrefixed.Kind == PacketLengthPrefixedProbeKind.Complete)
            {
                return AcceptCurrent(
                    PacketTransportFraming.LengthPrefixed,
                    initialLengthPrefixed.CanonicalPrefixLength);
            }

            var isTls = CapturedNonAionPayload.IsNonGameConnectionStart(payload);
            var isPotentialTls = CapturedNonAionPayload.IsPotentialNonGameConnectionStart(payload);
            var canonical = PacketTransportCodec.ProbeCanonicalFrame(
                payload,
                CaptureBufferLimits.StreamTailBufferSize);
            if (canonical.Kind == PacketCanonicalFrameProbeKind.Complete &&
                PacketTransportCodec.HasRecognizedCanonicalFrameStart(payload, in canonical))
            {
                return AcceptCurrent(PacketTransportFraming.DirectAligned, 0);
            }

            if (initialLengthPrefixed.Kind == PacketLengthPrefixedProbeKind.Invalid)
            {
                if (isTls)
                {
                    _kind = TcpStreamKind.NonGame;
                    return TcpConnectionStartResult.NonGame;
                }

                if (isPotentialTls)
                {
                    _pending.Append(payload);
                    return TcpConnectionStartResult.Pending;
                }

                return AcceptCurrent(PacketTransportFraming.DirectRecovery, 0);
            }

        }

        if (_pending.Length + payload.Length > _pending.Capacity)
        {
            _pending.Clear();
            _kind = TcpStreamKind.NonGame;
            return TcpConnectionStartResult.NonGame;
        }

        _pending.Append(payload);
        var pending = _pending.Data;
        var lengthPrefixed = PacketTransportCodec.ProbeLengthPrefixedStream(
            pending,
            PacketTransportCodec.MaximumEnvelopeBodyLength);
        if (lengthPrefixed.Kind == PacketLengthPrefixedProbeKind.Complete)
        {
            return AcceptPending(
                PacketTransportFraming.LengthPrefixed,
                lengthPrefixed.CanonicalPrefixLength);
        }

        if (lengthPrefixed.Kind == PacketLengthPrefixedProbeKind.NeedMore)
        {
            var canonical = PacketTransportCodec.ProbeCanonicalFrame(
                pending,
                CaptureBufferLimits.StreamTailBufferSize);
            if (canonical.Kind == PacketCanonicalFrameProbeKind.Complete &&
                PacketTransportCodec.HasRecognizedCanonicalFrameStart(pending, in canonical))
            {
                return AcceptPending(PacketTransportFraming.DirectAligned, 0);
            }

            return TcpConnectionStartResult.Pending;
        }

        if (CapturedNonAionPayload.IsNonGameConnectionStart(pending))
        {
            _pending.Clear();
            _kind = TcpStreamKind.NonGame;
            return TcpConnectionStartResult.NonGame;
        }

        if (CapturedNonAionPayload.IsPotentialNonGameConnectionStart(pending))
        {
            return TcpConnectionStartResult.Pending;
        }

        return AcceptPending(PacketTransportFraming.DirectRecovery, 0);
    }

    public void MarkGameStream()
    {
        _kind = TcpStreamKind.Game;
        _pending.Clear();
    }

    public void Dispose()
    {
        _pending.Dispose();
    }

    private TcpConnectionStartResult AcceptCurrent(
        PacketTransportFraming framing,
        int canonicalPrefixLength)
    {
        _kind = TcpStreamKind.Game;
        _framing = framing;
        _canonicalPrefixLength = canonicalPrefixLength;
        return TcpConnectionStartResult.Game(framing, canonicalPrefixLength);
    }

    private TcpConnectionStartResult AcceptPending(
        PacketTransportFraming framing,
        int canonicalPrefixLength)
    {
        var acceptedLength = _pending.Length;
        var acceptedOwner = MemoryPool<byte>.Shared.Rent(acceptedLength);
        _pending.Data.CopyTo(acceptedOwner.Memory.Span);
        _pending.Clear();
        _kind = TcpStreamKind.Game;
        _framing = framing;
        _canonicalPrefixLength = canonicalPrefixLength;
        return TcpConnectionStartResult.GameWithOwnedPayload(
            acceptedOwner,
            acceptedLength,
            framing,
            canonicalPrefixLength);
    }

    private enum TcpStreamKind
    {
        Unknown,
        Game,
        NonGame
    }
}

internal readonly struct TcpConnectionStartResult
{
    private readonly IMemoryOwner<byte>? _acceptedOwner;

    private TcpConnectionStartResult(
        TcpConnectionStartKind kind,
        int acceptedLength,
        IMemoryOwner<byte>? acceptedOwner,
        PacketTransportFraming framing,
        int canonicalPrefixLength)
    {
        Kind = kind;
        AcceptedLength = acceptedLength;
        _acceptedOwner = acceptedOwner;
        Framing = framing;
        CanonicalPrefixLength = canonicalPrefixLength;
    }

    public TcpConnectionStartKind Kind { get; }

    public int AcceptedLength { get; }

    public PacketTransportFraming Framing { get; }

    public int CanonicalPrefixLength { get; }

    public static TcpConnectionStartResult Pending { get; } = new(
        TcpConnectionStartKind.Pending,
        0,
        null,
        PacketTransportFraming.Auto,
        0);

    public static TcpConnectionStartResult NonGame { get; } = new(
        TcpConnectionStartKind.NonGame,
        0,
        null,
        PacketTransportFraming.Auto,
        0);

    public static TcpConnectionStartResult Game(
        PacketTransportFraming framing,
        int canonicalPrefixLength) => new(
            TcpConnectionStartKind.Game,
            0,
            null,
            framing,
            canonicalPrefixLength);

    public static TcpConnectionStartResult GameWithOwnedPayload(
        IMemoryOwner<byte> owner,
        int payloadLength,
        PacketTransportFraming framing,
        int canonicalPrefixLength) =>
        new(
            TcpConnectionStartKind.Game,
            payloadLength,
            owner,
            framing,
            canonicalPrefixLength);

    public ReadOnlySpan<byte> ResolveAcceptedPayload(ReadOnlySpan<byte> originalPayload) =>
        _acceptedOwner is null
            ? originalPayload
            : _acceptedOwner.Memory.Span[..AcceptedLength];

    public void Return()
    {
        _acceptedOwner?.Dispose();
    }
}

internal enum TcpConnectionStartKind
{
    Pending,
    Game,
    NonGame
}
