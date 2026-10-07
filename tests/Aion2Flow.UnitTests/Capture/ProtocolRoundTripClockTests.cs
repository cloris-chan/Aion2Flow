using System.Buffers.Binary;
using System.Diagnostics;
using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Protocol.Packets;
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
        var clientSentUnixMilliseconds = Origin.ToUnixTimeMilliseconds() + runtimeMilliseconds + utcOffsetMilliseconds;

        timeProvider.Set(
            parserTimestamp,
            Origin.AddMilliseconds(runtimeMilliseconds + roundTripMilliseconds + parserDelayMilliseconds + utcOffsetMilliseconds));
        var timelineArrivalUnixMilliseconds = mapper.ToTimelineUnixMilliseconds(arrivalTimestamp);
        var correctedArrivalUnixMilliseconds = mapper.ToCurrentUtcUnixMilliseconds(arrivalTimestamp);

        Assert.Equal(
            Origin.ToUnixTimeMilliseconds() + runtimeMilliseconds + roundTripMilliseconds + utcOffsetMilliseconds,
            correctedArrivalUnixMilliseconds);
        Assert.NotEqual(roundTripMilliseconds, timelineArrivalUnixMilliseconds - clientSentUnixMilliseconds);
    }

    [Fact]
    public void Current0336FormatReachesObserverWithClientMonotonicTimestamp()
    {
        const uint clientSentMonotonicMilliseconds = 6_364_226;
        const long captureTimestampAtTenMegahertz = 63_642_752_478;
        var captureTimestamp = (long)Math.Round(
            captureTimestampAtTenMegahertz * (double)Stopwatch.Frequency / TestTimestampFrequency);
        var packet = Convert.FromHexString("1803360000421C61E8030000009AE75815A1010000");
        var processingTimestamp = new PacketProcessingTimestamp(1_791_359_510_419, captureTimestamp);
        ProtocolRoundTripObservation? observation = null;
        using var processor = new PacketStreamProcessor(
            SceneSinkFactory.CreateForLive(new SceneLiveReadModel())(),
            value => observation = value);

        Assert.True(Packet0336RoundTripParser.TryParse(packet, out var parsed));
        Assert.Equal(clientSentMonotonicMilliseconds, parsed.ClientSentMonotonicMilliseconds);
        Assert.True(processor.AppendAndProcess(packet, in Connection, in processingTimestamp));
        Assert.True(observation.HasValue);
        Assert.Equal(clientSentMonotonicMilliseconds, observation.Value.ClientSentMonotonicMilliseconds);
        Assert.Equal(captureTimestamp, observation.Value.ArrivalTimestamp);
    }

    [Fact]
    public void Current0336FormatCalculatesRoundTripFromCaptureQpc()
    {
        const long captureTimestampAtTenMegahertz = 63_642_752_478;
        var captureTimestamp = (long)Math.Round(
            captureTimestampAtTenMegahertz * (double)Stopwatch.Frequency / TestTimestampFrequency);
        var packet = Convert.FromHexString("1803360000421C61E8030000009AE75815A1010000");
        var processingTimestamp = new PacketProcessingTimestamp(1_791_359_510_419, captureTimestamp);
        ProtocolRoundTripObservation? observation = null;
        using var processor = new PacketStreamProcessor(
            SceneSinkFactory.CreateForLive(new SceneLiveReadModel())(),
            value => observation = value);
        Assert.True(processor.AppendAndProcess(packet, in Connection, in processingTimestamp));

        var estimator = new ProtocolRoundTripEstimator();
        Assert.True(estimator.TryObserveEcho(
            1,
            observation!.Value.ClientSentMonotonicMilliseconds,
            observation.Value.ArrivalTimestamp,
            observation.Value.ArrivalTimestamp,
            out var roundTripMilliseconds));
        Assert.Equal(49.248, roundTripMilliseconds, 3);
    }

    [Fact]
    public void LegacyUtcTimestampFormatIsNotAcceptedAsCurrentEcho()
    {
        var packet = Convert.FromHexString("1803360000D5544D96233A00003E7D2084A0010000");

        Assert.False(Packet0336RoundTripParser.TryParse(packet, out _));
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
                Build0336(6_364_226, laterUnixMilliseconds),
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
                Build0336(6_374_226, earlierUnixMilliseconds),
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
        var frame = Build0336(6_454_226, headUnixMilliseconds);
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

    private static byte[] Build0336(uint clientSentMonotonicMilliseconds, long serverUnixMilliseconds)
    {
        var body = new byte[18];
        BinaryPrimitives.WriteUInt64LittleEndian(
            body.AsSpan(2),
            (1_000UL << 24) | clientSentMonotonicMilliseconds);
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
