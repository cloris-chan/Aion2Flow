namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class DetailProjectionAllocationGuardTests
{
    [Fact]
    public void DetailProjectionArchiveAndViewModel_DoNotMaterializeParsedCombatPacket()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Projection", "CombatDetailEvent.cs"),
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Projection", "SceneCombatSnapshotAdapter.cs"),
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Projection", "CombatDetailSubscription.cs"),
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Archive", "SceneArchivePayload.cs"),
            Path.Combine(root, "src", "Aion2Flow", "ViewModels", "CombatantDetailsFlyoutViewModel.cs")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("new ParsedCombatPacket", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ParsedCombatPacket", text, StringComparison.Ordinal);
            Assert.DoesNotContain(".Packet.", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RuntimeObservationCanonicalizationAndCapture_DoNotMaterializeParsedCombatPacket()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Canonicalization", "PeriodicLinkCanonicalizer.cs"),
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Canonicalization", "PeriodicChainCanonicalizer.cs"),
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Canonicalization", "CompactOutcomeCanonicalizer.cs"),
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Observation", "JournalingRuntimeObservationSink.cs"),
            Path.Combine(root, "src", "Aion2Flow.Capture", "Streams", "PacketCombatHandler.cs"),
            Path.Combine(root, "src", "Aion2Flow.Capture", "Diagnostics", "PacketLogReplayService.cs")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("new ParsedCombatPacket", text, StringComparison.Ordinal);
            Assert.DoesNotContain("NormalizePacketForStorage", text, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aion2Flow.slnx")))
            directory = directory.Parent;

        return directory!.FullName;
    }
}
