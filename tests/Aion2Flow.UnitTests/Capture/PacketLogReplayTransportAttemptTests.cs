using System.Buffers.Binary;
using System.Text;
using Cloris.Aion2Flow.Capture;
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

    [Fact]
    public void DistinctConnectionsKeepTheirOwnActiveTransportState()
    {
        var timestamp = DateTimeOffset.UtcNow;
        const string firstConnection = "16777226:7135->33554442:1541";
        const string secondConnection = "50331658:5464->67108874:1542";
        var log = string.Join(
            Environment.NewLine,
            FormatLine(timestamp, firstConnection, sequenceNumber: 100, connectionOrdinal: 129, Build3336Frame(1, "Direct")),
            FormatLine(timestamp.AddMilliseconds(1), secondConnection, sequenceNumber: 200, connectionOrdinal: 131, Build3336Frame(2, "Relay")),
            FormatLine(timestamp.AddMilliseconds(2), firstConnection, sequenceNumber: 1000, connectionOrdinal: 129, Build3336Frame(3, "Direct2")));

        using var reader = new StringReader(log);
        var replay = PacketLogReplayService.Replay(reader, "parallel-transports.stream.log");

        Assert.Equal(3, replay.ReplayedLines);
        Assert.True(replay.SceneOwner.Entities.TryGet(1, out var first));
        Assert.Equal("Direct", first.Nickname);
        Assert.True(replay.SceneOwner.Entities.TryGet(2, out var second));
        Assert.Equal("Relay", second.Nickname);
        Assert.True(replay.SceneOwner.Entities.TryGet(3, out var third));
        Assert.Equal("Direct2", third.Nickname);
        Assert.DoesNotContain(
            ReadJournal(replay.SceneJournal),
            static entry => entry.Domain == ObservedEventDomain.Scene &&
                            entry.SceneKind == SceneObservationKind.TransportStreamActivated);
    }

    [Fact]
    public void DistinctNonLoopbackConnectionsShareTheTransportSession()
    {
        var timestamp = DateTimeOffset.UtcNow;
        const string relayConnection = "16777226:5464->33554442:1542";
        const string directConnection = "50331658:7135->67108874:1541";
        var log = string.Join(
            Environment.NewLine,
            FormatLine(timestamp, relayConnection, sequenceNumber: 100, connectionOrdinal: 131, Build3336Frame(1, "Relay")),
            FormatLine(timestamp.AddMilliseconds(1), directConnection, sequenceNumber: 200, connectionOrdinal: 129, Build3336Frame(2, "Direct")));

        using var reader = new StringReader(log);
        var replay = PacketLogReplayService.Replay(reader, "relay-then-direct.stream.log");

        Assert.Equal(2, replay.ReplayedLines);
        Assert.True(replay.SceneOwner.Entities.TryGet(1, out var relay));
        Assert.Equal("Relay", relay.Nickname);
        Assert.True(replay.SceneOwner.Entities.TryGet(2, out var direct));
        Assert.Equal("Direct", direct.Nickname);
        Assert.DoesNotContain(
            ReadJournal(replay.SceneJournal),
            static entry => entry.Domain == ObservedEventDomain.Scene &&
                            entry.SceneKind == SceneObservationKind.TransportStreamActivated);
    }

    [Fact]
    public void RepeatedSupplementalConnectionsShareThePrimaryTransportSession()
    {
        var timestamp = DateTimeOffset.UtcNow;
        const string directConnection = "16777226:7135->33554442:1541";
        const string firstRelay = "50331658:5464->67108874:1542";
        const string secondRelay = "83886090:5464->100663306:1620";
        var log = string.Join(
            Environment.NewLine,
            FormatLine(timestamp, directConnection, sequenceNumber: 100, connectionOrdinal: 129, Build3336Frame(1, "Direct")),
            FormatLine(timestamp.AddMilliseconds(1), firstRelay, sequenceNumber: 200, connectionOrdinal: 131, Build3336Frame(2, "Relay2")),
            FormatLine(timestamp.AddMilliseconds(2), secondRelay, sequenceNumber: 300, connectionOrdinal: 400, Build3336Frame(3, "Relay3")));

        using var reader = new StringReader(log);
        var replay = PacketLogReplayService.Replay(reader, "repeated-supplemental.stream.log");

        Assert.Equal(3, replay.ReplayedLines);
        Assert.True(replay.SceneOwner.Entities.TryGet(1, out var direct));
        Assert.Equal("Direct", direct.Nickname);
        Assert.True(replay.SceneOwner.Entities.TryGet(2, out var first));
        Assert.Equal("Relay2", first.Nickname);
        Assert.True(replay.SceneOwner.Entities.TryGet(3, out var second));
        Assert.Equal("Relay3", second.Nickname);
        Assert.DoesNotContain(
            ReadJournal(replay.SceneJournal),
            static entry => entry.Domain == ObservedEventDomain.Scene &&
                            entry.SceneKind == SceneObservationKind.TransportStreamActivated);
    }

    [Fact]
    public void TransportCloseRetiresOnlyItsAttemptAndKeepsTheSupplementalSession()
    {
        var timestamp = DateTimeOffset.UtcNow;
        const string primaryConnection = "16777226:7135->33554442:1541";
        const string supplementalConnection = "50331658:5464->67108874:1542";
        var log = string.Join(
            Environment.NewLine,
            FormatLine(timestamp, primaryConnection, sequenceNumber: 100, connectionOrdinal: 129, Build3336Frame(1, "Primary")),
            FormatLine(timestamp.AddMilliseconds(1), supplementalConnection, sequenceNumber: 200, connectionOrdinal: 131, Build3336Frame(2, "Relay")),
            FormatClose(timestamp.AddMilliseconds(2), primaryConnection, connectionOrdinal: 129),
            FormatLine(timestamp.AddMilliseconds(3), supplementalConnection, sequenceNumber: 300, connectionOrdinal: 131, Build3336Frame(3, "Relay2")));

        using var reader = new StringReader(log);
        var replay = PacketLogReplayService.Replay(reader, "transport-close-supplemental.stream.log");

        Assert.Equal(4, replay.ReplayedLines);
        Assert.Equal(1, replay.ReplayedEventCounts["transport-close"]);
        Assert.True(replay.SceneOwner.Entities.TryGet(1, out var primary));
        Assert.Equal("Primary", primary.Nickname);
        Assert.True(replay.SceneOwner.Entities.TryGet(2, out var relay));
        Assert.Equal("Relay", relay.Nickname);
        Assert.True(replay.SceneOwner.Entities.TryGet(3, out var relayContinuation));
        Assert.Equal("Relay2", relayContinuation.Nickname);
    }

    [Fact]
    public void TransportClosePreventsTheClosedAttemptFromResuming()
    {
        var timestamp = DateTimeOffset.UtcNow;
        const string connection = "16777226:7135->33554442:1541";
        var log = string.Join(
            Environment.NewLine,
            FormatLine(timestamp, connection, sequenceNumber: 100, connectionOrdinal: 1, Build3336Frame(1, "Before")),
            FormatClose(timestamp.AddMilliseconds(1), connection, connectionOrdinal: 1),
            FormatLine(timestamp.AddMilliseconds(2), connection, sequenceNumber: 200, connectionOrdinal: 1, Build3336Frame(2, "Stale")),
            FormatLine(timestamp.AddMilliseconds(3), connection, sequenceNumber: 300, connectionOrdinal: 2, Build3336Frame(3, "Restarted")));

        using var reader = new StringReader(log);
        var replay = PacketLogReplayService.Replay(reader, "transport-close-restart.stream.log");

        Assert.Equal(4, replay.TotalLines);
        Assert.Equal(3, replay.ReplayedLines);
        Assert.Equal(1, replay.SkippedLines);
        Assert.Equal(1, replay.SkippedEventCounts["stale-transport-attempt"]);
        Assert.False(replay.SceneOwner.Entities.TryGet(2, out _));
        Assert.True(replay.SceneOwner.Entities.TryGet(3, out var restarted));
        Assert.Equal("Restarted", restarted.Nickname);
    }

    [Fact]
    public void LateCloseForAnOlderAttemptDoesNotRetireTheReusedTuple()
    {
        var timestamp = DateTimeOffset.UtcNow;
        const string connection = "16777226:7135->33554442:1541";
        var log = string.Join(
            Environment.NewLine,
            FormatLine(timestamp, connection, sequenceNumber: 100, connectionOrdinal: 1, Build3336Frame(1, "Old")),
            FormatLine(timestamp.AddMilliseconds(1), connection, sequenceNumber: 200, connectionOrdinal: 2, Build3336Frame(2, "New")),
            FormatClose(timestamp.AddMilliseconds(2), connection, connectionOrdinal: 1),
            FormatLine(timestamp.AddMilliseconds(3), connection, sequenceNumber: 300, connectionOrdinal: 2, Build3336Frame(3, "Continued")));

        using var reader = new StringReader(log);
        var replay = PacketLogReplayService.Replay(reader, "late-transport-close.stream.log");

        Assert.Equal(4, replay.TotalLines);
        Assert.Equal(3, replay.ReplayedLines);
        Assert.Equal(1, replay.SkippedEventCounts["stale-transport-close"]);
        Assert.True(replay.SceneOwner.Entities.TryGet(2, out var current));
        Assert.Equal("New", current.Nickname);
        Assert.True(replay.SceneOwner.Entities.TryGet(3, out var continued));
        Assert.Equal("Continued", continued.Nickname);
    }

    [Fact]
    public void ActiveTransportBudgetRetiresTheLeastRecentlyUsedSupplementalAttempt()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var lines = new List<string>(CaptureBufferLimits.CandidateStreamCountLimit + 4);
        for (var index = 0; index < CaptureBufferLimits.CandidateStreamCountLimit; index++)
        {
            var frame = Build3336Frame(index + 1, $"Active{index}");
            lines.Add(FormatLine(
                timestamp.AddMilliseconds(index),
                FormatConnection(index),
                sequenceNumber: (uint)(10_000 + (index * 100)),
                connectionOrdinal: index + 1,
                frame));
        }

        var refreshedFrame = Build3336Frame(2, "Active1");
        lines.Add(FormatLine(
            timestamp.AddMilliseconds(CaptureBufferLimits.CandidateStreamCountLimit),
            FormatConnection(1),
            sequenceNumber: 10_100u + (uint)refreshedFrame.Length,
            connectionOrdinal: 2,
            Build3336Frame(90, "Refreshed")));
        var newestIndex = CaptureBufferLimits.CandidateStreamCountLimit;
        var newestFrame = Build3336Frame(newestIndex + 1, $"Active{newestIndex}");
        lines.Add(FormatLine(
            timestamp.AddMilliseconds(CaptureBufferLimits.CandidateStreamCountLimit + 1),
            FormatConnection(newestIndex),
            sequenceNumber: (uint)(10_000 + (newestIndex * 100)),
            connectionOrdinal: newestIndex + 1,
            newestFrame));
        lines.Add(FormatLine(
            timestamp.AddMilliseconds(CaptureBufferLimits.CandidateStreamCountLimit + 2),
            FormatConnection(2),
            sequenceNumber: 40_000,
            connectionOrdinal: 3,
            Build3336Frame(100, "Stale")));
        lines.Add(FormatLine(
            timestamp.AddMilliseconds(CaptureBufferLimits.CandidateStreamCountLimit + 3),
            FormatConnection(newestIndex),
            sequenceNumber: (uint)(10_000 + (newestIndex * 100) + newestFrame.Length),
            connectionOrdinal: newestIndex + 1,
            Build3336Frame(101, "Continued")));

        using var reader = new StringReader(string.Join(Environment.NewLine, lines));
        var replay = PacketLogReplayService.Replay(reader, "bounded-active-transports.stream.log");

        Assert.Equal(CaptureBufferLimits.CandidateStreamCountLimit + 4, replay.TotalLines);
        Assert.Equal(CaptureBufferLimits.CandidateStreamCountLimit + 3, replay.ReplayedLines);
        Assert.Equal(1, replay.SkippedLines);
        Assert.Equal(1, replay.SkippedEventCounts["stale-transport-attempt"]);
        Assert.False(replay.SceneOwner.Entities.TryGet(100, out _));
        Assert.True(replay.SceneOwner.Entities.TryGet(90, out var refreshed));
        Assert.Equal("Refreshed", refreshed.Nickname);
        Assert.True(replay.SceneOwner.Entities.TryGet(101, out var continued));
        Assert.Equal("Continued", continued.Nickname);
    }

    [Fact]
    public void PrimaryCloseElectsTheMostRecentlyUsedOfThreeReplayTransports()
    {
        var timestamp = DateTimeOffset.UtcNow;
        const string primaryConnection = "16777226:7135->33554442:1541";
        const string firstSupplemental = "50331658:5464->67108874:1542";
        const string secondSupplemental = "83886090:5465->100663306:1543";
        var firstSupplementalFrame = Build3336Frame(2, "First");
        var secondSupplementalFrame = Build3336Frame(3, "Second");
        var log = string.Join(
            Environment.NewLine,
            FormatLine(timestamp, primaryConnection, sequenceNumber: 100, connectionOrdinal: 1, Build3336Frame(1, "Primary")),
            FormatLine(timestamp.AddMilliseconds(1), firstSupplemental, sequenceNumber: 200, connectionOrdinal: 2, firstSupplementalFrame),
            FormatLine(timestamp.AddMilliseconds(2), secondSupplemental, sequenceNumber: 300, connectionOrdinal: 3, secondSupplementalFrame),
            FormatLine(timestamp.AddMilliseconds(3), firstSupplemental, sequenceNumber: 200 + (uint)firstSupplementalFrame.Length, connectionOrdinal: 2, Build3336Frame(4, "Refreshed")),
            FormatClose(timestamp.AddMilliseconds(4), primaryConnection, connectionOrdinal: 1),
            FormatLine(timestamp.AddMilliseconds(5), firstSupplemental, sequenceNumber: 1_000, connectionOrdinal: 4, Build3336Frame(5, "Elected")),
            FormatLine(timestamp.AddMilliseconds(6), secondSupplemental, sequenceNumber: 300 + (uint)secondSupplementalFrame.Length, connectionOrdinal: 3, Build3336Frame(6, "Retired")));

        using var reader = new StringReader(log);
        var replay = PacketLogReplayService.Replay(reader, "three-active-election.stream.log");

        Assert.Equal(7, replay.TotalLines);
        Assert.Equal(6, replay.ReplayedLines);
        Assert.Equal(1, replay.SkippedLines);
        Assert.Equal(1, replay.SkippedEventCounts["stale-transport-attempt"]);
        Assert.True(replay.SceneOwner.Entities.TryGet(5, out var elected));
        Assert.Equal("Elected", elected.Nickname);
        Assert.False(replay.SceneOwner.Entities.TryGet(6, out _));
        Assert.Single(
            ReadJournal(replay.SceneJournal),
            static entry => entry.Domain == ObservedEventDomain.Scene &&
                            entry.SceneKind == SceneObservationKind.TransportStreamActivated);
    }

    private static string FormatLine(
        DateTimeOffset timestamp,
        string connection,
        uint sequenceNumber,
        long connectionOrdinal,
        ReadOnlySpan<byte> payload) =>
        $"{timestamp:O}|dir=inbound|{connection}|seq={sequenceNumber}|attempt={connectionOrdinal}|len={payload.Length}|data={Convert.ToHexString(payload)}";

    private static string FormatClose(
        DateTimeOffset timestamp,
        string connection,
        long connectionOrdinal) =>
        $"{timestamp:O}|event=transport-close|{connection}|attempt={connectionOrdinal}";

    private static string FormatLegacyLine(
        DateTimeOffset timestamp,
        string connection,
        uint sequenceNumber,
        ReadOnlySpan<byte> payload) =>
        $"{timestamp:O}|dir=inbound|{connection}|seq={sequenceNumber}|len={payload.Length}|data={Convert.ToHexString(payload)}";

    private static string FormatConnection(int index) =>
        $"{16_777_226u + (uint)index}:{10_000 + index}->{33_554_442u + (uint)index}:{20_000 + index}";

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
