using Cloris.Aion2Flow.Clients;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Playback;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PurpleMapReplayTests
{
    [Fact]
    public void DungeonEntryAndExitConfirmWithout2336AndKeepSameMapTeleportsInTheirScene()
    {
        var replay = ReplaySession();
        var scenes = ReadSceneEvents(replay);
        var boundaries = scenes.Where(static scene => scene.Kind == SceneObservationKind.MapContextStarted).ToArray();
        Assert.Equal(12_381, replay.TotalLines);
        Assert.False(replay.SkippedEventCounts.ContainsKey("<invalid>"));
        Assert.Equal([1010u, 600082u, 1010u], boundaries.Select(static scene => scene.MapId));
        Assert.Equal([0x2136, 0x0140, 0x0140], boundaries.Select(static scene => (int)scene.Opcode));
        Assert.DoesNotContain(scenes, static scene => scene.Opcode == 0x2336);
        Assert.Equal([SceneObservationKind.MapCandidateObserved, SceneObservationKind.CurrentMap, SceneObservationKind.CurrentMap],
            scenes.Where(static scene => scene.Opcode == 0x2136 && scene.MapId == 600082).Select(static scene => scene.Kind));
        Assert.Equal(1010u, replay.Snapshot.MapId);
        Assert.Equal(0u, replay.Snapshot.MapInstanceId);
    }

    [Fact]
    public void ArchivesKeepDisjointCombatRangesAndRetireDungeonSummonsFromTheLiveMap()
    {
        var replay = ReplaySession();
        Assert.Equal([1010u, 600082u], replay.MapTransitionArchives.Select(static archive => archive.Snapshot.MapId));
        Assert.Equal([0u, 21431u], replay.MapTransitionArchives.Select(static archive => archive.Snapshot.MapInstanceId));
        Assert.Equal([933, 1782], replay.MapTransitionArchives.Select(static archive => archive.CombatEvents.Count));
        Assert.Equal(425, replay.SceneOwner.Combat.Events.Count);
        Assert.Equal(207_561_681,
            replay.MapTransitionArchives.Sum(static archive => archive.Snapshot.Combatants.Sum(static row => row.Value.DamageAmount))
            + replay.Snapshot.Combatants.Sum(static row => row.Value.DamageAmount));

        var sourceScopes = new Dictionary<long, int>();
        for (var scope = 0; scope < replay.MapTransitionArchives.Count; scope++)
        {
            var archive = replay.MapTransitionArchives[scope];
            foreach (var entry in archive.CombatEvents)
            {
                Assert.InRange(entry.SourceObservationOrdinal, archive.TimelineSegment.StartObservationOrdinal,
                    archive.TimelineSegment.EndObservationOrdinalExclusive - 1);
                CheckScope(entry.SourceObservationOrdinal, scope);
            }
        }
        foreach (var entry in replay.SceneOwner.Combat.Events)
            CheckScope(entry.SourceObservationOrdinal, replay.MapTransitionArchives.Count);

        var dungeon = replay.MapTransitionArchives[1];
        var summons = dungeon.Entities.AsSpan().ToArray().Where(static entity => entity.OwnerKind == EntityOwnerKind.Summon).OrderBy(static entity => entity.EntityId).ToArray();
        Assert.Equal([24843, 27695, 31736, 33245, 35003, 39425, 44775, 56983, 61376, 61905, 62118, 64890], summons.Select(static entity => entity.EntityId));
        Assert.Equal([6522, 6522, 45184, 15362, 3418, 6522, 6522, 1551, 45184, 15362, 6522, 6522], summons.Select(static entity => entity.OwnerEntityId));
        foreach (var summon in summons)
        {
            Assert.False(replay.SceneOwner.Entities.TryGet(summon.EntityId, out _));
            Assert.False(dungeon.Snapshot.Combatants.TryGetValue(summon.EntityId, out var row) && row.IsVisiblePlayerCombatant);
        }
        Assert.Equal(2_423_659, dungeon.CombatEvents.Where(static row => row.SourceId == 33245 && row.Contribution.Metric == CombatMetricKind.Damage)
            .Sum(static row => row.Contribution.Amount));

        void CheckScope(long ordinal, int scope)
        {
            if (sourceScopes.TryGetValue(ordinal, out var previous)) Assert.Equal(previous, scope);
            else sourceScopes.Add(ordinal, scope);
        }
    }

    [Fact]
    public void ArchivePlaybackMatchesEachSceneAndStopsBeforeTheNextMap()
    {
        var replay = ReplaySession();
        foreach (var archive in replay.MapTransitionArchives)
        {
            using var source = new ArchivedScenePlaybackSource(archive);
            var frame = new ScenePlaybackSession(source).Seek(long.MaxValue, TestContext.Current.CancellationToken);
            Assert.Equal(archive.Snapshot.MapId, frame.Snapshot.MapId);
            Assert.Equal(archive.Snapshot.MapInstanceId, frame.Snapshot.MapInstanceId);
            Assert.Same(archive.ClientContext, frame.Snapshot.ClientContext);
            Assert.Same(replay.SceneJournal, archive.TimelineSegment.Journal);
            Assert.Equal(archive.TimelineSegment.EndObservationOrdinalExclusive, frame.AppliedSegment.EndObservationOrdinalExclusive);
            Assert.Equal(archive.Snapshot.Combatants.Select(static row => (row.Key, row.Value.DamageAmount, row.Value.HealingAmount)),
                frame.Snapshot.Combatants.Select(static row => (row.Key, row.Value.DamageAmount, row.Value.HealingAmount)));
        }
    }

    [Fact]
    public void MapIdentitySurvivesAnEmptyDisplayCatalog()
    {
        var shared = new ResourceSharedCatalog([], new Dictionary<int, SkillBaseProjection>(), new Dictionary<uint, int>(),
            SkillSemanticRuntimeIndex.Empty, new Dictionary<int, NpcDefinition>());
        var locale = new ResourceLocaleCatalog("zh-TW", new Dictionary<int, string>(), new Dictionary<int, string>(),
            new Dictionary<uint, string>(), new Dictionary<int, ServerNameEntry>());
        var context = new SceneClientContext(ClientProfiles.Purple,
            new ResourceCatalogSnapshot(shared, locale, ResourceCatalog.GetResourceSet(ClientProfiles.Purple)));
        var replay = ReplaySession(context);
        Assert.Equal([1010u, 600082u, 1010u], ReadSceneEvents(replay)
            .Where(static scene => scene.Kind == SceneObservationKind.MapContextStarted).Select(static scene => scene.MapId));
        Assert.Equal(2, replay.MapTransitionArchives.Count);
        Assert.Equal("600082", context.Resources.ResolveMapName(600082));
    }

    private static PacketLogReplayResult ReplaySession(SceneClientContext? context = null) => PacketLogReplayService.Replay(
        FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentPurpleDungeonCycle}"), context ?? SceneClientContext.Create(ClientProfiles.Purple));

    private static List<SceneFact> ReadSceneEvents(PacketLogReplayResult replay)
    {
        var scenes = new List<SceneFact>();
        var cursor = replay.SceneJournal.CreateCursor(0);
        while (true)
        {
            var result = replay.SceneJournal.ReadEntries(cursor, 1024, entries =>
            {
                foreach (var entry in entries)
                {
                    if (entry.Raw.Opcode is 0x0140 or 0x0240 or 0x2136 or 0x2336)
                        Assert.NotEqual(ObservedEventDomain.State, entry.Domain);
                    if (entry.Domain == ObservedEventDomain.Scene)
                        scenes.Add(new(entry.Scene.Kind, entry.Scene.MapId, entry.Raw.Opcode));
                }
            });
            if (result.Count == 0) return scenes;
            cursor = result.Cursor;
        }
    }

    private readonly record struct SceneFact(SceneObservationKind Kind, uint MapId, ushort Opcode);
}
