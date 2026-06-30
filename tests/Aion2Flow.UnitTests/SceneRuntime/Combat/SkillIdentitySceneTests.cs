using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Runtime;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class SkillIdentitySceneTests
{
    [Fact]
    public void Keeps_Confirmed_Specialization_Variant_Without_Resource_Normalization()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new SkillDisplayEntry(17750000, "Immortal Veil", SkillCategory.Chanter, SkillSourceType.PcSkill, "skill", null)
        ], new Dictionary<int, NpcDisplayEntry>());

        var packet = new ParsedCombatPacket
        {
            SourceId = 4342,
            TargetId = 4342,
            SkillCode = 17750010,
            Damage = 603
        };

        CombatResourceRegistry.NormalizePacketForStorage(ref packet);

        Assert.True(packet.IsNormalized);
        Assert.Equal(17750010, packet.SkillCode);
    }

    [Fact]
    public void Does_Not_Infer_SkillCode_From_ResourceEffectRef()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new SkillDisplayEntry(17750000, "Immortal Veil", SkillCategory.Chanter, SkillSourceType.PcSkill, "skill", null)
        ], new Dictionary<int, NpcDisplayEntry>());

        var packet = new ParsedCombatPacket
        {
            SourceId = 4342,
            TargetId = 4342,
            BodyResourceEffectRef = ResourceEffectRef.FromRaw(1775000011),
            DetailResourceEffectRef = ResourceEffectRef.FromRaw(1775000012),
            Damage = 603
        };

        CombatResourceRegistry.NormalizePacketForStorage(ref packet);

        Assert.True(packet.IsNormalized);
        Assert.Equal(0, packet.SkillCode);
        Assert.Equal(1775000011u, packet.BodyResourceEffectRef.RawId);
        Assert.Equal(1775000012u, packet.DetailResourceEffectRef.RawId);
    }

    [Fact]
    public void Keeps_Confirmed_Variant_As_Packet_Identity_And_Uses_Resources_Only_For_Display()
    {
        CombatResourceRegistry.LoadSkillMap("zh-TW");
        using var scene = new SceneTestHarness();
        const int sourceId = 3632;
        const int targetId = 19621;

        scene.AppendNickname(sourceId, "Cleric");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 17040257,
            Damage = 38641
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 17040257,
            Damage = 38641
        });

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
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 17040250,
            Damage = 9408
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 17040250,
            Damage = 9408
        });

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
        CombatResourceRegistry.SetGameResources(
        [
            new SkillDisplayEntry(12240000, "審判", SkillCategory.Templar, SkillSourceType.PcSkill, "pc", null),
            new SkillDisplayEntry(12240030, "審判", SkillCategory.Templar, SkillSourceType.PcSkill, "pc", null),
            new SkillDisplayEntry(12240350, "審判", SkillCategory.Templar, SkillSourceType.PcSkill, "pc", null)
        ], new Dictionary<int, NpcDisplayEntry>(), new Dictionary<int, SkillDisplayProjection>
        {
            [12240039] = new(12240039, 12240000, 12240030, 12240000, 0, 9, false)
        });

        using var scene = new SceneTestHarness();
        const int sourceId = 3038;
        const int targetId = 29219;

        scene.AppendNickname(sourceId, "Templar");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 12240350,
            Damage = 23108,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            SkillCode = 12240039,
            Damage = 15957,
            Timestamp = 1_100
        });

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
        CombatResourceRegistry.SetGameResources(
        [
            new SkillDisplayEntry(13060250, "Ambush", SkillCategory.Assassin, SkillSourceType.PcSkill, "pc", null)
        ], new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int playerId = 3406;
        const int npcId = 17629;

        scene.AppendNickname(playerId, "Perigee");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcId,
            SkillCode = 13060250,
            Damage = 1200,
            DrainHealAmount = 240,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 13060250,
            Damage = 240,
            DrainHealAmount = 240,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = npcId,
            SkillCode = 13060250,
            Damage = 800,
            Timestamp = 1_040
        });

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
        CombatResourceRegistry.SetGameResources([], new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int playerId = 9024;
        const int targetId = 262851;

        scene.AppendNickname(playerId, "Perigee");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 13360120,
            Damage = 18167,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = targetId,
            SkillCode = 13360120,
            Damage = 32404,
            Timestamp = 1_050
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = playerId,
            TargetId = playerId,
            SkillCode = 1900001,
            Damage = 35373,
            ResourceKind = CombatResourceKind.Health,
            Timestamp = 1_100
        });

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
        CombatResourceRegistry.SetGameResources(
        [
            new SkillDisplayEntry(17000000, "Should not be used", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null)
        ], new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int sourceId = 100;
        const int targetId = 200;
        var body = ResourceEffectRef.FromRaw(1700000011);
        var detail = ResourceEffectRef.FromRaw(1700000012);

        scene.AppendNickname(sourceId, "Cleric");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            BodyResourceEffectRef = body,
            DetailResourceEffectRef = detail,
            Damage = 1200,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            BodyResourceEffectRef = body,
            DetailResourceEffectRef = detail,
            Damage = 800,
            Timestamp = 1_100
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(sourceId, out _));
        var skills = scene.CreateSkillBreakdown(snapshot, sourceId).Skills;
        var entry = Assert.Single(skills.AsSpan().ToArray());
        Assert.Equal(new CombatActionKey(0, body, detail), entry.ActionKey);
        Assert.Equal(0, entry.Metrics.SkillCode);
        Assert.Equal(2000, entry.Metrics.DamageAmount);
        Assert.Equal("Unknown effect B:1700000011 D:1700000012", entry.ActionKey.FormatFallbackLabel());
    }

    [Fact]
    public void Compact0438_Body_Code_Is_Stored_As_Skill_Code()
    {
        CombatResourceRegistry.SetGameResources(
        [
            new SkillDisplayEntry(1218810, "攻擊", SkillCategory.Npc, SkillSourceType.ClientSkill, "npc", null)
        ], new Dictionary<int, NpcDisplayEntry>());

        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());
        const int sourceId = 100;
        const int targetId = 200;
        var source = new PacketObservationSource(1_000, 0, 1, 0x0438, 16, 0, default);

        sink.RegisterCompactValue0438(in source, targetId, sourceId, 1218810, 3, 0, 1);

        var entry = journal.Read(0);
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
        var source = new PacketObservationSource(1_000, 0, 1, 0x0238, 16, 0, default);

        sink.RegisterCompactControl0238(in source, 100, 0, 30011101, 3, 0, 100);

        var entry = journal.Read(0);
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
        var source = new PacketObservationSource(1_000, 0, 1, 0x0638, 16, 0, default);
        var effectRef = ResourceEffectRef.FromRaw(30011101);

        sink.RegisterCompactControl0638(in source, 100, effectRef, 3, 0);

        var entry = journal.Read(0);
        Assert.True(entry.Combat.HasValue);
        var combat = entry.Combat.Value;
        Assert.Equal(0, combat.SkillCode);
        Assert.Equal(0, combat.BodySkillVariantRaw);
        Assert.Equal(30011101u, combat.BodyResourceEffectRef.RawId);
    }
}
