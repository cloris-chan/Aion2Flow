#if DEBUG
using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.Services;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class ClientSwitchDumpTests
{
    [Fact]
    public async Task SwitchingRotatesDumpWithoutChangingPreviousClientMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "Aion2Flow-client-switch");
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        await using var ports = new ProcessPortDiscoveryService();
        await using var capture = new WinDivertCaptureService(ports, clientContext: SceneClientContext.Purple);
        try
        {
            RawPacketDump.ConfigureLogDirectory(directory);
            RawPacketDump.ConfigureClientContext(capture.Scene.ClientContext);
            var originalStream = RawPacketDump.StreamLogPath;
            var originalMetadataPath = Path.Combine(Path.GetDirectoryName(originalStream)!, "session.json");
            var originalMetadata = File.ReadAllBytes(originalMetadataPath);

            await capture.SwitchClientAsync(SceneClientContext.Create(ClientProfiles.Steam),
                () => capture.Scene.ArchiveAndReset(static () => DateTimeOffset.Now));

            Assert.NotEqual(originalStream, RawPacketDump.StreamLogPath);
            Assert.Equal(originalMetadata, File.ReadAllBytes(originalMetadataPath));
            Assert.Equal(ClientProfiles.Purple, CaptureClientMetadata.Resolve(originalStream, null).Profile);
            Assert.Equal(ClientProfiles.Steam, CaptureClientMetadata.Resolve(RawPacketDump.StreamLogPath, null).Profile);
        }
        finally
        {
            RawPacketDump.Shutdown();
            RawPacketDump.ConfigureLogDirectory(Path.Combine(AppContext.BaseDirectory, "logs"));
            RawPacketDump.ConfigureClientContext(SceneClientContext.Purple);
            Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, Path.GetFullPath(directory));
            Directory.Delete(directory, recursive: true);
        }
    }
}
#endif
