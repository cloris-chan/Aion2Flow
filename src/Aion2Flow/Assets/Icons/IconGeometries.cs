using Avalonia.Controls;
using Avalonia.Media;

namespace Cloris.Aion2Flow.Assets.Icons;

// Lucide outline icons (https://lucide.dev, ISC license).
// All paths normalized to a 24x24 bounding box via the leading "M0 0M24 24" prefix
internal sealed class IconGeometries : ResourceDictionary
{
    private const string Bbox = "M0 0M24 24";

    public IconGeometries()
    {
        Add("rotate-cw", Geometry($"M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8M21 3v5h-5"));
        Add("minus", Geometry("M5 12h14"));
        Add("x", Geometry("M18 6 6 18M6 6l12 12"));
        Add("wifi", Geometry("M12 20h.01M2 8.82a15 15 0 0 1 20 0M5 12.859a10 10 0 0 1 14 0M8.5 16.429a5 5 0 0 1 7 0"));
        Add("radio", Geometry("M4.9 19.1C1 15.2 1 8.8 4.9 4.9M7.8 16.2c-2.3-2.3-2.3-6.1 0-8.4M16.2 7.8c2.3 2.3 2.3 6.1 0 8.4M19.1 4.9C23 8.8 23 15.1 19.1 19M14 12a2 2 0 1 1-4 0 2 2 0 0 1 4 0z"));
        Add("history", Geometry("M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8M3 3v5h5M12 7v5l4 2"));
        Add("globe", Geometry("M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zM2 12h20M12 2a15.3 15.3 0 0 1 0 20M12 2a15.3 15.3 0 0 0 0 20"));
    }

    private static StreamGeometry Geometry(string data) => StreamGeometry.Parse(Bbox + data);
}
