using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.WinDivert.Network;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TcpHeader
{
    public ushort SourcePort;
    public ushort DestinationPort;
    public uint SequenceNumber;
    public uint AcknowledgmentNumber;
    public byte DataOffsetAndReserved;
    public TcpControlBits Flags;
    public ushort WindowSize;
    public ushort Checksum;
    public ushort UrgentPointer;

    public readonly uint HostSequenceNumber => BinaryPrimitives.ReverseEndianness(SequenceNumber);

    public readonly uint HostAcknowledgmentNumber => BinaryPrimitives.ReverseEndianness(AcknowledgmentNumber);

    public readonly byte DataOffset => (byte)(DataOffsetAndReserved >> 4);

    public readonly byte Reserved => (byte)(DataOffsetAndReserved & 0x0F);

    public readonly int HeaderLength => DataOffset * 4;
}
