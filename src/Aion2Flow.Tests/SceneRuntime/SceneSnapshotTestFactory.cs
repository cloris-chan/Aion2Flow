namespace Cloris.Aion2Flow.Tests.SceneRuntime;

internal static class SceneSnapshotTestFactory
{
    public static SceneCombatSnapshot Create(
        Guid? encounterId = null,
        long readModelRevision = 0,
        uint mapId = 0,
        uint mapInstanceId = 0,
        string targetName = "",
        long encounterStartTime = 0,
        long encounterEndTime = 0,
        long encounterTime = 0,
        IEnumerable<CombatantSnapshotEntry>? combatants = null,
        NpcRuntimeObservationSnapshot? targetObservation = null,
        EncounterSummarySnapshot? encounter = null,
        IEnumerable<SceneBossFocusSnapshot>? bossFocuses = null)
    {
        var combatantEntries = combatants?.ToArray() ?? [];
        Array.Sort(combatantEntries, static (left, right) => left.Id.CompareTo(right.Id));

        var bossFocusEntries = bossFocuses?.ToArray() ?? [];

        return new SceneCombatSnapshot(
            encounterId ?? Guid.NewGuid(),
            readModelRevision,
            mapId,
            mapInstanceId,
            targetName,
            encounterStartTime,
            encounterEndTime,
            encounterTime,
            combatantEntries,
            targetObservation,
            encounter ?? EncounterSummarySnapshot.Empty,
            bossFocusEntries);
    }

    public static CombatantSnapshotEntry Combatant(int id, SceneCombatantMetrics metrics)
    {
        return new CombatantSnapshotEntry(id, metrics);
    }
}
