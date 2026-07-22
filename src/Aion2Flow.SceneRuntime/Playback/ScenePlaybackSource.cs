using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Projection;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public interface IScenePlaybackSource : IDisposable
{
    Guid EncounterId { get; }
    DateTimeOffset SceneStarted { get; }
    ScenePlaybackSourceKind SourceKind { get; }
    SceneJournalSegment CreateTimelineSegment();
    SceneCombatSnapshot CreateSnapshot();
}

public sealed class ArchivedScenePlaybackSource(ArchivedEncounterRecord record) : IScenePlaybackSource
{
    public Guid EncounterId => record.EncounterId;

    public DateTimeOffset SceneStarted => record.ScenePayload.SceneStarted;

    public ScenePlaybackSourceKind SourceKind => ScenePlaybackSourceKind.Archived;

    public SceneJournalSegment CreateTimelineSegment() => record.ScenePayload.TimelineSegment;

    public SceneCombatSnapshot CreateSnapshot() => record.Snapshot;

    public void Dispose()
    {
    }
}

public sealed class LiveScenePlaybackSource : IScenePlaybackSource
{
    private readonly LiveScenePlaybackState _state;
    private int _isDisposed;

    internal LiveScenePlaybackSource(LiveScenePlaybackState state)
    {
        _state = state;
    }

    public Guid EncounterId => _state.EncounterId;

    public DateTimeOffset SceneStarted => _state.SceneStarted;

    public ScenePlaybackSourceKind SourceKind => ScenePlaybackSourceKind.Live;

    public SceneJournalSegment CreateTimelineSegment()
    {
        ThrowIfDisposed();
        return _state.TimelineSegment;
    }

    public SceneCombatSnapshot CreateSnapshot()
    {
        ThrowIfDisposed();
        return _state.CreateSnapshot();
    }

    public bool TryGetFrozenArchive(out SceneArchiveCapture capture)
    {
        ThrowIfDisposed();
        return _state.TryGetFrozenArchive(out capture);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            _state.Release();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
}

internal sealed class LiveScenePlaybackState
{
    private readonly SceneLiveReadModel _scene;
    private readonly SceneJournalLiveBoundary _timelineBoundary;
    private readonly SceneJournalSegment _timelineSegment;
    private readonly Lock _gate = new();
    private SceneArchiveCapture? _frozenCapture;

    public LiveScenePlaybackState(
        SceneLiveReadModel scene,
        Guid encounterId,
        DateTimeOffset sceneStarted,
        SceneCombatSnapshot openingSnapshot,
        ObservedEventJournal journal,
        long startObservationOrdinal,
        long endObservationOrdinalExclusive)
    {
        _scene = scene;
        EncounterId = encounterId;
        SceneStarted = sceneStarted;
        OpeningSnapshot = openingSnapshot;
        _timelineBoundary = new SceneJournalLiveBoundary(journal, endObservationOrdinalExclusive);
        _timelineSegment = new SceneJournalSegment(journal, startObservationOrdinal, _timelineBoundary);
    }

    private LiveScenePlaybackState(
        SceneLiveReadModel scene,
        SceneArchiveCapture frozenCapture)
        : this(
            scene,
            frozenCapture.Snapshot.EncounterId,
            frozenCapture.Payload.SceneStarted,
            frozenCapture.Snapshot,
            frozenCapture.Payload.TimelineSegment.Journal ?? throw new InvalidOperationException("Frozen playback timeline has no journal."),
            frozenCapture.Payload.TimelineSegment.StartObservationOrdinal,
            frozenCapture.Payload.TimelineSegment.EndObservationOrdinalExclusive)
    {
        Freeze(frozenCapture);
    }

    public Guid EncounterId { get; }

    public DateTimeOffset SceneStarted { get; }

    public SceneCombatSnapshot OpeningSnapshot { get; }

    public SceneJournalSegment TimelineSegment => _timelineSegment;

    public static LiveScenePlaybackState CreateFrozen(SceneLiveReadModel scene, SceneArchiveCapture capture) => new(scene, capture);

    public SceneCombatSnapshot CreateSnapshot()
    {
        lock (_gate)
        {
            if (_frozenCapture is { } capture)
                return capture.Snapshot;
        }

        return _scene.CreatePlaybackSnapshot(this);
    }

    public void Freeze(SceneArchiveCapture capture)
    {
        if (capture.Snapshot.EncounterId != EncounterId)
            throw new InvalidOperationException("Cannot freeze a live playback state with a different encounter.");

        lock (_gate)
            _frozenCapture = capture;
        _timelineBoundary.Freeze(capture.Payload.TimelineSegment.EndObservationOrdinalExclusive);
    }

    public bool TryGetFrozenArchive(out SceneArchiveCapture capture)
    {
        lock (_gate)
        {
            if (_frozenCapture is { } frozen)
            {
                capture = frozen;
                return true;
            }
        }

        capture = default;
        return false;
    }

    public SceneCombatSnapshot GetFrozenSnapshot()
    {
        lock (_gate)
            return _frozenCapture?.Snapshot ?? OpeningSnapshot;
    }

    public void Release() => _scene.ReleasePlaybackSource(this);

    public void StopGrowing(long endObservationOrdinalExclusive) => _timelineBoundary.Freeze(endObservationOrdinalExclusive);
}

public enum ScenePlaybackSourceKind
{
    Archived,
    Live
}
