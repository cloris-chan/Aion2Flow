using Cloris.Aion2Flow.Protocol.Packets;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Runtime;

namespace Cloris.Aion2Flow.Capture.Streams;

internal sealed class SceneObservationWriter(IRuntimeObservationSink sink)
{
    public IRuntimeObservationSink Sink => sink;

    public void ApplyNpcCatalog(in PacketObservationSource packet, int instanceId, int npcCode, bool requireCatalogEntry = false)
    {
        if (instanceId <= 0 || npcCode <= 0)
        {
            return;
        }

        var hasCatalogEntry = CombatResourceRegistry.TryResolveNpcCatalogEntry(npcCode, out var entry);
        if (requireCatalogEntry && !hasCatalogEntry)
        {
            return;
        }

        var lifecycleId = sink.ResolveLifecycleId(instanceId);
        if (hasCatalogEntry &&
            sink.TryGetNpcRuntimeState(lifecycleId, out var existing) &&
            existing.NpcCode is int existingCode &&
            existingCode != npcCode &&
            CombatResourceRegistry.TryResolveNpcCatalogEntry(existingCode, out _))
        {
            sink.RebindInstanceLifecycle(instanceId);
        }

        sink.AppendNpcCode(in packet, instanceId, npcCode);

        if (!hasCatalogEntry)
        {
            return;
        }

        sink.AppendNpcName(in packet, npcCode, entry.Name);

        var kind = CombatResourceRegistry.ResolveNpcKind(entry.Kind);
        if (kind != NpcKind.Unknown)
        {
            sink.AppendNpcKind(in packet, instanceId, kind);
        }
    }

    public bool StageSceneMapCandidateFromSceneState(in PacketObservationSource packet, uint value)
    {
        if (!SceneMapIdClassifier.IsAmbiguousSceneStateMapId(value))
        {
            return false;
        }

        sink.StageSceneMapCandidate(in packet, value);
        return true;
    }

    public bool ConfirmDestinationMapFromSceneState(in PacketObservationSource packet, uint value)
    {
        if (!SceneMapIdClassifier.IsAmbiguousSceneStateMapId(value))
        {
            return false;
        }

        sink.ConfirmSceneMap(in packet, value);
        return true;
    }

    public bool ApplyDestinationMapEvent(in PacketObservationSource packet, uint value, PacketMapEventSignal signal)
    {
        if (!SceneMapIdClassifier.IsPacketMapEventId(value))
        {
            return false;
        }

        switch (signal)
        {
            case PacketMapEventSignal.Current:
                sink.SetCurrentMap(in packet, value);
                break;
            case PacketMapEventSignal.TransitionAnnounced:
                sink.AnnounceDestinationMapTransition(in packet, value);
                break;
            case PacketMapEventSignal.TransitionCountdown:
                sink.CommitDestinationMapTransition(in packet, value);
                break;
            default:
                return false;
        }

        return true;
    }
}
