using Cloris.Aion2Flow.Battle.Runtime;

namespace Cloris.Aion2Flow.Scene.Stores;

public sealed class LegacyBattleSnapshotAdapter(EntityStore entities, CombatStore combat, MetadataStore? metadata = null)
{
    public DamageMeterSnapshot CreateSnapshot()
    {
        var snapshot = new DamageMeterSnapshot();
        foreach (var (combatantId, _) in combat.Combatants)
        {
            var displayName = ResolveDisplayName(combatantId);
            var metrics = new CombatantMetrics(displayName);

            snapshot.Combatants[combatantId] = metrics;
        }

        snapshot.MapId = metadata?.CurrentMapId ?? 0;
        snapshot.MapInstanceId = metadata?.CurrentMapInstanceId ?? 0;

        return snapshot;
    }

    private string ResolveDisplayName(int entityId)
    {
        if (entities.TryGet(entityId, out var entity) && entity.Nickname is { Length: > 0 } nick)
            return nick;

        if (entities.TryGet(entityId, out entity) && entity.NpcCode is { } npcCode)
            return $"NPC-{npcCode}";

        return entityId.ToString();
    }
}
