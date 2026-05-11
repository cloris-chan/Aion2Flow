using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.SceneRuntime.Projection;

internal sealed class SceneCombatSnapshotBuilder
{
    private readonly Dictionary<int, SceneCombatantMetricsAccumulator> _combatants = [];
    private readonly List<SceneBossFocusSnapshot> _bossFocuses = [];

    public Guid EncounterId { get; private set; }

    public uint MapId { get; private set; }

    public uint MapInstanceId { get; private set; }

    public string TargetName { get; private set; } = string.Empty;

    public long EncounterStartTime { get; private set; }

    public long EncounterEndTime { get; private set; }

    public long EncounterTime { get; private set; }

    public NpcRuntimeObservationSnapshot? TargetObservation { get; private set; }

    public EncounterSummarySnapshot Encounter { get; private set; } = EncounterSummarySnapshot.Empty;

    public int CombatantCount => _combatants.Count;

    public Dictionary<int, SceneCombatantMetricsAccumulator>.KeyCollection CombatantIds => _combatants.Keys;

    public void Reset(Guid encounterId, int combatantCapacity, int bossFocusCapacity)
    {
        _combatants.Clear();
        _bossFocuses.Clear();
        _combatants.EnsureCapacity(Math.Max(0, combatantCapacity));
        _bossFocuses.EnsureCapacity(Math.Max(0, bossFocusCapacity));

        EncounterId = encounterId == default ? Guid.NewGuid() : encounterId;
        MapId = 0;
        MapInstanceId = 0;
        TargetName = string.Empty;
        EncounterStartTime = 0;
        EncounterEndTime = 0;
        EncounterTime = 0;
        TargetObservation = null;
        Encounter = EncounterSummarySnapshot.Empty;
    }

    public void SetMap(uint mapId, uint mapInstanceId)
    {
        MapId = mapId;
        MapInstanceId = mapInstanceId;
    }

    public void SetTarget(string targetName, NpcRuntimeObservationSnapshot? targetObservation)
    {
        TargetName = targetName;
        TargetObservation = targetObservation;
    }

    public void SetEncounterWindow(long start, long end, long time)
    {
        EncounterStartTime = start;
        EncounterEndTime = end;
        EncounterTime = time;
    }

    public void SetEncounter(EncounterSummarySnapshot encounter)
    {
        Encounter = encounter;
    }

    public void AddBossFocus(SceneBossFocusSnapshot focus)
    {
        _bossFocuses.Add(focus);
    }

    public ref SceneCombatantMetricsAccumulator GetOrAddCombatant(int combatantId, string nickname)
    {
        ref var metrics = ref CollectionsMarshal.GetValueRefOrAddDefault(_combatants, combatantId, out var exists);
        if (!exists)
        {
            metrics = new SceneCombatantMetricsAccumulator(nickname);
        }

        return ref metrics;
    }

    public ref SceneCombatantMetricsAccumulator GetExistingCombatant(int combatantId)
    {
        ref var metrics = ref CollectionsMarshal.GetValueRefOrNullRef(_combatants, combatantId);
        if (Unsafe.IsNullRef(ref metrics))
        {
            throw new KeyNotFoundException($"The combatant id '{combatantId}' was not found in the snapshot builder.");
        }

        return ref metrics;
    }

    public SceneCombatSnapshot ToSnapshot(long readModelRevision)
    {
        if (_combatants.Count == 0 &&
            _bossFocuses.Count == 0 &&
            readModelRevision == 0 &&
            MapId == 0 &&
            MapInstanceId == 0 &&
            TargetName.Length == 0 &&
            EncounterStartTime == 0 &&
            EncounterEndTime == 0 &&
            EncounterTime == 0 &&
            TargetObservation is null &&
            Encounter.Equals(EncounterSummarySnapshot.Empty) &&
            EncounterId == Guid.Empty)
        {
            return SceneCombatSnapshot.Empty;
        }

        var entries = _combatants.Count == 0
            ? []
            : new CombatantSnapshotEntry[_combatants.Count];
        if (entries.Length > 0)
        {
            var index = 0;
            foreach (var (combatantId, accumulator) in _combatants)
            {
                entries[index++] = new CombatantSnapshotEntry(combatantId, accumulator.ToSnapshot());
            }

            Array.Sort(entries, static (left, right) => left.Id.CompareTo(right.Id));
        }

        var bossFocuses = _bossFocuses.Count == 0
            ? []
            : _bossFocuses.ToArray();

        return new SceneCombatSnapshot(
            EncounterId,
            readModelRevision,
            MapId,
            MapInstanceId,
            TargetName,
            EncounterStartTime,
            EncounterEndTime,
            EncounterTime,
            entries,
            TargetObservation,
            Encounter,
            bossFocuses);
    }
}
