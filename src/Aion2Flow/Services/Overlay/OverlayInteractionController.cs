namespace Cloris.Aion2Flow.Services.Overlay;

public sealed class OverlayInteractionController
{
    public event Action<OverlayInteractionMode>? ModeChanged;

    public OverlayInteractionMode Mode { get; private set; }

    public void Cycle() => SetMode(Mode switch
    {
        OverlayInteractionMode.Interactive => OverlayInteractionMode.ClickThrough,
        OverlayInteractionMode.ClickThrough => OverlayInteractionMode.Hidden,
        OverlayInteractionMode.Hidden => OverlayInteractionMode.Interactive,
        _ => throw new InvalidOperationException($"Unsupported overlay interaction mode: {Mode}")
    });

    internal void SetMode(OverlayInteractionMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        ModeChanged?.Invoke(Mode);
    }
}
