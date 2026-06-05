using Cloris.Aion2Flow.Resources;

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
            OriginalSkillCode = 17150342,
            SkillCode = 17150342,
            Damage = 4609,
            Type = 3
        });

        Thread.Sleep(5);

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = targetId,
            OriginalSkillCode = 17150342,
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
        CombatResourceRegistry.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        const int ownerId = 1734;
        const int summonId = 123483;
        const int targetId = 110150;

        scene.AppendNickname(ownerId, "Owner");
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = ownerId,
            TargetId = targetId,
            OriginalSkillCode = 16010000,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = targetId,
            OriginalSkillCode = 16100003,
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
        CombatResourceRegistry.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcCatalogEntry>());

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
            OriginalSkillCode = 16010000,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = otherElementalistId,
            TargetId = targetId,
            OriginalSkillCode = 16010000,
            SkillCode = 16010000,
            Damage = 777,
            Timestamp = 1_010
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = ownerId,
            TargetId = summonId,
            OriginalSkillCode = 16770001,
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
            OriginalSkillCode = 16100004,
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
    public void Does_Not_Infer_Preexisting_Summon_Owner_When_DirectSupport_Has_Multiple_SameClass_Candidates()
    {
        CombatResourceRegistry.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcCatalogEntry>());

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
            OriginalSkillCode = 16010000,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = secondElementalistId,
            TargetId = targetId,
            OriginalSkillCode = 16010000,
            SkillCode = 16010000,
            Damage = 777,
            Timestamp = 1_010
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = firstElementalistId,
            TargetId = summonId,
            OriginalSkillCode = 16770001,
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
            OriginalSkillCode = 16770001,
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
            OriginalSkillCode = 16100004,
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
        CombatResourceRegistry.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcCatalogEntry>());

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
            OriginalSkillCode = 16010000,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            OriginalSkillCode = 16990004,
            SkillCode = 16990004,
            Damage = 10_921,
            Timestamp = 1_050
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            OriginalSkillCode = 16990004,
            SkillCode = 16990004,
            Damage = 110_000,
            Timestamp = 1_051
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(ownerId, out var owner));
        var skills = scene.CreateSkillBreakdown(snapshot, ownerId).Skills;
        Assert.Equal(0, owner.HealingAmount);
        Assert.True(skills.TryGetValue(16990004, out var restore));
        Assert.Equal(0, restore.HealingAmount);
        Assert.Equal(0, restore.HealingTimes);
        Assert.Equal(2, restore.SupportTimes);
    }

    [Fact]
    public void Treats_Repeated_Spirit_Descent_Summon_Restore_As_Support()
    {
        CombatResourceRegistry.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcCatalogEntry>());

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
            OriginalSkillCode = 16010000,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            OriginalSkillCode = 16990004,
            SkillCode = 16990004,
            Damage = 9_410,
            Marker = 1,
            Timestamp = 1_050
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            OriginalSkillCode = 16990004,
            SkillCode = 16990004,
            Damage = 100_000,
            Marker = 1,
            Timestamp = 1_051
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            OriginalSkillCode = 16990004,
            SkillCode = 16990004,
            Damage = 9_410,
            Marker = 6,
            Timestamp = 2_050
        });

        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            OriginalSkillCode = 16990004,
            SkillCode = 16990004,
            Damage = 100_000,
            Marker = 6,
            Timestamp = 2_051
        });

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(ownerId, out var owner));
        var skills = scene.CreateSkillBreakdown(snapshot, ownerId).Skills;
        Assert.Equal(0, owner.HealingAmount);
        Assert.True(skills.TryGetValue(16990004, out var restore));
        Assert.Equal(0, restore.HealingAmount);
        Assert.Equal(0, restore.HealingTimes);
        Assert.Equal(4, restore.SupportTimes);
    }

    [Fact]
    public void Treats_Wind_Spirit_Descent_Restore_As_Support()
    {
        CombatResourceRegistry.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcCatalogEntry>());

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
            OriginalSkillCode = 16010000,
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
        Assert.True(skills.TryGetValue(16990004, out var restore));
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
            OriginalSkillCode = 16990004,
            SkillCode = 16990004,
            Damage = amount,
            Marker = marker,
            Timestamp = timestamp
        });
    }

    private static SkillCollection BuildElementalistSummonSkillMap()
    {
        return
        [
            new Skill(16010000, "Cold Shock", SkillCategory.Elementalist, SkillSourceType.PcSkill, "pc", null),
            new Skill(16100003, "Fire Spirit: Leaping Slam", SkillCategory.Elementalist, SkillSourceType.Unknown, "summon", null),
            new Skill(16100004, "Fire Spirit: Strike", SkillCategory.Elementalist, SkillSourceType.Unknown, "summon", null),
            new Skill(16770001, "Spirit Recovery", SkillCategory.Elementalist, SkillSourceType.PcSkill, "pc", null),
            new Skill(16990004, "Spirit's Descent Restore", SkillCategory.Elementalist, SkillSourceType.Unknown, "summon", null)
        ];
    }
}
