using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class SkillDetailPresentationAggregatorTests
{
    [Fact]
    public void AddOrMerge_Merges_Identical_Display_Identity_Without_Changing_Core_Action_Identity()
    {
        var rows = new List<SkillDetailRowData>();
        var indexes = new Dictionary<SkillPresentationKey, int>();
        var presentationKey = new SkillPresentationKey("元素融合", "ICON_EL_SKILL_030.webp");
        var main = CreateRow(presentationKey, 16300243, 140141, 2);
        var derived = CreateRow(presentationKey, 16300027, 11588, 1);

        SkillDetailPresentationAggregator.AddOrMerge(rows, indexes, in main);
        SkillDetailPresentationAggregator.AddOrMerge(rows, indexes, in derived);

        var row = Assert.Single(rows);
        Assert.Equal(151729, row.TotalAmount);
        Assert.Equal(3, row.Hits);
        Assert.Equal(16300027, row.SkillCode);
        Assert.Equal(16300027, row.ActionKey.SkillCode);
    }

    [Fact]
    public void AddOrMerge_Keeps_Different_Display_Names_Separate()
    {
        var rows = new List<SkillDetailRowData>();
        var indexes = new Dictionary<SkillPresentationKey, int>();
        var main = CreateRow(new SkillPresentationKey("空間支配", "ICON_EL_SKILL_033.webp"), 16330020, 60361, 7);
        var delayed = CreateRow(new SkillPresentationKey("空間支配 - 延遲傷害", "ICON_EL_SKILL_033.webp"), 16330027, 23791, 6);

        SkillDetailPresentationAggregator.AddOrMerge(rows, indexes, in main);
        SkillDetailPresentationAggregator.AddOrMerge(rows, indexes, in delayed);

        Assert.Equal(2, rows.Count);
    }

    private static SkillDetailRowData CreateRow(SkillPresentationKey presentationKey, int skillCode, long amount, int hits)
        => new()
        {
            PresentationKey = presentationKey,
            ActionKey = new CombatActionKey(skillCode, default, default),
            SkillCode = skillCode,
            DisplayName = presentationKey.DisplayName,
            TotalAmount = amount,
            DirectAmount = amount,
            Hits = hits,
            Attempts = hits
        };
}
