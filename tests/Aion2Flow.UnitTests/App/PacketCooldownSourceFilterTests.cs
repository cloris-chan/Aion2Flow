using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class PacketCooldownSourceFilterTests
{
    [Fact]
    public void Matches_NoPacketSource_WhenLocalPlayerIsUnknown()
    {
        Assert.False(PacketCooldownSourceFilter.MatchesKnownLocalPlayer(0, 5_379));
        Assert.False(PacketCooldownSourceFilter.MatchesKnownLocalPlayer(0, 18_990));
        Assert.False(PacketCooldownSourceFilter.MatchesKnownLocalPlayer(0, 0));
    }

    [Fact]
    public void Matches_OnlyLocalPlayer_WhenIdentityIsKnown()
    {
        Assert.True(PacketCooldownSourceFilter.MatchesKnownLocalPlayer(5_379, 5_379));
        Assert.False(PacketCooldownSourceFilter.MatchesKnownLocalPlayer(5_379, 18_990));
    }
}
