using Cloris.Aion2Flow.Combat;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Scene;

internal static class SceneReplayFixture
{
    public static void SetResources() => CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

    public static PacketLogReplayResult Replay(string fileName)
        => PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{fileName}"));
}
