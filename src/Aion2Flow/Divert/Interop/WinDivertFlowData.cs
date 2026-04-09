using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.Divert.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct WinDivertFlowData
{
    public ulong Endpoint;
    public ulong ParentEndpoint;
    public uint ProcessId;
    public WinDivertIpAddress LocalAddress;
    public WinDivertIpAddress RemoteAddress;
    public ushort LocalPort;
    public ushort RemotePort;
    public byte Protocol;
}
