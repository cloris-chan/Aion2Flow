namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public readonly record struct ScenePlaybackCheckpoint(long PositionMilliseconds, long ObservationOrdinal, ScenePlaybackFrame Frame);

public sealed class ScenePlaybackCheckpointCache
{
    private readonly Lock _gate = new();
    private readonly SortedDictionary<long, ScenePlaybackCheckpoint> _checkpoints = [];

    public int Count
    {
        get { lock (_gate) return _checkpoints.Count; }
    }

    public void Clear()
    {
        lock (_gate)
            _checkpoints.Clear();
    }

    public bool TryGet(long positionMilliseconds, out ScenePlaybackCheckpoint checkpoint)
    {
        lock (_gate)
            return _checkpoints.TryGetValue(positionMilliseconds, out checkpoint);
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
