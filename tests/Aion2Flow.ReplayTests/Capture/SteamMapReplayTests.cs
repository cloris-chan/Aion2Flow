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

public sealed class SteamMapReplayTests
{
    private static readonly uint[] ExpectedMaps = [1000, 210016, 200005, 200001, 1000, 100011, 1000, 100012, 1000, 100013, 1000, 100002, 1010];

    [Fact]
    public void ContinuousSessionTransitionsOnArrivalAndKeepsSameMapTeleportsInTheirScene()
    {
        var replay = ReplaySession();
        var scenes = ReadSceneEvents(replay);
        var boundaries = scenes.Where(static scene => scene.Kind == SceneObservationKind.MapContextStarted).ToArray();
        Assert.Equal(26_759, replay.TotalLines);
        Assert.False(replay.SkippedEventCounts.ContainsKey("<invalid>"));
        Assert.Equal(ExpectedMaps, boundaries.Select(static scene => scene.MapId));
        Assert.Equal((ushort)0x2136, boundaries[0].Raw.Opcode);
        Assert.All(boundaries.Skip(1), static scene => Assert.Equal((ushort)0x2336, scene.Raw.Opcode));
        Assert.Equal([1000u, 210016u, 200005u, 200005u, 1000u, 100002u],
            scenes.Where(static scene => scene.Kind == SceneObservationKind.DestinationMapArrival).Select(static scene => scene.MapId));
        Assert.Equal(1010u, replay.Snapshot.MapId);
        Assert.DoesNotContain(scenes, static scene => scene.MapId is 97 or 98 or 106);
    }

    [Fact]
    public void EveryCombatArchiveKeepsItsOwnMapInstanceAndSummonOwnership()
    {
        var context = SceneClientContext.Create(ClientProfiles.Steam);
        var replay = ReplaySession(context);
        Assert.Equal([1000u, 210016u, 200005u, 100011u, 100012u, 100013u, 100002u],
            replay.MapTransitionArchives.Select(static archive => archive.Snapshot.MapId));
        Assert.Equal([0u, 255408u, 255543u, 255774u, 256050u, 256114u, 256424u],
            replay.MapTransitionArchives.Select(static archive => archive.Snapshot.MapInstanceId));

        var expected = new (int Entity, uint Map, long Damage)[]
        {
            (17447, 1000, 92), (29580, 210016, 297), (19221, 100011, 541), (31995, 100011, 527),
            (22287, 100012, 552), (43222, 100002, 643), (35063, 100002, 706)
        };
        foreach (var (entityId, mapId, damage) in expected)
        {
            var archive = Assert.Single(replay.MapTransitionArchives, archive => archive.Snapshot.MapId == mapId);
            var entity = Assert.Single(archive.Entities.AsSpan().ToArray(), entity => entity.EntityId == entityId);
            Assert.Equal(1346, entity.OwnerEntityId);
            Assert.Equal(2_920_637, entity.NpcCode);
            Assert.DoesNotContain(entityId, archive.Snapshot.Combatants.Keys);
            Assert.False(replay.SceneOwner.Entities.TryGet(entityId, out _));
            Assert.Equal(damage, archive.CombatEvents.Where(row => row.SourceId == entityId && row.Contribution.Metric == CombatMetricKind.Damage)
                .Sum(static row => row.Contribution.Amount));
            Assert.Same(context, archive.ClientContext);
            Assert.Same(replay.SceneJournal, archive.TimelineSegment.Journal);
        }
        Assert.Equal(958, replay.Snapshot.Combatants[1346].DamageAmount);
        Assert.Equal(0u, replay.Snapshot.MapInstanceId);
    }

    [Fact]
    public void ArchivedAndLiveCombatUseDisjointJournalRangesWithoutLosingContributions()
    {
        var replay = ReplaySession();
        var sourceScopes = new Dictionary<long, int>();
        var count = 0;
        for (var scope = 0; scope < replay.MapTransitionArchives.Count; scope++)
        {
            var archive = replay.MapTransitionArchives[scope];
            foreach (var entry in archive.CombatEvents)
            {
                Assert.InRange(entry.SourceObservationOrdinal, archive.TimelineSegment.StartObservationOrdinal,
                    archive.TimelineSegment.EndObservationOrdinalExclusive - 1);
                CheckScope(entry.SourceObservationOrdinal, scope);
                count++;
            }
        }
        foreach (var entry in replay.SceneOwner.Combat.Events)
        {
            CheckScope(entry.SourceObservationOrdinal, replay.MapTransitionArchives.Count);
            count++;
        }
        Assert.Equal(924, count);
        Assert.Equal(33_251, replay.MapTransitionArchives.Sum(static archive => archive.Snapshot.Combatants[1346].DamageAmount)
            + replay.Snapshot.Combatants[1346].DamageAmount);

        void CheckScope(long ordinal, int scope)
        {
            if (sourceScopes.TryGetValue(ordinal, out var previous)) Assert.Equal(previous, scope);
            else sourceScopes.Add(ordinal, scope);
        }
    }

