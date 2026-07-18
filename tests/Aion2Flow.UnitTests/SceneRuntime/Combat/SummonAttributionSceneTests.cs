using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Tests.SceneRuntime.Combat;

public sealed class SummonAttributionSceneTests
{
    [Fact]
    public void Attributes_Summon_Damage_To_Owner_In_Snapshot()
    {
        CombatResourceRegistry.SetGameResources(ResourceCatalog.Load(ResourceLanguage.English));
        using var scene = new SceneTestHarness();
        const int ownerId = 12115;
        const int summonId = 18345;
        const int targetId = 17640;

        scene.AppendSummon(ownerId, summonId);
        scene.AppendNickname(ownerId, "Owner");

        AppendDamage(scene, summonId, targetId, 17150342, 4609, type: 3);

        Thread.Sleep(5);

        AppendDamage(scene, summonId, targetId, 17150342, 4384, type: 2);

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
    public void SummonLike_Source_Remains_Independent_Without_Packet_Ownership()
    {
        CombatResourceTestFixture.SetResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int ownerId = 10389;
        const int summonId = 26765;
        const int targetId = 163760;

        scene.AppendNickname(ownerId, "Owner");
        scene.AppendNpcKind(summonId, NpcKind.Summon);
        AppendDamage(scene, ownerId, targetId, 16010000, 405, 1_000);
        AppendHealing(scene, ownerId, summonId, 16770001, 587, 1_020);
        AppendDamage(scene, summonId, targetId, 16100004, 1_205, 1_030);

        var snapshot = scene.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(ownerId, out var owner));
        Assert.True(snapshot.Combatants.ContainsKey(summonId));
        Assert.Equal(405, owner.DamageAmount);
        Assert.Equal(1_205, snapshot.Combatants[summonId].DamageAmount);
        Assert.True(scene.Owner.Entities.TryGet(summonId, out var summon));
        Assert.Null(summon.OwnerEntityId);
    }

    [Fact]
    public void Spirit_Descent_Summon_Restore_Does_Not_Create_Combat_Contribution()
    {
        CombatResourceTestFixture.SetResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int ownerId = 1734;
        const int summonId = 76631;
        const int targetId = 110150;

        scene.AppendNickname(ownerId, "Owner");
        scene.AppendSummon(ownerId, summonId);
        AppendDamage(scene, ownerId, targetId, 16010000, 405, 1_000);
        AppendSpiritDescentRestore(scene, summonId, 0, 1_050, 10_921);
        AppendSpiritDescentRestore(scene, summonId, 0, 1_051, 110_000);

        _ = scene.CreateSnapshot();

        Assert.DoesNotContain(scene.Owner.Combat.Events, static combatEvent =>
            combatEvent.Observation.SkillCode == 16990004);
    }

    [Fact]
    public void Owner_To_Known_Summon_Resource_Value_Is_Suppressed_From_Combat()
    {
        using var scene = new SceneTestHarness();
        const int ownerId = 1734;
        const int summonId = 76631;

        scene.AppendNickname(ownerId, "Owner");
        scene.AppendSummon(ownerId, summonId);
        var resourceValue = new CombatWireObservation
        {
            SkillCode = 16770001,
            Damage = 587,
            LayoutTag = 4,
            Flag = 0,
            Type = 2,
            Loop = 1,
            HitCount = 1,
            AttemptCount = 1
        };
        scene.AppendCombatWireObservation(ownerId, summonId, in resourceValue, 1_000);

        _ = scene.CreateSnapshot();
        Assert.Empty(scene.Owner.Combat.Events);
    }

    [Fact]
    public void Known_Summon_To_Owner_Resource_Value_Is_Suppressed_From_Combat()
    {
        using var scene = new SceneTestHarness();
        const int ownerId = 1734;
        const int summonId = 76631;

        scene.AppendNickname(ownerId, "Owner");
        scene.AppendSummon(ownerId, summonId);
        var resourceValue = new CombatWireObservation
        {
            SkillCode = 16990004,
            Damage = 10_921,
            LayoutTag = 4,
            Flag = 0,
            Type = 2,
            Loop = 1,
            HitCount = 1,
            AttemptCount = 1
        };
        scene.AppendCombatWireObservation(summonId, ownerId, in resourceValue, 1_000);

        _ = scene.CreateSnapshot();
        Assert.Empty(scene.Owner.Combat.Events);
    }

