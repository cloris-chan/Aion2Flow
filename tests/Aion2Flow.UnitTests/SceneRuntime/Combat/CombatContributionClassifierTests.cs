using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class CombatContributionClassifierTests
{
    [Fact]
    public void Evaluate_PacketAndObservation_ReturnSameContribution()
    {
        var packet = new ParsedCombatPacket
        {
            SkillCode = 1100020,
            Damage = 0,
            HitContribution = -3,
            AttemptContribution = 1,
            Modifiers = DamageModifiers.Evade | DamageModifiers.MultiHit,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };
        var observation = new CombatObservation
        {
            SkillCode = packet.SkillCode,
            Damage = packet.Damage,
            HitCount = packet.HitContribution,
            AttemptCount = packet.AttemptContribution,
            Modifiers = packet.Modifiers,
            EventKind = packet.EventKind,
            ValueKind = packet.ValueKind,
            EffectTag = packet.EffectTag
        };

        Assert.Equal(CombatContributionClassifier.Evaluate(packet), CombatContributionClassifier.Evaluate(in observation));
    }

    [Theory]
    [InlineData(CombatValueKind.Damage, CombatEventKind.Damage, 123, true, false)]
    [InlineData(CombatValueKind.PeriodicDamage, CombatEventKind.Damage, 123, true, false)]
    [InlineData(CombatValueKind.DrainDamage, CombatEventKind.Damage, 123, true, false)]
    [InlineData(CombatValueKind.Unknown, CombatEventKind.Damage, 123, true, false)]
    [InlineData(CombatValueKind.Healing, CombatEventKind.Healing, 123, false, true)]
    [InlineData(CombatValueKind.PeriodicHealing, CombatEventKind.Healing, 123, false, true)]
    [InlineData(CombatValueKind.DrainHealing, CombatEventKind.Healing, 123, false, true)]
    [InlineData(CombatValueKind.Support, CombatEventKind.Support, 123, false, false)]
    public void Evaluate_Classifies_Primary_Contribution_Kinds(CombatValueKind valueKind, CombatEventKind eventKind, int amount, bool damage, bool healing)
    {
        var contribution = CombatContributionClassifier.Evaluate(new ParsedCombatPacket
        {
            Damage = amount,
            HitContribution = 1,
            AttemptContribution = 1,
            EventKind = eventKind,
            ValueKind = valueKind
        });

        Assert.Equal(damage, contribution.CountsAsDamage);
        Assert.Equal(healing, contribution.CountsAsHealing);
        Assert.False(contribution.CountsAsShieldGrant);
        Assert.False(contribution.CountsAsShieldAbsorbed);
    }

    [Fact]
    public void Evaluate_Classifies_ShieldGrant_And_Absorbed_Separately()
    {
        var grant = CombatContributionClassifier.Evaluate(new ParsedCombatPacket
        {
            Damage = 500,
            EventKind = CombatEventKind.Support,
            ValueKind = CombatValueKind.Shield
        });
        var absorbedPacket = new ParsedCombatPacket
        {
            Damage = 300,
            EventKind = CombatEventKind.Support,
            ValueKind = CombatValueKind.Shield
        };
        absorbedPacket.SetEffectTag(PacketEffectTag.ShieldAbsorbed);
        var absorbed = CombatContributionClassifier.Evaluate(absorbedPacket);

        Assert.True(grant.CountsAsShieldGrant);
        Assert.False(grant.CountsAsShieldAbsorbed);
        Assert.Equal(500, grant.ShieldGrantAmount);
        Assert.Equal(1, grant.ShieldGrantCount);
        Assert.False(absorbed.CountsAsShieldGrant);
        Assert.True(absorbed.CountsAsShieldAbsorbed);
        Assert.Equal(300, absorbed.ShieldAbsorbedAmount);
        Assert.Equal(1, absorbed.ShieldAbsorbedCount);
    }

    [Fact]
    public void Evaluate_OutcomeOnly_Avoidance_Counts_Damage_Attempt_Without_Amount()
    {
        var contribution = CombatContributionClassifier.Evaluate(new ParsedCombatPacket
        {
            Damage = 0,
            HitContribution = 0,
            AttemptContribution = 1,
            Modifiers = DamageModifiers.Invincible,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });

        Assert.True(contribution.CountsAsDamage);
        Assert.Equal(0, contribution.DamageAmount);
        Assert.Equal(0, contribution.HitCount);
        Assert.Equal(1, contribution.AttemptCount);
        Assert.Equal(0, contribution.EvadeCount);
        Assert.Equal(1, contribution.InvincibleCount);
    }

    [Fact]
    public void Evaluate_Normalizes_Hit_And_Attempt_Counts()
    {
        var contribution = CombatContributionClassifier.Evaluate(new ParsedCombatPacket
        {
            Damage = 1000,
            HitContribution = 3,
            AttemptContribution = 1,
            Modifiers = DamageModifiers.MultiHit,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });

        Assert.Equal(3, contribution.HitCount);
        Assert.Equal(3, contribution.AttemptCount);
        Assert.Equal(1, contribution.MultiHitCount);
    }

    [Fact]
    public void CombatStore_Uses_Classifier_Normalized_Contribution()
    {
        var store = new CombatStore();
        store.ApplyCombat(100, 200, new CombatObservation
        {
            SkillCode = 1100020,
            Damage = 100,
            HitCount = 2,
            AttemptCount = 1,
            Modifiers = DamageModifiers.Evade,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });

        Assert.True(store.TryGetPair(100, 200, out var pair));
        Assert.Equal(2, pair!.HitCount);
        Assert.Equal(2, pair.AttemptCount);
        Assert.Equal(2, pair.EvadeCount);
        Assert.True(store.TryGetCombatant(100, out var source));
        Assert.Equal(2, source!.OutgoingAttempts);
        Assert.True(store.TryGetCombatant(200, out var target));
        Assert.Equal(2, target!.IncomingAttempts);
    }

    [Fact]
    public void ArchivePairProjection_Uses_Same_Contribution_As_LiveStore()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendCombat(journal, sceneId, 100, 200, new CombatObservation
        {
            SkillCode = 1100020,
            Damage = 100,
            HitCount = 2,
            AttemptCount = 1,
            Modifiers = DamageModifiers.Evade,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        }, 1, 1_000);
        AppendCombat(journal, sceneId, 100, 200, new CombatObservation
        {
            SkillCode = 1200030,
            Damage = 50,
            EventKind = CombatEventKind.Healing,
            ValueKind = CombatValueKind.Healing
        }, 2, 1_100);
        AppendCombat(journal, sceneId, 100, 200, new CombatObservation
        {
            SkillCode = 1300040,
            Damage = 25,
            EventKind = CombatEventKind.Support,
            ValueKind = CombatValueKind.Shield
        }, 3, 1_200);
        journal.CompleteBatch(1);

        var owner = new SceneReadModelOwner(journal);
        var snapshot = owner.CreateSnapshot();
        var payload = owner.CreateArchivePayload(snapshot);

        Assert.True(owner.Combat.TryGetPair(100, 200, out var live));
        var archived = Assert.Single(payload.Pairs);
        Assert.Equal(live!.TotalDamage, archived.TotalDamage);
        Assert.Equal(live.TotalHealing, archived.TotalHealing);
        Assert.Equal(live.TotalShield, archived.TotalShield);
        Assert.Equal(live.HitCount, archived.HitCount);
        Assert.Equal(live.AttemptCount, archived.AttemptCount);
        Assert.Equal(live.EvadeCount, archived.EvadeCount);
    }

    private static void AppendCombat(ObservedEventJournal journal, Guid sceneId, int sourceId, int targetId, CombatObservation observation, long ordinal, long observedAt)
    {
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = ordinal - 1, FrameOrdinal = ordinal, BatchOrdinal = 1 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference(0, 0, ordinal, observedAt),
            Combat = observation
        });
    }
}