    [Fact]
    public void ArchivePlaybackMatchesEachFinalSceneWithoutReadingTheNextMap()
    {
        var replay = ReplaySession();
        foreach (var archive in replay.MapTransitionArchives)
        {
            using var source = new ArchivedScenePlaybackSource(archive);
            var frame = new ScenePlaybackSession(source).Seek(long.MaxValue, TestContext.Current.CancellationToken);
            Assert.Equal(archive.Snapshot.MapId, frame.Snapshot.MapId);
            Assert.Equal(archive.Snapshot.MapInstanceId, frame.Snapshot.MapInstanceId);
            Assert.Same(archive.ClientContext, frame.Snapshot.ClientContext);
            Assert.Equal(archive.TimelineSegment.EndObservationOrdinalExclusive, frame.AppliedSegment.EndObservationOrdinalExclusive);
            Assert.Equal(archive.Snapshot.Combatants.Select(static pair => (pair.Key, pair.Value.DamageAmount, pair.Value.HealingAmount)),
                frame.Snapshot.Combatants.Select(static pair => (pair.Key, pair.Value.DamageAmount, pair.Value.HealingAmount)));
        }
    }

    [Fact]
    public void MapIdentityDoesNotDependOnBundledNamesOrResourceLanguage()
    {
        var shared = new ResourceSharedCatalog([], new Dictionary<int, SkillBaseProjection>(), new Dictionary<uint, int>(),
            SkillSemanticRuntimeIndex.Empty, new Dictionary<int, NpcDefinition>());
        var locale = new ResourceLocaleCatalog("en-US", new Dictionary<int, string>(), new Dictionary<int, string>(),
            new Dictionary<uint, string>(), new Dictionary<int, ServerNameEntry>());
        var empty = new SceneClientContext(ClientProfiles.Steam,
            new ResourceCatalogSnapshot(shared, locale, ResourceCatalog.GetResourceSet(ClientProfiles.Steam)));
        Assert.Equal(ExpectedMaps, ReadSceneEvents(ReplaySession(empty))
            .Where(static scene => scene.Kind == SceneObservationKind.MapContextStarted).Select(static scene => scene.MapId));
        Assert.False(empty.Resources.Maps.ContainsKey(100011));
        Assert.Equal("100011", empty.Resources.ResolveMapName(100011));

        var japanese = new SceneClientContext(ClientProfiles.Steam, ResourceCatalog.Load(ClientProfiles.Steam, "ja-JP"));
        Assert.Equal(ExpectedMaps, ReadSceneEvents(ReplaySession(japanese))
            .Where(static scene => scene.Kind == SceneObservationKind.MapContextStarted).Select(static scene => scene.MapId));
        Assert.Equal("フォイニクスの空中島", japanese.Resources.ResolveMapName(100011));
    }

    private static PacketLogReplayResult ReplaySession(SceneClientContext? context = null) => PacketLogReplayService.ReplayMany(
        ReplayScenarioCatalog.SteamContinuousSession.Select(static file => FixtureHelper.GetPath($"logs/{file}")),
        context ?? SceneClientContext.Create(ClientProfiles.Steam));

    private static List<SceneFact> ReadSceneEvents(PacketLogReplayResult replay)
    {
        var scenes = new List<SceneFact>();
        var cursor = replay.SceneJournal.CreateCursor(0);
        while (true)
        {
            var result = replay.SceneJournal.ReadEntries(cursor, 1024, entries =>
            {
                foreach (var entry in entries)
                    if (entry.Domain == ObservedEventDomain.Scene)
                        scenes.Add(new(entry.Scene.Kind, entry.Scene.MapId, entry.Raw));
            });
            if (result.Count == 0) return scenes;
            cursor = result.Cursor;
        }
    }

    private readonly record struct SceneFact(SceneObservationKind Kind, uint MapId, RawPacketReference Raw);
}
