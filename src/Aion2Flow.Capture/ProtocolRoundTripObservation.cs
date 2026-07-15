using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Capture;

internal readonly record struct ProtocolRoundTripObservation(TcpConnection Connection, long ClientSentUnixMilliseconds, long ServerUnixMilliseconds, long ArrivalUnixMilliseconds);
