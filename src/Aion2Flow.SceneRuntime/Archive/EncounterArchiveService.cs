using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.SceneRuntime.Archive;

public sealed class EncounterArchiveService
{
    public const int MaxHistoryCount = 10;

    private readonly Lock _lock = new();
    private readonly List<ArchivedEncounterRecord> _history = [];
    private readonly Dictionary<Guid, ArchivedEncounterRecord> _historyByEncounterId = [];
    private ImmutableArray<ArchivedEncounterRecord> _historySnapshot = [];

    public event EventHandler? HistoryChanged;

    public IReadOnlyList<ArchivedEncounterRecord> History => _historySnapshot;

    public ArchivedEncounterRecord? Archive(SceneCombatSnapshot snapshot, SceneArchivePayload payload, string trigger, bool isAutomatic)
    {
        return AddArchiveRecord(snapshot, payload, trigger, isAutomatic);
    }

    private ArchivedEncounterRecord? AddArchiveRecord(SceneCombatSnapshot archivedSnapshot, SceneArchivePayload scenePayload, string trigger, bool isAutomatic)
    {
        if (archivedSnapshot.EncounterTime <= 0 || archivedSnapshot.EncounterEndTime < archivedSnapshot.EncounterStartTime || archivedSnapshot.Combatants.Count == 0)
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
            if (_history.Count > MaxHistoryCount)
            {
                for (var i = MaxHistoryCount; i < _history.Count; i++)
                {
                    _historyByEncounterId.Remove(_history[i].EncounterId);
                }

                _history.RemoveRange(MaxHistoryCount, _history.Count - MaxHistoryCount);
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
               left.Encounter.TrackingTargetId == right.Encounter.TrackingTargetId &&
               (left.TargetObservation?.InstanceId ?? 0) == (right.TargetObservation?.InstanceId ?? 0) &&
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
