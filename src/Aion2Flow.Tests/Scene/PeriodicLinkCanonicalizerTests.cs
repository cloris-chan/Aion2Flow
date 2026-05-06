using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.PacketCapture.Streams;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.Scene;
using Cloris.Aion2Flow.Scene.Compatibility;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Scene.Runtime;
using Cloris.Aion2Flow.Scene.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Scene;

public class PeriodicLinkCanonicalizerTests
{
    private static readonly TcpConnection TestConnection = new(0x0100007f, 0x0100007f, 49820, 57080);

    [Fact]
    public void ScenePath_SynthesizesInvincibleFromPeriodicLinkRecord()
    {
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());

        sink.RegisterPeriodicLink0538(16047, 16047, 29240, 608, 1237540, 1_000, 10, 100);

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(29240, 16047, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(0, pair.HitCount);
        Assert.Equal(1, pair.AttemptCount);
        Assert.Equal(1, pair.InvincibleCount);
        Assert.Equal(1230000, pair.LastSkillCode);
        Assert.True(combat.TryGetCombatant(29240, out var source));
        Assert.True(combat.TryGetCombatant(16047, out var target));
        Assert.Equal(1, source!.OutgoingInvincibles);
        Assert.Equal(1, target!.IncomingInvincibles);
    }

    [Fact]
    public void ScenePath_IgnoresSelfPeriodicLinkBuffTick()
    {
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());

        sink.RegisterPeriodicLink0538(14190, 14190, 14190, 313, 11800008, 1_000, 10, 100);

        var combat = Apply(journal);

        Assert.False(combat.TryGetPair(14190, 14190, out _));
        Assert.Equal(0, combat.Revision);
    }

    [Fact]
    public void ScenePath_DeduplicatesPeriodicLinkWithinBatch()
    {
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());

        sink.RegisterPeriodicLink0538(16047, 16047, 29240, 608, 1237540, 1_000, 10, 100);
        sink.RegisterPeriodicLink0538(16047, 16047, 29240, 608, 1237540, 1_001, 11, 100);

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(29240, 16047, out var pair));
        Assert.Equal(1, pair!.AttemptCount);
        Assert.Equal(1, pair.InvincibleCount);
    }

    [Fact]
    public void ScenePath_AllowsSamePeriodicLinkAcrossBatches()
    {
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());

        sink.RegisterPeriodicLink0538(16047, 16047, 29240, 608, 1237540, 1_000, 10, 100);
        sink.RegisterPeriodicLink0538(16047, 16047, 29240, 608, 1237540, 1_001, 11, 101);

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(29240, 16047, out var pair));
        Assert.Equal(2, pair!.AttemptCount);
        Assert.Equal(2, pair.InvincibleCount);
    }

    [Fact]
    public void JournalingSink_RecordsPeriodicLinkProtocolFields()
    {
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());

        sink.RegisterPeriodicLink0538(16047, 16047, 29240, 608, 1237540, 1_000, 10, 100);

        var entry = journal.Read(0);
        var observation = entry.Combat!.Value;
        Assert.Equal(0x0538, entry.Raw.Opcode);
        Assert.Equal(16047, entry.SourceEntityId);
        Assert.Equal(16047, entry.TargetEntityId);
        Assert.Equal(1237540, observation.SkillCode);
        Assert.Equal(1237540, observation.OriginalSkillCode);
        Assert.Equal(29240, observation.DetailRaw);
        Assert.Equal(608, observation.Marker);
        Assert.Equal(48, observation.Type);
    }

    [Fact]
    public void ScenePath_StreamMode48PeriodicLinkMatchesLegacyFixture()
    {
        CombatMetricsEngine.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var legacy = new CombatMetricsStore();
        var journal = new ObservedEventJournal();
        var journaling = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());
        var composite = new CompositeRuntimeObservationSink(new LegacyRuntimeObservationSink(legacy), journaling);
        using var processor = new PacketStreamProcessor(composite);

        var parsed = processor.AppendAndProcess(HexHelper.FromFixture("combat/0538-mode48-link.hex"), TestConnection);

        Assert.True(parsed);
        var legacyPacket = Assert.Single(legacy.CombatPacketsByTarget[16047]);
        var combat = Apply(journal);
        Assert.True(combat.TryGetPair(legacyPacket.SourceId, legacyPacket.TargetId, out var pair));
        Assert.Equal(legacyPacket.SkillCode, pair!.LastSkillCode);
        Assert.Equal(legacyPacket.AttemptContribution, pair.AttemptCount);
        Assert.Equal(legacyPacket.AttemptContribution, pair.InvincibleCount);
    }

    [Fact]
    public void ScenePath_Replay_PeriodicLinkInvincibles_MatchLegacyPairs()
    {
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        SceneDualWrite.Enabled = true;
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260412103519.log"));
        SceneDualWrite.Enabled = false;

        var legacyPacketsByPair = replay.Store.CombatPacketsBySource.Values
            .SelectMany(static packets => packets)
            .Where(static packet => packet.EffectTag == PacketEffectTag.PeriodicLinkInvincible)
            .GroupBy(static packet => (packet.SourceId, packet.TargetId))
            .ToDictionary(static group => group.Key, static group => group.Sum(static packet => packet.AttemptContribution));
        var combat = Apply(replay.SceneJournal!);

        Assert.NotEmpty(legacyPacketsByPair);
        foreach (var (pairKey, expectedInvincibles) in legacyPacketsByPair)
        {
            Assert.True(combat.TryGetPair(pairKey.SourceId, pairKey.TargetId, out var pair));
            Assert.True(pair!.InvincibleCount >= expectedInvincibles);
        }
    }

    private static CombatStore Apply(ObservedEventJournal journal)
    {
        var combat = new CombatStore();
        var applier = new DomainEventApplier(new EntityStore(), new MetadataStore(), combat);
        applier.ApplyJournal(journal);
        return combat;
    }

    private static SkillCollection BuildSkillMap() =>
    [
        new Skill(1230000, "Fangs", SkillCategory.Npc, SkillSourceType.Unknown, "npc", null),
        new Skill(11800008, "Buff Tick", SkillCategory.Npc, SkillSourceType.Unknown, "npc", null)
    ];
}
