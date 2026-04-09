using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.Divert.Interop;

[SkipLocalsInit]
internal static partial class WinDivertInterop
{
    private const string LibraryName = "WinDivert.dll";

    [LibraryImport(LibraryName, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint WinDivertOpen(string filter, WinDivertLayer layer, short priority, WinDivertFlags flags);

    [LibraryImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool WinDivertRecv(WinDivertSafeHandle handle, void* pPacket, uint packetLen, uint* pRecvLen, WinDivertAddress* pAddr);

    public static unsafe bool WinDivertRecv(WinDivertSafeHandle handle, Span<byte> buffer, out uint recviedLength, ref WinDivertAddress address)
    {
        uint recvLen = 0;

        fixed (byte* pPacket = buffer)
        fixed (WinDivertAddress* pAddr = &address)
        {
            if (WinDivertRecv(handle, pPacket, (uint)buffer.Length, &recvLen, pAddr))
            {
                recviedLength = recvLen;
                return true;
            }
        }
        recviedLength = 0;
        return false;
    }

    [LibraryImport(LibraryName, EntryPoint = "WinDivertClose", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinDivertClose(nint handle);
}
