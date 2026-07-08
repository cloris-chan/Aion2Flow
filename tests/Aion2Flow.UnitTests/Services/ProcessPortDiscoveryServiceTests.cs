using Cloris.Aion2Flow.Services;

namespace Cloris.Aion2Flow.Tests.Services;

public sealed class ProcessPortDiscoveryServiceTests
{
    [Fact]
    public async Task SynchronizeProcessPorts_ReplacesStaleCrossServerConnectionPort()
    {
        await using var service = new ProcessPortDiscoveryService();
        var discovered = new List<ushort>();
        var removed = new List<ushort>();
        service.Discovered += (_, port) => discovered.Add(port);
        service.Removed += (_, port) => removed.Add(port);

        service.SynchronizeProcessPorts(42, [new(31_001, 21_001)]);
        service.SynchronizeProcessPorts(42, [new(31_777, 21_777)]);

        Assert.Equal([31_777], service.AllPorts);
        Assert.Equal([31_001, 31_777], discovered);
        Assert.Equal([31_001], removed);
    }

    [Fact]
    public async Task SynchronizeProcessPorts_KeepsLocalPortUntilAllRemotePairsAreGone()
    {
        await using var service = new ProcessPortDiscoveryService();
        var discovered = new List<ushort>();
        var removed = new List<ushort>();
        service.Discovered += (_, port) => discovered.Add(port);
        service.Removed += (_, port) => removed.Add(port);

        service.SynchronizeProcessPorts(42, [new(31_001, 21_001), new(31_001, 21_002)]);
        service.SynchronizeProcessPorts(42, [new(31_001, 21_002)]);

        Assert.Equal([31_001], service.AllPorts);
        Assert.Equal([31_001], discovered);
        Assert.Empty(removed);
    }

    [Fact]
    public void ProcessPortDiscovery_DoesNotBufferUnknownProcessFlowEvents()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "Services", "ProcessPortDiscoveryService.cs"));

        Assert.DoesNotContain("ConcurrentQueue", text, StringComparison.Ordinal);
        Assert.DoesNotContain("QueueEventItem", text, StringComparison.Ordinal);
        Assert.DoesNotContain("_eventQueue", text, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aion2Flow.slnx")))
            directory = directory.Parent;

        return directory!.FullName;
    }
}
