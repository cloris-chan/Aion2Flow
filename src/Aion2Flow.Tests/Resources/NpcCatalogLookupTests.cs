using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Tests.Resources;

public sealed class NpcCatalogLookupTests
{
    [Theory]
    [InlineData(2980179)]
    [InlineData(2930820)]
    [InlineData(2931316)]
    [InlineData(2930817)]
    [InlineData(2920823)]
    [InlineData(2920821)]
    [InlineData(2400032)]
    [InlineData(2980159)]
    [InlineData(2980049)]
    public void NpcCatalog_Contains_Session_NpcCodes(int npcCode)
    {
        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        Assert.True(catalog.ContainsKey(npcCode), $"NPC code {npcCode} not found in catalog");
        Assert.False(string.IsNullOrWhiteSpace(catalog[npcCode].Name), $"NPC code {npcCode} has no name");
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ko-KR")]
    [InlineData("zh-TW")]
    public void NpcCatalog_Contains_2980179_In_All_Languages(string lang)
    {
        var catalog = ResourceDatabase.LoadNpcCatalog(lang);
        Assert.True(catalog.ContainsKey(2980179), $"NPC code 2980179 not found in {lang} catalog");
        Assert.False(string.IsNullOrWhiteSpace(catalog[2980179].Name), $"NPC code 2980179 has no name in {lang}");
    }
}
