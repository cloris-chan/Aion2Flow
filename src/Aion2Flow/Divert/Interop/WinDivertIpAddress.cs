using System.Net;
using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.Divert.Interop;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WinDivertIpAddress
{
    private fixed uint _words[4];

    public readonly bool IsZero
    {
        get
        {
            fixed (uint* p = _words)
            {
                return p[0] == 0 && p[1] == 0 && p[2] == 0 && p[3] == 0;
            }
        }
    }

    public readonly ReadOnlySpan<uint> AsUInt32Span()
    {
        fixed (uint* p = _words)
        {
            return new ReadOnlySpan<uint>(p, 4);
        }
    }

    public readonly IPAddress ToIPAddress(bool ipv6)
    {
        fixed (uint* p = _words)
        {
            if (!ipv6)
            {
                return new IPAddress(p[0]);
            }

            Span<byte> bytes = stackalloc byte[16];
            MemoryMarshal.AsBytes(new ReadOnlySpan<uint>(p, 4)).CopyTo(bytes);
            return new IPAddress(bytes);
        }
    }
}
