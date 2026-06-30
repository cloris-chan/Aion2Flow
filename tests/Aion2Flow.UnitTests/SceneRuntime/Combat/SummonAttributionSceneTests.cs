using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class SummonAttributionSceneTests
{
    [Fact]
    public void Attributes_Summon_Damage_To_Owner_In_Snapshot()
    {
        CombatResourceRegistry.SkillMap = [];
        using var scene = new SceneTestHarness();
        const int ownerId = 12115;
        const int summonId = 18345;
        const int targetId = 17640;

        scene.AppendSummon(ownerId, summonId);
        scene.AppendNickname(ownerId, "Owner");

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = targetId,
            SkillCode = 17150342,
            Damage = 4609,
            Type = 3
        });

        Thread.Sleep(5);

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = targetId,
            SkillCode = 17150342,
            Damage = 4384,
            Type = 2
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.EncounterTime > 0);
        Assert.True(snapshot.Combatants.ContainsKey(ownerId));
        Assert.False(snapshot.Combatants.ContainsKey(summonId));

        var owner = snapshot.Combatants[ownerId];
        var skills = scene.CreateSkillBreakdown(snapshot, ownerId).Skills;
        Assert.True(scene.Owner.MetadataRegistry.TryGetPcMetadata(ownerId, out var ownerMetadata));
        Assert.Equal("Owner", ownerMetadata.Nickname);
        Assert.Equal(8993, owner.DamageAmount);
        Assert.Single(skills);

        var skill = skills.Values.Single();
        Assert.Equal(8993, skill.DamageAmount);
        Assert.Equal(2, skill.Times);
    }

    [Fact]
    public void Infers_Preexisting_Elementalist_Summon_From_Signature_Skills()
    {
        CombatResourceRegistry.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int ownerId = 1734;
        const int summonId = 123483;
        const int targetId = 110150;

        scene.AppendNickname(ownerId, "Owner");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = ownerId,
            TargetId = targetId,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = targetId,
            SkillCode = 16100003,
            Damage = 1205,
            Timestamp = 1_010
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(ownerId, out var owner));
        Assert.False(snapshot.Combatants.ContainsKey(summonId));
        Assert.Equal(1610, owner.DamageAmount);
    }

    [Fact]
    public void Infers_Preexisting_Elementalist_Summon_From_OwnerSupport_When_Class_Candidates_Are_Ambiguous()
    {
        CombatResourceRegistry.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int ownerId = 10389;
        const int otherElementalistId = 9915;
        const int summonId = 26765;
        const int targetId = 163760;

        scene.AppendNickname(ownerId, "Owner");
        scene.AppendNickname(otherElementalistId, "Other");
        scene.AppendNpcHp(summonId, 19_649, 19_649, 1_005);
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = ownerId,
            TargetId = targetId,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = otherElementalistId,
            TargetId = targetId,
            SkillCode = 16010000,
            Damage = 777,
            Timestamp = 1_010
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = ownerId,
            TargetId = summonId,
            SkillCode = 16770001,
            Damage = 587,
            EventKind = CombatEventKind.Healing,
            ValueKind = CombatValueKind.Healing,
            Timestamp = 1_020
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = targetId,
            SkillCode = 16100004,
            Damage = 1205,
            Timestamp = 1_030
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(ownerId, out var owner));
        Assert.True(snapshot.Combatants.TryGetValue(otherElementalistId, out var other));
        Assert.False(snapshot.Combatants.ContainsKey(summonId));
        Assert.Equal(1610, owner.DamageAmount);
        Assert.Equal(777, other.DamageAmount);
        Assert.Equal(587, owner.HealingAmount);
    }

    [Fact]
    public void Infers_Preexisting_Elementalist_Catalog_Summon_From_OwnerSupport_When_Class_Candidates_Are_Ambiguous()
    {
        CombatResourceRegistry.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcDisplayEntry>
        {
            [2920115] = new(2920115, "火之精靈", NpcCatalogKind.Summon)
        });

        using var scene = new SceneTestHarness();
        const int ownerId = 10389;
        const int otherElementalistId = 9915;
        const int summonId = 26765;
        const int targetId = 163760;

        scene.AppendNickname(ownerId, "Owner");
        scene.AppendNickname(otherElementalistId, "Other");
        scene.AppendNpcCode(summonId, 2920115);
        scene.AppendNpcKind(summonId, NpcKind.Summon);
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = ownerId,
            TargetId = targetId,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = otherElementalistId,
            TargetId = targetId,
            SkillCode = 16010000,
            Damage = 777,
            Timestamp = 1_010
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = ownerId,
            TargetId = summonId,
            SkillCode = 16770001,
            Damage = 587,
            EventKind = CombatEventKind.Healing,
            ValueKind = CombatValueKind.Healing,
            Timestamp = 1_020
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = targetId,
            SkillCode = 16100004,
            Damage = 1205,
            Timestamp = 1_030
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(ownerId, out var owner));
        Assert.True(snapshot.Combatants.TryGetValue(otherElementalistId, out var other));
        Assert.False(snapshot.Combatants.ContainsKey(summonId));
        Assert.Equal(1610, owner.DamageAmount);
        Assert.Equal(777, other.DamageAmount);
        Assert.Equal(587, owner.HealingAmount);
    }

    [Fact]
    public void Infers_Preexisting_Elementalist_Catalog_Summon_With_NpcCode()
    {
        const int ownerId = 10389;
        const int summonId = 153484;
        const int targetId = 163760;
        const int summonNpcCode = 2920115;
        CombatResourceRegistry.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcDisplayEntry>
        {
            [summonNpcCode] = new(summonNpcCode, "火之精靈", NpcCatalogKind.Summon)
        });
        using var scene = new SceneTestHarness();
        var writer = new SceneObservationWriter(scene.Sink);

        scene.AppendNickname(ownerId, "Owner");
        writer.ApplyNpcCatalog(Source(1_005), summonId, summonNpcCode, requireCatalogEntry: true);
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = ownerId,
            TargetId = targetId,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = targetId,
            SkillCode = 16100004,
            Damage = 1_205,
            Timestamp = 1_030
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(scene.Owner.Entities.TryGet(summonId, out var summon));
        Assert.Equal(summonNpcCode, summon.NpcCode);
        Assert.Equal(NpcKind.Summon, summon.Kind);
        Assert.True(snapshot.Combatants.TryGetValue(ownerId, out var owner));
        Assert.False(snapshot.Combatants.ContainsKey(summonId));
        Assert.Equal(1_610, owner.DamageAmount);
    }

    [Fact]
    public void Does_Not_Infer_Preexisting_Summon_Owner_When_DirectSupport_Has_Multiple_SameClass_Candidates()
    {
        CombatResourceRegistry.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int firstElementalistId = 10389;
        const int secondElementalistId = 9915;
        const int summonId = 26765;
        const int targetId = 163760;

        scene.AppendNickname(firstElementalistId, "First");
        scene.AppendNickname(secondElementalistId, "Second");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = firstElementalistId,
            TargetId = targetId,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = secondElementalistId,
            TargetId = targetId,
            SkillCode = 16010000,
            Damage = 777,
            Timestamp = 1_010
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = firstElementalistId,
            TargetId = summonId,
            SkillCode = 16770001,
            Damage = 587,
            EventKind = CombatEventKind.Healing,
            ValueKind = CombatValueKind.Healing,
            Timestamp = 1_020
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = secondElementalistId,
            TargetId = summonId,
            SkillCode = 16770001,
            Damage = 586,
            EventKind = CombatEventKind.Healing,
            ValueKind = CombatValueKind.Healing,
            Timestamp = 1_025
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = targetId,
            SkillCode = 16100004,
            Damage = 1205,
            Timestamp = 1_030
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.ContainsKey(summonId));
    }

    [Fact]
    public void Treats_Spirit_Descent_Summon_Restore_As_Support()
    {
        CombatResourceRegistry.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int ownerId = 1734;
        const int summonId = 76631;
        const int targetId = 110150;

        scene.AppendNickname(ownerId, "Owner");
        scene.AppendSummon(ownerId, summonId);
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = ownerId,
            TargetId = targetId,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            SkillCode = 16990004,
            Damage = 10_921,
            Timestamp = 1_050
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            SkillCode = 16990004,
            Damage = 110_000,
            Timestamp = 1_051
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(ownerId, out var owner));
        var skills = scene.CreateSkillBreakdown(snapshot, ownerId).Skills;
        Assert.Equal(0, owner.HealingAmount);
        Assert.True(skills.TryGetBySkillCode(16990004, out var restore));
        Assert.Equal(0, restore.HealingAmount);
        Assert.Equal(0, restore.HealingTimes);
        Assert.Equal(2, restore.SupportTimes);
    }

    [Fact]
    public void Treats_Owner_To_Known_Summon_Direct_Resource_Value_As_Support()
    {
        using var scene = new SceneTestHarness();
        const int ownerId = 1734;
        const int summonId = 76631;

        scene.AppendNickname(ownerId, "Owner");
        scene.AppendSummon(ownerId, summonId);
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = ownerId,
            TargetId = summonId,
            SkillCode = 16770001,
            Damage = 587,
            LayoutTag = 4,
            Flag = 0,
            Type = 2,
            Loop = 1,
            HitContribution = 1,
            AttemptContribution = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage,
            Timestamp = 1_000
        });

        _ = scene.CreateSnapshot();
        var combatEvent = Assert.Single(scene.Owner.Combat.Events);
        Assert.Equal(CombatEventKind.Support, combatEvent.Observation.EventKind);
        Assert.Equal(CombatValueKind.Support, combatEvent.Observation.ValueKind);
    }

    [Fact]
    public void Treats_Known_Summon_To_Owner_Direct_Resource_Value_As_Support()
    {
        using var scene = new SceneTestHarness();
        const int ownerId = 1734;
        const int summonId = 76631;

        scene.AppendNickname(ownerId, "Owner");
        scene.AppendSummon(ownerId, summonId);
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = ownerId,
            SkillCode = 16990004,
            Damage = 10_921,
            LayoutTag = 4,
            Flag = 0,
            Type = 2,
            Loop = 1,
            HitContribution = 1,
            AttemptContribution = 1,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage,
            Timestamp = 1_000
        });

        _ = scene.CreateSnapshot();
        var combatEvent = Assert.Single(scene.Owner.Combat.Events);
        Assert.Equal(CombatEventKind.Support, combatEvent.Observation.EventKind);
        Assert.Equal(CombatValueKind.Support, combatEvent.Observation.ValueKind);
    }

    [Fact]
    public void Treats_Repeated_Spirit_Descent_Summon_Restore_As_Support()
    {
        CombatResourceRegistry.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int ownerId = 314;
        const int summonId = 34799;
        const int targetId = 23089;

        scene.AppendNickname(ownerId, "Owner");
        scene.AppendSummon(ownerId, summonId);
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = ownerId,
            TargetId = targetId,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            SkillCode = 16990004,
            Damage = 9_410,
            Marker = 1,
            Timestamp = 1_050
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            SkillCode = 16990004,
            Damage = 100_000,
            Marker = 1,
            Timestamp = 1_051
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            SkillCode = 16990004,
            Damage = 9_410,
            Marker = 6,
            Timestamp = 2_050
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            SkillCode = 16990004,
            Damage = 100_000,
            Marker = 6,
            Timestamp = 2_051
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(ownerId, out var owner));
        var skills = scene.CreateSkillBreakdown(snapshot, ownerId).Skills;
        Assert.Equal(0, owner.HealingAmount);
        Assert.True(skills.TryGetBySkillCode(16990004, out var restore));
        Assert.Equal(0, restore.HealingAmount);
        Assert.Equal(0, restore.HealingTimes);
        Assert.Equal(4, restore.SupportTimes);
    }

    [Fact]
    public void Treats_Wind_Spirit_Descent_Restore_As_Support()
    {
        CombatResourceRegistry.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int ownerId = 314;
        const int summonId = 21821;
        const int targetId = 23089;

        scene.AppendNickname(ownerId, "Owner");
        scene.AppendSummon(ownerId, summonId);
        scene.AppendNpcCode(summonId, 2920148);
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = ownerId,
            TargetId = targetId,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });

        AppendSpiritDescentRestore(scene, summonId, 1, 1_050, 8_588);
        AppendSpiritDescentRestore(scene, summonId, 1, 1_051, 100_000);
        AppendSpiritDescentRestore(scene, summonId, 5, 2_050, 8_588);
        AppendSpiritDescentRestore(scene, summonId, 5, 2_051, 100_000);

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(ownerId, out var owner));
        var skills = scene.CreateSkillBreakdown(snapshot, ownerId).Skills;
        Assert.Equal(0, owner.HealingAmount);
        Assert.True(skills.TryGetBySkillCode(16990004, out var restore));
        Assert.Equal(0, restore.HealingAmount);
        Assert.Equal(0, restore.HealingTimes);
        Assert.Equal(4, restore.SupportTimes);
    }

    private static void AppendSpiritDescentRestore(
        SceneTestHarness scene,
        int summonId,
        int marker,
        long timestamp,
        int amount)
    {
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            SkillCode = 16990004,
            Damage = amount,
            Marker = marker,
            Timestamp = timestamp
        });
    }

    private static SkillDisplayCatalog BuildElementalistSummonSkillMap()
    {
        return
        [
            new SkillDisplayEntry(16010000, "Cold Shock", SkillCategory.Elementalist, SkillSourceType.PcSkill, "pc", null),
            new SkillDisplayEntry(16100003, "Fire Spirit: Leaping Slam", SkillCategory.Elementalist, SkillSourceType.Unknown, "summon", null),
            new SkillDisplayEntry(16100004, "Fire Spirit: Strike", SkillCategory.Elementalist, SkillSourceType.Unknown, "summon", null),
            new SkillDisplayEntry(16770001, "Spirit Recovery", SkillCategory.Elementalist, SkillSourceType.PcSkill, "pc", null),
            new SkillDisplayEntry(16990004, "Spirit's Descent Restore", SkillCategory.Elementalist, SkillSourceType.Unknown, "summon", null)
        ];
    }

    private static PacketObservationSource Source(long timestamp) =>
        new(timestamp, 0, 1, 0, 0, 0, default);
}
