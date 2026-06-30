using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public static class SceneReplayFixture
{
    public static void SetResources() => CombatResourceRegistry.SetGameResources(ResourceCatalog.Load(ResourceLanguage.TraditionalChinese).Skills, new Dictionary<int, NpcDisplayEntry>());

    public static PacketLogReplayResult Replay(string fileName) => PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{fileName}"));
}
