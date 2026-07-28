using System.Buffers.Binary;
using System.Text;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketLogReplayTransportAttemptTests
{
    [Fact]
    public void OrdinalLessNewConnectionActivationPrecedesBufferedFirstObservation()
    {
        var timestamp = DateTimeOffset.UtcNow;
        const string firstConnection = "33554442:21060->16777226:49628";
        const string secondConnection = "33554442:21060->16777226:49629";
        var log = string.Join(
            Environment.NewLine,
            FormatLegacyLine(timestamp, firstConnection, sequenceNumber: 100, Build3336Frame(1, "Old")),
            FormatLegacyLine(timestamp.AddMilliseconds(1), secondConnection, sequenceNumber: 200, Build3336Frame(2, "New")));

        using var reader = new StringReader(log);
        var replay = PacketLogReplayService.Replay(reader, "ordinal-less-connections.stream.log");

        Assert.Equal(2, replay.ReplayedLines);
        Assert.True(replay.SceneOwner.Entities.TryGet(1, out var oldPlayer));
        Assert.Equal("Old", oldPlayer.Nickname);
        Assert.True(replay.SceneOwner.Entities.TryGet(2, out var currentPlayer));
        Assert.Equal("New", currentPlayer.Nickname);

        var entries = ReadJournal(replay.SceneJournal);
        Assert.Collection(
            entries,
            static oldIdentity =>
            {
                Assert.Equal(ObservedEventDomain.State, oldIdentity.Domain);
                Assert.Equal(1, oldIdentity.EntityId);
                Assert.Equal("Old", oldIdentity.Text);
            },
            static boundary =>
            {
                Assert.Equal(ObservedEventDomain.Scene, boundary.Domain);
                Assert.Equal(SceneObservationKind.TransportStreamActivated, boundary.SceneKind);
            },
            static newIdentity =>
            {
                Assert.Equal(ObservedEventDomain.State, newIdentity.Domain);
                Assert.Equal(2, newIdentity.EntityId);
                Assert.Equal("New", newIdentity.Text);
            });
    }

    [Fact]
    public void NewAttemptActivationPrecedesItsFirstObservationAndStaleAttemptCannotResume()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var firstAttemptFrame = Build3336Frame(1, "Old");
        var secondAttemptFrame = Build3336Frame(2, "New");
        var split = firstAttemptFrame.Length / 2;
        const string connection = "33554442:21060->16777226:49628";
        var log = string.Join(
            Environment.NewLine,
            FormatLine(timestamp, connection, sequenceNumber: 100, connectionOrdinal: 1, firstAttemptFrame.AsSpan(0, split)),
            FormatLine(timestamp.AddMilliseconds(1), connection, sequenceNumber: 200, connectionOrdinal: 2, secondAttemptFrame),
            FormatLine(timestamp.AddMilliseconds(2), connection, sequenceNumber: 100 + (uint)split, connectionOrdinal: 1, firstAttemptFrame.AsSpan(split)));

        using var reader = new StringReader(log);
        var replay = PacketLogReplayService.Replay(reader, "transport-attempts.stream.log");

        Assert.Equal(3, replay.TotalLines);
        Assert.Equal(1, replay.ReplayedLines);
        Assert.Equal(2, replay.SkippedLines);
        Assert.Equal(1, replay.SkippedEventCounts["stale-transport-attempt"]);
        Assert.False(replay.SceneOwner.Entities.TryGet(1, out _));
        Assert.True(replay.SceneOwner.Entities.TryGet(2, out var currentPlayer));
        Assert.Equal("New", currentPlayer.Nickname);

        var entries = ReadJournal(replay.SceneJournal);
        Assert.Collection(
            entries,
            static boundary =>
            {
                Assert.Equal(ObservedEventDomain.Scene, boundary.Domain);
                Assert.Equal(SceneObservationKind.TransportStreamActivated, boundary.SceneKind);
            },
            static identity =>
            {
                Assert.Equal(ObservedEventDomain.State, identity.Domain);
                Assert.Equal(2, identity.EntityId);
                Assert.Equal("New", identity.Text);
            });
    }

    private static string FormatLine(
        DateTimeOffset timestamp,
        string connection,
        uint sequenceNumber,
        long connectionOrdinal,
        ReadOnlySpan<byte> payload) =>
        $"{timestamp:O}|dir=inbound|{connection}|seq={sequenceNumber}|attempt={connectionOrdinal}|len={payload.Length}|data={Convert.ToHexString(payload)}";

    private static string FormatLegacyLine(
        DateTimeOffset timestamp,
        string connection,
        uint sequenceNumber,
        ReadOnlySpan<byte> payload) =>
        $"{timestamp:O}|dir=inbound|{connection}|seq={sequenceNumber}|len={payload.Length}|data={Convert.ToHexString(payload)}";

    private static List<JournalObservation> ReadJournal(ObservedEventJournal journal)
    {
        var observations = new List<JournalObservation>(journal.Count);
        var cursor = journal.CreateCursor(journal.FirstObservationOrdinal);
        while (cursor.NextObservationOrdinal < journal.NextObservationOrdinal)
        {
            var result = journal.ReadEntries(cursor, ObservedEventJournal.SegmentCapacity, entries =>
            {
                for (var index = 0; index < entries.Count; index++)
                {
                    var entry = entries[index];
                    observations.Add(entry.Domain switch
                    {
                        ObservedEventDomain.Scene => new JournalObservation(
                            entry.Domain,
                            entry.Scene.Kind,
                            0,
                            null),
                        ObservedEventDomain.State => new JournalObservation(
                            entry.Domain,
                            SceneObservationKind.None,
                            entry.State.EntityId,
                            entry.State.Text),
                        _ => new JournalObservation(entry.Domain, SceneObservationKind.None, 0, null)
                    });
                }
            });
            if (result.Count == 0)
            {
                break;
            }

            cursor = result.Cursor;
        }

        return observations;
    }

    private static byte[] Build3336Frame(int playerId, string nickname)
    {
        var nicknameBytes = Encoding.UTF8.GetBytes(nickname);
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
        offset += sizeof(ushort) + sizeof(int);
        body[offset] = 1;
        return BuildFrame(0x33, 0x36, body);
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

    private readonly record struct JournalObservation(
        ObservedEventDomain Domain,
        SceneObservationKind SceneKind,
        int EntityId,
        string? Text);
}
