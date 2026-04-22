using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.WinDivert;

[StructLayout(LayoutKind.Sequential)]
public struct WinDivertReflectData
{
    public long Timestamp;
    public uint ProcessId;
    public byte Layer;
    public ulong Flags;
    public short Priority;
}
