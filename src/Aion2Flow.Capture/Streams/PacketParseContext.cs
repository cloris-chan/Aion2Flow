using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Capture.Streams;

internal ref struct PacketParseContext(IRuntimeObservationSink sink, SceneObservationWriter writer, PacketFlushState flushState, PacketPlayerGroupState playerGroupState, Action<ProtocolRoundTripObservation>? protocolRoundTripObserver, in TcpConnection connection, long timestampMilliseconds)
{
    public readonly IRuntimeObservationSink Sink = sink;
    public readonly SceneObservationWriter Writer = writer;
    public readonly TcpConnection Connection = connection;
    public readonly long TimestampMilliseconds = timestampMilliseconds;
    public bool Parsed;
    public PacketStructurePath CurrentStructurePath { get; private set; }
    public readonly PacketStructureReference CurrentStructure => CurrentStructurePath.Leaf;

    public readonly bool TryRegisterPartyStatusMember(int entityId) => playerGroupState.TryRegisterPartyStatusMember(entityId);

    public readonly bool TryRegisterForceStatusMember(int entityId) => playerGroupState.TryRegisterForceStatusMember(entityId);

    public readonly long FlushId => flushState.CurrentFlushId;

    public readonly PacketObservationSource CreateObservationSource(ushort opcode, int payloadLength, long captureSequence = 0)
        => new(TimestampMilliseconds, FlushId, opcode, payloadLength, captureSequence, CurrentStructurePath);

    public readonly void ObserveProtocolRoundTrip(long clientSentUnixMilliseconds, long serverUnixMilliseconds)
        => protocolRoundTripObserver?.Invoke(new ProtocolRoundTripObservation(
            Connection,
            clientSentUnixMilliseconds,
            serverUnixMilliseconds,
            TimestampMilliseconds));

    public PacketStructurePath EnterStructure(PacketStructureKind kind, int offset, int length, int bodyOffset, int bodyLength, int siblingIndex)
    {
        var previous = CurrentStructurePath;
        var previousLeaf = previous.Leaf;
        var next = new PacketStructureReference(kind, flushState.NextStructureScopeId(), previousLeaf.ScopeId, previousLeaf.ScopeId == 0 ? 1 : previousLeaf.Depth + 1, siblingIndex, offset, length, bodyOffset, bodyLength);
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
