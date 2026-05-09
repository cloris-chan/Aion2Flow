using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Runtime;

namespace Cloris.Aion2Flow.Capture.Streams;

internal sealed class SceneObservationWriter(IRuntimeObservationSink sink)
{
    public IRuntimeObservationSink Sink => sink;

    public void ApplyNpcCatalog(int instanceId, int npcCode, bool requireCatalogEntry = false)
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

        sink.AppendNpcCode(instanceId, npcCode);

        if (!hasCatalogEntry)
        {
            return;
        }

        sink.AppendNpcName(npcCode, entry.Name);

        var kind = CombatResourceRegistry.ResolveNpcKind(entry.Kind);
        if (kind != NpcKind.Unknown && kind != NpcKind.Summon)
        {
            sink.AppendNpcKind(instanceId, kind);
        }
    }

    public bool StageDestinationMapFromSceneState(uint value)
    {
        if (!SceneMapIdClassifier.IsSceneStateMapId(value))
        {
            return false;
        }

        sink.StageDestinationMap(value);
        return true;
    }
}
