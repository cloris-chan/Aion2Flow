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

public sealed class ArchivedScenePlaybackSource(SceneArchivePayload payload) : IScenePlaybackSource
{
    public Guid EncounterId => payload.Snapshot.EncounterId;

    public DateTimeOffset SceneStarted => payload.SceneStarted;

    public ScenePlaybackSourceKind SourceKind => ScenePlaybackSourceKind.Archived;

    public SceneJournalSegment CreateTimelineSegment() => payload.TimelineSegment;

    public SceneCombatSnapshot CreateSnapshot() => payload.Snapshot;

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

    public bool TryGetFrozenPayload(out SceneArchivePayload payload)
    {
        ThrowIfDisposed();
        return _state.TryGetFrozenPayload(out payload);
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
    private SceneArchivePayload? _frozenPayload;

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
        SceneArchivePayload frozenPayload)
        : this(
            scene,
            frozenPayload.Snapshot.EncounterId,
            frozenPayload.SceneStarted,
            frozenPayload.Snapshot,
            frozenPayload.TimelineSegment.Journal ?? throw new InvalidOperationException("Frozen playback timeline has no journal."),
            frozenPayload.TimelineSegment.StartObservationOrdinal,
            frozenPayload.TimelineSegment.EndObservationOrdinalExclusive)
    {
        Freeze(frozenPayload);
    }

    public Guid EncounterId { get; }

    public DateTimeOffset SceneStarted { get; }

    public SceneCombatSnapshot OpeningSnapshot { get; }

    public SceneJournalSegment TimelineSegment => _timelineSegment;

    public static LiveScenePlaybackState CreateFrozen(SceneLiveReadModel scene, SceneArchivePayload payload) => new(scene, payload);

    public SceneCombatSnapshot CreateSnapshot()
    {
        lock (_gate)
        {
            if (_frozenPayload is { } payload)
                return payload.Snapshot;
        }

        return _scene.CreatePlaybackSnapshot(this);
    }

    public void Freeze(SceneArchivePayload payload)
    {
        if (payload.Snapshot.EncounterId != EncounterId)
            throw new InvalidOperationException("Cannot freeze a live playback state with a different encounter.");

        lock (_gate)
            _frozenPayload = payload;
        _timelineBoundary.Freeze(payload.TimelineSegment.EndObservationOrdinalExclusive);
    }

    public bool TryGetFrozenPayload(out SceneArchivePayload payload)
    {
        lock (_gate)
        {
            if (_frozenPayload is { } frozen)
            {
                payload = frozen;
                return true;
            }
        }

        payload = default!;
        return false;
    }

    public SceneCombatSnapshot GetFrozenSnapshot()
    {
        lock (_gate)
            return _frozenPayload?.Snapshot ?? OpeningSnapshot;
    }

    public void Release() => _scene.ReleasePlaybackSource(this);

    public void StopGrowing(long endObservationOrdinalExclusive) => _timelineBoundary.Freeze(endObservationOrdinalExclusive);
}

public enum ScenePlaybackSourceKind
{
    Archived,
    Live
}
