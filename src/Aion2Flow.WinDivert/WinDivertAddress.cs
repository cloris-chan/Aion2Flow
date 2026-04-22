using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.WinDivert;

[StructLayout(LayoutKind.Explicit, Size = 80)]
public unsafe ref struct WinDivertAddress
{
    [FieldOffset(0)]
    public long Timestamp;

    [FieldOffset(8)]
    private ulong _flags;

    [FieldOffset(16)]
    private fixed ulong _union[8];

    private const int LayerShift = 0;
    private const int EventShift = 8;

    private const ulong LayerMask = 0xFFUL << LayerShift;
    private const ulong EventMask = 0xFFUL << EventShift;

    private const ulong SniffedBitMask = 1UL << 16;
    private const ulong OutboundBitMask = 1UL << 17;
    private const ulong LoopbackBitMask = 1UL << 18;
    private const ulong ImpostorBitMask = 1UL << 19;
    private const ulong IPv6BitMask = 1UL << 20;
    private const ulong IPChecksumBitMask = 1UL << 21;
    private const ulong TCPChecksumBitMask = 1UL << 22;
    private const ulong UDPChecksumBitMask = 1UL << 23;

    public WinDivertLayer Layer
    {
        readonly get => (WinDivertLayer)((_flags & LayerMask) >> LayerShift);
        set => _flags = (_flags & ~LayerMask) | ((ulong)value << LayerShift);
    }

    public WinDivertEvent Event
    {
        readonly get => (WinDivertEvent)((_flags & EventMask) >> EventShift);
        set => _flags = (_flags & ~EventMask) | ((ulong)value << EventShift);
    }

    public bool Sniffed
    {
        readonly get => (_flags & SniffedBitMask) != 0;
        set => _flags = value ? _flags | SniffedBitMask : _flags & ~SniffedBitMask;
    }

    public bool Outbound
    {
        readonly get => (_flags & OutboundBitMask) != 0;
        set => _flags = value ? _flags | OutboundBitMask : _flags & ~OutboundBitMask;
    }

    public bool IPv6
    {
        readonly get => (_flags & IPv6BitMask) != 0;
        set => _flags = value ? _flags | IPv6BitMask : _flags & ~IPv6BitMask;
    }

    public bool TryGetNetworkData(out WinDivertNetworkData data)
    {
        if (Layer != WinDivertLayer.Network &&
            Layer != WinDivertLayer.NetworkForward)
        {
            data = default;
            return false;
        }

        fixed (ulong* p = _union)
        {
            data = *(WinDivertNetworkData*)p;
            return true;
        }
    }

    public bool TryGetFlowData(out WinDivertFlowData data)
    {
        if (Layer != WinDivertLayer.Flow)
        {
            data = default;
            return false;
        }

        fixed (ulong* p = _union)
        {
            data = *(WinDivertFlowData*)p;
            return true;
        }
    }

    public bool TryGetSocketData(out WinDivertSocketData data)
    {
        if (Layer != WinDivertLayer.Socket)
        {
            data = default;
            return false;
        }

        fixed (ulong* p = _union)
        {
            data = *(WinDivertSocketData*)p;
            return true;
        }
    }

    public bool TryGetReflectData(out WinDivertReflectData data)
    {
        if (Layer != WinDivertLayer.Reflect)
        {
            data = default;
            return false;
        }

        fixed (ulong* p = _union)
        {
            data = *(WinDivertReflectData*)p;
            return true;
        }
    }
}
