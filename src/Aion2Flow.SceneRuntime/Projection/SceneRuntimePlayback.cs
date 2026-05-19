using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Runtime;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

public sealed class SceneRuntimePlayback
{
    private readonly ObservedEventEnvelope[] _timeline;
    private readonly SceneRuntimeCheckpoint[] _checkpoints;
    private readonly ObservedEventJournal _journal;

    public SceneRuntimePlayback(IReadOnlyList<ObservedEventEnvelope> timeline, IReadOnlyList<SceneRuntimeCheckpoint> checkpoints)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(checkpoints);

        _timeline = new ObservedEventEnvelope[timeline.Count];
        for (var i = 0; i < _timeline.Length; i++)
            _timeline[i] = timeline[i];

        _checkpoints = new SceneRuntimeCheckpoint[checkpoints.Count];
        for (var i = 0; i < _checkpoints.Length; i++)
            _checkpoints[i] = checkpoints[i].DeepClone();

        Array.Sort(_timeline, static (a, b) => a.Stamp.ObservationOrdinal.CompareTo(b.Stamp.ObservationOrdinal));
        Array.Sort(_checkpoints, static (a, b) =>
        {
            var cmp = a.CapturedAtMilliseconds.CompareTo(b.CapturedAtMilliseconds);
            return cmp != 0 ? cmp : a.Anchor.LastObservationOrdinal.CompareTo(b.Anchor.LastObservationOrdinal);
        });
        _journal = ObservedEventJournal.FromEntries(_timeline);
    }

    public static SceneRuntimePlayback FromArchive(SceneArchivePayload payload)
        => new(payload.Timeline, payload.Checkpoints);

    public long StartTimeMilliseconds
    {
        get
        {
            for (var i = 0; i < _timeline.Length; i++)
            {
                var timestamp = _timeline[i].Raw.TimestampMilliseconds;
                if (timestamp > 0)
                    return timestamp;
            }

            return 0;
        }
    }

    public long EndTimeMilliseconds
    {
        get
        {
            for (var i = _timeline.Length - 1; i >= 0; i--)
            {
                var timestamp = _timeline[i].Raw.TimestampMilliseconds;
                if (timestamp > 0)
                    return timestamp;
            }

            return _checkpoints.Length > 0 ? _checkpoints[^1].CapturedAtMilliseconds : 0;
        }
    }

    public SceneCombatSnapshot CreateSnapshotAt(long observedAtMilliseconds)
    {
        var owner = RestoreNearestOwner(observedAtMilliseconds);
        return owner?.CreateSnapshotAt(observedAtMilliseconds) ?? SceneCombatSnapshot.Empty;
    }

    public SceneReadModelFrame CreateFrameAt(long observedAtMilliseconds, int detailCombatantId = 0, bool forceDetailRefresh = false)
    {
        var owner = RestoreNearestOwner(observedAtMilliseconds);
        return owner?.CreateFrameAt(observedAtMilliseconds, detailCombatantId, forceDetailRefresh) ?? new SceneReadModelFrame();
    }

    public SceneCombatSnapshot CreateEndSnapshot()
        => CreateSnapshotAt(EndTimeMilliseconds);

    private SceneReadModelOwner? RestoreNearestOwner(long observedAtMilliseconds)
    {
        if (_checkpoints.Length == 0)
            return null;

        var checkpoint = _checkpoints[0];
        for (var i = 1; i < _checkpoints.Length; i++)
        {
            var candidate = _checkpoints[i];
            if (candidate.CapturedAtMilliseconds > observedAtMilliseconds)
                break;
            checkpoint = candidate;
        }

        return SceneReadModelOwner.RestoreFromCheckpoint(_journal, checkpoint);
    }
}
