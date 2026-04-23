using System.Runtime.CompilerServices;

namespace Cloris.Aion2Flow.PacketCapture.Streams;

public readonly record struct TcpConnection(uint SourceAddress, uint DestinationAddress, ushort SourcePort, ushort DestinationPort)
{
    public bool SourceIsLocal => IsLocalNetworkAddress(SourceAddress);

    public bool DestinationIsLocal => IsLocalNetworkAddress(DestinationAddress);

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

    private static bool IsLocalNetworkAddress(uint address) => (address & 0xFF) switch
    {
        127 or 10 => true,
        172 => (address & 0xF0FF) == 0x10AC,
        192 => (address & 0xFFFF) == 0xA8C0,
        _ => false,
    };
}
