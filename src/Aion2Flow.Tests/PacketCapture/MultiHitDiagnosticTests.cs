using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.PacketCapture;

public sealed class MultiHitDiagnosticTests
{
    [Theory]
    [InlineData("aion2flow.stream.20260413173207.log", 3)]
    [InlineData("aion2flow.stream.20260412121709.log", 38)]
    [InlineData("aion2flow.stream.20260412180806.log", 41)]
    [InlineData("aion2flow.stream.20260412182736.log", 51)]
    [InlineData("aion2flow.stream.20260413012324.log", 7)]
    [InlineData("aion2flow.stream.20260413012534.log", 6)]
    [InlineData("aion2flow.stream.20260413012637.log", 19)]
    [InlineData("aion2flow.stream.20260413021020.log", 39)]
    [InlineData("aion2flow.stream.20260413021314.log", 17)]
    [InlineData("aion2flow.stream.20260413021419.log", 30)]
    [InlineData("aion2flow.stream.20260414044851.log", 54)]
    [InlineData("aion2flow.stream.20260414045123.log", 61)]
    [InlineData("aion2flow.stream.20260414045207.log", 119)]
    public void Replay_Detects_Correct_MultiHit_Count_From_Stream_Log(string fileName, int expectedMultiHitCount)
    {
        CombatMetricsEngine.SetGameResources(
            new SkillCollection(),
            new Dictionary<int, NpcCatalogEntry>());

        var path = FixtureHelper.GetPath($"logs/{fileName}");
        var replay = PacketLogReplayService.Replay(path);

        var player = replay.Combatants
            .OrderByDescending(static s => s.OutgoingDamage)
            .First();

        var sourceIds = new HashSet<int> { player.CombatantId };
        foreach (var (summonId, ownerId) in replay.Store.SummonOwnerByInstance)
        {
            if (ownerId == player.CombatantId)
            {
                sourceIds.Add(summonId);
            }
        }

        var totalMultiHit = 0;
        foreach (var sourceId in sourceIds)
        {
            if (replay.Store.CombatPacketsBySource.TryGetValue(sourceId, out var packets))
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

        Assert.Equal(expectedMultiHitCount, totalMultiHit);
    }
}