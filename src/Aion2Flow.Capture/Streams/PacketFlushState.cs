namespace Cloris.Aion2Flow.Capture.Streams;

internal sealed class PacketFlushState
{
    private long _currentFlushId;
    private long _currentAppendFlushId;
    private long _nextFlushId;
    private int _nextStructureScopeId;

    public long CurrentAppendFlushId => _currentAppendFlushId;

    public int NextStructureScopeId() => ++_nextStructureScopeId;

    public long CurrentFlushId => _currentFlushId > 0
        ? _currentFlushId
        : _currentAppendFlushId > 0
            ? _currentAppendFlushId
            : ++_nextFlushId;

    public long BeginAppendFlush()
    {
        var previous = _currentAppendFlushId;
        _currentAppendFlushId = ++_nextFlushId;
        return previous;
    }

    public void EndAppendFlush(long previous)
    {
        _currentAppendFlushId = previous;
    }

    public long BeginFrameFlush()
    {
        var previous = _currentFlushId;
        _currentFlushId = _currentAppendFlushId > 0
            ? _currentAppendFlushId
            : ++_nextFlushId;
        return previous;
    }

    public void EndFrameFlush(long previous)
    {
        _currentFlushId = previous;
    }

    public long BeginFramePayload()
    {
        var previous = _currentFlushId;
        if (_currentFlushId <= 0)
        {
            _currentFlushId = _currentAppendFlushId > 0
                ? _currentAppendFlushId
                : ++_nextFlushId;
        }

        return previous;
    }

    public void EndFramePayload(long previous)
    {
        _currentFlushId = previous;
    }
}
