using Cloris.Aion2Flow.SceneRuntime.Journal;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public sealed class ScenePlaybackCheckpoint
{
    internal ScenePlaybackCheckpoint(long positionMilliseconds, JournalCursor journalCursor)
    {
        PositionMilliseconds = Math.Max(0, positionMilliseconds);
        JournalCursor = journalCursor;
    }

    public long PositionMilliseconds { get; }

    public JournalCursor JournalCursor { get; }
}

internal sealed class ScenePlaybackCheckpointCache
{
    private readonly Lock _gate = new();
    private readonly SortedDictionary<long, ScenePlaybackCheckpoint> _checkpoints = [];

    public int Count
    {
        get { lock (_gate) return _checkpoints.Count; }
    }

    public bool TryGetCeiling(long positionMilliseconds, out ScenePlaybackCheckpoint? checkpoint)
    {
        lock (_gate)
        {
            foreach (var candidate in _checkpoints.Values)
            {
                if (candidate.PositionMilliseconds >= positionMilliseconds)
                {
                    checkpoint = candidate;
                    return true;
                }
            }

            checkpoint = null;
            return false;
        }
    }

    public void Upsert(ScenePlaybackCheckpoint checkpoint)
    {
        lock (_gate)
            _checkpoints[checkpoint.PositionMilliseconds] = checkpoint;
    }

    public void Replace(IReadOnlyList<ScenePlaybackCheckpoint> checkpoints)
    {
        lock (_gate)
        {
            _checkpoints.Clear();
            for (var i = 0; i < checkpoints.Count; i++)
            {
                var checkpoint = checkpoints[i];
                _checkpoints[checkpoint.PositionMilliseconds] = checkpoint;
            }
        }
    }

    public ScenePlaybackCheckpoint[] Snapshot()
    {
        lock (_gate)
        {
            if (_checkpoints.Count == 0)
                return [];

            var result = new ScenePlaybackCheckpoint[_checkpoints.Count];
            var index = 0;
            foreach (var checkpoint in _checkpoints.Values)
                result[index++] = checkpoint;
            return result;
        }
    }
}
