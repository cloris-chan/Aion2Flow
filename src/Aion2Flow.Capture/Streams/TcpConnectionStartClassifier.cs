using System.Buffers;

namespace Cloris.Aion2Flow.Capture.Streams;

internal sealed class TcpConnectionStartClassifier
{
    private readonly byte[] _prefix = new byte[CapturedNonAionPayload.TlsHeaderLength];
    private TcpStreamKind _kind;
    private int _prefixLength;

    public TcpConnectionStartResult Classify(ReadOnlySpan<byte> payload)
    {
        if (_kind == TcpStreamKind.NonGame)
        {
            return TcpConnectionStartResult.NonGame;
        }

        if (_kind == TcpStreamKind.Game)
        {
            return TcpConnectionStartResult.Game;
        }

        if (_prefixLength != 0)
        {
            return ClassifyWithPrefix(payload);
        }

        if (CapturedNonAionPayload.IsNonGameConnectionStart(payload))
        {
            _kind = TcpStreamKind.NonGame;
            return TcpConnectionStartResult.NonGame;
        }

        if (CapturedNonAionPayload.IsPotentialNonGameConnectionStart(payload))
        {
            StorePrefix(payload);
            return TcpConnectionStartResult.Pending;
        }

        _kind = TcpStreamKind.Game;
        return TcpConnectionStartResult.Game;
    }

    public void MarkGameStream()
    {
        _kind = TcpStreamKind.Game;
        _prefixLength = 0;
    }

    private TcpConnectionStartResult ClassifyWithPrefix(ReadOnlySpan<byte> payload)
    {
        var requiredBytes = CapturedNonAionPayload.TlsHeaderLength - _prefixLength;
        var copyLength = Math.Min(requiredBytes, payload.Length);
        payload[..copyLength].CopyTo(_prefix.AsSpan(_prefixLength));
        _prefixLength += copyLength;

        var header = _prefix.AsSpan(0, _prefixLength);
        if (CapturedNonAionPayload.IsNonGameConnectionStart(header))
        {
            _kind = TcpStreamKind.NonGame;
            _prefixLength = 0;
            return TcpConnectionStartResult.NonGame;
        }

        if (CapturedNonAionPayload.IsPotentialNonGameConnectionStart(header))
        {
            return TcpConnectionStartResult.Pending;
        }

        var acceptedLength = _prefixLength + payload.Length - copyLength;
        var acceptedOwner = MemoryPool<byte>.Shared.Rent(acceptedLength);
        var accepted = acceptedOwner.Memory.Span[..acceptedLength];
        header.CopyTo(accepted);
        payload[copyLength..].CopyTo(accepted[_prefixLength..]);
        _prefixLength = 0;
        _kind = TcpStreamKind.Game;
        return TcpConnectionStartResult.GameWithOwnedPayload(acceptedOwner, acceptedLength);
    }

    private void StorePrefix(ReadOnlySpan<byte> payload)
    {
        payload.CopyTo(_prefix);
        _prefixLength = payload.Length;
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

    private TcpConnectionStartResult(TcpConnectionStartKind kind, int acceptedLength, IMemoryOwner<byte>? acceptedOwner)
    {
        Kind = kind;
        AcceptedLength = acceptedLength;
        _acceptedOwner = acceptedOwner;
    }

    public TcpConnectionStartKind Kind { get; }

    public int AcceptedLength { get; }

    public static TcpConnectionStartResult Pending { get; } = new(TcpConnectionStartKind.Pending, 0, null);

    public static TcpConnectionStartResult NonGame { get; } = new(TcpConnectionStartKind.NonGame, 0, null);

    public static TcpConnectionStartResult Game { get; } = new(TcpConnectionStartKind.Game, 0, null);

    public static TcpConnectionStartResult GameWithOwnedPayload(IMemoryOwner<byte> owner, int payloadLength) =>
        new(TcpConnectionStartKind.Game, payloadLength, owner);

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
