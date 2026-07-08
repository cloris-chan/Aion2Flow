using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class SkillDetailBaseAggregatorTests
{
    [Fact]
    public void AddOrMerge_Merges_Generated_Base_Group()
    {
        SetSkillBaseProjections(
            new SkillBaseProjection(16300243, 16300000),
            new SkillBaseProjection(16300027, 16300000));

        var rows = new List<SkillDetailRowData>();
        var indexes = new Dictionary<SkillBaseKey, int>();
        var main = CreateRow(16300243, 140141, 2, eventCount: 2);
        var derived = CreateRow(16300027, 11588, 1);

        SkillDetailBaseAggregator.AddOrMerge(rows, indexes, in main);
        SkillDetailBaseAggregator.AddOrMerge(rows, indexes, in derived);

        var row = Assert.Single(rows);
        Assert.Equal(151729, row.TotalAmount);
        Assert.Equal(3, row.Hits);
        Assert.Equal(3, row.EventCount);
        Assert.Equal(16300000, row.SkillCode);
        Assert.Equal(16300000, row.BaseKey.SkillCode);
    }

    [Fact]
    public void AddOrMerge_Merges_Derived_Skill_With_Same_Base()
    {
        SetSkillBaseProjections(
            new SkillBaseProjection(16330020, 16330000),
            new SkillBaseProjection(16330027, 16330000));

        var rows = new List<SkillDetailRowData>();
        var indexes = new Dictionary<SkillBaseKey, int>();
        var main = CreateRow(16330020, 60361, 7);
        var delayed = CreateRow(16330027, 23791, 6);

        SkillDetailBaseAggregator.AddOrMerge(rows, indexes, in main);
        SkillDetailBaseAggregator.AddOrMerge(rows, indexes, in delayed);

        var row = Assert.Single(rows);
        Assert.Equal(84152, row.TotalAmount);
        Assert.Equal(13, row.Hits);
        Assert.Equal(16330000, row.SkillCode);
    }

    [Theory]
    [InlineData(11010047, 11420000)]
    [InlineData(16330027, 16330000)]
    [InlineData(19010047, 19010000)]
    public void FromEventKey_Uses_Generated_Base_Relations(int skillCode, int expectedBaseSkillCode)
    {
        SetSkillBaseProjections(
            new SkillBaseProjection(11010047, 11420000),
            new SkillBaseProjection(16330027, 16330000),
            new SkillBaseProjection(19010047, 19010000));

        var key = SkillBaseKey.FromEventKey(new CombatEventKey(skillCode, default, default));

        Assert.Equal(expectedBaseSkillCode, key.SkillCode);
    }

    [Theory]
    [InlineData(1218810)]
    [InlineData(11000001)]
    [InlineData(16000047)]
    [InlineData(10_010_047)]
    [InlineData(20_010_047)]
    public void FromEventKey_Preserves_Skills_Without_Base_Relation(int skillCode)
    {
        SetSkillBaseProjections();

        var key = SkillBaseKey.FromEventKey(new CombatEventKey(skillCode, default, default));

        Assert.Equal(skillCode, key.SkillCode);
    }

    [Fact]
    public void FromEventKey_Preserves_Effect_Reference_Identity()
    {
        SetSkillBaseProjections();

        var actionKey = new CombatEventKey(0, ResourceEffectRef.FromRaw(1234), ResourceEffectRef.FromRaw(5678));

        var key = SkillBaseKey.FromEventKey(actionKey);

        Assert.Equal(actionKey, key.EventKey);
    }

    [Fact]
    public void FromEventKey_Uses_EffectRef_Owner_Base_When_ResourceRefs_Agree()
    {
        SetSkillBaseResources(
            [new SkillBaseProjection(16300027, 16300000)],
            [
                new SkillEffectReference(16300027, 0, SkillEffectReferenceKind.EffectId, 1234),
                new SkillEffectReference(16300027, 1, SkillEffectReferenceKind.EffectDataId, 5678)
            ]);

        var actionKey = new CombatEventKey(0, ResourceEffectRef.FromRaw(1234), ResourceEffectRef.FromRaw(5678));

        var key = SkillBaseKey.FromEventKey(actionKey);

        Assert.Equal(new CombatEventKey(16300000, default, default), key.EventKey);
    }

    private static SkillDetailRowData CreateRow(int skillCode, long amount, int hits, int eventCount = 1)
    {
        var baseKey = SkillBaseKey.FromEventKey(new CombatEventKey(skillCode, default, default));
        return new SkillDetailRowData
        {
            BaseKey = baseKey,
            SkillCode = baseKey.SkillCode,
            DisplayName = baseKey.SkillCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            EventCount = eventCount,
            TotalAmount = amount,
            DirectAmount = amount,
            Hits = hits,
            Attempts = hits
        };
    }

    private static void SetSkillBaseProjections(params SkillBaseProjection[] projections)
        => SetSkillBaseResources(projections, []);

    private static void SetSkillBaseResources(SkillBaseProjection[] projections, SkillEffectReference[] effectReferences)
    {
        CombatResourceRegistry.SetGameResources(
            [],
            new Dictionary<int, NpcDisplayEntry>(),
            new Dictionary<int, SkillClientMetadata>(),
            projections.ToDictionary(static projection => projection.SkillCode),
            effectReferences);
    }
}
