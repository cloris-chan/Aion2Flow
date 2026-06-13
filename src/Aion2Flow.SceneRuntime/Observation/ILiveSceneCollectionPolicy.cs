namespace Cloris.Aion2Flow.SceneRuntime.Observation;

internal interface ILiveSceneCollectionPolicy
{
    bool ShouldAppendCombat(in PacketObservationSource packet, int sourceId, int targetId);

    bool ShouldAppendExtendedObservation();

    bool ShouldAppendResourceObservation();

    void OnBossMetadataChanged();
}
