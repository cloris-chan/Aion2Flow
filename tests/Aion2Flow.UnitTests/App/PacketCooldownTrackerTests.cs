using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class PacketCooldownTrackerTests
{
    [Fact]
    public void ObserveStart0238_RequiresKnownMatchingLocalPlayer()
    {
        var tracker = new PacketCooldownTracker();

        Assert.False(tracker.ObserveStart0238(0, 5_379, 13_050_000, 13_050_040, 10_000, 1_000));
        Assert.False(tracker.ObserveStart0238(5_379, 18_990, 13_050_000, 13_050_040, 10_000, 1_000));
        Assert.Empty(tracker.States);

        Assert.True(tracker.ObserveStart0238(5_379, 5_379, 13_050_000, 13_050_040, 10_000, 1_000));
        var state = Assert.Single(tracker.States);
        Assert.Equal(13_050_000, state.RowBaseSkillId);
        Assert.Equal(5_379, state.SourceEntityId);
        Assert.Equal((ushort)0x0238, state.EvidenceOpcode);
    }

    [Fact]
    public void ObserveUpdate4738_CreatesEntryWithoutLocalPlayerIdentity()
    {
        var tracker = new PacketCooldownTracker();

        Assert.True(tracker.ObserveUpdate4738(13_130_000, 13_130_000, 5_850, 1_000));

        var state = Assert.Single(tracker.States);
        Assert.Equal(13_130_000, state.RowBaseSkillId);
        Assert.Equal(5_850, state.RemainingMilliseconds);
        Assert.Equal(5_850, state.CycleDurationMilliseconds);
        Assert.Equal(0, state.SourceEntityId);
        Assert.Equal(PacketCooldownTransition.Observed, state.Transition);
        Assert.Equal((ushort)0x4738, state.EvidenceOpcode);
    }

    [Fact]
    public void ObserveUpdate4738_Updates0238EntryWithSameRowBaseSkillId()
    {
        var tracker = new PacketCooldownTracker();
        Assert.True(tracker.ObserveStart0238(5_379, 5_379, 13_050_000, 13_050_040, 10_000, 1_000));

        Assert.True(tracker.ObserveUpdate4738(13_050_000, 13_050_000, 5_800, 2_000));

        var state = Assert.Single(tracker.States);
        Assert.Equal(13_050_000, state.RowBaseSkillId);
        Assert.Equal(5_800, state.RemainingMilliseconds);
        Assert.Equal(10_000, state.CycleDurationMilliseconds);
        Assert.Equal(PacketCooldownTransition.ResetOrReduced, state.Transition);
        Assert.Equal((ushort)0x4738, state.EvidenceOpcode);
    }

    [Fact]
    public void ObserveUpdate4738_StartsNextRechargeCycleInSameEntry()
    {
        var tracker = new PacketCooldownTracker();
        Assert.True(tracker.ObserveUpdate4738(13_130_000, 13_130_000, 0, 1_000));

        Assert.True(tracker.ObserveUpdate4738(13_130_000, 13_130_000, 5_850, 1_100));

        var state = Assert.Single(tracker.States);
        Assert.Equal(5_850, state.RemainingMilliseconds);
        Assert.Equal(5_850, state.CycleDurationMilliseconds);
        Assert.Equal(PacketCooldownTransition.Refresh, state.Transition);
    }

    [Fact]
    public void ObserveCharge2238_TracksSequentialRechargeInSameEntry()
    {
        var tracker = new PacketCooldownTracker();
        Assert.True(tracker.ObserveStart0238(9_150, 9_150, 13_050_000, 13_050_240, 7_450, 1_000));
        Assert.True(tracker.ObserveStart0238(9_150, 9_150, 13_050_000, 13_050_240, 6_500, 1_952));

        var naturalUpdate = Assert.Single(tracker.States);
        Assert.Equal(PacketCooldownTransition.NaturalDecay, naturalUpdate.Transition);

        Assert.True(tracker.ObserveCharge2238(13_050_000, 13_050_240, 1, 7_450, 8_466));

        var firstRecharge = Assert.Single(tracker.States);
        Assert.Equal(1, firstRecharge.AvailableCount);
        Assert.Equal(7_450, firstRecharge.RemainingMilliseconds);
        Assert.Equal(PacketCooldownTransition.Refresh, firstRecharge.Transition);
        Assert.Equal((ushort)0x2238, firstRecharge.EvidenceOpcode);

        Assert.True(tracker.ObserveCharge2238(13_050_000, 13_050_240, 2, 0, 15_890));

        var full = Assert.Single(tracker.States);
        Assert.Equal(2, full.AvailableCount);
        Assert.Equal(0, full.RemainingMilliseconds);
        Assert.Equal(0, full.CycleDurationMilliseconds);
        Assert.Equal(PacketCooldownTransition.Ready, full.Transition);
    }

    [Fact]
    public void ObserveUpdate4738_PreservesChargeCountFrom2238()
    {
        var tracker = new PacketCooldownTracker();
        Assert.True(tracker.ObserveCharge2238(13_050_000, 13_050_240, 1, 7_450, 1_000));

        Assert.True(tracker.ObserveUpdate4738(13_050_000, 13_050_000, 6_500, 1_950));

        var state = Assert.Single(tracker.States);
        Assert.Equal(1, state.AvailableCount);
        Assert.Equal(6_500, state.RemainingMilliseconds);
        Assert.Equal(7_450, state.CycleDurationMilliseconds);
        Assert.Equal((ushort)0x4738, state.EvidenceOpcode);
    }

    [Fact]
    public void ObserveStart0238_PreservesKnownCharge()
    {
        var tracker = new PacketCooldownTracker();
        Assert.True(tracker.ObserveCharge2238(13_050_000, 13_050_240, 3, 0, 1_000));

        Assert.True(tracker.ObserveStart0238(5_379, 5_379, 13_050_000, 13_050_240, 7_450, 1_100));

        var state = Assert.Single(tracker.States);
        Assert.Equal(3, state.AvailableCount);
        Assert.Equal(7_450, state.RemainingMilliseconds);
        Assert.Equal((ushort)0x0238, state.EvidenceOpcode);
    }

    [Fact]
    public void ObserveStart0238_DoesNotConsumeChargeForDuplicateUpdates()
    {
        var tracker = new PacketCooldownTracker();
        Assert.True(tracker.ObserveCharge2238(13_050_000, 13_050_240, 3, 0, 1_000));

        Assert.True(tracker.ObserveStart0238(5_379, 5_379, 13_050_000, 13_050_240, 7_450, 1_100));
        Assert.True(tracker.ObserveStart0238(5_379, 5_379, 13_050_000, 13_050_240, 7_450, 1_101));

        var state = Assert.Single(tracker.States);
        Assert.Equal(3, state.AvailableCount);
    }

    [Fact]
    public void ObserveStart0238_InitializesAvailableCountFromSkillMetadata()
    {
        var tracker = new PacketCooldownTracker();

        Assert.True(tracker.ObserveStart0238(5_379, 5_379, 13_050_000, 13_050_240, 7_450, 1_000, 3));

        var state = Assert.Single(tracker.States);
        Assert.Equal(3, state.AvailableCount);
    }

    [Fact]
    public void ObserveStart0238_UsesPacketChargeCount()
    {
        var tracker = new PacketCooldownTracker();

        Assert.True(tracker.ObserveStart0238(5_379, 5_379, 13_050_000, 13_050_240, 7_450, 1_000, 3, 2));

        var state = Assert.Single(tracker.States);
        Assert.Equal(2, state.AvailableCount);
        Assert.Equal(7_450, state.RemainingMilliseconds);
        Assert.Equal(7_450, state.CycleDurationMilliseconds);
    }

    [Fact]
    public void ObserveControl0238_ConsumesEveryUseWithoutResultDependency()
    {
        var tracker = new PacketCooldownTracker();

        Assert.True(tracker.ObserveControl0238(13_050_000, 13_050_240, 5_379, 1_000, 3));
        Assert.Equal(2, Assert.Single(tracker.States).AvailableCount);

        Assert.True(tracker.ObserveControl0238(13_050_000, 13_050_240, 5_379, 1_100, 3));
        Assert.Equal(1, Assert.Single(tracker.States).AvailableCount);

        Assert.True(tracker.ObserveControl0238(13_050_000, 13_050_240, 5_379, 1_200, 3));
        var state = Assert.Single(tracker.States);
        Assert.Equal(0, state.AvailableCount);
        Assert.Equal((ushort)0x0238, state.EvidenceOpcode);
    }

    [Fact]
    public void ObserveCharge2238_ReconcilesServerCountAfterControls()
    {
        var tracker = new PacketCooldownTracker();

        Assert.True(tracker.ObserveCharge2238(13_050_000, 13_050_240, 3, 0, 1_000));
        Assert.True(tracker.ObserveControl0238(13_050_000, 13_050_240, 5_379, 1_100, 3));
        Assert.True(tracker.ObserveControl0238(13_050_000, 13_050_240, 5_379, 1_200, 3));
        Assert.True(tracker.ObserveControl0238(13_050_000, 13_050_240, 5_379, 1_300, 3));
        Assert.Equal(0, Assert.Single(tracker.States).AvailableCount);

        Assert.True(tracker.ObserveCharge2238(13_050_000, 13_050_240, 2, 7_450, 2_000));
        var state = Assert.Single(tracker.States);
        Assert.Equal(2, state.AvailableCount);
        Assert.Equal(7_450, state.RemainingMilliseconds);
        Assert.Equal((ushort)0x2238, state.EvidenceOpcode);
    }

    [Fact]
    public void ObserveUpdate4738_PreservesExistingCooldownCycleDuration()
    {
        var tracker = new PacketCooldownTracker();
        Assert.True(tracker.ObserveStart0238(5_379, 5_379, 13_050_000, 13_050_040, 10_000, 1_000));

        Assert.True(tracker.ObserveUpdate4738(13_050_000, 13_050_000, 3_000, 7_000));

        var state = Assert.Single(tracker.States);
        Assert.Equal(3_000, state.RemainingMilliseconds);
        Assert.Equal(10_000, state.CycleDurationMilliseconds);
    }
}
