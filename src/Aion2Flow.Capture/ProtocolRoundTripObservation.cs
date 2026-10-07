using Cloris.Aion2Flow.Capture.Streams;

namespace Cloris.Aion2Flow.Capture;

internal readonly record struct ProtocolRoundTripObservation(
    TcpConnection Connection,
    uint ClientSentMonotonicMilliseconds,
    long ArrivalTimestamp);
