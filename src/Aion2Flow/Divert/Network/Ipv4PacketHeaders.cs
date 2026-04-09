using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.Divert.Network;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IPv4Header
{
    public byte VersionAndIHL;
    public byte DscpAndEcn;
    public ushort TotalLength;
    public ushort Identification;
    public ushort FlagsAndFragmentOffset;
    public byte TimeToLive;
    public IPv4Protocol Protocol;
    public ushort HeaderChecksum;
    public uint SourceAddress;
    public uint DestinationAddress;

    private const ushort LeMask_IsFragmented = 0xFF3F;
    private const ushort LeMask_MF = 0x0020;
    private const ushort LeMask_Offset = 0xFF1F;

    public readonly bool IsFragmented => (FlagsAndFragmentOffset & LeMask_IsFragmented) != 0;
    public readonly bool IsFirstFragment => IsFragmented && (FlagsAndFragmentOffset & LeMask_Offset) == 0;
    public readonly byte Version => (byte)(VersionAndIHL >> 4);
    public readonly byte IHL => (byte)(VersionAndIHL & 0x0F);
    public readonly int HeaderLength => IHL * 4;
}
