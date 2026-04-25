using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Tests.Combat;

public sealed class CombatMetricsEngineSummonAttributionTests
{
    [Fact]
    public void Attributes_Summon_Damage_To_Owner_In_Snapshot()
    {
        CombatMetricsEngine.SkillMap = new SkillCollection();
        var engine = new CombatMetricsEngine();
        const int ownerId = 12115;
        const int summonId = 18345;
        const int targetId = 17640;

        engine.Store.AppendSummon(ownerId, summonId);
        engine.Store.AppendNickname(ownerId, "Owner");

        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = targetId,
            OriginalSkillCode = 17150342,
            SkillCode = 17150342,
            Damage = 4609,
            Type = 3
        });

        Thread.Sleep(5);

        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = targetId,
            OriginalSkillCode = 17150342,
            SkillCode = 17150342,
            Damage = 4384,
            Type = 2
        });

        var snapshot = engine.CreateSnapshot();

        Assert.True(snapshot.BattleTime > 0);
        Assert.True(snapshot.Combatants.ContainsKey(ownerId));
        Assert.False(snapshot.Combatants.ContainsKey(summonId));

        var owner = snapshot.Combatants[ownerId];
        Assert.Equal("Owner", owner.Nickname);
        Assert.Equal(8993, owner.DamageAmount);
        Assert.Single(owner.Skills);

        var skill = owner.Skills.Values.Single();
        Assert.Equal(8993, skill.DamageAmount);
        Assert.Equal(2, skill.Times);
    }

    [Fact]
    public void Infers_Preexisting_Elementalist_Summon_From_Signature_Skills()
    {
        CombatMetricsEngine.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var engine = new CombatMetricsEngine();
        const int ownerId = 1734;
        const int summonId = 123483;
        const int targetId = 110150;

        engine.Store.AppendNickname(ownerId, "Owner");
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = ownerId,
            TargetId = targetId,
            OriginalSkillCode = 16010000,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });

        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = targetId,
            OriginalSkillCode = 16100003,
            SkillCode = 16100003,
            Damage = 1205,
            Timestamp = 1_010
        });

        var snapshot = engine.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(ownerId, out var owner));
        Assert.False(snapshot.Combatants.ContainsKey(summonId));
        Assert.Equal(1610, owner.DamageAmount);
        Assert.True(engine.Store.SummonOwnerByInstance.TryGetValue(summonId, out var inferredOwnerId));
        Assert.Equal(ownerId, inferredOwnerId);
    }

    [Fact]
    public void Counts_Spirit_Descent_Summon_Restore_As_Owner_Healing()
    {
        CombatMetricsEngine.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var engine = new CombatMetricsEngine();
        const int ownerId = 1734;
        const int summonId = 76631;
        const int targetId = 110150;

        engine.Store.AppendNickname(ownerId, "Owner");
        engine.Store.AppendSummon(ownerId, summonId);
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = ownerId,
            TargetId = targetId,
            OriginalSkillCode = 16010000,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });

        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            OriginalSkillCode = 16990004,
            SkillCode = 16990004,
            Damage = 10_921,
            Timestamp = 1_050
        });

        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            OriginalSkillCode = 16990004,
            SkillCode = 16990004,
            Damage = 110_000,
            Timestamp = 1_051
        });

        var snapshot = engine.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(ownerId, out var owner));
        Assert.Equal(120_921, owner.HealingAmount);
        Assert.True(owner.Skills.TryGetValue(16990004, out var restore));
        Assert.Equal(120_921, restore.HealingAmount);
        Assert.Equal(2, restore.HealingTimes);
    }

    [Fact]
    public void Counts_Spirit_Descent_Summon_Restore_Only_Once_Per_Summon_Instance()
    {
        CombatMetricsEngine.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var engine = new CombatMetricsEngine();
        const int ownerId = 314;
        const int summonId = 34799;
        const int targetId = 23089;

        engine.Store.AppendNickname(ownerId, "Owner");
        engine.Store.AppendSummon(ownerId, summonId);
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = ownerId,
            TargetId = targetId,
            OriginalSkillCode = 16010000,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });

        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            OriginalSkillCode = 16990004,
            SkillCode = 16990004,
            Damage = 9_410,
            Marker = 1,
            Timestamp = 1_050
        });

        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            OriginalSkillCode = 16990004,
            SkillCode = 16990004,
            Damage = 100_000,
            Marker = 1,
            Timestamp = 1_051
        });

        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            OriginalSkillCode = 16990004,
            SkillCode = 16990004,
            Damage = 9_410,
            Marker = 6,
            Timestamp = 2_050
        });

        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = summonId,
            TargetId = summonId,
            OriginalSkillCode = 16990004,
            SkillCode = 16990004,
            Damage = 100_000,
            Marker = 6,
            Timestamp = 2_051
        });

        var snapshot = engine.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(ownerId, out var owner));
        Assert.Equal(109_410, owner.HealingAmount);
        Assert.True(owner.Skills.TryGetValue(16990004, out var restore));
        Assert.Equal(109_410, restore.HealingAmount);
        Assert.Equal(2, restore.HealingTimes);
    }

    [Fact]
    public void Counts_Wind_Spirit_Descent_Restore_As_Separate_Marker_Windows()
    {
        CombatMetricsEngine.SetGameResources(BuildElementalistSummonSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var engine = new CombatMetricsEngine();
        const int ownerId = 314;
        const int summonId = 21821;
        const int targetId = 23089;

        engine.Store.AppendNickname(ownerId, "Owner");
        engine.Store.AppendSummon(ownerId, summonId);
        engine.Store.AppendNpcCode(summonId, 2920148);
        engine.Store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = ownerId,
            TargetId = targetId,
            OriginalSkillCode = 16010000,
            SkillCode = 16010000,
            Damage = 405,
            Timestamp = 1_000
        });

        AppendSpiritDescentRestore(engine.Store, summonId, 1, 1_050, 8_588);
        AppendSpiritDescentRestore(engine.Store, summonId, 1, 1_051, 100_000);
        AppendSpiritDescentRestore(engine.Store, summonId, 5, 2_050, 8_588);
        AppendSpiritDescentRestore(engine.Store, summonId, 5, 2_051, 100_000);

        var snapshot = engine.CreateSnapshot();

        Assert.True(snapshot.Combatants.TryGetValue(ownerId, out var owner));
        Assert.Equal(217_176, owner.HealingAmount);
        Assert.True(owner.Skills.TryGetValue(16990004, out var restore));
        Assert.Equal(217_176, restore.HealingAmount);
        Assert.Equal(4, restore.HealingTimes);
    }

    private static void AppendSpiritDescentRestore(
        CombatMetricsStore store,
        int summonId,
        int marker,
        long timestamp,
        int amount)
    {
        store.AppendCombatPacket(new ParsedCombatPacket
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
            new Skill(16010000, "Cold Shock", SkillCategory.Elementalist, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage, null),
            new Skill(16100003, "Fire Spirit: Leaping Slam", SkillCategory.Elementalist, SkillSourceType.Unknown, "summon", SkillKind.Unknown, SkillSemantics.None, null),
            new Skill(16990004, "Spirit's Descent Restore", SkillCategory.Elementalist, SkillSourceType.Unknown, "summon", SkillKind.Support, SkillSemantics.Support, null)
        ];
    }
}
