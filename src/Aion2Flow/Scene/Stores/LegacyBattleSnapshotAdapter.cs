using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Scene.Projection;

namespace Cloris.Aion2Flow.Scene.Stores;

public sealed class LegacyBattleSnapshotAdapter(EntityStore entities, CombatStore combat, MetadataStore? metadata = null)
{
    public DamageMeterSnapshot CreateSnapshot() => new SceneCombatSnapshotAdapter(entities, combat, metadata ?? new MetadataStore()).CreateSnapshot();
}
