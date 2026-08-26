using Cloris.Aion2Flow.Presentation;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class SkillMonitorTimeFormatterTests
{
    [Theory]
    [InlineData(0L, "0.0s")]
    [InlineData(68_340L, "68.3s")]
    [InlineData(68_360L, "68.4s")]
    public void DecimalSeconds_MatchesTheSettingsFormat(long milliseconds, string expected)
    {
        var text = SkillMonitorTimeFormatter.Format(
            milliseconds,
            EncounterTimeDisplayFormat.DecimalSeconds);

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(0L, "00:00")]
    [InlineData(59_999L, "00:59")]
    [InlineData(68_999L, "01:08")]
    [InlineData(3_601_000L, "60:01")]
    public void MinutesSeconds_MatchesTheSettingsFormat(long milliseconds, string expected)
    {
        var text = SkillMonitorTimeFormatter.Format(
            milliseconds,
            EncounterTimeDisplayFormat.MinutesSeconds);

        Assert.Equal(expected, text);
    }
}
