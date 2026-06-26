using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.Tests;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.ReplayTests.Capture;

public sealed class PlaybackTimestampRegressionTests
{
    [Fact]
    public void Replay_20260611151958_PacketObservationsRetainSceneRelativeTime()
    {
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.PlaybackSceneRelativeTimestamps}"));
        var compactControlOffsets = new List<long>();
        var summonOffsets = new List<long>();

        for (var index = 0L; index < replay.SceneJournal.Count; index++)
        {
            var entry = replay.SceneJournal.Read(index);
            var offset = entry.Stamp.OffsetTicks / TimeSpan.TicksPerMillisecond;
            Assert.True(offset >= 0);
            if (entry.Raw.Opcode == 0x0238)
                compactControlOffsets.Add(offset);
            if (entry.Domain == ObservedEventDomain.State && entry.State is { StateCode: 0 })
                summonOffsets.Add(offset);
        }

        Assert.Equal(1133, compactControlOffsets.Count);
        Assert.Equal(104, summonOffsets.Count);
        Assert.Contains(compactControlOffsets, static offset => offset > 0);
        Assert.Contains(summonOffsets, static offset => offset > 0);
        Assert.True(compactControlOffsets.Distinct().Count() > 1);
        Assert.True(summonOffsets.Distinct().Count() > 1);
    }
}
