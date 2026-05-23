using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Capture.Streams;

internal ref struct PacketParseContext(IRuntimeObservationSink sink, SceneObservationWriter writer, PacketOrdinalState ordinals, in TcpConnection connection, long timestampMilliseconds)
{
    public readonly IRuntimeObservationSink Sink = sink;
    public readonly SceneObservationWriter Writer = writer;
    public readonly TcpConnection Connection = connection;
    public readonly long TimestampMilliseconds = timestampMilliseconds;
    private int _nextStructureScopeId;
    public bool Parsed;
    public PacketStructureReference CurrentStructure { get; private set; }

    public readonly long FrameOrdinal => ordinals.CurrentFrameOrdinal;

    public readonly long BatchOrdinal => ordinals.CurrentBatchOrdinal;

    public PacketStructureReference EnterStructure(PacketStructureKind kind, int offset, int length, int bodyOffset, int bodyLength, int siblingIndex)
    {
        var previous = CurrentStructure;
        CurrentStructure = new PacketStructureReference(kind, ++_nextStructureScopeId, previous.ScopeId, previous.ScopeId == 0 ? 1 : previous.Depth + 1, siblingIndex, offset, length, bodyOffset, bodyLength);
        return previous;
    }

    public void RestoreStructure(PacketStructureReference previous) => CurrentStructure = previous;

    public bool MarkParsed()
    {
        Parsed = true;
        return true;
    }
}
