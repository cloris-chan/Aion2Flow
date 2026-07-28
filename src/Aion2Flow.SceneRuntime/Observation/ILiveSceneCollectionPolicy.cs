namespace Cloris.Aion2Flow.SceneRuntime.Observation;

internal interface ILiveSceneCollectionPolicy
{
    void StartMapContext(in PacketObservationSource packet, uint mapId);

    bool ShouldAppendCombat(in PacketObservationSource packet, int sourceId, int targetId, IRuntimeObservationSink sink);

    bool ShouldAppendExtendedObservation();

    bool ShouldAppendEntityVitalObservation();

    void OnBossMetadataChanged();
}
