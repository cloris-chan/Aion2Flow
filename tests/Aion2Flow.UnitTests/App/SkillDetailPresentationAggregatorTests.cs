using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class SkillDetailPresentationAggregatorTests
{
    [Fact]
    public void AddOrMerge_Merges_Standard_Class_Skill_Family()
    {
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
    [InlineData(16990002, 16990000)]
    [InlineData(18091243, 18090000)]
    [InlineData(1218810, 1218810)]
    [InlineData(11000001, 11000001)]
    [InlineData(16000047, 16000047)]
    [InlineData(10_010_047, 10_010_047)]
    [InlineData(19_010_047, 19_010_047)]
    public void FromActionKey_Normalizes_Only_Standard_Class_Skill_Families(int skillCode, int expectedPresentationSkillCode)
    {
        var key = SkillPresentationKey.FromActionKey(new CombatActionKey(skillCode, default, default));

        Assert.Equal(expectedPresentationSkillCode, key.SkillCode);
    }

    [Fact]
    public void FromActionKey_Preserves_Effect_Reference_Identity()
    {
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
}
