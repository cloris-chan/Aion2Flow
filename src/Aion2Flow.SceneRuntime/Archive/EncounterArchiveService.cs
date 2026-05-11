using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.SceneRuntime.Archive;

public sealed class EncounterArchiveService
{
    private readonly Lock _lock = new();
    private readonly List<ArchivedEncounterRecord> _history = [];
    private readonly Dictionary<Guid, ArchivedEncounterRecord> _historyByEncounterId = [];
    private ImmutableArray<ArchivedEncounterRecord> _historySnapshot = [];

    public event EventHandler? HistoryChanged;

    public IReadOnlyList<ArchivedEncounterRecord> History => _historySnapshot;

    public ArchivedEncounterRecord? Archive(SceneArchivePayload payload, string trigger, bool isAutomatic)
    {
        var archivedPayload = payload.DeepClone();
        return AddArchiveRecord(archivedPayload.Snapshot, archivedPayload, trigger, isAutomatic);
    }

    private ArchivedEncounterRecord? AddArchiveRecord(SceneCombatSnapshot archivedSnapshot, SceneArchivePayload scenePayload, string trigger, bool isAutomatic)
    {
        if (archivedSnapshot.EncounterTime <= 0 || archivedSnapshot.EncounterStartTime <= 0 || archivedSnapshot.EncounterEndTime < archivedSnapshot.EncounterStartTime || archivedSnapshot.Combatants.Count == 0)
        {
            return null;
        }

        ArchivedEncounterRecord? record;
        bool historyChanged;
        lock (_lock)
        {
            if (_history.Count > 0 && IsEquivalent(_history[0].Snapshot, archivedSnapshot))
            {
                return null;
            }

            record = new ArchivedEncounterRecord
            {
                EncounterId = archivedSnapshot.EncounterId,
                ArchivedAt = scenePayload.SceneStarted.ToLocalTime(),
                Trigger = trigger,
                IsAutomatic = isAutomatic,
                Snapshot = archivedSnapshot,
                ScenePayload = scenePayload
            };

            _history.Insert(0, record);
            _historyByEncounterId[record.EncounterId] = record;
            if (_history.Count > 100)
            {
                for (var i = 100; i < _history.Count; i++)
                {
                    _historyByEncounterId.Remove(_history[i].EncounterId);
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

    public bool TryGetEncounter(Guid encounterId, [NotNullWhen(true)] out ArchivedEncounterRecord? record)
    {
        lock (_lock)
        {
            return _historyByEncounterId.TryGetValue(encounterId, out record);
        }
    }

    private static bool IsEquivalent(SceneCombatSnapshot left, SceneCombatSnapshot right)
    {
        return left.EncounterTime == right.EncounterTime &&
               left.EncounterId == right.EncounterId &&
               string.Equals(left.TargetName, right.TargetName, StringComparison.Ordinal) &&
               left.MapId == right.MapId &&
               left.MapInstanceId == right.MapInstanceId &&
               left.Combatants.Count == right.Combatants.Count &&
               SumDamage(left) == SumDamage(right);
    }

    private static double SumDamage(SceneCombatSnapshot snapshot)
    {
        var totalDamage = 0d;
        var combatants = snapshot.Combatants.AsSpan();
        foreach (ref readonly var entry in combatants)
        {
            totalDamage += entry.Metrics.DamageAmount;
        }

        return totalDamage;
    }
}
