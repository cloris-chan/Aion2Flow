namespace Cloris.Aion2Flow.Scene.Observation;

public interface IObservedEventSink
{
    void Append(in ObservedEventEnvelope observation);

    void CompleteBatch(long batchOrdinal);
}
