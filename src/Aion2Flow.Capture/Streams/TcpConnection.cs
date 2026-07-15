namespace Cloris.Aion2Flow.Capture.Streams;

public readonly record struct TcpConnection(uint SourceAddress, uint DestinationAddress, ushort SourcePort, ushort DestinationPort);
