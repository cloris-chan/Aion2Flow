namespace Cloris.Aion2Flow.PacketCapture.Readers;

public readonly ref struct PacketVarIntReadResult(int value, int byteCount)
{
    public readonly int Value => value;

    public readonly int ByteCount => byteCount;
}
