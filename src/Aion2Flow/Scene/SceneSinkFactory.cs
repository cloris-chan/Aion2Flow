using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Scene.Compatibility;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Scene.Projection;
using Cloris.Aion2Flow.Scene.Runtime;

namespace Cloris.Aion2Flow.Scene;

public static class SceneSinkFactory
{
    public static Func<IRuntimeObservationSink> CreateForStore(CombatMetricsStore store) => CreateForStore(store, new SceneLiveReadModel());

    public static Func<IRuntimeObservationSink> CreateForStore(CombatMetricsStore store, SceneLiveReadModel scene) =>
        () =>
        {
            if (!SceneDualWrite.Enabled)
                return scene.Synchronize(new LegacyRuntimeObservationSink(store));

            var legacy = new LegacyRuntimeObservationSink(store);
            var journaling = new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal);
            return scene.Synchronize(new CompositeRuntimeObservationSink(legacy, journaling));
        };

    public static ReplaySinkHolder CreateForReplay(CombatMetricsStore store)
    {
        if (!SceneDualWrite.Enabled)
            return new ReplaySinkHolder(new LegacyRuntimeObservationSink(store), null, null);

        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        var clock = new SceneRuntimeClock(DateTimeOffset.UtcNow.Ticks);
        var legacy = new LegacyRuntimeObservationSink(store);
        var journaling = new JournalingRuntimeObservationSink(journal, clock, sceneId);
        return new ReplaySinkHolder(new CompositeRuntimeObservationSink(legacy, journaling), journal, new SceneReadModelOwner(journal, sceneId));
    }
}

public sealed class SceneLiveReadModel
{
    private readonly Lock _gate = new();
    private long _nextBatchOrdinal;

    public Guid SessionId { get; private set; } = Guid.NewGuid();
    public ObservedEventJournal Journal { get; } = new();
    public SceneRuntimeClock Clock { get; } = new(DateTimeOffset.UtcNow.Ticks);
    public SceneReadModelOwner Owner { get; }

    public SceneLiveReadModel()
    {
        Owner = new SceneReadModelOwner(Journal, SessionId);
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

    public long NextBatchOrdinal() => Interlocked.Increment(ref _nextBatchOrdinal);

    public IRuntimeObservationSink Synchronize(IRuntimeObservationSink sink) => new SynchronizedRuntimeObservationSink(sink, _gate);

    private void ResetCore()
    {
        SessionId = Guid.NewGuid();
        Owner.ResetCombat(SessionId, Clock.NextObservationOrdinal);
    }
}

public readonly struct ReplaySinkHolder(IRuntimeObservationSink sink, ObservedEventJournal? journal, SceneReadModelOwner? owner) : IDisposable
{
    public IRuntimeObservationSink Sink { get; } = sink;
    public ObservedEventJournal? Journal { get; } = journal;
    public SceneReadModelOwner? Owner { get; } = owner;

    public void Dispose() { }
}
