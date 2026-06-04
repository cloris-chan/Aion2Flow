using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public interface IScenePlaybackSource
{
    Guid EncounterId { get; }
    DateTimeOffset SceneStarted { get; }
    SceneJournalSegment CreateTimelineSegment();
    SceneCombatSnapshot CreateSnapshot();
}

public sealed class ArchivedScenePlaybackSource(ArchivedEncounterRecord record) : IScenePlaybackSource
{
    public Guid EncounterId => record.EncounterId;

    public DateTimeOffset SceneStarted => record.ScenePayload.SceneStarted;

    public SceneJournalSegment CreateTimelineSegment() => record.ScenePayload.TimelineSegment;

    public SceneCombatSnapshot CreateSnapshot() => record.Snapshot;
}

public sealed class LiveScenePlaybackSource(SceneLiveReadModel scene) : IScenePlaybackSource
{
    public Guid EncounterId => scene.SessionId;

    public DateTimeOffset SceneStarted => scene.SessionStarted;

    public SceneJournalSegment CreateTimelineSegment() => scene.Owner.CreateLiveTimelineSegment();

    public SceneCombatSnapshot CreateSnapshot() => scene.Owner.CreateSnapshot();
}
