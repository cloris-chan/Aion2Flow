using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class CanonicalPacketMirrorDeduplicatorTests
{
    private static readonly CanonicalPacketTransportIdentity First = new(
        new TcpConnection(1, 2, 3, 4),
        1);
    private static readonly CanonicalPacketTransportIdentity Second = new(
        new TcpConnection(5, 6, 7, 8),
        2);
    private static readonly CanonicalPacketTransportIdentity Third = new(
        new TcpConnection(9, 10, 11, 12),
        3);

    [Fact]
    public void SameTransportRepetitionCreatesIndependentOccurrences()
    {
        var deduplicator = new CanonicalPacketMirrorDeduplicator();
        ReadOnlySpan<byte> packet = [1, 2, 3, 4];

        Assert.True(ParseAndRemember(deduplicator, in First, packet, 1_000));
        Assert.True(ParseAndRemember(deduplicator, in First, packet, 1_100));
        Assert.Equal(2, deduplicator.TrackedOccurrenceCount);

        Assert.False(ParseAndRemember(deduplicator, in Second, packet, 1_200));
        Assert.False(ParseAndRemember(deduplicator, in Second, packet, 1_300));
        Assert.True(ParseAndRemember(deduplicator, in Second, packet, 1_400));
        Assert.Equal(3, deduplicator.TrackedOccurrenceCount);
    }

    [Fact]
    public void DifferentTransportsJoinOneOccurrenceAtMostOnce()
    {
        var deduplicator = new CanonicalPacketMirrorDeduplicator();
        ReadOnlySpan<byte> packet = [1, 2, 3, 4];

        Assert.True(ParseAndRemember(deduplicator, in First, packet, 1_000));
        Assert.False(ParseAndRemember(deduplicator, in Second, packet, 1_100));
        Assert.False(ParseAndRemember(deduplicator, in Third, packet, 1_200));
        Assert.True(ParseAndRemember(deduplicator, in Third, packet, 1_300));
        Assert.Equal(2, deduplicator.TrackedOccurrenceCount);
    }

    [Fact]
    public void NewAttemptOnSameTupleIsAnotherTransport()
    {
        var deduplicator = new CanonicalPacketMirrorDeduplicator();
        var restarted = First with { ConnectionOrdinal = First.ConnectionOrdinal + 1 };
        ReadOnlySpan<byte> packet = [1, 2, 3, 4];

        Assert.True(ParseAndRemember(deduplicator, in First, packet, 1_000));
        Assert.False(ParseAndRemember(deduplicator, in restarted, packet, 1_100));
    }

    [Fact]
    public void UnrememberedFrameDoesNotSuppressAnotherTransport()
    {
        var deduplicator = new CanonicalPacketMirrorDeduplicator();
        ReadOnlySpan<byte> packet = [1, 2, 3, 4];

        var firstProbe = deduplicator.Probe(in First, packet, 1_000);
        var secondProbe = deduplicator.Probe(in Second, packet, 1_100);

        Assert.False(firstProbe.IsDuplicate);
        Assert.False(secondProbe.IsDuplicate);
        deduplicator.Remember(in Second, packet, in secondProbe);
        Assert.True(deduplicator.Probe(in First, packet, 1_200).IsDuplicate);
    }

    [Fact]
    public void ExactFrameOutsideWindowIsAdmitted()
    {
        var deduplicator = new CanonicalPacketMirrorDeduplicator();
        ReadOnlySpan<byte> packet = [1, 2, 3, 4];

        Assert.True(ParseAndRemember(deduplicator, in First, packet, 1_000));
        Assert.False(ParseAndRemember(deduplicator, in Second, packet, 3_000));
        Assert.True(ParseAndRemember(deduplicator, in Third, packet, 3_001));
    }

    [Fact]
    public void EntryAndByteBudgetsEvictOldestOccurrences()
    {
        var deduplicator = new CanonicalPacketMirrorDeduplicator(
            TimeSpan.FromSeconds(2),
            occurrenceCountLimit: 2,
            retainedByteLimit: 4);

        Assert.True(ParseAndRemember(deduplicator, in First, [1, 1], 1_000));
        Assert.True(ParseAndRemember(deduplicator, in First, [2, 2], 1_100));
        Assert.True(ParseAndRemember(deduplicator, in First, [3, 3], 1_200));

        Assert.Equal(2, deduplicator.TrackedOccurrenceCount);
        Assert.Equal(4, deduplicator.RetainedByteCount);
        Assert.True(ParseAndRemember(deduplicator, in Second, [1, 1], 1_300));
    }

    [Fact]
    public void FrameLargerThanByteBudgetIsNeverTracked()
    {
        var deduplicator = new CanonicalPacketMirrorDeduplicator(
            TimeSpan.FromSeconds(2),
            occurrenceCountLimit: 2,
            retainedByteLimit: 3);
        ReadOnlySpan<byte> packet = [1, 2, 3, 4];

        Assert.True(ParseAndRemember(deduplicator, in First, packet, 1_000));
        Assert.True(ParseAndRemember(deduplicator, in Second, packet, 1_100));
        Assert.Equal(0, deduplicator.TrackedOccurrenceCount);
        Assert.Equal(0, deduplicator.RetainedByteCount);
    }

    [Fact]
    public void ClearStartsAnIndependentSession()
    {
        var deduplicator = new CanonicalPacketMirrorDeduplicator();
        ReadOnlySpan<byte> packet = [1, 2, 3, 4];

        Assert.True(ParseAndRemember(deduplicator, in First, packet, 1_000));
        deduplicator.Clear();
        Assert.True(ParseAndRemember(deduplicator, in Second, packet, 1_100));
        Assert.Equal(1, deduplicator.TrackedOccurrenceCount);
    }

    [Fact]
    public void PacketProcessorsRecognizeMirrorWithoutAppendingItTwice()
    {
        var scene = new SceneLiveReadModel();
        var sinkFactory = SceneSinkFactory.CreateForLive(scene);
        var deduplicator = new CanonicalPacketMirrorDeduplicator();
        var firstConnection = First.Connection;
        var secondConnection = Second.Connection;
        var observedAtMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var frame = Convert.FromHexString(
            "1A0238DE6D00E38E15012E00DE6D00006443C487010100");
        using var firstProcessor = new PacketStreamProcessor(
            sinkFactory(),
            null,
            PacketTransportFraming.DirectAligned,
            0,
            mirrorDeduplicator: deduplicator,
            connectionOrdinal: First.ConnectionOrdinal);
        using var secondProcessor = new PacketStreamProcessor(
            sinkFactory(),
            null,
            PacketTransportFraming.DirectAligned,
            0,
            mirrorDeduplicator: deduplicator,
            connectionOrdinal: Second.ConnectionOrdinal);

        Assert.True(firstProcessor.AppendAndProcess(
            frame,
            in firstConnection,
            observedAtMilliseconds));
        Assert.True(secondProcessor.AppendAndProcess(
            frame,
            in secondConnection,
            observedAtMilliseconds + 100));
        Assert.Equal(1, CountCombatEntries(scene.Journal));
    }

    private static bool ParseAndRemember(
        CanonicalPacketMirrorDeduplicator deduplicator,
        in CanonicalPacketTransportIdentity transport,
        ReadOnlySpan<byte> packet,
        long observedAtMilliseconds)
    {
        var probe = deduplicator.Probe(in transport, packet, observedAtMilliseconds);
        if (probe.IsDuplicate)
            return false;

        deduplicator.Remember(in transport, packet, in probe);
        return true;
    }

    private static int CountCombatEntries(ObservedEventJournal journal)
    {
        var count = 0;
        var cursor = journal.CreateCursor(journal.FirstObservationOrdinal);
        while (true)
        {
            var result = journal.ReadEntries(cursor, 512, entries =>
            {
                foreach (var entry in entries)
                {
                    if (entry.Domain == ObservedEventDomain.Combat)
                        count++;
                }
            });
            if (result.Count == 0)
                return count;

            cursor = result.Cursor;
        }
    }
}
