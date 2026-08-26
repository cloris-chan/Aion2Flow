using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class SkillMonitorSkillSlotBuilderTests
{
    [Fact]
    public void Build_MergesLocalBuffAndActiveCooldownByRowBaseSkillId()
    {
        var auras = new[]
        {
            new SkillMonitorAuraCandidate(7, 13_050_000, 10_000, 8_000),
            new SkillMonitorAuraCandidate(7, 13_050_000, 10_000, 9_000),
            new SkillMonitorAuraCandidate(8, 13_050_000, 10_000, 15_000),
        };
        var cooldowns = new[]
        {
            new PacketCooldownState(13_050_000, 13_050_040, 7, 12_000, 2_000, PacketCooldownTransition.Observed, null, 0x0238),
            new PacketCooldownState(13_130_000, 13_130_040, 7, 0, 2_000, PacketCooldownTransition.Ready, null, 0x4738),
        };

        var builder = new SkillMonitorSkillSlotBuilder();
        var slots = builder.Build(auras, 7, cooldowns, static _ => true, 3_000);

        Assert.Equal(1, slots.Length);
        var slot = slots[0];
        Assert.Equal(13_050_000, slot.RowBaseSkillId);
        Assert.Equal(6_000, slot.BuffTimer!.Value.RemainingMilliseconds);
        Assert.Equal(10_000, slot.BuffTimer!.Value.ReferenceMilliseconds);
        Assert.Equal(11_000, slot.CooldownTimer!.Value.RemainingMilliseconds);
        Assert.Equal(12_000, slot.CooldownTimer!.Value.ReferenceMilliseconds);
    }

    [Fact]
    public void Build_KeepsIndefiniteLocalBuffAndExcludesOtherTargets()
    {
        var auras = new[]
        {
            new SkillMonitorAuraCandidate(7, 13_050_000, 10_000, 8_000),
            new SkillMonitorAuraCandidate(7, 13_050_000, ushort.MaxValue, null),
            new SkillMonitorAuraCandidate(8, 13_130_000, ushort.MaxValue, null),
        };

        var builder = new SkillMonitorSkillSlotBuilder();
        var slots = builder.Build(auras, 7, [], static _ => true, 3_000);

        Assert.Equal(1, slots.Length);
        var slot = slots[0];
        Assert.Equal(13_050_000, slot.RowBaseSkillId);
        Assert.True(slot.BuffTimer!.Value.IsIndefinite);
        Assert.Null(slot.CooldownTimer);
    }

    [Fact]
    public void Build_PreservesCooldownCycleDurationAcross4738Updates()
    {
        var cooldowns = new[]
        {
            new PacketCooldownState(
                13_050_000,
                13_050_040,
                0,
                3_000,
                7_000,
                PacketCooldownTransition.ResetOrReduced,
                null,
                0x4738,
                10_000),
        };

        var builder = new SkillMonitorSkillSlotBuilder();
        var slots = builder.Build([], 0, cooldowns, static _ => true, 7_000);
        Assert.Equal(1, slots.Length);
        var slot = slots[0];

        Assert.Equal(3_000, slot.CooldownTimer!.Value.RemainingMilliseconds);
        Assert.Equal(10_000, slot.CooldownTimer!.Value.ReferenceMilliseconds);
        Assert.Equal(0.3d, slot.CooldownTimer!.Value.ProgressValue, 3);
    }

    [Fact]
    public void Build_ExcludesReadyChargeWithoutActiveCooldown()
    {
        var cooldowns = new[]
        {
            new PacketCooldownState(
                13_050_000,
                13_050_040,
                0,
                0,
                7_000,
                PacketCooldownTransition.Ready,
                3,
                0x2238),
        };

        var builder = new SkillMonitorSkillSlotBuilder();
        var slots = builder.Build([], 0, cooldowns, static _ => true, 7_000);

        Assert.Equal(0, slots.Length);
    }

    [Fact]
    public void Build_PreservesReadyChargeCountWhenBuffIsActive()
    {
        var auras = new[]
        {
            new SkillMonitorAuraCandidate(7, 13_050_000, 10_000, 15_000)
        };
        var cooldowns = new[]
        {
            new PacketCooldownState(
                13_050_000,
                13_050_040,
                0,
                0,
                7_000,
                PacketCooldownTransition.Ready,
                3,
                0x2238),
        };

        var builder = new SkillMonitorSkillSlotBuilder();
        var slots = builder.Build(auras, 7, cooldowns, static _ => true, 7_000);
        Assert.Equal(1, slots.Length);
        var slot = slots[0];

        Assert.NotNull(slot.BuffTimer);
        Assert.Null(slot.CooldownTimer);
        Assert.Equal(3, slot.AvailableCount);
    }

    [Fact]
    public void Build_ExcludesCooldownRejectedBySelection()
    {
        var cooldowns = new[]
        {
            new PacketCooldownState(
                13_050_000,
                13_050_040,
                0,
                5_000,
                7_000,
                PacketCooldownTransition.Observed,
                null,
                0x0238),
        };

        var builder = new SkillMonitorSkillSlotBuilder();
        var slots = builder.Build([], 0, cooldowns, static _ => false, 7_000);

        Assert.Equal(0, slots.Length);
    }
}
