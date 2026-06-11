using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Runtime;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime;

public static class SceneSinkFactory
{
    public static Func<IRuntimeObservationSink> CreateForLive(SceneLiveReadModel scene) =>
        () => scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal));

    public static ReplaySinkHolder CreateForReplay()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        var sceneStarted = DateTimeOffset.UtcNow;
        var clock = new SceneRuntimeClock(0);
        var journaling = new JournalingRuntimeObservationSink(journal, clock, sceneId);
        var metadataRegistry = new RuntimeMetadataRegistry();
        return new ReplaySinkHolder(journaling, journal, new SceneReadModelOwner(journal, sceneId, sceneStarted, metadataRegistry));
    }
}

public sealed class SceneLiveReadModel
{
    private const int LiveJournalInitialCapacity = 4_096;
    private const int LiveCombatEventInitialCapacity = 4_096;
    private const int LiveCombatantInitialCapacity = 128;
    private const int LivePairInitialCapacity = 512;
    private readonly Lock _gate = new();
    private long _nextBatchOrdinal;

    public Guid SessionId { get; private set; }
    public DateTimeOffset SessionStarted { get; private set; }
    public ObservedEventJournal Journal { get; }
    public SceneRuntimeClock Clock { get; }
    public RuntimeMetadataRegistry MetadataRegistry { get; } = new();
    public SceneReadModelOwner Owner { get; }

    public SceneLiveReadModel() : this(DateTimeOffset.Now)
    {
    }

    public SceneLiveReadModel(DateTimeOffset sessionStarted)
    {
        SessionId = Guid.NewGuid();
        SessionStarted = sessionStarted;
        Clock = new SceneRuntimeClock(sessionStarted.ToUnixTimeMilliseconds());
        Journal = new ObservedEventJournal(LiveJournalInitialCapacity);
        Owner = new SceneReadModelOwner(
            Journal,
            SessionId,
            sessionStarted,
            new EntityStore(),
            new SceneBoundaryStore(),
            MetadataRegistry,
            new CombatStore(LiveCombatEventInitialCapacity, LiveCombatantInitialCapacity, LivePairInitialCapacity));
    }

    public void Reset()
    {
        lock (_gate)
        {
            ResetCore();
        }
    }

    public void Reset(Action reset)
    {
        lock (_gate)
        {
            reset();
            ResetCore();
        }
    }

    public void Reset(Func<DateTimeOffset> resolveSessionStarted)
    {
        lock (_gate)
        {
            ResetCore(resolveSessionStarted());
        }
    }

    public long NextBatchOrdinal() => Interlocked.Increment(ref _nextBatchOrdinal);

    public IRuntimeObservationSink Synchronize(IRuntimeObservationSink sink) => new SynchronizedRuntimeObservationSink(sink, _gate);

    private void ResetCore() => ResetCore(DateTimeOffset.Now);

    public void Reset(DateTimeOffset sessionStarted)
    {
        lock (_gate)
        {
            ResetCore(sessionStarted);
        }
    }

    private void ResetCore(DateTimeOffset sessionStarted)
    {
        SessionId = Guid.NewGuid();
        SessionStarted = sessionStarted;
        Clock.Reset(sessionStarted);
        Owner.ResetCombat(SessionId, Clock.NextObservationOrdinal, sessionStarted);
    }
}

public readonly struct ReplaySinkHolder(IRuntimeObservationSink sink, ObservedEventJournal journal, SceneReadModelOwner owner) : IDisposable
{
    public IRuntimeObservationSink Sink { get; } = sink;

    public ObservedEventJournal Journal { get; } = journal;

    public SceneReadModelOwner Owner { get; } = owner;

    public void Dispose() { }
}
