using System.Buffers.Binary;
using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.SceneRuntime;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class ProtocolRoundTripClockTests
{
    private const long TestTimestampFrequency = 10_000_000;
    private static readonly TcpConnection Connection = new(1, 2, 3, 4);
    private static readonly DateTimeOffset Origin = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);

    [Theory]
    [InlineData(-200)]
    [InlineData(200)]
    public void CurrentUtcCalibrationRemovesAccumulatedClockOffset(int utcOffsetMilliseconds)
    {
        var timeProvider = new ManualTimeProvider(Origin);
        var mapper = new CaptureTimestampMapper(timeProvider);
        const long runtimeMilliseconds = 24 * 60 * 60 * 1_000;
        const long roundTripMilliseconds = 60;
        const long parserDelayMilliseconds = 5_000;
        var sendTimestamp = ToTimestamp(runtimeMilliseconds);
        var arrivalTimestamp = sendTimestamp + ToTimestamp(roundTripMilliseconds);
        var parserTimestamp = arrivalTimestamp + ToTimestamp(parserDelayMilliseconds);
        var clientSentUnixMilliseconds = Origin.ToUnixTimeMilliseconds() +
            runtimeMilliseconds +
            utcOffsetMilliseconds;

        timeProvider.Set(
            parserTimestamp,
            Origin.AddMilliseconds(
                runtimeMilliseconds +
                roundTripMilliseconds +
                parserDelayMilliseconds +
                utcOffsetMilliseconds));
        var timelineArrivalUnixMilliseconds = mapper.ToTimelineUnixMilliseconds(arrivalTimestamp);
        var correctedArrivalUnixMilliseconds = mapper.ToCurrentUtcUnixMilliseconds(arrivalTimestamp);
        var estimator = new ProtocolRoundTripEstimator();

        Assert.Equal(
            Origin.ToUnixTimeMilliseconds() +
            runtimeMilliseconds +
            roundTripMilliseconds +
            utcOffsetMilliseconds,
            correctedArrivalUnixMilliseconds);
        Assert.NotEqual(roundTripMilliseconds, timelineArrivalUnixMilliseconds - clientSentUnixMilliseconds);
        Assert.True(estimator.TryObserveEcho(
            1,
            clientSentUnixMilliseconds,
            correctedArrivalUnixMilliseconds,
            arrivalTimestamp,
            parserTimestamp,
            out var estimatedRoundTripMilliseconds));
        Assert.Equal(roundTripMilliseconds, estimatedRoundTripMilliseconds);
    }

    [Fact]
    public void StructurallyValidEchoReachesObserverBeforeUtcCorrection()
    {
        var mappedArrivalUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var clientSentUnixMilliseconds = mappedArrivalUnixMilliseconds + 100;
        const long arrivalTimestamp = 123_456_789;
        var processingTimestamp = new PacketProcessingTimestamp(
            mappedArrivalUnixMilliseconds + 500,
            arrivalTimestamp);
        ProtocolRoundTripObservation? observation = null;
        using var processor = new PacketStreamProcessor(
            SceneSinkFactory.CreateForLive(new SceneLiveReadModel())(),
            value => observation = value);

        Assert.True(processor.AppendAndProcess(
            Build0336(clientSentUnixMilliseconds, mappedArrivalUnixMilliseconds),
            in Connection,
            in processingTimestamp));
        Assert.True(observation.HasValue);
        Assert.Equal(clientSentUnixMilliseconds, observation.Value.ClientSentUnixMilliseconds);
        Assert.Equal(arrivalTimestamp, observation.Value.ArrivalTimestamp);
    }

    [Fact]
    public async Task DispatcherPreservesRawArrivalAcrossClampedSceneTimeline()
    {
        var laterUnixMilliseconds = Origin.AddSeconds(3).ToUnixTimeMilliseconds();
        var earlierUnixMilliseconds = Origin.AddSeconds(1).ToUnixTimeMilliseconds();
        const long laterArrivalTimestamp = 30_000;
        const long earlierArrivalTimestamp = 10_000;
        var firstConnection = new TcpConnection(1, 2, 3, 4);
        var secondConnection = new TcpConnection(5, 6, 7, 8);
        var observations = new List<ProtocolRoundTripObservation>();
        var dispatcher = new PacketCaptureDispatcher(
            SceneSinkFactory.CreateForLive(new SceneLiveReadModel(Origin)),
            observations.Add,
            connectionLockedObserver: null);

        CaptureConnectionGate.Unlock();
        try
        {
            Assert.True(CaptureConnectionGate.TryPromote(in firstConnection, out var firstAdmission, out _));
            var firstPacket = CapturedPacket.CreateCopy(
                firstConnection,
                firstAdmission,
                Build0336(laterUnixMilliseconds - 50, laterUnixMilliseconds),
                sequenceNumber: 100,
                captureTimestampMilliseconds: laterUnixMilliseconds,
                captureTimestamp: laterArrivalTimestamp);
            try
            {
                Assert.True(dispatcher.DispatchCapturedPacket(firstPacket));
            }
            finally
            {
                firstPacket.Return();
            }

            Assert.True(CaptureConnectionGate.TryPromote(in secondConnection, out var secondAdmission, out _));
            var secondPacket = CapturedPacket.CreateCopy(
                secondConnection,
                secondAdmission,
                Build0336(earlierUnixMilliseconds - 50, earlierUnixMilliseconds),
                sequenceNumber: 200,
                captureTimestampMilliseconds: earlierUnixMilliseconds,
                captureTimestamp: earlierArrivalTimestamp);
            try
            {
                Assert.True(dispatcher.DispatchCapturedPacket(secondPacket));
            }
            finally
            {
                secondPacket.Return();
            }

            Assert.Equal(2, observations.Count);
            Assert.Equal(earlierArrivalTimestamp, observations[1].ArrivalTimestamp);
        }
        finally
        {
            await dispatcher.StopAsync();
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public async Task OutOfOrderFrameUsesTimeWhenAllSegmentsBecomeAvailable()
    {
        var tailUnixMilliseconds = Origin.AddSeconds(1).ToUnixTimeMilliseconds();
        var headUnixMilliseconds = Origin.AddSeconds(3).ToUnixTimeMilliseconds();
        const long tailArrivalTimestamp = 10_000;
        const long headArrivalTimestamp = 30_000;
        var connection = new TcpConnection(1, 2, 3, 4);
        ProtocolRoundTripObservation? observation = null;
        var dispatcher = new PacketCaptureDispatcher(
            SceneSinkFactory.CreateForLive(new SceneLiveReadModel(Origin)),
            value => observation = value,
            connectionLockedObserver: null);
        var frame = Build0336(headUnixMilliseconds - 50, headUnixMilliseconds);
        var split = frame.Length / 2;
        const uint sequenceNumber = 1_000;

        CaptureConnectionGate.Unlock();
        try
        {
            Assert.True(CaptureConnectionGate.TryPromote(in connection, out var admission, out _));
            var tail = CapturedPacket.CreateCopy(
                connection,
                admission,
                frame.AsSpan(split),
                sequenceNumber + (uint)split,
                tailUnixMilliseconds,
                captureTimestamp: tailArrivalTimestamp);
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
                headUnixMilliseconds,
                captureTimestamp: headArrivalTimestamp);
            try
            {
                Assert.True(dispatcher.DispatchCapturedPacket(head));
            }
            finally
            {
                head.Return();
            }

            Assert.True(observation.HasValue);
            Assert.Equal(headArrivalTimestamp, observation.Value.ArrivalTimestamp);
        }
        finally
        {
            await dispatcher.StopAsync();
            CaptureConnectionGate.Unlock();
        }
    }

    private static byte[] Build0336(long clientSentUnixMilliseconds, long serverUnixMilliseconds)
    {
        const long yearOneToUnixEpochMilliseconds = 62_135_596_800_000;
        var body = new byte[18];
        BinaryPrimitives.WriteInt64LittleEndian(
            body.AsSpan(2),
            yearOneToUnixEpochMilliseconds + clientSentUnixMilliseconds);
        BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(10), serverUnixMilliseconds);

        Span<byte> prefix = stackalloc byte[5];
        Assert.True(PacketTransportCodec.TryWriteVarInt(body.Length + 6, prefix, out var prefixLength));
        var frame = new byte[prefixLength + sizeof(ushort) + body.Length];
        prefix[..prefixLength].CopyTo(frame);
        frame[prefixLength] = 0x03;
        frame[prefixLength + 1] = 0x36;
        body.CopyTo(frame.AsSpan(prefixLength + sizeof(ushort)));
        return frame;
    }

    private static long ToTimestamp(long milliseconds)
        => checked(milliseconds * TestTimestampFrequency / 1_000);

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        private long _timestamp;

        public override long TimestampFrequency => TestTimestampFrequency;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public void Set(long timestamp, DateTimeOffset utcNow)
        {
            _timestamp = timestamp;
            _utcNow = utcNow;
        }
    }
}
