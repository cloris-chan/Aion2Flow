using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Cloris.Aion2Flow.Battle.Runtime;

namespace Cloris.Aion2Flow.Battle.Archive;

public sealed class BattleArchiveService
{
    private readonly Lock _lock = new();
    private readonly List<ArchivedBattleRecord> _history = [];
    private readonly Dictionary<Guid, ArchivedBattleRecord> _historyByBattleId = [];
    private ImmutableArray<ArchivedBattleRecord> _historySnapshot = [];

    public event EventHandler? HistoryChanged;

    public IReadOnlyList<ArchivedBattleRecord> History => _historySnapshot;

    public ArchivedBattleRecord? Archive(DamageMeterSnapshot snapshot, CombatMetricsStore store, string trigger, bool isAutomatic)
        => AddArchiveRecord(snapshot.DeepClone(), store.CreateArchiveSlice(snapshot), null, trigger, isAutomatic);

    public ArchivedBattleRecord? Archive(SceneArchivePayload payload, string trigger, bool isAutomatic)
    {
        var archivedPayload = payload.DeepClone();
        return AddArchiveRecord(archivedPayload.Snapshot, new CombatMetricsStore(), archivedPayload, trigger, isAutomatic);
    }

    private ArchivedBattleRecord? AddArchiveRecord(DamageMeterSnapshot archivedSnapshot, CombatMetricsStore archivedStore, SceneArchivePayload? payload, string trigger, bool isAutomatic)
    {
        if (archivedSnapshot.BattleTime <= 0 || archivedSnapshot.Combatants.Count == 0)
        {
            return null;
        }

        ArchivedBattleRecord? record;
        bool historyChanged;
        lock (_lock)
        {
            if (_history.Count > 0 && IsEquivalent(_history[0].Snapshot, archivedSnapshot))
            {
                return null;
            }

            record = new ArchivedBattleRecord
            {
                BattleId = archivedSnapshot.BattleId,
                ArchivedAt = DateTimeOffset.Now,
                Trigger = trigger,
                IsAutomatic = isAutomatic,
                Snapshot = archivedSnapshot,
                Store = archivedStore,
                ScenePayload = payload
            };

            _history.Insert(0, record);
            _historyByBattleId[record.BattleId] = record;
            if (_history.Count > 100)
            {
                for (var i = 100; i < _history.Count; i++)
                {
                    _historyByBattleId.Remove(_history[i].BattleId);
                }

                _history.RemoveRange(100, _history.Count - 100);
            }

            _historySnapshot = [.. _history];
            historyChanged = true;
        }

        if (historyChanged)
        {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        return record;
    }

    public bool TryGetBattle(Guid battleId, [NotNullWhen(true)] out ArchivedBattleRecord? record)
    {
        lock (_lock)
        {
            return _historyByBattleId.TryGetValue(battleId, out record);
        }
    }

    private static bool IsEquivalent(DamageMeterSnapshot left, DamageMeterSnapshot right)
    {
        return left.BattleTime == right.BattleTime &&
               left.BattleId == right.BattleId &&
               string.Equals(left.TargetName, right.TargetName, StringComparison.Ordinal) &&
               left.MapId == right.MapId &&
               left.MapInstanceId == right.MapInstanceId &&
               left.Combatants.Count == right.Combatants.Count &&
               SumDamage(left) == SumDamage(right);
    }

    private static double SumDamage(DamageMeterSnapshot snapshot)
    {
        var totalDamage = 0d;
        foreach (var combatant in snapshot.Combatants.Values)
        {
            totalDamage += combatant.DamageAmount;
        }

        return totalDamage;
    }
}
