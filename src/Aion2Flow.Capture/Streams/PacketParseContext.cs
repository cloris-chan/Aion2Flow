using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Capture.Streams;

internal ref struct PacketParseContext(IRuntimeObservationSink sink, SceneObservationWriter writer, PacketOrdinalState ordinals, in TcpConnection connection, long timestampMilliseconds)
{
    public readonly IRuntimeObservationSink Sink = sink;
    public readonly SceneObservationWriter Writer = writer;
    public readonly TcpConnection Connection = connection;
    public readonly long TimestampMilliseconds = timestampMilliseconds;
    public bool Parsed;

    public readonly long FrameOrdinal => ordinals.CurrentFrameOrdinal;

    public readonly long BatchOrdinal => ordinals.CurrentBatchOrdinal;

    public bool MarkParsed()
    {
        Parsed = true;
        return true;
    }
}
