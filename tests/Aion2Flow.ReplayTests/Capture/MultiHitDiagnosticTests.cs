using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class MultiHitDiagnosticTests
{
    [Theory]
    [MemberData(nameof(ReplayScenarioCatalog.MultiHitDiagnostics), MemberType = typeof(ReplayScenarioCatalog))]
    public void Replay_Detects_Correct_MultiHit_Count_From_Stream_Log(ReplayMultiHitScenario scenario)
    {
        CombatResourceRegistry.SetGameResources([], new Dictionary<int, NpcDisplayEntry>());

        var path = FixtureHelper.GetPath($"logs/{scenario.FileName}");
        var replay = PacketLogReplayService.Replay(path);

        var player = replay.Combatants
            .OrderByDescending(static s => s.OutgoingDamage)
            .First();

        var sourceIds = new HashSet<int> { player.CombatantId };
        foreach (var (summonId, ownerId) in SceneReplayTestView.SummonOwnerByInstance(replay))
        {
            if (ownerId == player.CombatantId)
            {
                sourceIds.Add(summonId);
            }
        }

        var totalMultiHit = 0;
        foreach (var sourceId in sourceIds)
        {
            if (SceneReplayTestView.BySource(replay).TryGetValue(sourceId, out var packets))
            {
                foreach (var packet in packets)
                {
                    if ((packet.Modifiers & DamageModifiers.MultiHit) != 0)
                    {
                        totalMultiHit++;
                    }
                }
            }
        }

        Assert.Equal(scenario.ExpectedMultiHitCount, totalMultiHit);
    }
}
