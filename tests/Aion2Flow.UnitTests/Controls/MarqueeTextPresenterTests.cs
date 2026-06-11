using Cloris.Aion2Flow.Controls;

namespace Cloris.Aion2Flow.UnitTests.Controls;

public sealed class MarqueeTextPresenterTests
{
    [Fact]
    public void CycleWithoutOverflowRemainsStatic()
    {
        var state = MarqueeAnimationCycle.Resolve(10_000, 0);

        Assert.Equal(0, state.Offset);
        Assert.Equal(1, state.Opacity);
    }

    [Fact]
    public void CycleScrollsLinearlyToOverflowDistance()
    {
        const double distance = 64;
        var scrollDuration = MarqueeAnimationCycle.ResolveScrollDuration(distance);
        var state = MarqueeAnimationCycle.Resolve(
            MarqueeAnimationCycle.StartHoldMilliseconds + scrollDuration / 2,
            distance);

        Assert.Equal(-32, state.Offset, 6);
        Assert.Equal(1, state.Opacity);
    }

    [Fact]
    public void CycleFadesAtEndThenResetsAtStart()
    {
        const double distance = 96;
        var scrollDuration = MarqueeAnimationCycle.ResolveScrollDuration(distance);
        var fadeOut = MarqueeAnimationCycle.Resolve(
            MarqueeAnimationCycle.StartHoldMilliseconds +
            scrollDuration +
            MarqueeAnimationCycle.EndHoldMilliseconds +
            MarqueeAnimationCycle.FadeOutMilliseconds / 2,
            distance);
        var fadeIn = MarqueeAnimationCycle.Resolve(
            MarqueeAnimationCycle.StartHoldMilliseconds +
            scrollDuration +
            MarqueeAnimationCycle.EndHoldMilliseconds +
            MarqueeAnimationCycle.FadeOutMilliseconds +
            MarqueeAnimationCycle.FadeInMilliseconds / 2,
            distance);

        Assert.Equal(-distance, fadeOut.Offset);
        Assert.Equal(0.5, fadeOut.Opacity, 6);
        Assert.Equal(0, fadeIn.Offset);
        Assert.Equal(0.5, fadeIn.Opacity, 6);
    }
}
