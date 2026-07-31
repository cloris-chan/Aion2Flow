using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.SceneRuntime;

namespace Cloris.Aion2Flow.Capture;

public sealed class PendingPromotionRegistryTests
{
    [Fact]
    public void SupersededAttemptCancelsOnlyItsQueuedPromotion()
    {
        var connection = CreateConnection();
        var registry = new PendingPromotionRegistry();
        var promotion = CreatePromotion(connection, candidateOrdinal: 7);

        registry.Register(in connection, promotion);

        Assert.True(registry.CancelForSupersededAttempt(in connection));
        Assert.False(promotion.TryAcquireForDispatch());
        Assert.False(registry.TryGetForPayload(in connection, 7, out _));

        promotion.Return();
    }

    [Fact]
    public void SupersededAttemptDoesNotCancelPromotionWithTheCurrentOrdinal()
    {
        var connection = CreateConnection();
        var registry = new PendingPromotionRegistry();
        var promotion = CreatePromotion(connection, candidateOrdinal: 7);

        registry.Register(in connection, promotion);

        Assert.False(registry.CancelForSupersededAttempt(in connection, newerConnectionOrdinal: 7));
        Assert.True(registry.TryGetForPayload(in connection, 7, out var selected));
        Assert.Same(promotion, selected);

        promotion.Return();
    }

    [Fact]
    public void SupersededAttemptCancelsOnlyOlderPromotion()
    {
        var connection = CreateConnection();
        var registry = new PendingPromotionRegistry();
        var promotion = CreatePromotion(connection, candidateOrdinal: 7);

        registry.Register(in connection, promotion);

        Assert.True(registry.CancelForSupersededAttempt(in connection, newerConnectionOrdinal: 8));
        Assert.False(promotion.TryAcquireForDispatch());

        promotion.Return();
    }

    [Fact]
    public void CloseSelectionRequiresTheMatchingTupleAndOrdinal()
    {
        var connection = CreateConnection();
        var reverseConnection = connection.Reverse();
        var registry = new PendingPromotionRegistry();
        var promotion = CreatePromotion(connection, candidateOrdinal: 7);

        registry.Register(in connection, promotion);

        Assert.True(registry.TryGetForClose(in reverseConnection, 7, out var selected));
        Assert.Same(promotion, selected);
        Assert.False(registry.TryGetForClose(in reverseConnection, 8, out _));

        promotion.Return();
    }

    [Fact]
    public void QueuedCloseDetachesWithoutCancellingPromotion()
    {
        var connection = CreateConnection();
        var reverseConnection = connection.Reverse();
        var registry = new PendingPromotionRegistry();
        var promotion = CreatePromotion(connection, candidateOrdinal: 7);

        registry.Register(in connection, promotion);

        Assert.True(registry.DetachAfterQueuedClose(in reverseConnection, promotion));
        Assert.True(promotion.TryAcquireForDispatch());
        Assert.False(registry.TryGetForPayload(in connection, 7, out _));

        promotion.Return();
    }

    [Fact]
    public void FailedCloseCancelsOnlyTheExpectedPromotion()
    {
        var connection = CreateConnection();
        var reverseConnection = connection.Reverse();
        var registry = new PendingPromotionRegistry();
        var failedPromotion = CreatePromotion(connection, candidateOrdinal: 7);
        var currentPromotion = CreatePromotion(reverseConnection, candidateOrdinal: 8);

        registry.Register(in connection, failedPromotion);
        registry.Register(in reverseConnection, currentPromotion);

        Assert.True(registry.CancelAfterFailedClose(in reverseConnection, failedPromotion));
        Assert.False(failedPromotion.TryAcquireForDispatch());
        Assert.True(registry.TryGetForPayload(in reverseConnection, 8, out var selected));
        Assert.Same(currentPromotion, selected);

        failedPromotion.Return();
        currentPromotion.Return();
    }

    [Fact]
    public void CloseCleanupCancelsOnlyUnselectedMatchingOrdinal()
    {
        var connection = CreateConnection();
        var reverseConnection = connection.Reverse();
        var registry = new PendingPromotionRegistry();
        var selectedPromotion = CreatePromotion(connection, candidateOrdinal: 7);
        var otherPromotion = CreatePromotion(reverseConnection, candidateOrdinal: 7);

        registry.Register(in connection, selectedPromotion);
        registry.Register(in reverseConnection, otherPromotion);

        registry.CancelUnselectedForClose(
            in connection,
            expectedConnectionOrdinal: 7,
            selectedPromotion: selectedPromotion);

        Assert.True(selectedPromotion.TryAcquireForDispatch());
        Assert.False(otherPromotion.TryAcquireForDispatch());
        Assert.True(registry.DetachAfterQueuedClose(in connection, selectedPromotion));

        selectedPromotion.Return();
        otherPromotion.Return();
    }

