using System.Text.Json;
using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class ClientProfileReplayTests
{
    [Fact]
    public void CompleteStreamPinsTheSelectedClientContextThroughoutTheScene()
    {
        var context = new SceneClientContext(ClientProfiles.Purple, ResourceCatalog.Load(ClientProfiles.Purple, ResourceLanguage.TraditionalChinese));

        var replay = PacketLogReplayService.Replay(CurrentStream, context);

        Assert.True(replay.ReplayedLines > 0);
        Assert.Same(context, replay.Snapshot.ClientContext);
        Assert.Same(context, replay.SceneJournal.ClientContext);
        Assert.Same(context, replay.SceneOwner.ClientContext);
    }

    [Fact]
    public void CompleteStreamProducesTheSameMetricsWithDifferentResourceLanguages()
    {
        var english = SceneClientContext.Create(ClientProfiles.Purple);
        var chinese = new SceneClientContext(ClientProfiles.Purple, ResourceCatalog.Load(ClientProfiles.Purple, ResourceLanguage.TraditionalChinese));

        var first = PacketLogReplayService.Replay(CurrentStream, english);
        var second = PacketLogReplayService.Replay(CurrentStream, chinese);

        Assert.Equal(first.ReplayedLines, second.ReplayedLines);
        Assert.Equal(first.SceneJournal.Count, second.SceneJournal.Count);
        Assert.Equal(
            first.Combatants.Select(static combatant => (combatant.CombatantId, combatant.OutgoingDamage, combatant.OutgoingHealing, combatant.IncomingDamage)),
            second.Combatants.Select(static combatant => (combatant.CombatantId, combatant.OutgoingDamage, combatant.OutgoingHealing, combatant.IncomingDamage)));
    }

    [Theory]
    [InlineData(ClientVersion.Purple)]
    [InlineData(ClientVersion.Steam)]
    public void DumpMetadataRestoresItsClientAndResourceRevision(ClientVersion version)
    {
        var context = SceneClientContext.Create(ClientProfiles.Get(version));
        var source = version == ClientVersion.Steam
            ? FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.SteamSummonOwnership}") : CurrentStream;
        var directory = CreateDumpDirectory(source);
        try
        {
            CaptureClientMetadata.Write(directory, context);

            var replay = PacketLogReplayService.Replay(Path.Combine(directory, "stream.log"));

            Assert.True(replay.ReplayedLines > 0);
            Assert.Equal(version, replay.Snapshot.ClientContext.Profile.Version);
            Assert.Equal(context.Profile.ProtocolRevision, replay.Snapshot.ClientContext.Profile.ProtocolRevision);
            Assert.Equal(context.Resources.ResourceSet.Revision, replay.Snapshot.ClientContext.Resources.ResourceSet.Revision);
            var other = version == ClientVersion.Steam ? SceneClientContext.Purple : SceneClientContext.Create(ClientProfiles.Steam);
            Assert.Throws<InvalidDataException>(() => PacketLogReplayService.Replay(Path.Combine(directory, "stream.log"), other));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DumpWithUnavailableResourceRevisionIsRejectedBeforeParsing()
    {
        var directory = CreateDumpDirectory();
        try
        {
            var metadata = CaptureClientMetadata.FromContext(SceneClientContext.Purple) with { ResourceRevision = "unavailable-revision" };
            File.WriteAllText(
                Path.Combine(directory, CaptureClientMetadata.FileName),
                JsonSerializer.Serialize(metadata, CaptureClientMetadataJsonContext.Default.CaptureClientMetadata));

            Assert.Throws<InvalidDataException>(() => PacketLogReplayService.Replay(Path.Combine(directory, "stream.log")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CurrentStream => FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentRoundTripLongUptime}");

    private static string CreateDumpDirectory(string? source = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Aion2Flow-client-replay", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.Copy(source ?? CurrentStream, Path.Combine(directory, "stream.log"));
        return directory;
    }
}
