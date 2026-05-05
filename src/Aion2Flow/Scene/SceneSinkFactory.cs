using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Scene.Compatibility;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Scene.Runtime;

namespace Cloris.Aion2Flow.Scene;

public static class SceneSinkFactory
{
    public static Func<IRuntimeObservationSink> CreateForStore(CombatMetricsStore store) =>
        SceneDualWrite.Enabled
            ? () =>
            {
                var journal = new ObservedEventJournal();
                var clock = new SceneRuntimeClock(DateTimeOffset.UtcNow.Ticks);
                var legacy = new LegacyRuntimeObservationSink(store);
                var journaling = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());
                return new CompositeRuntimeObservationSink(legacy, journaling);
            }
            : () => new LegacyRuntimeObservationSink(store);

    public static ReplaySinkHolder CreateForReplay(CombatMetricsStore store)
    {
        if (!SceneDualWrite.Enabled)
            return new ReplaySinkHolder(new LegacyRuntimeObservationSink(store), null);

        var journal = new ObservedEventJournal();
        var clock = new SceneRuntimeClock(DateTimeOffset.UtcNow.Ticks);
        var legacy = new LegacyRuntimeObservationSink(store);
        var journaling = new JournalingRuntimeObservationSink(journal, clock, Guid.NewGuid());
        return new ReplaySinkHolder(new CompositeRuntimeObservationSink(legacy, journaling), journal);
    }
}

public readonly struct ReplaySinkHolder(IRuntimeObservationSink sink, ObservedEventJournal? journal) : IDisposable
{
    public IRuntimeObservationSink Sink { get; } = sink;
    public ObservedEventJournal? Journal { get; } = journal;

    public void Dispose() { }
}
