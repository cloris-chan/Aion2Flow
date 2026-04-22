using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.WinDivert;

[StructLayout(LayoutKind.Sequential)]
public struct WinDivertSocketData
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
