namespace Cloris.Aion2Flow.SceneRuntime.Observation;

public interface IObservedEventSink
{
    void Append(in ObservedEventEnvelope observation);

    void CompleteBatch(long batchOrdinal);
}
