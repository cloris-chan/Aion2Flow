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
    public PacketStructurePath CurrentStructurePath { get; private set; }
    public readonly PacketStructureReference CurrentStructure => CurrentStructurePath.Leaf;

    public readonly long FrameOrdinal => ordinals.CurrentFrameOrdinal;

    public readonly long BatchOrdinal => ordinals.CurrentBatchOrdinal;

    public PacketStructurePath EnterStructure(PacketStructureKind kind, int offset, int length, int bodyOffset, int bodyLength, int siblingIndex)
    {
        var previous = CurrentStructurePath;
        var previousLeaf = previous.Leaf;
        var next = new PacketStructureReference(kind, ++_nextStructureScopeId, previousLeaf.ScopeId, previousLeaf.ScopeId == 0 ? 1 : previousLeaf.Depth + 1, siblingIndex, offset, length, bodyOffset, bodyLength);
        CurrentStructurePath = previous.Push(next);
        return previous;
    }

    public void RestoreStructure(PacketStructurePath previous) => CurrentStructurePath = previous;

    public bool MarkParsed()
    {
        Parsed = true;
        return true;
    }
}
