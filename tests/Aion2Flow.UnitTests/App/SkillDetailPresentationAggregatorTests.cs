using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class SkillDetailPresentationAggregatorTests
{
    [Fact]
    public void AddOrMerge_Merges_Generated_Presentation_Group()
    {
        SetSkillDisplayProjections(
            new SkillDisplayProjection(16300243, 16300000, 16300240, 16300000, 0b01010, 3, false),
            new SkillDisplayProjection(16300027, 16300000, 16300020, 16300000, 0, 7, false));

        var rows = new List<SkillDetailRowData>();
        var indexes = new Dictionary<SkillPresentationKey, int>();
        var main = CreateRow(16300243, 140141, 2);
        var derived = CreateRow(16300027, 11588, 1);

        SkillDetailPresentationAggregator.AddOrMerge(rows, indexes, in main);
        SkillDetailPresentationAggregator.AddOrMerge(rows, indexes, in derived);

        var row = Assert.Single(rows);
        Assert.Equal(151729, row.TotalAmount);
        Assert.Equal(3, row.Hits);
        Assert.Equal(16300000, row.SkillCode);
        Assert.Equal(16300000, row.PresentationKey.SkillCode);
    }

    [Fact]
    public void AddOrMerge_Merges_Derived_Skill_With_Different_Display_Name()
    {
        SetSkillDisplayProjections(
            new SkillDisplayProjection(16330020, 16330000, 16330020, 16330000, 0, 0, false),
            new SkillDisplayProjection(16330027, 16330000, 16330020, 16330000, 0, 7, false));

        var rows = new List<SkillDetailRowData>();
        var indexes = new Dictionary<SkillPresentationKey, int>();
        var main = CreateRow(16330020, 60361, 7);
        var delayed = CreateRow(16330027, 23791, 6);

        SkillDetailPresentationAggregator.AddOrMerge(rows, indexes, in main);
        SkillDetailPresentationAggregator.AddOrMerge(rows, indexes, in delayed);

        var row = Assert.Single(rows);
        Assert.Equal(84152, row.TotalAmount);
        Assert.Equal(13, row.Hits);
        Assert.Equal(16330000, row.SkillCode);
    }

    [Theory]
    [InlineData(11010047, 11010000)]
    [InlineData(16330027, 16330000)]
    [InlineData(19010047, 19010000)]
    public void FromActionKey_Uses_Generated_Presentation_Relations(int skillCode, int expectedPresentationSkillCode)
    {
        SetSkillDisplayProjections(
            new SkillDisplayProjection(11010047, 11010000, 11010040, 11010000, 0b01000, 7, false),
            new SkillDisplayProjection(16330027, 16330000, 16330020, 16330000, 0, 7, false),
            new SkillDisplayProjection(19010047, 19010000, 19010040, 19010000, 0b01000, 7, false));

        var key = SkillPresentationKey.FromActionKey(new CombatActionKey(skillCode, default, default));

        Assert.Equal(expectedPresentationSkillCode, key.SkillCode);
    }

    [Theory]
    [InlineData(1218810)]
    [InlineData(11000001)]
    [InlineData(16000047)]
    [InlineData(10_010_047)]
    [InlineData(20_010_047)]
    public void FromActionKey_Preserves_Skills_Without_Presentation_Relation(int skillCode)
    {
        SetSkillDisplayProjections();

        var key = SkillPresentationKey.FromActionKey(new CombatActionKey(skillCode, default, default));

        Assert.Equal(skillCode, key.SkillCode);
    }

    [Fact]
    public void FromActionKey_Preserves_Effect_Reference_Identity()
    {
        SetSkillDisplayProjections();

        var actionKey = new CombatActionKey(0, ResourceEffectRef.FromRaw(1234), ResourceEffectRef.FromRaw(5678));

        var key = SkillPresentationKey.FromActionKey(actionKey);

        Assert.Equal(actionKey, key.ActionKey);
    }

    private static SkillDetailRowData CreateRow(int skillCode, long amount, int hits)
    {
        var presentationKey = SkillPresentationKey.FromActionKey(new CombatActionKey(skillCode, default, default));
        return new SkillDetailRowData
        {
            PresentationKey = presentationKey,
            SkillCode = presentationKey.SkillCode,
            DisplayName = presentationKey.SkillCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TotalAmount = amount,
            DirectAmount = amount,
            Hits = hits,
            Attempts = hits
        };
    }

    private static void SetSkillDisplayProjections(params SkillDisplayProjection[] presentations)
    {
        CombatResourceRegistry.SetGameResources(
            [],
            new Dictionary<int, NpcDisplayEntry>(),
            presentations.ToDictionary(static presentation => presentation.SkillCode));
    }
}