    [Fact]
    public void Repeated_Spirit_Descent_Summon_Restore_Does_Not_Create_Combat_Contribution()
    {
        CombatResourceTestFixture.SetResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int ownerId = 314;
        const int summonId = 34799;
        const int targetId = 23089;

        scene.AppendNickname(ownerId, "Owner");
        scene.AppendSummon(ownerId, summonId);
        AppendDamage(scene, ownerId, targetId, 16010000, 405, 1_000);
        AppendSpiritDescentRestore(scene, summonId, 1, 1_050, 9_410);
        AppendSpiritDescentRestore(scene, summonId, 1, 1_051, 100_000);
        AppendSpiritDescentRestore(scene, summonId, 6, 2_050, 9_410);
        AppendSpiritDescentRestore(scene, summonId, 6, 2_051, 100_000);

        _ = scene.CreateSnapshot();

        Assert.DoesNotContain(scene.Owner.Combat.Events, static combatEvent =>
            combatEvent.Observation.SkillCode == 16990004);
    }

    [Fact]
    public void Wind_Spirit_Descent_Restore_Does_Not_Create_Combat_Contribution()
    {
        CombatResourceTestFixture.SetResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcDisplayEntry>());

        using var scene = new SceneTestHarness();
        const int ownerId = 314;
        const int summonId = 21821;
        const int targetId = 23089;

        scene.AppendNickname(ownerId, "Owner");
        scene.AppendSummon(ownerId, summonId);
        scene.AppendNpcCode(summonId, 2920148);
        AppendDamage(scene, ownerId, targetId, 16010000, 405, 1_000);

        AppendSpiritDescentRestore(scene, summonId, 1, 1_050, 8_588);
        AppendSpiritDescentRestore(scene, summonId, 1, 1_051, 100_000);
        AppendSpiritDescentRestore(scene, summonId, 5, 2_050, 8_588);
        AppendSpiritDescentRestore(scene, summonId, 5, 2_051, 100_000);

        _ = scene.CreateSnapshot();

        Assert.DoesNotContain(scene.Owner.Combat.Events, static combatEvent =>
            combatEvent.Observation.SkillCode == 16990004);
    }

    private static void AppendSpiritDescentRestore(
        SceneTestHarness scene,
        int summonId,
        int marker,
        long timestamp,
        int amount)
    {
        var observation = new CombatWireObservation
        {
            SkillCode = 16990004,
            Damage = amount,
            Marker = marker
        };
        scene.AppendCombatWireObservation(summonId, summonId, in observation, timestamp);
    }

    private static void AppendDamage(
        SceneTestHarness scene,
        int sourceId,
        int targetId,
        int skillCode,
        int amount,
        long timestamp = 0,
        int type = 0)
    {
        var observation = new CombatWireObservation
        {
            SkillCode = skillCode,
            Damage = amount,
            HitCount = 1,
            AttemptCount = 1,
            Type = type
        };
        scene.AppendCombatWireObservation(sourceId, targetId, in observation, timestamp);
    }

    private static void AppendHealing(
        SceneTestHarness scene,
        int sourceId,
        int targetId,
        int skillCode,
        int amount,
        long timestamp)
    {
        var observation = new CombatWireObservation
        {
            SkillCode = skillCode,
            Damage = amount,
            ResourceKind = CombatResourceKind.Health
        };
        scene.AppendCombatWireObservation(sourceId, targetId, in observation, timestamp);
    }

    private static SkillDisplayCatalog BuildElementalistSummonSkillMap()
    {
        return
        [
            new SkillDisplayEntry(16010000, "Cold Shock", SkillCategory.Elementalist, SkillSourceType.PcSkill),
            new SkillDisplayEntry(16100003, "Fire Spirit: Leaping Slam", SkillCategory.Elementalist, SkillSourceType.Unknown),
            new SkillDisplayEntry(16100004, "Fire Spirit: Strike", SkillCategory.Elementalist, SkillSourceType.Unknown),
            new SkillDisplayEntry(16770001, "Spirit Recovery", SkillCategory.Elementalist, SkillSourceType.PcSkill),
            new SkillDisplayEntry(16990004, "Spirit's Descent Restore", SkillCategory.Elementalist, SkillSourceType.Unknown)
        ];
    }

}
