using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Runtime;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class SkillIdentitySceneTests
{
    [Fact]
    public void Journal_Keeps_Confirmed_Specialization_Variant_As_Wire_Identity()
    {
        CombatResourceTestFixture.SetResources(
        [
            new SkillDisplayEntry(17750000, "Immortal Veil", SkillCategory.Chanter, SkillSourceType.PcSkill)
        ], new Dictionary<int, NpcDisplayEntry>());

        var observation = new CombatWireObservation
        {
            SkillCode = 17750010,
            Damage = 603
        };

        var journaled = JournalObservation(4342, 4342, in observation);

        Assert.Equal(observation, journaled);
        Assert.Equal(17750010, journaled.SkillCode);
    }

    [Fact]
    public void Does_Not_Infer_SkillCode_From_ResourceEffectRef()
    {
        CombatResourceTestFixture.SetResources(
        [
            new SkillDisplayEntry(17750000, "Immortal Veil", SkillCategory.Chanter, SkillSourceType.PcSkill)
        ], new Dictionary<int, NpcDisplayEntry>());

        var observation = new CombatWireObservation
        {
            BodyResourceEffectRef = ResourceEffectRef.FromRaw(1775000011),
            DetailResourceEffectRef = ResourceEffectRef.FromRaw(1775000012),
            Damage = 603
        };

        var journaled = JournalObservation(4342, 4342, in observation);

        Assert.Equal(observation, journaled);
        Assert.Equal(0, journaled.SkillCode);
        Assert.Equal(1775000011u, journaled.BodyResourceEffectRef.RawId);
        Assert.Equal(1775000012u, journaled.DetailResourceEffectRef.RawId);
    }

    [Fact]
    public void Keeps_Confirmed_Variant_As_Packet_Identity_And_Uses_Resources_Only_For_Display()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        using var scene = new SceneTestHarness();
        const int sourceId = 3632;
        const int targetId = 19621;

        scene.AppendNickname(sourceId, "Cleric");
        AppendDamage(scene, sourceId, targetId, 17040257, 38641);
        AppendDamage(scene, sourceId, targetId, 17040257, 38641);

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(sourceId, out _));
        var skills = scene.CreateSkillBreakdown(snapshot, sourceId).Skills;
        Assert.True(skills.TryGetBySkillCode(17040257, out var skill));
        Assert.Equal("天罰", CombatResourceRegistry.DisplaySkillNameFor(skill.SkillCode));
        Assert.Equal(77282, skill.DamageAmount);
        Assert.Equal(2, skill.Times);
    }

    [Fact]
    public void Keeps_Exact_Known_Confirmed_Skill_Code()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        using var scene = new SceneTestHarness();
        const int sourceId = 3632;
        const int targetId = 19621;

        scene.AppendNickname(sourceId, "Cleric");
        AppendDamage(scene, sourceId, targetId, 17040250, 9408);
        AppendDamage(scene, sourceId, targetId, 17040250, 9408);

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(sourceId, out _));
        var skills = scene.CreateSkillBreakdown(snapshot, sourceId).Skills;
        Assert.True(skills.TryGetBySkillCode(17040250, out var skill));
        Assert.Equal("審判之電", CombatResourceRegistry.DisplaySkillNameFor(skill.SkillCode));
        Assert.Equal(18816, skill.DamageAmount);
        Assert.Equal(2, skill.Times);
    }

    [Fact]
    public void Keeps_SameName_PcSkill_Variants_As_Distinct_Packet_Identities()
    {
        CombatResourceTestFixture.SetResources(
        [
            new SkillDisplayEntry(12240000, "審判", SkillCategory.Templar, SkillSourceType.PcSkill),
            new SkillDisplayEntry(12240030, "審判", SkillCategory.Templar, SkillSourceType.PcSkill),
            new SkillDisplayEntry(12240039, "審判", SkillCategory.Templar, SkillSourceType.PcSkill),
            new SkillDisplayEntry(12240350, "審判", SkillCategory.Templar, SkillSourceType.PcSkill)
        ], new Dictionary<int, NpcDisplayEntry>(), new Dictionary<int, SkillBaseProjection>
        {
            [12240039] = new(12240039, 12240000)
        });

        using var scene = new SceneTestHarness();
        const int sourceId = 3038;
        const int targetId = 29219;

        scene.AppendNickname(sourceId, "Templar");
        AppendDamage(scene, sourceId, targetId, 12240350, 23108, 1_000);
        AppendDamage(scene, sourceId, targetId, 12240039, 15957, 1_100);

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(sourceId, out _));
        var skills = scene.CreateSkillBreakdown(snapshot, sourceId).Skills;
        Assert.True(skills.TryGetBySkillCode(12240350, out var specialized));
        Assert.True(skills.TryGetBySkillCode(12240039, out var variantState));
        Assert.Equal("審判", CombatResourceRegistry.DisplaySkillNameFor(specialized.SkillCode));
        Assert.Equal("審判", CombatResourceRegistry.DisplaySkillNameFor(variantState.SkillCode));
        Assert.Equal(23108, specialized.DamageAmount);
        Assert.Equal(15957, variantState.DamageAmount);
        Assert.Equal(1, specialized.Times);
        Assert.Equal(1, variantState.Times);
    }

    [Fact]
    public void Attributes_Drain_Heal_To_Confirmed_Skill_Code()
    {
        CombatResourceTestFixture.SetResources(
        [
            new SkillDisplayEntry(13060250, "Ambush", SkillCategory.Assassin, SkillSourceType.PcSkill)
        ], new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int playerId = 3406;
        const int npcId = 17629;

        scene.AppendNickname(playerId, "Perigee");
        var drainDamage = new CombatWireObservation
        {
            SkillCode = 13060250,
            Damage = 1200,
            DrainHealAmount = 240,
            HitCount = 1,
            AttemptCount = 1
        };
        scene.AppendCombatWireObservation(playerId, npcId, in drainDamage, 1_000);
        AppendDamage(scene, playerId, npcId, 13060250, 800, 1_040);

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(2000, combatant.DamageAmount);
        Assert.Equal(240, combatant.HealingAmount);
        Assert.Equal(240, combatant.DrainHealingAmount);

        var skills = scene.CreateSkillBreakdown(snapshot, playerId).Skills;
        Assert.True(skills.TryGetBySkillCode(13060250, out var skill));
        Assert.Equal(2000, skill.DamageAmount);
        Assert.Equal(240, skill.DrainHealingAmount);
        Assert.Equal(2, skill.Times);
        Assert.Equal(1, skill.DrainHealingTimes);
    }

    [Fact]
    public void ResourceKind_Health_Classifies_Confirmed_Skill_As_Healing_Without_Damage_Total()
    {
        CombatResourceTestFixture.SetResources([], new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int playerId = 9024;
        const int targetId = 262851;

        scene.AppendNickname(playerId, "Perigee");
        AppendDamage(scene, playerId, targetId, 13360120, 18167, 1_000);
        AppendDamage(scene, playerId, targetId, 13360120, 32404, 1_050);
        var healing = new CombatWireObservation
        {
            SkillCode = 1900001,
            Damage = 35373,
            ResourceKind = CombatResourceKind.Health
        };
        scene.AppendCombatWireObservation(playerId, playerId, in healing, 1_100);

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(playerId, out var combatant));
        Assert.Equal(50571, combatant.DamageAmount);

        var skills = scene.CreateSkillBreakdown(snapshot, playerId).Skills;
        Assert.True(skills.TryGetBySkillCode(13360120, out var damageSkill));
        Assert.Equal(50571, damageSkill.DamageAmount);
        Assert.True(skills.TryGetBySkillCode(1900001, out var followupSkill));
        Assert.Equal(0, followupSkill.DamageAmount);
        Assert.Equal(35373, followupSkill.HealingAmount);
    }

    [Fact]
    public void NoSkill_Direct_Events_Group_By_ResourceEffectRefs_With_Fallback_Label()
    {
        CombatResourceTestFixture.SetResources(
        [
            new SkillDisplayEntry(17000000, "Should not be used", SkillCategory.Cleric, SkillSourceType.PcSkill)
        ], new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int sourceId = 100;
        const int targetId = 200;
        var body = ResourceEffectRef.FromRaw(1700000011);
        var detail = ResourceEffectRef.FromRaw(1700000012);

        scene.AppendNickname(sourceId, "Cleric");
        var firstEffect = new CombatWireObservation
        {
            BodyResourceEffectRef = body,
            DetailResourceEffectRef = detail,
            Damage = 1200,
            HitCount = 1,
            AttemptCount = 1
        };
        scene.AppendCombatWireObservation(sourceId, targetId, in firstEffect, 1_000);
        var secondEffect = new CombatWireObservation
        {
            BodyResourceEffectRef = body,
            DetailResourceEffectRef = detail,
            Damage = 800,
            HitCount = 1,
            AttemptCount = 1
        };
        scene.AppendCombatWireObservation(sourceId, targetId, in secondEffect, 1_100);

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(sourceId, out _));
        var skills = scene.CreateSkillBreakdown(snapshot, sourceId).Skills;
        var entry = Assert.Single(skills.AsSpan().ToArray());
        Assert.Equal(new CombatEventKey(0, body, detail), entry.EventKey);
        Assert.Equal(0, entry.Metrics.SkillCode);
        Assert.Equal(2000, entry.Metrics.DamageAmount);
        Assert.Equal("Unknown effect B:1700000011 D:1700000012", entry.EventKey.FormatFallbackLabel());
    }

    [Fact]
    public void Compact0438_Body_Code_Is_Stored_As_Skill_Code()
    {
        CombatResourceTestFixture.SetResources(
        [
            new SkillDisplayEntry(1218810, "攻擊", SkillCategory.Npc, SkillSourceType.ClientSkill)
        ], new Dictionary<int, NpcDisplayEntry>());

        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());
        const int sourceId = 100;
        const int targetId = 200;
        var source = new PacketObservationSource(1_000, 1, 0x0438, 16, 0, default);

        sink.RegisterCompactValue0438(in source, targetId, sourceId, 1218810, 3, 0, 1);

        var entry = journal.ReadSnapshot(0);
        Assert.True(entry.Combat.HasValue);
        var combat = entry.Combat.Value;
        Assert.Equal(sourceId, entry.SourceEntityId);
        Assert.Equal(targetId, entry.TargetEntityId);
        Assert.Equal(1218810, combat.SkillCode);
        Assert.Equal(1218810, combat.BodySkillVariantRaw);
        Assert.Equal(0u, combat.BodyResourceEffectRef.RawId);
    }

    [Fact]
    public void Compact0238_Body_Code_Is_Stored_As_Packet_Raw_Body_Code_Not_Skill_Code()
    {
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());
        var source = new PacketObservationSource(1_000, 1, 0x0238, 16, 0, default);

        sink.RegisterCompactControl0238(in source, 100, 0, 30011101, 3, 0, 100);

        var entry = journal.ReadSnapshot(0);
        Assert.True(entry.Combat.HasValue);
        var combat = entry.Combat.Value;
        Assert.Equal(0, combat.SkillCode);
        Assert.Equal(0, combat.BodySkillVariantRaw);
        Assert.Equal(30011101u, combat.BodyCodeRaw);
        Assert.Equal(0u, combat.BodyResourceEffectRef.RawId);
    }

    [Fact]
    public void Compact0638_Body_Code_Is_Stored_As_ResourceEffectRef_Not_Skill_Code()
    {
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());
        var source = new PacketObservationSource(1_000, 1, 0x0638, 16, 0, default);
        var effectRef = ResourceEffectRef.FromRaw(30011101);

        sink.RegisterCompactControl0638(in source, 100, effectRef, 3, 0);

        var entry = journal.ReadSnapshot(0);
        Assert.True(entry.Combat.HasValue);
        var combat = entry.Combat.Value;
        Assert.Equal(0, combat.SkillCode);
        Assert.Equal(0, combat.BodySkillVariantRaw);
        Assert.Equal(30011101u, combat.BodyResourceEffectRef.RawId);
    }

    private static CombatWireObservation JournalObservation(
        int sourceId,
        int targetId,
        in CombatWireObservation observation)
    {
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());
        var source = new PacketObservationSource(1_000, 1, 0x0438, 0, 0, default);
        sink.AppendCombatWireObservation(in source, sourceId, targetId, in observation);

        var entry = journal.ReadSnapshot(0);
        Assert.True(entry.Combat.HasValue);
        return entry.Combat.Value;
    }

    private static void AppendDamage(
        SceneTestHarness scene,
        int sourceId,
        int targetId,
        int skillCode,
        int damage,
        long timestamp = 0)
    {
        var observation = new CombatWireObservation
        {
            SkillCode = skillCode,
            Damage = damage,
            HitCount = 1,
            AttemptCount = 1
        };
        scene.AppendCombatWireObservation(sourceId, targetId, in observation, timestamp);
    }
}
