namespace Cloris.Aion2Flow.Capture.Streams;

internal sealed class PacketOrdinalState
{
    private long _currentFrameOrdinal;
    private long _nextFrameOrdinal;
    private long _currentBatchOrdinal;
    private long _currentAppendBatchOrdinal;
    private long _nextBatchOrdinal;

    public long CurrentAppendBatchOrdinal => _currentAppendBatchOrdinal;

    public long CurrentFrameOrdinal => _currentFrameOrdinal > 0 ? _currentFrameOrdinal : ++_nextFrameOrdinal;

    public long CurrentBatchOrdinal => _currentBatchOrdinal > 0
        ? _currentBatchOrdinal
        : _currentAppendBatchOrdinal > 0
            ? _currentAppendBatchOrdinal
            : ++_nextBatchOrdinal;

    public long BeginAppendBatch()
    {
        var previous = _currentAppendBatchOrdinal;
        _currentAppendBatchOrdinal = ++_nextBatchOrdinal;
        return previous;
    }

    public void EndAppendBatch(long previous)
    {
        _currentAppendBatchOrdinal = previous;
    }

    public long BeginFrameBatch()
    {
        var previous = _currentBatchOrdinal;
        _currentBatchOrdinal = _currentAppendBatchOrdinal > 0
            ? _currentAppendBatchOrdinal
            : ++_nextBatchOrdinal;
        return previous;
    }

    public void EndFrameBatch(long previous)
    {
        _currentBatchOrdinal = previous;
    }

    public (long Frame, long Batch) BeginFramePayload()
    {
        var previous = (_currentFrameOrdinal, _currentBatchOrdinal);
        _currentFrameOrdinal = ++_nextFrameOrdinal;
        if (_currentBatchOrdinal <= 0)
        {
            _currentBatchOrdinal = _currentAppendBatchOrdinal > 0
                ? _currentAppendBatchOrdinal
                : ++_nextBatchOrdinal;
        }

        return previous;
    }

    public void EndFramePayload(long previousFrame, long previousBatch)
    {
        _currentFrameOrdinal = previousFrame;
        _currentBatchOrdinal = previousBatch;
    }
}
