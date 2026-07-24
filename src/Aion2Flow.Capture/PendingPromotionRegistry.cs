using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Capture;

internal sealed class PendingPromotionRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<TcpConnection, CaptureConnectionPromotion> _promotions = [];

    internal void Register(
        in TcpConnection connection,
        CaptureConnectionPromotion promotion)
    {
        ArgumentNullException.ThrowIfNull(promotion);
        CaptureConnectionPromotion? replacedPromotion;
        lock (_gate)
        {
            _promotions.TryGetValue(connection, out replacedPromotion);
            _promotions[connection] = promotion;
        }

        if (replacedPromotion is not null &&
            !ReferenceEquals(replacedPromotion, promotion))
        {
            replacedPromotion.TryCancelQueued();
        }
    }

    internal bool TryGetForPayload(
        in TcpConnection connection,
        long expectedConnectionOrdinal,
        out CaptureConnectionPromotion? promotion)
    {
        lock (_gate)
        {
            if (_promotions.TryGetValue(connection, out var current) &&
                (expectedConnectionOrdinal <= 0 ||
                 current.CandidateOrdinal == expectedConnectionOrdinal))
            {
                promotion = current;
                return true;
            }
        }

        promotion = null;
        return false;
    }

    internal bool TryGetForClose(
        in TcpConnection connection,
        long packetConnectionOrdinal,
        out CaptureConnectionPromotion? promotion)
    {
        lock (_gate)
        {
            _promotions.TryGetValue(connection, out var directPromotion);
            _promotions.TryGetValue(connection.Reverse(), out var reversePromotion);
            promotion = SelectForClose(
                packetConnectionOrdinal,
                directPromotion,
                reversePromotion);
            return promotion is not null;
        }
    }

    internal bool CancelForSupersededAttempt(in TcpConnection connection)
    {
        if (!TryRemoveAtKey(in connection, expectedPromotion: null, out var promotion))
        {
            return false;
        }

        return promotion!.TryCancelQueued();
    }

    internal bool DetachAfterQueuedClose(
        in TcpConnection connection,
        CaptureConnectionPromotion expectedPromotion) =>
        TryRemoveAcrossTuple(in connection, expectedPromotion, out _);

    internal bool CancelAfterFailedClose(
        in TcpConnection connection,
        CaptureConnectionPromotion expectedPromotion)
    {
        if (!TryRemoveAcrossTuple(in connection, expectedPromotion, out var promotion))
        {
            return false;
        }

        return promotion!.TryCancelQueued();
    }

    internal void CancelUnselectedForClose(
        in TcpConnection connection,
        long expectedConnectionOrdinal,
        CaptureConnectionPromotion? selectedPromotion)
    {
        CaptureConnectionPromotion? directCancelled;
        CaptureConnectionPromotion? reverseCancelled;
        lock (_gate)
        {
            var directConnection = connection;
            var reverseConnection = connection.Reverse();
            directCancelled = RemoveCloseMatchLocked(
                in directConnection,
                expectedConnectionOrdinal,
                selectedPromotion);
            reverseCancelled = directConnection == reverseConnection
                ? null
                : RemoveCloseMatchLocked(
                    in reverseConnection,
                    expectedConnectionOrdinal,
                    selectedPromotion);
        }

        directCancelled?.TryCancelQueued();
        reverseCancelled?.TryCancelQueued();
    }

    internal bool DetachPromotion(CaptureConnectionPromotion expectedPromotion)
    {
        var connection = expectedPromotion.Connection;
        return TryRemoveAtKey(in connection, expectedPromotion, out _);
    }

    internal void CancelAll()
    {
        CaptureConnectionPromotion[] promotions;
        lock (_gate)
        {
            if (_promotions.Count == 0)
            {
                return;
            }

            promotions = [.. _promotions.Values];
            _promotions.Clear();
        }

        foreach (var promotion in promotions)
        {
            promotion.TryCancelQueued();
        }
    }

    private bool TryRemoveAcrossTuple(
        in TcpConnection connection,
        CaptureConnectionPromotion? expectedPromotion,
        out CaptureConnectionPromotion? promotion)
    {
        lock (_gate)
        {
            if (TryRemoveAtKeyLocked(
                    in connection,
                    expectedPromotion,
                    out promotion))
            {
                return true;
            }

            var reverseConnection = connection.Reverse();
            return reverseConnection != connection &&
                   TryRemoveAtKeyLocked(
                       in reverseConnection,
                       expectedPromotion,
                       out promotion);
        }
    }

    private bool TryRemoveAtKey(
        in TcpConnection connection,
        CaptureConnectionPromotion? expectedPromotion,
        out CaptureConnectionPromotion? promotion)
    {
        lock (_gate)
        {
            return TryRemoveAtKeyLocked(
                in connection,
                expectedPromotion,
                out promotion);
        }
    }

    private bool TryRemoveAtKeyLocked(
        in TcpConnection connection,
        CaptureConnectionPromotion? expectedPromotion,
        out CaptureConnectionPromotion? promotion)
    {
        if (!_promotions.TryGetValue(connection, out var current) ||
            (expectedPromotion is not null &&
             !ReferenceEquals(current, expectedPromotion)))
        {
            promotion = null;
            return false;
        }

        _promotions.Remove(connection);
        promotion = current;
        return true;
    }

    private CaptureConnectionPromotion? RemoveCloseMatchLocked(
        in TcpConnection connection,
        long expectedConnectionOrdinal,
        CaptureConnectionPromotion? selectedPromotion)
    {
        if (!_promotions.TryGetValue(connection, out var promotion) ||
            ReferenceEquals(promotion, selectedPromotion) ||
            expectedConnectionOrdinal > 0 && promotion.CandidateOrdinal != expectedConnectionOrdinal)
        {
            return null;
        }

        _promotions.Remove(connection);
        return promotion;
    }

    private static CaptureConnectionPromotion? SelectForClose(
        long packetConnectionOrdinal,
        CaptureConnectionPromotion? directPromotion,
        CaptureConnectionPromotion? reversePromotion)
    {
        if (directPromotion is not null &&
            (packetConnectionOrdinal <= 0 ||
             directPromotion.CandidateOrdinal == packetConnectionOrdinal))
        {
            return directPromotion;
        }

        if (reversePromotion is not null &&
            (packetConnectionOrdinal <= 0 ||
             reversePromotion.CandidateOrdinal == packetConnectionOrdinal))
        {
            return reversePromotion;
        }

        return null;
    }
}
