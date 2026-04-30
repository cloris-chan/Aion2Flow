using Cloris.Aion2Flow.Battle.Runtime;

namespace Cloris.Aion2Flow.Battle.Archive;

public sealed class ArchivedBattleRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BattleId { get; init; }
    public DateTimeOffset ArchivedAt { get; init; }
    public string Trigger { get; init; } = string.Empty;
    public bool IsAutomatic { get; init; }
    public DamageMeterSnapshot Snapshot { get; init; } = new();
    public CombatMetricsStore Store { get; init; } = new();
}
