using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class SkillMonitorSkillSlotPresentationTrackerTests
{
    [Fact]
    public void CompletedCooldown_IsPresentedForTheWholeClientAnimation()
    {
        var tracker = new SkillMonitorSkillSlotPresentationTracker();
        var active = new[]
        {
            new SkillMonitorSkillSlotState(
                13_050_000,
                null,
                new SkillMonitorTimer(1_000, 1_000, IsIndefinite: false),
                null)
        };

        _ = tracker.Update(active, 1_000);
        var completed = tracker.Update([], 1_100);

        Assert.Equal(1, completed.Length);
        Assert.Equal(1_100, completed[0].CompletionStartedUtcMilliseconds);
        Assert.Null(completed[0].Slot.CooldownTimer);
        Assert.Equal(1, tracker.Update([], 1_416).Length);
        Assert.Equal(0, tracker.Update([], 1_417).Length);
    }

    [Fact]
    public void Completion_PreservesABuffThatIsStillActive()
    {
        var tracker = new SkillMonitorSkillSlotPresentationTracker();
        var active = new[]
        {
            new SkillMonitorSkillSlotState(
                13_050_000,
                new SkillMonitorTimer(4_000, 8_000, IsIndefinite: false),
                new SkillMonitorTimer(1_000, 1_000, IsIndefinite: false),
                null)
        };
        var buffOnly = new[]
        {
            new SkillMonitorSkillSlotState(
                13_050_000,
                new SkillMonitorTimer(3_900, 8_000, IsIndefinite: false),
                null,
                null)
        };

        _ = tracker.Update(active, 1_000);
        var completed = tracker.Update(buffOnly, 1_100);

        Assert.Equal(1, completed.Length);
        Assert.Equal(3_900, completed[0].Slot.BuffTimer!.Value.RemainingMilliseconds);
        Assert.Null(completed[0].Slot.CooldownTimer);
        Assert.Equal(1_100, completed[0].CompletionStartedUtcMilliseconds);
    }
}
