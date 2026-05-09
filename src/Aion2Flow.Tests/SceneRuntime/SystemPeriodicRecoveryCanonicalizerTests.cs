using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Canonicalization;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Runtime;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public class SystemPeriodicRecoveryCanonicalizerTests
{
    [Fact]
    public void ScenePath_TreatsSystemPeriodicSelfRecoveryTickAsHealingAfterSeed()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        const int playerId = 4086;
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());

        var unseededTick = CreatePacket(playerId, 190000151, 2934, 500, 1, 1, 2);
        var seed = CreatePacket(playerId, 190000131, 7634, 1_000, 10, 10, 1);
        var tick = CreatePacket(playerId, 190000131, 7634, 60_000, 11, 11, 2);

        sink.AppendCombatPacket(unseededTick);
        sink.AppendCombatPacket(seed);
        sink.AppendCombatPacket(tick);

        var combat = Apply(journal);

        Assert.True(combat.TryGetCombatant(playerId, out var combatant));
        Assert.Equal(7634, combatant!.OutgoingHealing);
        Assert.Equal(7634, combatant.IncomingHealing);
        Assert.Equal(0, combatant.OutgoingDamage);
    }

    [Fact]
    public void ScenePath_ConsumesSystemPeriodicSelfRecoverySeedOnFirstContinuation()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        const int playerId = 4086;
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());

        var seed = CreatePacket(playerId, 190000131, 7634, 1_000, 10, 10, 1);
        var mismatchedTick = CreatePacket(playerId, 190000131, 1111, 2_000, 11, 11, 2);
        var laterMatchingTick = CreatePacket(playerId, 190000131, 7634, 3_000, 12, 12, 2);

        sink.AppendCombatPacket(seed);
        sink.AppendCombatPacket(mismatchedTick);
        sink.AppendCombatPacket(laterMatchingTick);

        var combat = Apply(journal);

        Assert.True(combat.TryGetCombatant(playerId, out var combatant));
        Assert.Equal(0, combatant!.OutgoingHealing);
        Assert.Equal(0, combatant.OutgoingDamage);
    }

    [Fact]
    public void ScenePath_DoesNotPromoteContinuationBeforeSeedOrdinal()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        const int playerId = 4086;
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());

        var seed = CreatePacket(playerId, 190000131, 7634, 1_000, 10, 10, 1);
        var earlierTick = CreatePacket(playerId, 190000131, 7634, 2_000, 9, 9, 2);

        sink.AppendCombatPacket(seed);
        sink.AppendCombatPacket(earlierTick);

        var combat = Apply(journal);

        Assert.True(combat.TryGetCombatant(playerId, out var combatant));
        Assert.Equal(0, combatant!.OutgoingHealing);
        Assert.Equal(0, combatant.OutgoingDamage);
    }

    [Fact]
    public void ScenePath_Replay_SystemPeriodicSelfRecovery_MatchesCorpusGroundTruth()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260426140354.log"));

        var entries = replay.SceneJournal.GetEntries(replay.SceneJournal.CreateCursor(0), replay.SceneJournal.Count)
            .ToArray()
            .Where(IsRawSystemPeriodicRecoveryEntry)
            .ToArray();
        var canonicalizer = new SystemPeriodicRecoveryCanonicalizer();
        var sceneHealingBySource = entries
            .Select(entry =>
            {
                var stamp = entry.Stamp;
                var observation = entry.Combat!.Value;
                return canonicalizer.Normalize(entry.SourceEntityId, entry.TargetEntityId, in stamp, in observation);
            })
            .Where(static result => result.Observation.ValueKind == CombatValueKind.PeriodicHealing)
            .GroupBy(static result => result.SourceId)
            .ToDictionary(static group => group.Key, static group => group.Sum(static result => result.Observation.Damage));

        Assert.Contains(entries, static entry => entry.SourceEntityId == 10744 && entry.Combat!.Value.PeriodicMode == 1 && entry.Combat.Value.OriginalSkillCode == 190000131 && entry.Combat.Value.Damage == 13656 && entry.Stamp.BatchOrdinal == 859);
        Assert.Contains(entries, static entry => entry.SourceEntityId == 10744 && entry.Combat!.Value.PeriodicMode == 2 && entry.Combat.Value.OriginalSkillCode == 190000131 && entry.Combat.Value.Damage == 13656 && entry.Stamp.BatchOrdinal == 869);
        Assert.NotEmpty(sceneHealingBySource);
        Assert.True(sceneHealingBySource[10744] > 0);
    }

    private static ParsedCombatPacket CreatePacket(int playerId, int originalSkillCode, int damage, long timestamp, long frameOrdinal, long batchOrdinal, int mode)
    {
        var packet = new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            OriginalSkillCode = originalSkillCode,
            SkillCode = originalSkillCode,
            Damage = damage,
            Timestamp = timestamp,
            FrameOrdinal = frameOrdinal,
            BatchOrdinal = batchOrdinal
        };
        packet.SetPeriodicEffect(PeriodicEffectRelation.Self, mode);
        return packet;
    }

    private static CombatStore Apply(ObservedEventJournal journal)
    {
        var combat = new CombatStore();
        var applier = new DomainEventApplier(new EntityStore(), new MetadataStore(), combat);
        applier.ApplyJournal(journal);
        return combat;
    }

    private static bool IsRawSystemPeriodicRecoveryEntry(ObservedEventEnvelope entry)
    {
        if (entry.Domain != ObservedEventDomain.Combat || entry.SourceEntityId != entry.TargetEntityId || entry.Combat is not { } observation || observation.PeriodicRelation != PeriodicEffectRelation.Self || observation.PeriodicMode is not (1 or 2))
            return false;

        var originalSkillCode = observation.OriginalSkillCode != 0 ? observation.OriginalSkillCode : observation.SkillCode;
        return CombatResourceRegistry.ParseSkillVariant(originalSkillCode).BaseSkillCode == 190000000;
    }
}
