namespace Cloris.Aion2Flow.Services.Overlay;

internal enum OverlayWindowInputState
{
    Interactive,
    ClickThroughArmed,
    ClickThroughActive,
    Hidden
}

internal static class OverlayWindowInputStateLogic
{
    public static OverlayWindowInputState EnterMode(OverlayInteractionMode mode, bool isPointerInside) => mode switch
    {
        OverlayInteractionMode.Interactive => OverlayWindowInputState.Interactive,
        OverlayInteractionMode.ClickThrough when isPointerInside => OverlayWindowInputState.ClickThroughActive,
        OverlayInteractionMode.ClickThrough => OverlayWindowInputState.ClickThroughArmed,
        OverlayInteractionMode.Hidden => OverlayWindowInputState.Hidden,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    public static bool RequiresInputTransparency(this OverlayWindowInputState state) => state switch
    {
        OverlayWindowInputState.Interactive => false,
        OverlayWindowInputState.ClickThroughArmed => false,
        OverlayWindowInputState.ClickThroughActive => true,
        OverlayWindowInputState.Hidden => true,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    public static bool ShouldPollCursor(this OverlayWindowInputState state) => state == OverlayWindowInputState.ClickThroughActive;
}
