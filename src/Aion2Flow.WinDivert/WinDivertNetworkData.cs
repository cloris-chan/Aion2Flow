using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.WinDivert;

[StructLayout(LayoutKind.Sequential)]
public struct WinDivertNetworkData
{
    public uint IfIdx;
    public uint SubIfIdx;
}
