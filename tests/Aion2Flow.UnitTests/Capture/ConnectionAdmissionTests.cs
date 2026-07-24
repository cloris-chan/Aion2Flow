using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Playback;
using K4os.Compression.LZ4;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class ConnectionAdmissionTests
{
    private const long YearOneToUnixEpochMilliseconds = 62_135_596_800_000;

    [Fact]
    public void CompletedPromotionRefreshesStaleActiveAdmissionInsteadOfReclassifying()
    {
        var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);

        CaptureConnectionGate.Unlock();
        try
        {
            Assert.True(CaptureConnectionGate.TryPromote(
                in connection,
                out var staleAdmission,
                out _,
                forceNewGeneration: true,
                connectionOrdinal: 1));
            Assert.True(CaptureConnectionGate.TryPromote(
                in connection,
                out var currentAdmission,
                out _,
                forceNewGeneration: true,
                connectionOrdinal: 2));

            var resolved = WinDivertCaptureService.ResolveActivePayloadAdmission(
                in connection,
                staleAdmission,
                isExpectedDownstream: true,
                hasPendingPromotion: false,
                connectionOrdinal: 2);
            Assert.Equal(CapturePacketAdmissionKind.ActiveConnection, resolved.Kind);
            Assert.Equal(currentAdmission.Generation, resolved.Generation);

            var nextAttempt = WinDivertCaptureService.ResolveActivePayloadAdmission(
                in connection,
                currentAdmission,
                isExpectedDownstream: true,
                hasPendingPromotion: false,
                connectionOrdinal: 3);
            Assert.Equal(CapturePacketAdmissionKind.Candidate, nextAttempt.Kind);
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public void ConfirmedOverlapReplacesActiveConnectionWithoutWaitingForFin()
    {
        var firstConnection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var nextConnection = new TcpConnection(0x0300000A, 0x0400000A, 21_061, 49_629);

        try
        {
            Assert.Equal(
                CapturePacketAdmissionKind.Candidate,
                CaptureConnectionGate.EvaluatePacket(in firstConnection, hasStartFlag: false, hasCloseFlag: false).Kind);
            Assert.True(CaptureConnectionGate.TryPromote(in firstConnection, out var firstAdmission, out var replaced));
            Assert.False(replaced);
            Assert.True(CaptureConnectionGate.IsAdmissionCurrent(in firstConnection, in firstAdmission));

            Assert.Equal(
                CapturePacketAdmissionKind.Candidate,
                CaptureConnectionGate.EvaluatePacket(in nextConnection, hasStartFlag: false, hasCloseFlag: false).Kind);
            Assert.True(CaptureConnectionGate.TryPromote(in nextConnection, out var nextAdmission, out replaced));
            Assert.True(replaced);
            Assert.False(CaptureConnectionGate.IsAdmissionCurrent(in firstConnection, in firstAdmission));
            Assert.True(CaptureConnectionGate.IsAdmissionCurrent(in nextConnection, in nextAdmission));
            Assert.True(CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection));
            Assert.Equal(nextConnection, lockedConnection);
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public async Task OverlappingHandshakeReplaysEarlyPayloadBeforeTheOldFinArrives()
    {
        var oldConnection = new TcpConnection(0x0200000A, 0x0100000A, 21_060, 49_628);
        var newConnection = new TcpConnection(0x0400000A, 0x0300000A, 21_061, 49_629);
        var oldUpstream = oldConnection.Reverse();
        var newUpstream = newConnection.Reverse();
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var observedTimestamp = Stopwatch.GetTimestamp();
        var scene = new SceneLiveReadModel();
        var downstreamConnections = new TcpDownstreamConnectionTracker();
        using var candidates = new TcpWorldConnectionCandidateTracker();
        var dispatcher = new PacketCaptureDispatcher(
            SceneSinkFactory.CreateForLive(scene),
            protocolRoundTripObserver: null,
            connectionLockedObserver: null,
            connectionChangedObserver: null,
            (promotion, wasPromoted) =>
            {
                if (wasPromoted)
                {
                    var promotedConnection = promotion.Connection;
                    downstreamConnections.MarkPromoted(
                        in promotedConnection,
                        promotion.CandidateOrdinal);
                }
            });
        CaptureConnectionPromotion? promotion = null;

        try
        {
            Assert.True(downstreamConnections.ObserveSyn(
                in oldUpstream,
                hasAcknowledgment: false,
                acceptUnpairedAcknowledgment: false,
                sequenceNumber: 100,
                acknowledgmentNumber: 0,
                newConnectionOrdinal: 1,
                observedTimestamp));
            Assert.False(downstreamConnections.ObserveSyn(
                in oldConnection,
                hasAcknowledgment: true,
                acceptUnpairedAcknowledgment: false,
                sequenceNumber: 1_000,
                acknowledgmentNumber: 101,
                newConnectionOrdinal: 2,
                observedTimestamp));
            Assert.True(CaptureConnectionGate.TryPromote(
                in oldConnection,
                out var oldAdmission,
                out _,
                connectionOrdinal: 1));
            downstreamConnections.MarkPromoted(in oldConnection, expectedConnectionOrdinal: 1);

            var oldPacket = CapturedPacket.CreateCopy(
                oldConnection,
                oldAdmission,
                Build3336Frame(1, "Old"),
                sequenceNumber: 1_001,
                captureTimestampMilliseconds: captureMilliseconds);
            try
            {
                Assert.True(dispatcher.DispatchCapturedPacket(oldPacket));
            }
            finally
            {
                oldPacket.Return();
            }

            Assert.True(downstreamConnections.ObserveSyn(
                in newUpstream,
                hasAcknowledgment: false,
                acceptUnpairedAcknowledgment: false,
                sequenceNumber: 200,
                acknowledgmentNumber: 0,
                newConnectionOrdinal: 3,
                observedTimestamp: observedTimestamp + 1));
            Assert.False(downstreamConnections.ObserveSyn(
                in newConnection,
                hasAcknowledgment: true,
                acceptUnpairedAcknowledgment: false,
                sequenceNumber: 2_000,
                acknowledgmentNumber: 201,
                newConnectionOrdinal: 4,
                observedTimestamp: observedTimestamp + 2));
            Assert.True(downstreamConnections.TryGet(
                in newConnection,
                observedTimestamp + 2,
                out var initialSequenceNumber,
                out var newConnectionOrdinal));
            Assert.Equal(2_001u, initialSequenceNumber);
            Assert.Equal(3, newConnectionOrdinal);

            var earlyFrame = Build3336Frame(2, "New");
            var split = earlyFrame.Length / 2;
            var candidateAdmission = CaptureConnectionGate.EvaluatePacket(
                in newConnection,
                hasStartFlag: true,
                hasCloseFlag: false);
            var firstEarlyPacket = CapturedPacket.CreateCopy(
                newConnection,
                candidateAdmission,
                earlyFrame.AsSpan(0, split),
                sequenceNumber: initialSequenceNumber!.Value,
                captureTimestampMilliseconds: captureMilliseconds + 1);
            Assert.Equal(
                CandidatePacketDisposition.Buffered,
                candidates.Add(
                    firstEarlyPacket,
                    allowNewCandidate: true,
                    allowMidstreamRecovery: false,
                    initialSequenceNumber,
                    newConnectionOrdinal,
                    CandidateConnectionPriority.ObservedHandshake,
                    observedTimestamp + 3,
                    out _));

            var secondEarlyPacket = CapturedPacket.CreateCopy(
                newConnection,
                candidateAdmission,
                earlyFrame.AsSpan(split),
                sequenceNumber: initialSequenceNumber.Value + (uint)split,
                captureTimestampMilliseconds: captureMilliseconds + 2);
            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                candidates.Add(
                    secondEarlyPacket,
                    allowNewCandidate: true,
                    allowMidstreamRecovery: false,
                    initialSequenceNumber,
                    newConnectionOrdinal,
                    CandidateConnectionPriority.ObservedHandshake,
                    observedTimestamp + 4,
                    out promotion));
            Assert.NotNull(promotion);

            var promotionItem = CaptureDispatchItem.ForPromotion(promotion);
            try
            {
                Assert.True(dispatcher.DispatchItem(promotionItem));
            }
            finally
            {
                promotionItem.Return();
            }

            Assert.True(CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection));
            Assert.Equal(newConnection, lockedConnection);
            Assert.True(CaptureConnectionGate.TryGetActiveConnectionOrdinal(
                in newConnection,
                out var activeConnectionOrdinal));
            Assert.Equal(3, activeConnectionOrdinal);
            _ = scene.CreateFrame();
            Assert.True(scene.Owner.Entities.TryGet(2, out var newPlayer));
            Assert.Equal("New", newPlayer.Nickname);
            Assert.Equal(1, CountStateObservations(scene.Journal, entityId: 2));

            var delayedOldClose = CaptureDispatchItem.ForConnectionClose(
                in oldConnection,
                oldAdmission.Generation,
                connectionOrdinal: 1);
            Assert.False(dispatcher.DispatchItem(delayedOldClose));
            Assert.True(CaptureConnectionGate.TryGetLockedConnection(out lockedConnection));
            Assert.Equal(newConnection, lockedConnection);
        }
        finally
        {
            promotion?.Return();
            await dispatcher.StopAsync();
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public async Task OlderCandidateCannotReplaceANewerPromotedConnection()
    {
        var olderConnection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var newerConnection = new TcpConnection(0x0300000A, 0x0400000A, 21_061, 49_629);
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var observedTimestamp = Stopwatch.GetTimestamp();
        var downstreamConnections = new TcpDownstreamConnectionTracker();
        using var candidates = new TcpWorldConnectionCandidateTracker();
        var dispatcher = new PacketCaptureDispatcher(
            SceneSinkFactory.CreateForLive(new SceneLiveReadModel()),
            protocolRoundTripObserver: null,
            connectionLockedObserver: null,
            connectionChangedObserver: null,
            (promotion, wasPromoted) =>
            {
                if (wasPromoted)
                {
                    var promotedConnection = promotion.Connection;
                    downstreamConnections.MarkPromoted(in promotedConnection, promotion.CandidateOrdinal);
                }
            });
        CaptureConnectionPromotion? newerPromotion = null;
        CaptureConnectionPromotion? olderPromotion = null;

        try
        {
            var olderUpstream = olderConnection.Reverse();
            Assert.True(downstreamConnections.ObserveSyn(
                in olderUpstream,
                hasAcknowledgment: false,
                acceptUnpairedAcknowledgment: false,
                sequenceNumber: 100,
                acknowledgmentNumber: 0,
                newConnectionOrdinal: 1,
                observedTimestamp));
            var newerUpstream = newerConnection.Reverse();
            Assert.True(downstreamConnections.ObserveSyn(
                in newerUpstream,
                hasAcknowledgment: false,
                acceptUnpairedAcknowledgment: false,
                sequenceNumber: 200,
                acknowledgmentNumber: 0,
                newConnectionOrdinal: 2,
                observedTimestamp: observedTimestamp + 1));
            Assert.True(downstreamConnections.TryGet(
                in newerConnection,
                observedTimestamp + 1,
                out var newerInitialSequence,
                out var newerConnectionOrdinal));

            var newerPacket = CapturedPacket.CreateCopy(
                newerConnection,
                new CapturePacketAdmission(CapturePacketAdmissionKind.Candidate, 0, ReleasedLock: false),
                Build3336Frame(2, "B"),
                sequenceNumber: 200,
                captureTimestampMilliseconds: captureMilliseconds);
            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                candidates.Add(
                    newerPacket,
                    allowNewCandidate: true,
                    allowMidstreamRecovery: false,
                    newerInitialSequence,
                    newerConnectionOrdinal,
                    observedTimestamp + 2,
                    out newerPromotion));
            Assert.NotNull(newerPromotion);
            var newerItem = CaptureDispatchItem.ForPromotion(newerPromotion);
            try
            {
                Assert.True(dispatcher.DispatchItem(newerItem));
            }
            finally
            {
                newerItem.Return();
            }

            Assert.True(downstreamConnections.TryGet(
                in olderConnection,
                observedTimestamp + 2,
                out var olderInitialSequence,
                out var olderConnectionOrdinal));
            var olderPacket = CapturedPacket.CreateCopy(
                olderConnection,
                new CapturePacketAdmission(CapturePacketAdmissionKind.Candidate, 0, ReleasedLock: false),
                Build3336Frame(1, "A"),
                sequenceNumber: 100,
                captureTimestampMilliseconds: captureMilliseconds);
            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                candidates.Add(
                    olderPacket,
                    allowNewCandidate: true,
                    allowMidstreamRecovery: false,
                    olderInitialSequence,
                    olderConnectionOrdinal,
                    observedTimestamp + 3,
                    out olderPromotion));
            Assert.NotNull(olderPromotion);
            Assert.True(olderPromotion.CandidateOrdinal < newerPromotion.CandidateOrdinal);
            var olderItem = CaptureDispatchItem.ForPromotion(olderPromotion);
            try
            {
                Assert.False(dispatcher.DispatchItem(olderItem));
            }
            finally
            {
                olderItem.Return();
            }

            Assert.True(CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection));
            Assert.Equal(newerConnection, lockedConnection);

            Assert.True(downstreamConnections.TryGet(
                in olderConnection,
                observedTimestamp + 4,
                out olderInitialSequence,
                out olderConnectionOrdinal));
            var olderRetryPacket = CapturedPacket.CreateCopy(
                olderConnection,
                new CapturePacketAdmission(CapturePacketAdmissionKind.Candidate, 0, ReleasedLock: false),
                Build3336Frame(1, "A"),
                sequenceNumber: 100,
                captureTimestampMilliseconds: captureMilliseconds);
            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                candidates.Add(
                    olderRetryPacket,
                    allowNewCandidate: true,
                    allowMidstreamRecovery: false,
                    olderInitialSequence,
                    olderConnectionOrdinal,
                    observedTimestamp + 5,
                    out var olderRetryPromotion));
            Assert.NotNull(olderRetryPromotion);
            var olderRetryItem = CaptureDispatchItem.ForPromotion(olderRetryPromotion);
            try
            {
                Assert.False(dispatcher.DispatchItem(olderRetryItem));
            }
            finally
            {
                olderRetryItem.Return();
            }

            Assert.True(CaptureConnectionGate.TryGetLockedConnection(out lockedConnection));
            Assert.Equal(newerConnection, lockedConnection);
        }
        finally
        {
            newerPromotion?.Return();
            olderPromotion?.Return();
            await dispatcher.StopAsync();
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public void RetiredConnectionCannotTakeTheLockBackOrReleaseItsReplacement()
    {
        var firstConnection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var nextConnection = new TcpConnection(0x0300000A, 0x0400000A, 21_061, 49_629);

        try
        {
            Assert.True(CaptureConnectionGate.TryPromote(in firstConnection, out _, out _));
            Assert.True(CaptureConnectionGate.TryPromote(in nextConnection, out _, out _));

            Assert.Equal(
                CapturePacketAdmissionKind.Rejected,
                CaptureConnectionGate.EvaluatePacket(in firstConnection, hasStartFlag: false, hasCloseFlag: false).Kind);
            Assert.Equal(
                CapturePacketAdmissionKind.Rejected,
                CaptureConnectionGate.EvaluatePacket(in firstConnection, hasStartFlag: false, hasCloseFlag: true).Kind);
            Assert.False(CaptureConnectionGate.TryPromote(in firstConnection, out _, out _));
            Assert.True(CaptureConnectionGate.TryGetLockedConnection(out var lockedConnection));
            Assert.Equal(nextConnection, lockedConnection);

            Assert.Equal(
                CapturePacketAdmissionKind.Candidate,
                CaptureConnectionGate.EvaluatePacket(in firstConnection, hasStartFlag: true, hasCloseFlag: false).Kind);
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public void ActiveConnectionRemainsAdmittedAfterTheIdleWindow()
    {
        var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var otherConnection = new TcpConnection(0x0300000A, 0x0400000A, 21_061, 49_629);
        var futureTimestamp = Stopwatch.GetTimestamp() + (Stopwatch.Frequency * 6);

        try
        {
            Assert.True(CaptureConnectionGate.TryPromote(in connection, out var activeAdmission, out _));
            Assert.Equal(
                CapturePacketAdmissionKind.Candidate,
                CaptureConnectionGate.EvaluatePacket(in otherConnection, false, false, futureTimestamp).Kind);

            var resumedAdmission = CaptureConnectionGate.EvaluatePacket(
                in connection,
                hasStartFlag: false,
                hasCloseFlag: false,
                observedTimestamp: futureTimestamp);
            Assert.Equal(CapturePacketAdmissionKind.ActiveConnection, resumedAdmission.Kind);
            Assert.Equal(activeAdmission.Generation, resumedAdmission.Generation);
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public void ClassifierConfirms0036AcrossEveryTcpSplit()
    {
        const long captureMilliseconds = 1_800_000_000_000;
        var frame = BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds - 125));
        Assert.Equal(11, frame.Length);

        for (var split = 1; split < frame.Length; split++)
        {
            using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
            Assert.Equal(TcpWorldStreamClassification.Pending, classifier.Append(frame.AsSpan(0, split), captureMilliseconds));
            Assert.Equal(TcpWorldStreamClassification.Confirmed, classifier.Append(frame.AsSpan(split), captureMilliseconds));
        }
    }

    [Fact]
    public void ClassifierRejectsInvalidClockAndDoesNotScanEmbedded0036()
    {
        const long captureMilliseconds = 1_800_000_000_000;
        var staleFrame = BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds - 10_001));
        using var staleClassifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(TcpWorldStreamClassification.Pending, staleClassifier.Append(staleFrame, captureMilliseconds));

        var embedded = new byte[32];
        staleFrame.CopyTo(embedded.AsSpan(9));
        using var embeddedClassifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(TcpWorldStreamClassification.Rejected, embeddedClassifier.Append(embedded, captureMilliseconds));
    }

    [Fact]
    public void ClassifierKeepsSplitTlsPendingThenRejectsTheCompleteHeader()
    {
        byte[] tlsRecord = [0x17, 0x03, 0x03, 0x00, 0x01, 0x00];
        Assert.True(TcpWorldStreamClassifier.IsPlausibleConnectionStart(tlsRecord.AsSpan(0, 1)));

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(TcpWorldStreamClassification.Pending, classifier.Append(tlsRecord.AsSpan(0, 2), 1_000));
        Assert.Equal(TcpWorldStreamClassification.Rejected, classifier.Append(tlsRecord.AsSpan(2), 1_000));
    }

    [Fact]
    public void Weak1136WaitsForValidCompressedGameplayBatch()
    {
        var handshake = BuildFrame(0x11, 0x36, new byte[276]);
        Assert.Equal(280, handshake.Length);

        var gameplayInner = BuildFrame(0x15, 0x36, new byte[512]);
        var compressedGameplay = BuildCompressedFrame(gameplayInner);
        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(TcpWorldStreamClassification.Pending, classifier.Append(handshake, 1_000));

        var split = compressedGameplay.Length / 2;
        Assert.Equal(TcpWorldStreamClassification.Pending, classifier.Append(compressedGameplay.AsSpan(0, split), 1_100));
        Assert.Equal(TcpWorldStreamClassification.Confirmed, classifier.Append(compressedGameplay.AsSpan(split), 1_200));
    }

    [Fact]
    public void CompressedLoginBatchDoesNotConfirmWorldStream()
    {
        var loginInner = BuildFrame(0x01, 0x39, new byte[256]);
        var compressedLogin = BuildCompressedFrame(loginInner);
        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);

        Assert.Equal(TcpWorldStreamClassification.Rejected, classifier.Append(compressedLogin, 1_000));
    }

    [Fact]
    public async Task ConfirmedCandidateReplaysEachBufferedSegmentOnce()
    {
        var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var frame = Build0336(captureMilliseconds);
        var candidateAdmission = CaptureConnectionGate.EvaluatePacket(in connection, hasStartFlag: false, hasCloseFlag: false);
        using var candidates = new TcpWorldConnectionCandidateTracker();
        CaptureConnectionPromotion? promotion = null;

        try
        {
            var first = CapturedPacket.CreateCopy(connection, candidateAdmission, frame.AsSpan(0, 9), 100, captureMilliseconds);
            Assert.Equal(
                CandidatePacketDisposition.Buffered,
                candidates.Add(
                    first,
                    allowNewCandidate: true,
                    allowMidstreamRecovery: false,
                    initialSequenceNumber: null,
                    connectionOrdinal: 1,
                    observedTimestamp: Stopwatch.GetTimestamp(),
                    out _));

            var second = CapturedPacket.CreateCopy(connection, candidateAdmission, frame.AsSpan(9), 109, captureMilliseconds);
            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                candidates.Add(
                    second,
                    allowNewCandidate: true,
                    allowMidstreamRecovery: false,
                    initialSequenceNumber: null,
                    connectionOrdinal: 1,
                    observedTimestamp: Stopwatch.GetTimestamp(),
                    out promotion));
            Assert.NotNull(promotion);
            Assert.Equal(2, promotion.Packets.Count);

            var observedEchoes = 0;
            using var sinkHolder = SceneSinkFactory.CreateForReplay();
            var dispatcher = new PacketCaptureDispatcher(
                () => sinkHolder.Sink,
                _ => observedEchoes++,
                connectionLockedObserver: null);
            try
            {
                var item = CaptureDispatchItem.ForPromotion(promotion);
                try
                {
                    Assert.True(dispatcher.DispatchItem(item));
                }
                finally
                {
                    item.Return();
                }

                Assert.Equal(1, observedEchoes);
            }
            finally
            {
                await dispatcher.StopAsync();
            }
        }
        finally
        {
            if (promotion is not null)
            {
                promotion.Return();
            }

            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public void PromotedDownstreamConnectionRejectsItsReverseDirection()
    {
        var downstream = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var upstream = downstream.Reverse();

        try
        {
            Assert.True(CaptureConnectionGate.TryPromote(in downstream, out _, out _));
            Assert.Equal(
                CapturePacketAdmissionKind.Rejected,
                CaptureConnectionGate.EvaluatePacket(in upstream, hasStartFlag: false, hasCloseFlag: false).Kind);
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public async Task CandidateContinuationRequiresThePromotedAttemptOrdinal()
    {
        var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dispatcher = new PacketCaptureDispatcher(SceneSinkFactory.CreateForLive(new SceneLiveReadModel()));

        try
        {
            Assert.True(CaptureConnectionGate.TryPromote(
                in connection,
                out _,
                out _,
                forceNewGeneration: true,
                connectionOrdinal: 7));

            var stalePacket = CapturedPacket.CreateCopy(
                connection,
                new CapturePacketAdmission(CapturePacketAdmissionKind.Candidate, 0, ReleasedLock: false),
                Build0336(captureMilliseconds),
                sequenceNumber: 100,
                captureTimestampMilliseconds: captureMilliseconds);
            var staleContinuation = CaptureDispatchItem.ForCandidateContinuation(
                stalePacket,
                connectionOrdinal: 6);
            try
            {
                Assert.False(dispatcher.DispatchItem(staleContinuation));
            }
            finally
            {
                staleContinuation.Return();
            }

            var currentPacket = CapturedPacket.CreateCopy(
                connection,
                new CapturePacketAdmission(CapturePacketAdmissionKind.Candidate, 0, ReleasedLock: false),
                Build0336(captureMilliseconds),
                sequenceNumber: 200,
                captureTimestampMilliseconds: captureMilliseconds);
            var currentContinuation = CaptureDispatchItem.ForCandidateContinuation(
                currentPacket,
                connectionOrdinal: 7);
            try
            {
                Assert.True(dispatcher.DispatchItem(currentContinuation));
            }
            finally
            {
                currentContinuation.Return();
            }
        }
        finally
        {
            await dispatcher.StopAsync();
            CaptureConnectionGate.Unlock();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CandidateFinClosesImmediatelyAfterItsQueuedPromotion(bool closeFromReverseDirection)
    {
        var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dispatcher = new PacketCaptureDispatcher(SceneSinkFactory.CreateForLive(new SceneLiveReadModel()));
        var packet = CapturedPacket.CreateCopy(
            connection,
            new CapturePacketAdmission(CapturePacketAdmissionKind.Candidate, 0, ReleasedLock: false),
            Build0336(captureMilliseconds),
            sequenceNumber: 100,
            captureTimestampMilliseconds: captureMilliseconds);
        var promotion = new CaptureConnectionPromotion(
            connection,
            replayStartSequenceNumber: packet.SequenceNumber,
            candidateOrdinal: 7,
            packets: [packet]);

        try
        {
            var candidateAdmission = packet.Admission;
            var closeConnection = closeFromReverseDirection ? connection.Reverse() : connection;
            var registry = new PendingPromotionRegistry();
            registry.Register(in connection, promotion);
            Assert.True(registry.TryGetForClose(
                in closeConnection,
                packetConnectionOrdinal: 7,
                out var selectedPromotion));
            Assert.Same(promotion, selectedPromotion);
            Assert.True(WinDivertCaptureService.ShouldDispatchConnectionClose(
                in closeConnection,
                in candidateAdmission,
                connectionOrdinal: 7,
                hasQueuedPromotion: true));
            Assert.True(registry.DetachAfterQueuedClose(in closeConnection, promotion));

            var promotionItem = CaptureDispatchItem.ForPromotion(promotion);
            try
            {
                Assert.True(dispatcher.DispatchItem(promotionItem));
            }
            finally
            {
                promotionItem.Return();
            }

            var closeItem = CaptureDispatchItem.ForConnectionClose(
                in closeConnection,
                connectionGeneration: 0,
                connectionOrdinal: 7);
            Assert.True(dispatcher.DispatchItem(closeItem));
            Assert.False(CaptureConnectionGate.TryGetLockedConnection(out _));
        }
        finally
        {
            promotion.Return();
            await dispatcher.StopAsync();
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public void SameTuplePromotionCanStartANewGeneration()
    {
        var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);

        try
        {
            Assert.True(CaptureConnectionGate.TryPromote(in connection, out var first, out _));
            Assert.True(CaptureConnectionGate.TryPromote(
                in connection,
                out var restarted,
                out var replaced,
                forceNewGeneration: true));

            Assert.True(replaced);
            Assert.True(restarted.Generation > first.Generation);
            Assert.False(CaptureConnectionGate.IsAdmissionCurrent(in connection, in first));
            Assert.True(CaptureConnectionGate.IsAdmissionCurrent(in connection, in restarted));
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public async Task SameTupleStaleCloseCannotBlockOrCloseANewerPromotion()
    {
        var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dispatcher = new PacketCaptureDispatcher(SceneSinkFactory.CreateForLive(new SceneLiveReadModel()));

        try
        {
            Assert.True(DispatchPromotion(candidateOrdinal: 1, playerId: 1));
            Assert.True(CaptureConnectionGate.TryGetActiveAdmission(in connection, out var firstAdmission));

            var firstClose = CaptureDispatchItem.ForConnectionClose(
                in connection,
                firstAdmission.Generation,
                connectionOrdinal: 1);
            Assert.True(dispatcher.DispatchItem(firstClose));

            Assert.True(DispatchPromotion(candidateOrdinal: 2, playerId: 2));
            Assert.True(CaptureConnectionGate.TryGetActiveAdmission(in connection, out var secondAdmission));
            Assert.True(secondAdmission.Generation > firstAdmission.Generation);

            var staleClose = CaptureDispatchItem.ForConnectionClose(
                in connection,
                firstAdmission.Generation,
                connectionOrdinal: 1);
            Assert.False(dispatcher.DispatchItem(staleClose));
            Assert.True(CaptureConnectionGate.IsAdmissionCurrent(in connection, in secondAdmission));
        }
        finally
        {
            await dispatcher.StopAsync();
            CaptureConnectionGate.Unlock();
        }

        bool DispatchPromotion(long candidateOrdinal, int playerId)
        {
            var packet = CapturedPacket.CreateCopy(
                connection,
                new CapturePacketAdmission(CapturePacketAdmissionKind.Candidate, 0, ReleasedLock: false),
                Build3336Frame(playerId, $"P{playerId}"),
                sequenceNumber: (uint)(candidateOrdinal * 100),
                captureTimestampMilliseconds: captureMilliseconds);
            var promotion = new CaptureConnectionPromotion(
                connection,
                replayStartSequenceNumber: packet.SequenceNumber,
                candidateOrdinal,
                packets: [packet]);
            var item = CaptureDispatchItem.ForPromotion(promotion);
            try
            {
                return dispatcher.DispatchItem(item);
            }
            finally
            {
                item.Return();
            }
        }
    }

    [Fact]
    public async Task SameTupleCandidateWithoutSynAckRecreatesDispatcherStream()
    {
        var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var scene = new SceneLiveReadModel();
        var sinkFactory = SceneSinkFactory.CreateForLive(scene);
        var lockedCount = 0;
        var dispatcher = new PacketCaptureDispatcher(
            sinkFactory,
            protocolRoundTripObserver: null,
            connectionLockedObserver: _ => lockedCount++);
        CaptureConnectionPromotion? promotion = null;

        try
        {
            Assert.True(CaptureConnectionGate.TryPromote(in connection, out var firstAdmission, out _));
            var firstPacket = CapturedPacket.CreateCopy(
                connection,
                firstAdmission,
                Build0336(captureMilliseconds),
                sequenceNumber: 100,
                captureTimestampMilliseconds: captureMilliseconds);
            try
            {
                Assert.True(dispatcher.DispatchCapturedPacket(firstPacket));
            }
            finally
            {
                firstPacket.Return();
            }

            var candidateAdmission = new CapturePacketAdmission(
                CapturePacketAdmissionKind.Candidate,
                firstAdmission.Generation,
                ReleasedLock: false);
            var restartedPacket = CapturedPacket.CreateCopy(
                connection,
                candidateAdmission,
                Build0336(captureMilliseconds),
                sequenceNumber: 500,
                captureTimestampMilliseconds: captureMilliseconds);
            promotion = new CaptureConnectionPromotion(
                connection,
                replayStartSequenceNumber: null,
                candidateOrdinal: 1,
                packets: [restartedPacket]);
            var promotionItem = CaptureDispatchItem.ForPromotion(promotion);
            try
            {
                Assert.True(dispatcher.DispatchItem(promotionItem));
            }
            finally
            {
                promotionItem.Return();
            }

            Assert.True(CaptureConnectionGate.TryGetActiveAdmission(in connection, out var restartedAdmission));
            Assert.True(restartedAdmission.Generation > firstAdmission.Generation);
            Assert.Equal(2, lockedCount);
        }
        finally
        {
            promotion?.Return();
            await dispatcher.StopAsync();
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public void SynHandshakeIdentifiesOnlyTheServerToClientDirection()
    {
        var upstream = new TcpConnection(0x0100000A, 0x0200000A, 49_628, 21_060);
        var downstream = upstream.Reverse();
        var tracker = new TcpDownstreamConnectionTracker();
        var observedTimestamp = Stopwatch.GetTimestamp();

        Assert.True(tracker.ObserveSyn(
            in upstream,
            hasAcknowledgment: false,
            acceptUnpairedAcknowledgment: false,
            sequenceNumber: 100,
            acknowledgmentNumber: 0,
            newConnectionOrdinal: 1,
            observedTimestamp: observedTimestamp));
        Assert.True(tracker.TryGet(in downstream, observedTimestamp, out var pendingSequence, out var connectionOrdinal));
        Assert.Null(pendingSequence);
        Assert.Equal(1, connectionOrdinal);
        Assert.False(tracker.TryGet(in upstream, observedTimestamp, out _, out _));

        Assert.False(tracker.ObserveSyn(
            in downstream,
            hasAcknowledgment: true,
            acceptUnpairedAcknowledgment: false,
            sequenceNumber: uint.MaxValue,
            acknowledgmentNumber: 101,
            newConnectionOrdinal: 2,
            observedTimestamp: observedTimestamp));
        Assert.True(tracker.TryGet(in downstream, observedTimestamp, out var initialSequence, out connectionOrdinal));
        Assert.Equal(0u, initialSequence);
        Assert.Equal(1, connectionOrdinal);

        Assert.False(tracker.ObserveSyn(
            in upstream,
            hasAcknowledgment: false,
            acceptUnpairedAcknowledgment: false,
            sequenceNumber: 100,
            acknowledgmentNumber: 0,
            newConnectionOrdinal: 3,
            observedTimestamp: observedTimestamp + 1));
        Assert.True(tracker.TryGet(in downstream, observedTimestamp + 1, out initialSequence, out connectionOrdinal));
        Assert.Equal(0u, initialSequence);
        Assert.Equal(1, connectionOrdinal);

        Assert.True(tracker.ObserveSyn(
            in upstream,
            hasAcknowledgment: false,
            acceptUnpairedAcknowledgment: false,
            sequenceNumber: 101,
            acknowledgmentNumber: 0,
            newConnectionOrdinal: 4,
            observedTimestamp: observedTimestamp + 2));
        Assert.True(tracker.TryGet(in downstream, observedTimestamp + 2, out initialSequence, out connectionOrdinal));
        Assert.Null(initialSequence);
        Assert.Equal(4, connectionOrdinal);
    }

    [Fact]
    public void UnpairedInboundSynAckCanSeedADownstreamSequenceAnchor()
    {
        var downstream = new TcpConnection(0x0200000A, 0x0100000A, 21_060, 49_628);
        var tracker = new TcpDownstreamConnectionTracker();

        tracker.ObserveSyn(
            in downstream,
            hasAcknowledgment: true,
            acceptUnpairedAcknowledgment: true,
            sequenceNumber: uint.MaxValue,
            acknowledgmentNumber: 0,
            newConnectionOrdinal: 1,
            observedTimestamp: Stopwatch.GetTimestamp());

        Assert.True(tracker.TryGet(in downstream, Stopwatch.GetTimestamp(), out var initialSequence, out var connectionOrdinal));
        Assert.Equal(0u, initialSequence);
        Assert.Equal(1, connectionOrdinal);
    }

    [Fact]
    public void CombinedPacketResolutionReturnsPendingAndResolvedAttemptFromOneSnapshot()
    {
        var downstream = new TcpConnection(0x0200000A, 0x0100000A, 21_060, 49_628);
        var tracker = new TcpDownstreamConnectionTracker();
        var observedTimestamp = Stopwatch.GetTimestamp();

        Assert.True(tracker.ObserveSyn(
            in downstream,
            hasAcknowledgment: true,
            acceptUnpairedAcknowledgment: true,
            sequenceNumber: 100,
            acknowledgmentNumber: 0,
            newConnectionOrdinal: 1,
            observedTimestamp));

        var pending = tracker.ResolvePacket(
            in downstream,
            sequenceNumber: 101,
            hasAcknowledgment: true,
            acknowledgmentNumber: 0,
            observedTimestamp: observedTimestamp + 1);
        Assert.True(pending.IsExpectedDownstream);
        Assert.Equal(101u, pending.InitialSequenceNumber);
        Assert.Equal(1, pending.ExpectedConnectionOrdinal);
        Assert.True(pending.HasResolvedConnectionOrdinal);
        Assert.Equal(1, pending.ResolvedConnectionOrdinal);

        tracker.MarkPromoted(in downstream, expectedConnectionOrdinal: 1);
        var promoted = tracker.ResolvePacket(
            in downstream,
            sequenceNumber: 102,
            hasAcknowledgment: true,
            acknowledgmentNumber: 0,
            observedTimestamp: observedTimestamp + 2);
        Assert.False(promoted.IsExpectedDownstream);
        Assert.True(promoted.HasResolvedConnectionOrdinal);
        Assert.Equal(1, promoted.ResolvedConnectionOrdinal);
    }

    [Fact]
    public void ThrottledHousekeepingCannotReviveAnExpiredPendingConnection()
    {
        var target = new TcpConnection(0x0200000A, 0x0100000A, 21_060, 49_628);
        var unrelated = new TcpConnection(0x0400000A, 0x0300000A, 21_061, 49_629);
        var tracker = new TcpDownstreamConnectionTracker();
        var observedTimestamp = Stopwatch.GetTimestamp();

        Assert.True(tracker.ObserveSyn(
            in target,
            hasAcknowledgment: true,
            acceptUnpairedAcknowledgment: true,
            sequenceNumber: 100,
            acknowledgmentNumber: 0,
            newConnectionOrdinal: 1,
            observedTimestamp));

        var justBeforeExpiry = observedTimestamp + ((Stopwatch.Frequency * 299L) / 10);
        var unrelatedResolution = tracker.ResolvePacket(
            in unrelated,
            sequenceNumber: 1,
            hasAcknowledgment: true,
            acknowledgmentNumber: 0,
            observedTimestamp: justBeforeExpiry);
        Assert.False(unrelatedResolution.HasResolvedConnectionOrdinal);

        var justAfterExpiry = observedTimestamp + ((Stopwatch.Frequency * 301L) / 10);
        var expiredResolution = tracker.ResolvePacket(
            in target,
            sequenceNumber: 101,
            hasAcknowledgment: true,
            acknowledgmentNumber: 0,
            observedTimestamp: justAfterExpiry);
        Assert.False(expiredResolution.IsExpectedDownstream);
        Assert.False(expiredResolution.HasResolvedConnectionOrdinal);
    }

    [Fact]
    public void ExpiredFailedAttemptRetainsBothItsTombstoneAndTheOlderPromotedIdentity()
    {
        var downstream = new TcpConnection(0x0200000A, 0x0100000A, 21_060, 49_628);
        var upstream = downstream.Reverse();
        var tracker = new TcpDownstreamConnectionTracker();
        var observedTimestamp = Stopwatch.GetTimestamp();

        Assert.True(tracker.ObserveSyn(
            in upstream,
            hasAcknowledgment: false,
            acceptUnpairedAcknowledgment: false,
            sequenceNumber: 100,
            acknowledgmentNumber: 0,
            newConnectionOrdinal: 1,
            observedTimestamp));
        Assert.False(tracker.ObserveSyn(
            in downstream,
            hasAcknowledgment: true,
            acceptUnpairedAcknowledgment: false,
            sequenceNumber: 1_000,
            acknowledgmentNumber: 101,
            newConnectionOrdinal: 2,
            observedTimestamp));
        tracker.MarkPromoted(in downstream, expectedConnectionOrdinal: 1);

        Assert.True(tracker.ObserveSyn(
            in upstream,
            hasAcknowledgment: false,
            acceptUnpairedAcknowledgment: false,
            sequenceNumber: 200,
            acknowledgmentNumber: 0,
            newConnectionOrdinal: 3,
            observedTimestamp: observedTimestamp + 1));

        var afterPendingExpiry = observedTimestamp + (Stopwatch.Frequency * 31L);
        var failedAttempt = tracker.ResolvePacket(
            in upstream,
            sequenceNumber: 201,
            hasAcknowledgment: false,
            acknowledgmentNumber: 0,
            observedTimestamp: afterPendingExpiry);
        Assert.False(failedAttempt.IsExpectedDownstream);
        Assert.True(failedAttempt.HasResolvedConnectionOrdinal);
        Assert.Equal(3, failedAttempt.ResolvedConnectionOrdinal);

        var olderPromotedAttempt = tracker.ResolvePacket(
            in downstream,
            sequenceNumber: 1_050,
            hasAcknowledgment: true,
            acknowledgmentNumber: 101,
            observedTimestamp: afterPendingExpiry + 1);
        Assert.False(olderPromotedAttempt.IsExpectedDownstream);
        Assert.True(olderPromotedAttempt.HasResolvedConnectionOrdinal);
        Assert.Equal(1, olderPromotedAttempt.ResolvedConnectionOrdinal);
    }

    [Fact]
    public void PromotedHandshakeIgnoresRetransmissionButDetectsANewServerIsn()
    {
        var downstream = new TcpConnection(0x0200000A, 0x0100000A, 21_060, 49_628);
        var tracker = new TcpDownstreamConnectionTracker();
        var observedTimestamp = Stopwatch.GetTimestamp();

        Assert.True(tracker.ObserveSyn(
            in downstream,
            hasAcknowledgment: true,
            acceptUnpairedAcknowledgment: true,
            sequenceNumber: 100,
            acknowledgmentNumber: 0,
            newConnectionOrdinal: 1,
            observedTimestamp));
        tracker.MarkPromoted(in downstream, expectedConnectionOrdinal: 1);
        Assert.False(tracker.TryGet(in downstream, observedTimestamp, out _, out _));

        Assert.False(tracker.ObserveSyn(
            in downstream,
            hasAcknowledgment: true,
            acceptUnpairedAcknowledgment: true,
            sequenceNumber: 100,
            acknowledgmentNumber: 0,
            newConnectionOrdinal: 2,
            observedTimestamp: observedTimestamp + 1));
        Assert.False(tracker.TryGet(in downstream, observedTimestamp + 1, out _, out _));

        Assert.True(tracker.ObserveSyn(
            in downstream,
            hasAcknowledgment: true,
            acceptUnpairedAcknowledgment: true,
            sequenceNumber: 200,
            acknowledgmentNumber: 0,
            newConnectionOrdinal: 3,
            observedTimestamp: observedTimestamp + 2));
        Assert.True(tracker.TryGet(in downstream, observedTimestamp + 2, out var initialSequence, out var connectionOrdinal));
        Assert.Equal(201u, initialSequence);
        Assert.Equal(3, connectionOrdinal);

        tracker.MarkPromoted(in downstream, expectedConnectionOrdinal: 1);
        Assert.True(tracker.TryGet(in downstream, observedTimestamp + 3, out initialSequence, out connectionOrdinal));
        Assert.Equal(3, connectionOrdinal);
    }

    [Fact]
    public void SameTuplePacketsResolveByAcknowledgmentBeforeSequenceProximity()
    {
        var downstream = new TcpConnection(0x0200000A, 0x0100000A, 21_060, 49_628);
        var upstream = downstream.Reverse();
        var tracker = new TcpDownstreamConnectionTracker();
        var observedTimestamp = Stopwatch.GetTimestamp();

        Assert.True(tracker.ObserveSyn(
            in upstream,
            hasAcknowledgment: false,
            acceptUnpairedAcknowledgment: false,
            sequenceNumber: 100,
            acknowledgmentNumber: 0,
            newConnectionOrdinal: 1,
            observedTimestamp));
        Assert.False(tracker.ObserveSyn(
            in downstream,
            hasAcknowledgment: true,
            acceptUnpairedAcknowledgment: false,
            sequenceNumber: 1_000,
            acknowledgmentNumber: 101,
            newConnectionOrdinal: 2,
            observedTimestamp));
        Assert.True(tracker.TryResolvePacketOrdinal(
            in downstream,
            sequenceNumber: 1_050,
            hasAcknowledgment: true,
            acknowledgmentNumber: 101,
            observedTimestamp,
            out var connectionOrdinal));
        Assert.Equal(1, connectionOrdinal);
        tracker.MarkPromoted(in downstream, expectedConnectionOrdinal: 1);

        Assert.True(tracker.ObserveSyn(
            in upstream,
            hasAcknowledgment: false,
            acceptUnpairedAcknowledgment: false,
            sequenceNumber: 200,
            acknowledgmentNumber: 0,
            newConnectionOrdinal: 3,
            observedTimestamp: observedTimestamp + 1));

        Assert.True(tracker.TryResolvePacketOrdinal(
            in downstream,
            sequenceNumber: 1_051,
            hasAcknowledgment: true,
            acknowledgmentNumber: 101,
            observedTimestamp + 1,
            out connectionOrdinal));
        Assert.Equal(1, connectionOrdinal);
        Assert.True(tracker.TryResolvePacketOrdinal(
            in downstream,
            sequenceNumber: 1_052,
            hasAcknowledgment: true,
            acknowledgmentNumber: 201,
            observedTimestamp + 1,
            out connectionOrdinal));
        Assert.Equal(3, connectionOrdinal);

        Assert.False(tracker.ObserveSyn(
            in downstream,
            hasAcknowledgment: true,
            acceptUnpairedAcknowledgment: false,
            sequenceNumber: 1_000,
            acknowledgmentNumber: 101,
            newConnectionOrdinal: 4,
            observedTimestamp: observedTimestamp + 2));
        Assert.True(tracker.TryGet(
            in downstream,
            observedTimestamp + 2,
            out var initialSequenceNumber,
            out connectionOrdinal));
        Assert.Null(initialSequenceNumber);
        Assert.Equal(3, connectionOrdinal);

        Assert.False(tracker.ObserveSyn(
            in downstream,
            hasAcknowledgment: true,
            acceptUnpairedAcknowledgment: false,
            sequenceNumber: 2_000,
            acknowledgmentNumber: 201,
            newConnectionOrdinal: 5,
            observedTimestamp: observedTimestamp + 3));
        Assert.True(tracker.TryGet(
            in downstream,
            observedTimestamp + 3,
            out initialSequenceNumber,
            out connectionOrdinal));
        Assert.Equal(2_001u, initialSequenceNumber);
        Assert.Equal(3, connectionOrdinal);

        Assert.True(tracker.TryResolvePacketOrdinal(
            in downstream,
            sequenceNumber: 1_053,
            hasAcknowledgment: true,
            acknowledgmentNumber: 101,
            observedTimestamp + 3,
            out connectionOrdinal));
        Assert.Equal(1, connectionOrdinal);
        Assert.True(tracker.TryResolvePacketOrdinal(
            in downstream,
            sequenceNumber: 2_050,
            hasAcknowledgment: true,
            acknowledgmentNumber: 201,
            observedTimestamp + 3,
            out connectionOrdinal));
        Assert.Equal(3, connectionOrdinal);
    }

    [Fact]
    public void RepeatedStaleCloseKeepsResolvingToThePreviousAttempt()
    {
        var downstream = new TcpConnection(0x0200000A, 0x0100000A, 21_060, 49_628);
        var upstream = downstream.Reverse();
        var tracker = new TcpDownstreamConnectionTracker();
        var observedTimestamp = Stopwatch.GetTimestamp();

        Assert.True(tracker.ObserveSyn(
            in upstream,
            hasAcknowledgment: false,
            acceptUnpairedAcknowledgment: false,
            sequenceNumber: 100,
            acknowledgmentNumber: 0,
            newConnectionOrdinal: 1,
            observedTimestamp));
        Assert.False(tracker.ObserveSyn(
            in downstream,
            hasAcknowledgment: true,
            acceptUnpairedAcknowledgment: false,
            sequenceNumber: 1_000,
            acknowledgmentNumber: 101,
            newConnectionOrdinal: 2,
            observedTimestamp));
        tracker.MarkPromoted(in downstream, expectedConnectionOrdinal: 1);

        var afterPendingExpiry = observedTimestamp + (Stopwatch.Frequency * 31L);
        Assert.True(tracker.TryResolvePacketOrdinal(
            in downstream,
            sequenceNumber: 1_050,
            hasAcknowledgment: true,
            acknowledgmentNumber: 101,
            afterPendingExpiry,
            out var connectionOrdinal));
        Assert.Equal(1, connectionOrdinal);

        Assert.True(tracker.ObserveSyn(
            in upstream,
            hasAcknowledgment: false,
            acceptUnpairedAcknowledgment: false,
            sequenceNumber: 200,
            acknowledgmentNumber: 0,
            newConnectionOrdinal: 3,
            observedTimestamp: afterPendingExpiry + 1));
        tracker.Remove(in downstream, expectedConnectionOrdinal: 1);
        tracker.Remove(in downstream, expectedConnectionOrdinal: 1);

        Assert.True(tracker.TryResolvePacketOrdinal(
            in downstream,
            sequenceNumber: 1_051,
            hasAcknowledgment: true,
            acknowledgmentNumber: 101,
            observedTimestamp: afterPendingExpiry + 2,
            out connectionOrdinal));
        Assert.Equal(1, connectionOrdinal);
        Assert.True(tracker.TryGet(
            in downstream,
            observedTimestamp: afterPendingExpiry + 2,
            out _,
            out connectionOrdinal));
        Assert.Equal(3, connectionOrdinal);
    }

    [Fact]
    public void PromotedConnectionTrackingSurvivesUnrelatedSynFlood()
    {
        var downstream = new TcpConnection(0x0200000A, 0x0100000A, 21_060, 49_628);
        var tracker = new TcpDownstreamConnectionTracker();
        var observedTimestamp = Stopwatch.GetTimestamp();

        Assert.True(tracker.ObserveSyn(
            in downstream,
            hasAcknowledgment: true,
            acceptUnpairedAcknowledgment: true,
            sequenceNumber: 1_000,
            acknowledgmentNumber: 0,
            newConnectionOrdinal: 1,
            observedTimestamp));
        tracker.MarkPromoted(in downstream, expectedConnectionOrdinal: 1);

        for (var index = 0; index < 256; index++)
        {
            var unrelatedUpstream = new TcpConnection(
                SourceAddress: 0x0300000A + (uint)index,
                DestinationAddress: 0x0400000A,
                SourcePort: (ushort)(30_000 + index),
                DestinationPort: 443);
            Assert.True(tracker.ObserveSyn(
                in unrelatedUpstream,
                hasAcknowledgment: false,
                acceptUnpairedAcknowledgment: false,
                sequenceNumber: (uint)(2_000 + index),
                acknowledgmentNumber: 0,
                newConnectionOrdinal: index + 2,
                observedTimestamp: observedTimestamp + index + 1));
        }

        Assert.True(tracker.TryResolvePacketOrdinal(
            in downstream,
            sequenceNumber: 1_050,
            hasAcknowledgment: true,
            acknowledgmentNumber: 0,
            observedTimestamp: observedTimestamp + 300,
            out var connectionOrdinal));
        Assert.Equal(1, connectionOrdinal);
    }

    [Fact]
    public void SynSequenceAnchorReassemblesOutOfOrderCandidateSegments()
    {
        var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var frame = Build0336(captureMilliseconds);
        const uint initialSequence = 10_000;
        const int split = 9;
        var admission = new CapturePacketAdmission(CapturePacketAdmissionKind.Candidate, 1, ReleasedLock: false);
        using var candidates = new TcpWorldConnectionCandidateTracker();
        CaptureConnectionPromotion? promotion = null;

        try
        {
            var tail = CapturedPacket.CreateCopy(
                connection,
                admission,
                frame.AsSpan(split),
                initialSequence + split,
                captureMilliseconds);
            Assert.Equal(
                CandidatePacketDisposition.Buffered,
                candidates.Add(
                    tail,
                    allowNewCandidate: true,
                    allowMidstreamRecovery: false,
                    initialSequenceNumber: initialSequence,
                    connectionOrdinal: 1,
                    observedTimestamp: Stopwatch.GetTimestamp(),
                    out _));

            var head = CapturedPacket.CreateCopy(
                connection,
                admission,
                frame.AsSpan(0, split),
                initialSequence,
                captureMilliseconds);
            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                candidates.Add(
                    head,
                    allowNewCandidate: true,
                    allowMidstreamRecovery: false,
                    initialSequenceNumber: initialSequence,
                    connectionOrdinal: 1,
                    observedTimestamp: Stopwatch.GetTimestamp(),
                    out promotion));
            Assert.NotNull(promotion);
            Assert.Equal(initialSequence, promotion.ReplayStartSequenceNumber);
            Assert.Equal(2, promotion.Packets.Count);
        }
        finally
        {
            promotion?.Return();
        }
    }

    [Fact]
    public void AnchoredCandidateRecoversWhenTheFirstCapturedDataSegmentIsMissing()
    {
        var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var firstFrame = Build0336(captureMilliseconds);
        var secondFrame = Build3336Frame(1, "A");
        const uint initialSequence = 10_000;
        const uint firstCapturedSequence = initialSequence + 100;
        var admission = new CapturePacketAdmission(CapturePacketAdmissionKind.Candidate, 1, ReleasedLock: false);
        var firstObservedTimestamp = Stopwatch.GetTimestamp();
        var recoveryTimestamp = firstObservedTimestamp +
            (long)(CaptureBufferLimits.CandidateAnchorRecoveryDelay.TotalSeconds * Stopwatch.Frequency) + 1;
        using var candidates = new TcpWorldConnectionCandidateTracker();
        CaptureConnectionPromotion? promotion = null;

        try
        {
            var first = CapturedPacket.CreateCopy(
                connection,
                admission,
                firstFrame,
                firstCapturedSequence,
                captureMilliseconds);
            Assert.Equal(
                CandidatePacketDisposition.Buffered,
                candidates.Add(
                    first,
                    allowNewCandidate: true,
                    allowMidstreamRecovery: false,
                    initialSequence,
                    connectionOrdinal: 1,
                    firstObservedTimestamp,
                    out _));

            var second = CapturedPacket.CreateCopy(
                connection,
                admission,
                secondFrame,
                firstCapturedSequence + (uint)firstFrame.Length,
                captureMilliseconds + 1);
            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                candidates.Add(
                    second,
                    allowNewCandidate: true,
                    allowMidstreamRecovery: false,
                    initialSequence,
                    connectionOrdinal: 1,
                    recoveryTimestamp,
                    out promotion));
            Assert.NotNull(promotion);
            Assert.Equal(firstCapturedSequence, promotion.ReplayStartSequenceNumber);
        }
        finally
        {
            promotion?.Return();
        }
    }

    [Fact]
    public void UnanchoredCandidateRebuildsWhenAnEarlierTcpSegmentArrives()
    {
        var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var frame = Build3336Frame(1, "A");
        const uint sequenceNumber = 10_000;
        var split = frame.Length / 2;
        var admission = new CapturePacketAdmission(CapturePacketAdmissionKind.Candidate, 1, ReleasedLock: false);
        using var candidates = new TcpWorldConnectionCandidateTracker();
        CaptureConnectionPromotion? promotion = null;

        try
        {
            var tail = CapturedPacket.CreateCopy(
                connection,
                admission,
                frame.AsSpan(split),
                sequenceNumber + (uint)split,
                captureMilliseconds);
            Assert.Equal(
                CandidatePacketDisposition.Buffered,
                candidates.Add(
                    tail,
                    allowNewCandidate: true,
                    allowMidstreamRecovery: false,
                    initialSequenceNumber: null,
                    connectionOrdinal: 1,
                    observedTimestamp: Stopwatch.GetTimestamp(),
                    out _));

            var head = CapturedPacket.CreateCopy(
                connection,
                admission,
                frame.AsSpan(0, split),
                sequenceNumber,
                captureMilliseconds);
            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                candidates.Add(
                    head,
                    allowNewCandidate: true,
                    allowMidstreamRecovery: false,
                    initialSequenceNumber: null,
                    connectionOrdinal: 1,
                    observedTimestamp: Stopwatch.GetTimestamp(),
                    out promotion));
            Assert.NotNull(promotion);
            Assert.Equal(sequenceNumber, promotion.ReplayStartSequenceNumber);
        }
        finally
        {
            promotion?.Return();
        }
    }

    [Theory]
    [InlineData(272, 276)]
    [InlineData(276, 280)]
    public void Weak1136HandshakeNeverConfirmsByItself(int bodyLength, int frameLength)
    {
        var handshake = BuildFrame(0x11, 0x36, new byte[bodyLength]);
        Assert.Equal(frameLength, handshake.Length);

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(TcpWorldStreamClassification.Pending, classifier.Append(handshake, 1_000));
    }

    [Fact]
    public void UnsupportedOpcodeShapesDoNotConfirmWorldStream()
    {
        var unsupported36 = BuildFrame(0xff, 0x36, new byte[32]);
        var unsupported38 = BuildFrame(0xaa, 0x38, new byte[32]);
        var combined = new byte[unsupported36.Length + unsupported38.Length];
        unsupported36.CopyTo(combined, 0);
        unsupported38.CopyTo(combined, unsupported36.Length);

        using var directClassifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(TcpWorldStreamClassification.Pending, directClassifier.Append(combined, 1_000));

        using var compressedClassifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: false);
        Assert.Equal(
            TcpWorldStreamClassification.Pending,
            compressedClassifier.Append(BuildCompressedFrame(unsupported36), 1_000));
    }

    [Fact]
    public void MidstreamRecoveryStillParsesCanonicalConnectionStart()
    {
        var handshake = BuildFrame(0x11, 0x36, new byte[276]);
        var compressedGameplay = BuildCompressedFrame(BuildFrame(0x15, 0x36, new byte[512]));

        using var classifier = new TcpWorldStreamClassifier(allowMidstreamRecovery: true);
        Assert.Equal(TcpWorldStreamClassification.Pending, classifier.Append(handshake, 1_000));
        Assert.Equal(TcpWorldStreamClassification.Confirmed, classifier.Append(compressedGameplay, 1_100));
    }

    [Fact]
    public async Task MidstreamPromotionReplaysFromTheRecoveredTcpSequence()
    {
        var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var tick = BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds));
        var echo = Build0336(captureMilliseconds);
        const int recoveredOffset = 9;
        var payload = new byte[recoveredOffset + tick.Length + echo.Length];
        payload.AsSpan(0, recoveredOffset).Fill(0x7a);
        payload[0] = 0x20;
        tick.CopyTo(payload, recoveredOffset);
        echo.CopyTo(payload, recoveredOffset + tick.Length);

        var candidateAdmission = new CapturePacketAdmission(
            CapturePacketAdmissionKind.Candidate,
            1,
            ReleasedLock: false);
        using var candidates = new TcpWorldConnectionCandidateTracker();
        CaptureConnectionPromotion? promotion = null;
        var scene = new SceneLiveReadModel();
        var dispatcher = new PacketCaptureDispatcher(SceneSinkFactory.CreateForLive(scene));

        try
        {
            var packet = CapturedPacket.CreateCopy(
                connection,
                candidateAdmission,
                payload,
                sequenceNumber: 5_000,
                captureTimestampMilliseconds: captureMilliseconds);
            Assert.Equal(
                CandidatePacketDisposition.Confirmed,
                candidates.Add(
                    packet,
                    allowNewCandidate: true,
                    allowMidstreamRecovery: true,
                    initialSequenceNumber: null,
                    connectionOrdinal: 1,
                    observedTimestamp: Stopwatch.GetTimestamp(),
                    out promotion));
            Assert.NotNull(promotion);
            Assert.Equal(5_000u + recoveredOffset, promotion.ReplayStartSequenceNumber);

            var item = CaptureDispatchItem.ForPromotion(promotion);
            try
            {
                Assert.True(dispatcher.DispatchItem(item));
            }
            finally
            {
                item.Return();
            }
        }
        finally
        {
            promotion?.Return();
            await dispatcher.StopAsync();
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public async Task DispatcherKeepsTimelineMonotonicWhenPromotionReplaysOlderPackets()
    {
        var started = DateTimeOffset.UtcNow.AddSeconds(-10);
        var laterTimestamp = started.AddSeconds(3).ToUnixTimeMilliseconds();
        var earlierTimestamp = started.AddSeconds(1).ToUnixTimeMilliseconds();
        var firstConnection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var nextConnection = new TcpConnection(0x0300000A, 0x0400000A, 21_061, 49_629);
        var scene = new SceneLiveReadModel(started);
        var dispatcher = new PacketCaptureDispatcher(SceneSinkFactory.CreateForLive(scene));

        try
        {
            DispatchPromotion(
                dispatcher,
                firstConnection,
                Build3336Frame(1, "A"),
                sequenceNumber: 100,
                captureTimestampMilliseconds: laterTimestamp,
                candidateOrdinal: 1);
            DispatchPromotion(
                dispatcher,
                nextConnection,
                Build3336Frame(2, "B"),
                sequenceNumber: 200,
                captureTimestampMilliseconds: earlierTimestamp,
                candidateOrdinal: 2);

            var offsets = ReadJournalOffsets(scene);
            Assert.True(offsets.Length >= 3);
            for (var index = 1; index < offsets.Length; index++)
            {
                Assert.True(offsets[index] >= offsets[index - 1]);
            }

            using var source = scene.CreatePlaybackSource();
            _ = ScenePlaybackTrackIndex.Build(source.CreateTimelineSegment(), TestContext.Current.CancellationToken);
        }
        finally
        {
            await dispatcher.StopAsync();
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public async Task DispatcherUsesContinuousDeliveryTimeForOutOfOrderSegments()
    {
        var started = DateTimeOffset.UtcNow.AddSeconds(-10);
        var tailTimestamp = started.AddSeconds(1).ToUnixTimeMilliseconds();
        var headTimestamp = started.AddSeconds(3).ToUnixTimeMilliseconds();
        var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var scene = new SceneLiveReadModel(started);
        var dispatcher = new PacketCaptureDispatcher(SceneSinkFactory.CreateForLive(scene));
        var frame = Build3336Frame(1, "A");
        var split = frame.Length / 2;
        const uint sequenceNumber = 10_000;

        try
        {
            Assert.True(CaptureConnectionGate.TryPromote(in connection, out var admission, out _));
            var tail = CapturedPacket.CreateCopy(
                connection,
                admission,
                frame.AsSpan(split),
                sequenceNumber + (uint)split,
                tailTimestamp);
            try
            {
                Assert.False(dispatcher.DispatchCapturedPacket(tail, sequenceNumber));
            }
            finally
            {
                tail.Return();
            }

            var head = CapturedPacket.CreateCopy(
                connection,
                admission,
                frame.AsSpan(0, split),
                sequenceNumber,
                headTimestamp);
            try
            {
                Assert.True(dispatcher.DispatchCapturedPacket(head));
            }
            finally
            {
                head.Return();
            }

            Assert.Equal(
                (headTimestamp - started.ToUnixTimeMilliseconds()) * TimeSpan.TicksPerMillisecond,
                Assert.Single(ReadJournalOffsets(scene)));
        }
        finally
        {
            await dispatcher.StopAsync();
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public void SynPayloadSequenceStartsAfterTheSynByte()
    {
        Assert.Equal(100u, WinDivertCaptureService.ResolvePayloadSequenceNumber(100, hasSynFlag: false));
        Assert.Equal(101u, WinDivertCaptureService.ResolvePayloadSequenceNumber(100, hasSynFlag: true));
        Assert.Equal(0u, WinDivertCaptureService.ResolvePayloadSequenceNumber(uint.MaxValue, hasSynFlag: true));
    }

    [Fact]
    public void UnknownInboundPayloadCanEnterTheBoundedWorldClassifier()
    {
        Assert.True(WinDivertCaptureService.ShouldTrackCandidate(
            isInbound: true,
            isExpectedDownstream: false,
            isKnownProcessPort: false,
            hasBufferedCandidate: false));
        Assert.False(WinDivertCaptureService.ShouldTrackCandidate(
            isInbound: false,
            isExpectedDownstream: false,
            isKnownProcessPort: false,
            hasBufferedCandidate: false));
    }

    [Fact]
    public void OnlyTheActiveAttemptQueuesAConnectionClose()
    {
        var connection = new TcpConnection(0x0100000A, 0x0200000A, 21_060, 49_628);
        var unrelatedConnection = new TcpConnection(0x0300000A, 0x0400000A, 443, 49_629);
        var rejectedAdmission = new CapturePacketAdmission(
            CapturePacketAdmissionKind.Rejected,
            Generation: 0,
            ReleasedLock: false);

        try
        {
            Assert.False(WinDivertCaptureService.ShouldDispatchConnectionClose(
                in unrelatedConnection,
                in rejectedAdmission,
                connectionOrdinal: 0));
            Assert.True(CaptureConnectionGate.TryPromote(
                in connection,
                out var activeAdmission,
                out _,
                connectionOrdinal: 7));
            Assert.True(WinDivertCaptureService.ShouldDispatchConnectionClose(
                in connection,
                in activeAdmission,
                connectionOrdinal: 7));

            var reverseConnection = connection.Reverse();
            Assert.True(WinDivertCaptureService.ShouldDispatchConnectionClose(
                in reverseConnection,
                in rejectedAdmission,
                connectionOrdinal: 7));
            Assert.False(WinDivertCaptureService.ShouldDispatchConnectionClose(
                in reverseConnection,
                in rejectedAdmission,
                connectionOrdinal: 8));

            var fallbackOrdinal = WinDivertCaptureService.ResolveCloseConnectionOrdinal(
                in reverseConnection,
                packetConnectionOrdinal: 0,
                queuedPromotionOrdinal: 0);
            Assert.Equal(7, fallbackOrdinal);
            Assert.True(WinDivertCaptureService.ShouldDispatchConnectionClose(
                in reverseConnection,
                in rejectedAdmission,
                fallbackOrdinal));
            Assert.True(CaptureConnectionGate.TryClose(
                in reverseConnection,
                expectedGeneration: 0,
                expectedConnectionOrdinal: fallbackOrdinal,
                out var closedConnection));
            Assert.Equal(connection, closedConnection);
        }
        finally
        {
            CaptureConnectionGate.Unlock();
        }
    }

    private static byte[] Build0336(long captureMilliseconds)
    {
        var body = new byte[18];
        body[0] = 0;
        body[1] = 0;
        BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(2), YearOneToUnixEpochMilliseconds + captureMilliseconds - 50);
        BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(10), captureMilliseconds);
        return BuildFrame(0x03, 0x36, body);
    }

    private static byte[] Build3336Frame(int playerId, string nickname)
    {
        Assert.InRange(playerId, 1, 0x7f);
        var nicknameBytes = Encoding.UTF8.GetBytes(nickname);
        Assert.InRange(nicknameBytes.Length, 1, 72);
        var body = new byte[12 + nicknameBytes.Length];
        var offset = 0;
        body[offset++] = (byte)playerId;
        body[offset++] = 0x5f;
        body[offset++] = 0;
        body[offset++] = 0x37;
        body[offset++] = (byte)nicknameBytes.Length;
        nicknameBytes.CopyTo(body.AsSpan(offset));
        offset += nicknameBytes.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset), 1001);
        offset += sizeof(ushort);
        offset += sizeof(int);
        body[offset] = 1;
        return BuildFrame(0x33, 0x36, body);
    }

    private static void DispatchPromotion(
        PacketCaptureDispatcher dispatcher,
        in TcpConnection connection,
        byte[] frame,
        uint sequenceNumber,
        long captureTimestampMilliseconds,
        long candidateOrdinal)
    {
        var packet = CapturedPacket.CreateCopy(
            connection,
            new CapturePacketAdmission(CapturePacketAdmissionKind.Candidate, 0, ReleasedLock: false),
            frame,
            sequenceNumber,
            captureTimestampMilliseconds);
        var promotion = new CaptureConnectionPromotion(
            connection,
            replayStartSequenceNumber: sequenceNumber,
            candidateOrdinal,
            packets: [packet]);
        var item = CaptureDispatchItem.ForPromotion(promotion);
        try
        {
            Assert.True(dispatcher.DispatchItem(item));
        }
        finally
        {
            item.Return();
        }
    }

    private static long[] ReadJournalOffsets(SceneLiveReadModel scene)
    {
        var offsets = new List<long>(scene.Journal.Count);
        var cursor = scene.Journal.CreateCursor(scene.Journal.FirstObservationOrdinal);
        while (cursor.NextObservationOrdinal < scene.Journal.NextObservationOrdinal)
        {
            var result = scene.Journal.ReadEntries(cursor, ObservedEventJournal.SegmentCapacity, entries =>
            {
                for (var index = 0; index < entries.Count; index++)
                {
                    offsets.Add(entries[index].Stamp.OffsetTicks);
                }
            });
            if (result.Count == 0)
            {
                break;
            }

            cursor = result.Cursor;
        }

        return [.. offsets];
    }

    private static int CountStateObservations(ObservedEventJournal journal, int entityId)
    {
        var count = 0;
        var cursor = journal.CreateCursor(journal.FirstObservationOrdinal);
        while (cursor.NextObservationOrdinal < journal.NextObservationOrdinal)
        {
            var result = journal.ReadEntries(cursor, ObservedEventJournal.SegmentCapacity, entries =>
            {
                for (var index = 0; index < entries.Count; index++)
                {
                    if (entries[index].Domain == ObservedEventDomain.State &&
                        entries[index].State.EntityId == entityId)
                    {
                        count++;
                    }
                }
            });
            if (result.Count == 0)
            {
                break;
            }

            cursor = result.Cursor;
        }

        return count;
    }

    private static byte[] BuildCompressedFrame(ReadOnlySpan<byte> inner)
    {
        var compressedBuffer = new byte[LZ4Codec.MaximumOutputSize(inner.Length)];
        var compressedLength = LZ4Codec.Encode(inner, compressedBuffer);
        Assert.True(compressedLength > 0);

        Span<byte> prefix = stackalloc byte[5];
        Assert.True(PacketTransportCodec.TryWriteVarInt(compressedLength + 10, prefix, out var prefixLength));
        var frame = new byte[prefixLength + 6 + compressedLength];
        prefix[..prefixLength].CopyTo(frame);
        frame[prefixLength] = 0xff;
        frame[prefixLength + 1] = 0xff;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(prefixLength + 2), inner.Length);
        compressedBuffer.AsSpan(0, compressedLength).CopyTo(frame.AsSpan(prefixLength + 6));
        return frame;
    }

    private static byte[] BuildFrame(byte opcode0, byte opcode1, ReadOnlySpan<byte> body)
    {
        Span<byte> prefix = stackalloc byte[5];
        Assert.True(PacketTransportCodec.TryWriteVarInt(body.Length + 6, prefix, out var prefixLength));
        var frame = new byte[prefixLength + sizeof(ushort) + body.Length];
        prefix[..prefixLength].CopyTo(frame);
        frame[prefixLength] = opcode0;
        frame[prefixLength + 1] = opcode1;
        body.CopyTo(frame.AsSpan(prefixLength + sizeof(ushort)));
        return frame;
    }

    private static byte[] WriteInt64(long value)
    {
        var bytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return bytes;
    }
}
