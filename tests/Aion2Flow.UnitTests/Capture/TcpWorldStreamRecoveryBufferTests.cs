using System.Buffers.Binary;
using System.Text;
using Cloris.Aion2Flow.Capture.Streams;

using static Cloris.Aion2Flow.Tests.Capture.TransportTestData;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class TcpWorldStreamRecoveryBufferTests
{
    [Fact]
    public void ReplaysDirectDataFromTheRecoveredCanonicalBoundary()
    {
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var prefix = Enumerable.Repeat((byte)0x7a, 9).ToArray();
        var tick = BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds));
        var identity = Build3336Frame(23, "Player");
        var payload = Concat(prefix, tick, identity);
        var replayed = new List<(uint SequenceNumber, byte[] Payload)>();

        using var buffer = new TcpWorldStreamRecoveryBuffer();
        Assert.Equal(
            TcpWorldStreamRecoveryResult.Confirmed,
            buffer.Append(5_000, payload, At(captureMilliseconds)));

        Assert.True(buffer.Replay((sequenceNumber, chunk, _) =>
        {
            replayed.Add((sequenceNumber, chunk.ToArray()));
            return true;
        }));

        Assert.Equal(5_009u, replayed[0].SequenceNumber);
        Assert.Equal(Concat(tick, identity), replayed[0].Payload);
    }

    [Fact]
    public void ReplaysLengthPrefixedDataFromTheOuterEnvelope()
    {
        var captureMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var tick = BuildFrame(0x00, 0x36, WriteInt64(captureMilliseconds));
        var identity = Build3336Frame(23, "Player");
        var gameplay = BuildFrame(0x15, 0x36, new byte[32]);
        var payload = Concat(
            BuildLengthPrefixedEnvelope(Concat(tick, identity)),
            BuildLengthPrefixedEnvelope(gameplay));
        var replayed = new List<(uint SequenceNumber, byte[] Payload)>();

        using var buffer = new TcpWorldStreamRecoveryBuffer();
        Assert.Equal(
            TcpWorldStreamRecoveryResult.Confirmed,
            buffer.Append(8_000, payload, At(captureMilliseconds)));

        Assert.True(buffer.Replay((sequenceNumber, chunk, _) =>
        {
            replayed.Add((sequenceNumber, chunk.ToArray()));
            return true;
        }));

        Assert.Equal(8_000u, replayed[0].SequenceNumber);
        Assert.Equal(payload, replayed[0].Payload);
    }

    private static CapturedPacketTimestamp At(long unixMilliseconds)
        => new(unixMilliseconds, unixMilliseconds * 10);

    private static byte[] Build3336Frame(int playerId, string nickname)
    {
        var nicknameBytes = Encoding.UTF8.GetBytes(nickname);
        var body = new byte[12 + nicknameBytes.Length];
        body[0] = (byte)playerId;
        body[1] = 0x5f;
        body[3] = 0x37;
        body[4] = (byte)nicknameBytes.Length;
        nicknameBytes.CopyTo(body.AsSpan(5));
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(5 + nicknameBytes.Length), 1001);
        body[11 + nicknameBytes.Length] = 1;
        return BuildFrame(0x33, 0x36, body);
    }

    private static byte[] WriteInt64(long value)
    {
        var result = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(result, value);
        return result;
    }

}
