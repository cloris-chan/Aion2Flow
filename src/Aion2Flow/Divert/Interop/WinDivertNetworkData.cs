using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.Divert.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct WinDivertNetworkData
{
    public uint IfIdx;
    public uint SubIfIdx;
}
