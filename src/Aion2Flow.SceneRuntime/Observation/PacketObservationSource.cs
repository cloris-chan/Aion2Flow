namespace Cloris.Aion2Flow.SceneRuntime.Observation;

public readonly record struct PacketObservationSource(long CaptureTimestampMilliseconds, long FrameOrdinal, long BatchOrdinal, ushort Opcode, int PayloadLength, long CaptureSequence, PacketStructurePath StructurePath)
{
    public RawPacketReference Raw => new(Opcode, PayloadLength, CaptureSequence, StructurePath);
}
