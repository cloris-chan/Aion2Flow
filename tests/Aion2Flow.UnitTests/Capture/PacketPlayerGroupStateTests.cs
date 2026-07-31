using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketPlayerGroupStateTests
{
    [Fact]
    public void Reset_AllowsGroupStatusToBeObservedInTheNextMapScope()
    {
        var state = new PacketPlayerGroupState();

        Assert.True(state.TryRegisterPartyStatusMember(101));
        Assert.True(state.TryRegisterForceStatusMember(202));
        Assert.False(state.TryRegisterPartyStatusMember(101));
        Assert.False(state.TryRegisterForceStatusMember(202));

        state.Reset();

        Assert.True(state.TryRegisterPartyStatusMember(101));
        Assert.True(state.TryRegisterForceStatusMember(202));
    }
}
