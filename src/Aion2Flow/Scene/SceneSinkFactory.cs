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
        SceneDualWrite.Enabled
            ? () =>
            {
                var clock = new SceneRuntimeClock(DateTimeOffset.UtcNow.Ticks);
                var legacy = new LegacyRuntimeObservationSink(store);
                var journaling = new JournalingRuntimeObservationSink(scene.Journal, clock, scene.SessionId);
                return new CompositeRuntimeObservationSink(legacy, journaling);
            }
            : () => new LegacyRuntimeObservationSink(store);

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
    public Guid SessionId { get; } = Guid.NewGuid();
    public ObservedEventJournal Journal { get; } = new();
    public SceneReadModelOwner Owner { get; }

    public SceneLiveReadModel()
    {
        Owner = new SceneReadModelOwner(Journal, SessionId);
    }
}

public readonly struct ReplaySinkHolder(IRuntimeObservationSink sink, ObservedEventJournal? journal, SceneReadModelOwner? owner) : IDisposable
{
    public IRuntimeObservationSink Sink { get; } = sink;
    public ObservedEventJournal? Journal { get; } = journal;
    public SceneReadModelOwner? Owner { get; } = owner;

    public void Dispose() { }
}
