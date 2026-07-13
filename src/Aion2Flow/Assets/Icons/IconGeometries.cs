using Avalonia.Controls;
using Avalonia.Media;

namespace Cloris.Aion2Flow.Assets.Icons;

internal sealed class IconGeometries : ResourceDictionary
{
    private const string Bbox = "M0 0M24 24";
    internal static StreamGeometry Play { get; } = Geometry("M6 3l14 9-14 9V3z");

    public IconGeometries()
    {
        Add("rotate-cw", Geometry($"M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8M21 3v5h-5"));
        Add("minus", Geometry("M5 12h14"));
        Add("x", Geometry("M18 6 6 18M6 6l12 12"));
        Add("wifi", Geometry("M12 20h.01M2 8.82a15 15 0 0 1 20 0M5 12.859a10 10 0 0 1 14 0M8.5 16.429a5 5 0 0 1 7 0"));
        Add("radio", Geometry("M4.9 19.1C1 15.2 1 8.8 4.9 4.9M7.8 16.2c-2.3-2.3-2.3-6.1 0-8.4M16.2 7.8c2.3 2.3 2.3 6.1 0 8.4M19.1 4.9C23 8.8 23 15.1 19.1 19M14 12a2 2 0 1 1-4 0 2 2 0 0 1 4 0z"));
        Add("history", Geometry("M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8M3 3v5h5M12 7v5l4 2"));
        Add("pin", Geometry("M12 17v5M5 17h14M15 3.5a4 4 0 0 0-6 0C7.6 4.4 7 6 7 8c0 2-1 3-2 4h14c-1-1-2-2-2-4 0-2-.6-3.6-2-4.5"));
        Add("globe", Geometry("M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zM2 12h20M12 2a15.3 15.3 0 0 1 0 20M12 2a15.3 15.3 0 0 0 0 20"));
        Add("settings", Geometry("M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1zM15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0z"));
        Add("check", Geometry("M20 6 9 17l-5-5"));
        Add("play", Play);
        Add("pause", Geometry("M6 4h4v16H6zM14 4h4v16h-4z"));
        Add("square", Geometry("M5 5h14v14H5z"));
        Add("chevron-left", Geometry("M15 18l-6-6 6-6"));
        Add("chevron-right", Geometry("M9 18l6-6-6-6"));
        Add("chevron-down", Geometry("M6 9l6 6 6-6"));
        Add("chevron-up", Geometry("M18 15l-6-6-6 6"));
        Add("skip-back", Geometry("M19 20 9 12l10-8v16zM5 19V5"));
        Add("skip-forward", Geometry("M5 4l10 8-10 8V4zM19 5v14"));
    }

    private static StreamGeometry Geometry(string data) => StreamGeometry.Parse(Bbox + data);
}