    [Fact]
    public void CompletionCannotUntrackAReplacementPromotion()
    {
        var connection = CreateConnection();
        var registry = new PendingPromotionRegistry();
        var completedPromotion = CreatePromotion(connection, candidateOrdinal: 7);

        registry.Register(in connection, completedPromotion);
        Assert.True(registry.DetachPromotion(completedPromotion));

        var replacementPromotion = CreatePromotion(connection, candidateOrdinal: 8);
        registry.Register(in connection, replacementPromotion);

        Assert.False(registry.DetachPromotion(completedPromotion));
        Assert.True(registry.TryGetForPayload(in connection, 8, out var selected));
        Assert.Same(replacementPromotion, selected);

        completedPromotion.Return();
        replacementPromotion.Return();
    }

    [Fact]
    public void RegisterReplacesQueuedPromotionWithoutLeavingTheOldOwnerActive()
    {
        var connection = CreateConnection();
        var registry = new PendingPromotionRegistry();
        var firstPromotion = CreatePromotion(connection, candidateOrdinal: 7);
        var replacementPromotion = CreatePromotion(connection, candidateOrdinal: 8);

        registry.Register(in connection, firstPromotion);
        registry.Register(in connection, replacementPromotion);

        Assert.False(firstPromotion.TryAcquireForDispatch());
        Assert.True(registry.TryGetForPayload(in connection, 8, out var selected));
        Assert.Same(replacementPromotion, selected);

        firstPromotion.Return();
        replacementPromotion.Return();
    }

    [Fact]
    public void CancelAllCancelsEveryQueuedPromotion()
    {
        var firstConnection = CreateConnection();
        var secondConnection = new TcpConnection(
            0x0300000A,
            0x0400000A,
            21_061,
            49_629);
        var registry = new PendingPromotionRegistry();
        var firstPromotion = CreatePromotion(firstConnection, candidateOrdinal: 1);
        var secondPromotion = CreatePromotion(secondConnection, candidateOrdinal: 2);

        registry.Register(in firstConnection, firstPromotion);
        registry.Register(in secondConnection, secondPromotion);
        registry.CancelAll();

        Assert.False(firstPromotion.TryAcquireForDispatch());
        Assert.False(secondPromotion.TryAcquireForDispatch());
        Assert.False(registry.TryGetForPayload(in firstConnection, 1, out _));
        Assert.False(registry.TryGetForPayload(in secondConnection, 2, out _));

        firstPromotion.Return();
        secondPromotion.Return();
    }

    [Fact]
    public void DispatchClaimWinsOverLaterCancellation()
    {
        var connection = CreateConnection();
        var promotion = CreatePromotion(connection, candidateOrdinal: 7);

        Assert.True(promotion.TryAcquireForDispatch());
        Assert.False(promotion.TryCancelQueued());

        promotion.Return();
    }

    [Fact]
    public async Task CancelledPromotionIsDiscardedBeforeGateMutation()
    {
        var connection = CreateConnection();
        var promotion = CreatePromotion(connection, candidateOrdinal: 7);
        var registry = new PendingPromotionRegistry();
        registry.Register(in connection, promotion);
        Assert.True(registry.CancelForSupersededAttempt(in connection));

        var dispatcher = new PacketCaptureDispatcher(
            SceneSinkFactory.CreateForLive(new SceneLiveReadModel()));
        try
        {
            var item = CaptureDispatchItem.ForPromotion(promotion);
            Assert.False(dispatcher.DispatchItem(item));
            Assert.False(CaptureConnectionGate.TryGetLockedConnection(out _));
        }
        finally
        {
            promotion.Return();
            await dispatcher.StopAsync();
            CaptureConnectionGate.Unlock();
        }
    }

    private static CaptureConnectionPromotion CreatePromotion(
        TcpConnection connection,
        long candidateOrdinal) =>
        new(
            connection,
            replayStartSequenceNumber: null,
            candidateOrdinal: candidateOrdinal,
            packets: []);

    private static TcpConnection CreateConnection() =>
        new(0x0100000A, 0x0200000A, 21_060, 49_628);
}
