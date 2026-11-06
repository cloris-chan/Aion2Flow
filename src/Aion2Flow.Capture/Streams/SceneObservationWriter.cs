using Cloris.Aion2Flow.Protocol.Packets;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

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

        var resources = sink.ClientContext.CombatResources;
        var hasCatalogEntry = resources.TryResolveNpcCatalogEntry(npcCode, out var entry);
        if (requireCatalogEntry && !hasCatalogEntry)
        {
            return;
        }

        var lifecycleId = sink.ResolveLifecycleId(instanceId);
        if (hasCatalogEntry &&
            sink.TryGetNpcRuntimeState(lifecycleId, out var existing) &&
            existing.NpcCode is int existingCode &&
            existingCode != npcCode &&
            resources.TryResolveNpcCatalogEntry(existingCode, out _))
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

    public bool ApplyMapScopeSignal(in PacketObservationSource packet, PacketScopeSignal signal)
    {
        if (signal == PacketScopeSignal.Current)
        {
            sink.EnsureUnknownMapScope(in packet);
        }

        return true;
    }
}
