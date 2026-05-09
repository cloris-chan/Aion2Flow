using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Runtime;

namespace Cloris.Aion2Flow.SceneRuntime;

public static class SceneSinkFactory
{
    public static Func<IRuntimeObservationSink> CreateForLive(SceneLiveReadModel scene) =>
        () => scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal));

    public static ReplaySinkHolder CreateForReplay()
    {
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        var clock = new SceneRuntimeClock(DateTimeOffset.UtcNow.Ticks);
        var journaling = new JournalingRuntimeObservationSink(journal, clock, sceneId);
        return new ReplaySinkHolder(journaling, journal, new SceneReadModelOwner(journal, sceneId, DateTimeOffset.Now));
    }
}

public sealed class SceneLiveReadModel
{
    private readonly Lock _gate = new();
    private long _nextBatchOrdinal;

    public Guid SessionId { get; private set; }
    public DateTimeOffset SessionStarted { get; private set; }
    public ObservedEventJournal Journal { get; } = new();
    public SceneRuntimeClock Clock { get; } = new(DateTimeOffset.UtcNow.Ticks);
    public SceneReadModelOwner Owner { get; }

    public SceneLiveReadModel() : this(DateTimeOffset.Now)
    {
    }

    public SceneLiveReadModel(DateTimeOffset sessionStarted)
    {
        SessionId = Guid.NewGuid();
        SessionStarted = sessionStarted;
        Owner = new SceneReadModelOwner(Journal, SessionId, sessionStarted);
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

    private void ResetCore()
        => ResetCore(DateTimeOffset.Now);

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
