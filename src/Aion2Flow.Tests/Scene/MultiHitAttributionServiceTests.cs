using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.Scene.Canonicalization;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Model;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Scene.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Scene;

public class MultiHitAttributionServiceTests
{
    [Fact]
    public void ScenePath_SynthesizesAux2C38InvincibleFromRecentDamageTarget()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0, FrameOrdinal = 10, BatchOrdinal = 100 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 1734,
            TargetEntityId = 110150,
            Combat = new CombatObservation
            {
                SkillCode = 16330000,
                OriginalSkillCode = 16330000,
                Damage = 1900,
                HitCount = 1,
                AttemptCount = 1,
                Marker = 209,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 1, FrameOrdinal = 11, BatchOrdinal = 100 },
            Domain = ObservedEventDomain.Aura,
            SourceEntityId = 1734,
            TargetEntityId = 1734,
            Aura = new AuraObservation
            {
                SourceEntityId = 1734,
                TargetEntityId = 1734,
                SkillCode = 16330000,
                SequenceId = 202,
                ResultCode = 11,
                Mode = 1
            }
        });

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(1734, 110150, out var pair));
        Assert.Equal(1900, pair!.TotalDamage);
        Assert.Equal(0, pair.InvincibleCount);
        Assert.True(combat.TryGetCombatant(1734, out var source));
        Assert.True(combat.TryGetCombatant(110150, out var target));
        Assert.Equal(0, source!.OutgoingInvincibles);
        Assert.Equal(0, target!.IncomingInvincibles);
        Assert.Equal(2, combat.Revision);
    }

    [Fact]
    public void ScenePath_IgnoresAux2C38InvincibleOutsideFrameWindow()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0, FrameOrdinal = 10, BatchOrdinal = 100 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 1734,
            TargetEntityId = 110150,
            Combat = new CombatObservation
            {
                SkillCode = 16330000,
                OriginalSkillCode = 16330000,
                Damage = 1900,
                HitCount = 1,
                AttemptCount = 1,
                Marker = 209,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 1, FrameOrdinal = 30, BatchOrdinal = 105 },
            Domain = ObservedEventDomain.Aura,
            SourceEntityId = 1734,
            TargetEntityId = 1734,
            Aura = new AuraObservation
            {
                SourceEntityId = 1734,
                TargetEntityId = 1734,
                SkillCode = 16330000,
                SequenceId = 206,
                ResultCode = 11,
                Mode = 1
            }
        });

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(1734, 110150, out var pair));
        Assert.Equal(1900, pair!.TotalDamage);
        Assert.Equal(1, combat.Revision);
    }

    [Fact]
    public void ScenePath_PreservesParserAuthoritativeMultiHitFact()
    {
        var journal = new ObservedEventJournal();
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = Guid.NewGuid(),
            Stamp = new TimelineStamp { ObservationOrdinal = 0, FrameOrdinal = 10, BatchOrdinal = 100 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 8171,
            TargetEntityId = 42995,
            Combat = new CombatObservation
            {
                SkillCode = 17010230,
                OriginalSkillCode = 17010230,
                Damage = 2400,
                HitCount = 1,
                AttemptCount = 1,
                Marker = 5,
                MultiHitCount = 2,
                Modifiers = DamageModifiers.MultiHit,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(8171, 42995, out var pair));
        Assert.Equal(1, pair!.MultiHitCount);
        Assert.True(combat.TryGetCombatant(8171, out var source));
        Assert.Equal(1, source!.OutgoingMultiHits);
    }

    [Fact]
    public void PeriodicNormalizer_PreservesParserAuthoritativeMultiHitCount()
    {
        var canonicalizer = new PeriodicChainCanonicalizer();
        var observation = new CombatObservation
        {
            SkillCode = 17010230,
            OriginalSkillCode = 17010230,
            Damage = 2400,
            HitCount = 1,
            AttemptCount = 1,
            Marker = 5,
            MultiHitCount = 2,
            Modifiers = DamageModifiers.MultiHit,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };

        var result = Assert.Single(canonicalizer.Normalize(8171, 42995, in observation));

        Assert.Equal(2, result.Observation.MultiHitCount);
        Assert.Equal(DamageModifiers.MultiHit, result.Observation.Modifiers & DamageModifiers.MultiHit);
    }

    [Fact]
    public void ScenePath_Replay_Aux2C38Invincible_ProjectsScenePairs()
    {
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260426140354.log"));

        var packets = SceneReplayTestView.Packets(replay)
            .Where(static packet => packet.EffectTag == PacketEffectTag.Aux2C38Invincible)
            .ToArray();
        var combat = Apply(replay.SceneJournal);

        Assert.NotEmpty(packets);
        foreach (var packet in packets)
        {
            Assert.True(combat.TryGetPair(packet.SourceId, packet.TargetId, out var pair));
            Assert.NotNull(pair);
        }
    }

    private static CombatStore Apply(ObservedEventJournal journal)
    {
        var combat = new CombatStore();
        var applier = new DomainEventApplier(new EntityStore(), new MetadataStore(), combat);
        applier.ApplyJournal(journal);
        return combat;
    }
}
