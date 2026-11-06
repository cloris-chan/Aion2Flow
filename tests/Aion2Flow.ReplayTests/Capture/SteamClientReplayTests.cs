using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class SteamClientReplayTests
{
    [Theory]
    [InlineData("20261031060531", 51, 49, 2)]
    [InlineData("20261106030558", 0, 0, 0)]
    [InlineData("20261106030640", 209, 208, 1)]
    [InlineData("20261106031049", 16, 15, 1)]
    [InlineData("20261106031238", 9, 9, 0)]
    [InlineData("20261106031351", 2, 2, 0)]
    [InlineData("20261106031402", 129, 117, 2)]
    [InlineData("20261106031815", 76, 76, 0)]
    [InlineData("20261106031937", 7, 5, 1)]
    [InlineData("20261106032017", 15, 15, 0)]
    [InlineData("20261106032038", 108, 104, 0)]
    [InlineData("20261106032326", 99, 99, 0)]
    [InlineData("20261106032510", 67, 65, 2)]
    [InlineData("20261106032740", 65, 65, 0)]
    public void CompleteSteamStreamPreservesNpcHpAndOwnerFacts(string dump, int npcCount, int hpCount, int ownerCount)
    {
        var context = SceneClientContext.Create(ClientProfiles.Steam);
        var replay = PacketLogReplayService.Replay(StreamPath(dump), context);
        var facts = ReadEntityFacts(replay);

        Assert.Equal(npcCount, facts.NpcCodes.Count);
        Assert.Equal(hpCount, facts.Hp.Count);
        Assert.Equal(ownerCount, facts.Owners.Count);
        Assert.False(replay.SkippedEventCounts.ContainsKey("<invalid>"));
        Assert.Same(context, replay.SceneJournal.ClientContext);
        Assert.Same(context, replay.SceneOwner.ClientContext);
        foreach (var combatant in replay.Snapshot.Combatants)
        {
            if (replay.SceneOwner.Entities.TryGet(combatant.Id, out var entity) && entity.NpcCode.HasValue && !entity.IsPlayer)
                Assert.False(combatant.Metrics.IsVisiblePlayerCombatant, $"NPC {entity.EntityId} appeared as a player.");
        }
    }

    [Fact]
    public void OctoberSummonsAttributeDamageWithoutChangingWireSources()
    {
        var replay = PacketLogReplayService.Replay(StreamPath("20261031060531"), SceneClientContext.Create(ClientProfiles.Steam));
        AssertSummon(replay, 48_138, 13_297, 363);
        AssertSummon(replay, 21_542, 13_297, 518);
        Assert.Equal(4_195, replay.Snapshot.Combatants[13_297].DamageAmount);
        Assert.Equal(1_044, replay.Snapshot.Combatants[11_466].DamageAmount);
        foreach (var id in new[] { 48_126, 43_402, 21_269, 38_901 })
        {
            Assert.True(replay.SceneOwner.Entities.TryGet(id, out var entity));
            Assert.Equal(2_101_066, entity.NpcCode);
            Assert.Equal(NpcKind.Monster, entity.Kind);
        }
    }

    [Theory]
    [InlineData("20261106030640", 17_447, 92)]
    [InlineData("20261106031049", 29_580, 297)]
    [InlineData("20261106031402", 19_221, 541)]
    [InlineData("20261106031402", 31_995, 527)]
    [InlineData("20261106031937", 22_287, 552)]
    [InlineData("20261106032510", 43_222, 643)]
    [InlineData("20261106032510", 35_063, 706)]
    public void NovemberSummonsUseExplicitPacketOwner(string dump, int entityId, long damage)
    {
        var replay = PacketLogReplayService.Replay(StreamPath(dump), SceneClientContext.Create(ClientProfiles.Steam));
        AssertSummon(replay, entityId, 1_346, damage);
    }

    [Theory]
    [InlineData(ReplayScenarioCatalog.CurrentRoundTripShortUptime, 92, 32)]
    [InlineData(ReplayScenarioCatalog.CurrentRoundTripLongUptime, 8, 7)]
    public void PurpleEntityLayoutsRemainIsolated(string fixture, int npcCount, int ownerCount)
    {
        var path = FixtureHelper.GetPath($"logs/{fixture}");
        var purple = ReadEntityFacts(PacketLogReplayService.Replay(path, SceneClientContext.Purple));
        var steam = ReadEntityFacts(PacketLogReplayService.Replay(path, SceneClientContext.Create(ClientProfiles.Steam)));
        Assert.Equal(npcCount, purple.NpcCodes.Count);
        Assert.Equal(ownerCount, purple.Owners.Count);
        Assert.Empty(steam.NpcCodes);
        Assert.Empty(steam.Owners);
    }

    [Fact]
    public void SteamResourceLanguageDoesNotChangeEntityOrCombatFacts()
    {
        var english = PacketLogReplayService.Replay(StreamPath("20261031060531"), SceneClientContext.Create(ClientProfiles.Steam));
        var japanese = PacketLogReplayService.Replay(StreamPath("20261031060531"),
            new SceneClientContext(ClientProfiles.Steam, ResourceCatalog.Load(ClientProfiles.Steam, "ja-JP")));
        Assert.Equal(english.Combatants.Select(static row => (row.CombatantId, row.OutgoingDamage, row.OutgoingHealing)),
            japanese.Combatants.Select(static row => (row.CombatantId, row.OutgoingDamage, row.OutgoingHealing)));
        Assert.Equal(SceneReplayTestView.SummonOwnerByInstance(english), SceneReplayTestView.SummonOwnerByInstance(japanese));
    }

    [Fact]
    public void NpcIdentityAndExplicitOwnershipDoNotRequireResourceCatalogEntries()
    {
        var shared = new ResourceSharedCatalog([], new Dictionary<int, SkillBaseProjection>(), new Dictionary<uint, int>(),
            SkillSemanticRuntimeIndex.Empty, new Dictionary<int, NpcDefinition>());
        var locale = new ResourceLocaleCatalog("en-US", new Dictionary<int, string>(), new Dictionary<int, string>(),
            new Dictionary<uint, string>(), new Dictionary<int, ServerNameEntry>());
        var context = new SceneClientContext(ClientProfiles.Steam,
            new ResourceCatalogSnapshot(shared, locale, ResourceCatalog.GetResourceSet(ClientProfiles.Steam)));
        var replay = PacketLogReplayService.Replay(StreamPath("20261031060531"), context);
        var facts = ReadEntityFacts(replay);
        Assert.Equal(51, facts.NpcCodes.Count);
        Assert.Equal(2, facts.Owners.Count);
        AssertSummon(replay, 48_138, 13_297, 363);
        AssertSummon(replay, 21_542, 13_297, 518);
    }

    [Theory]
    [InlineData("20261106031049", 22_915, 2_920_328)]
    [InlineData("20261106032038", 28_416, 2_290_013)]
    public void UnprovedOwnerSectionsPreserveNpcIdentityWithoutGuessingOwner(string dump, int entityId, int npcCode)
    {
        var replay = PacketLogReplayService.Replay(StreamPath(dump), SceneClientContext.Create(ClientProfiles.Steam));
        var facts = ReadEntityFacts(replay);
        Assert.Contains((entityId, npcCode), facts.NpcIdentities);
        Assert.DoesNotContain(facts.Ownership, owner => owner.EntityId == entityId);
    }

    private static void AssertSummon(PacketLogReplayResult replay, int entityId, int ownerId, long sourceDamage)
    {
        if (replay.SceneOwner.Entities.TryGet(entityId, out var entity) && entity.OwnerEntityId == ownerId)
        {
            AssertSummonScope(SceneArchiveEntityIdentity.From(entity), replay.Snapshot, replay.SceneOwner.Combat.Events, sourceDamage);
            return;
        }
        foreach (var archive in replay.MapTransitionArchives)
        {
            foreach (ref readonly var identity in archive.Entities.AsSpan())
            {
                if (identity.EntityId != entityId || identity.OwnerEntityId != ownerId) continue;
                AssertSummonScope(identity, archive.Snapshot, archive.CombatEvents, sourceDamage);
                return;
            }
        }
        Assert.Fail($"Missing summon {entityId} for owner {ownerId} in current or archived scenes.");
    }

    private static void AssertSummonScope(SceneArchiveEntityIdentity entity, SceneCombatSnapshot snapshot,
        IEnumerable<CombatEventRecord> events, long sourceDamage)
    {
        Assert.Equal(2_920_637, entity.NpcCode);
        Assert.Equal(EntityOwnerKind.Summon, entity.OwnerKind);
        Assert.Equal(NpcKind.Summon, entity.Kind);
        Assert.DoesNotContain(entity.EntityId, snapshot.Combatants.Keys);
        Assert.Equal(sourceDamage, events.Where(row => row.SourceId == entity.EntityId && row.Contribution.Metric == CombatMetricKind.Damage)
            .Sum(static row => row.Contribution.Amount));
    }

    private static string StreamPath(string dump) => FixtureHelper.GetPath($"logs/aion2flow.stream.{dump}.log");

    private static EntityFacts ReadEntityFacts(PacketLogReplayResult replay)
    {
        var facts = new EntityFacts();
        var cursor = replay.SceneJournal.CreateCursor(0);
        while (true)
        {
            var result = replay.SceneJournal.ReadEntries(cursor, 1024, entries =>
            {
                foreach (var entry in entries)
                {
                    if (entry.Raw.Opcode != 0x4136) continue;
                    // Context priming can replay a fact with its original packet reference.
                    if (entry.Domain == ObservedEventDomain.State)
                    {
                        if (entry.State.StateCode is >= 2_000_000 and <= 2_999_999)
                        {
                            facts.NpcCodes.Add(entry.Raw);
                            facts.NpcIdentities.Add((entry.State.EntityId, entry.State.StateCode));
                        }
                        if (entry.State.StateCode == 0 && entry.TargetEntityId != 0)
                        {
                            facts.Owners.Add(entry.Raw);
                            facts.Ownership.Add((entry.State.EntityId, entry.State.Value0));
                        }
                    }
                    else if (entry.Domain == ObservedEventDomain.EntityVital) facts.Hp.Add(entry.Raw);
                }
            });
            if (result.Count == 0) return facts;
            cursor = result.Cursor;
        }
    }

    private sealed class EntityFacts
    {
        public HashSet<RawPacketReference> NpcCodes { get; } = [];
        public HashSet<RawPacketReference> Hp { get; } = [];
        public HashSet<RawPacketReference> Owners { get; } = [];
        public HashSet<(int EntityId, int Code)> NpcIdentities { get; } = [];
        public HashSet<(int EntityId, long OwnerId)> Ownership { get; } = [];
    }
}
