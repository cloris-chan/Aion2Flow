using System.Runtime.CompilerServices;

namespace Cloris.Aion2Flow.PacketCapture.Streams;

public readonly record struct TcpConnection(uint SourceAddress, uint DestinationAddress, ushort SourcePort, ushort DestinationPort)
{
    public bool IsLoopback => IsLoopbackAddress(SourceAddress) || IsLoopbackAddress(DestinationAddress);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsSameConnection(in TcpConnection other, out bool isReversed)
    {
        if (SourceAddress == other.SourceAddress && DestinationAddress == other.DestinationAddress && SourcePort == other.SourcePort && DestinationPort == other.DestinationPort)
        {
            isReversed = false;
            return true;
        }
        if (SourceAddress == other.DestinationAddress && DestinationAddress == other.SourceAddress && SourcePort == other.DestinationPort && DestinationPort == other.SourcePort)
        {
            isReversed = true;
            return true;
        }
        isReversed = false;
        return false;
    }

    private static bool IsLoopbackAddress(uint address)
    {
        return (address & 0xffu) == 127u || ((address >> 24) & 0xffu) == 127u;
    }
}
