using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

internal static class SceneSnapshotTestFactory
{
    public static SceneCombatSnapshot Create(
        Guid? encounterId = null,
        long readModelRevision = 0,
        long sceneTransitionRevision = 0,
        uint mapId = 0,
        uint mapInstanceId = 0,
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
            sceneTransitionRevision,
            mapId,
            mapInstanceId,
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

    public static SceneCombatantMetrics VisibleMetrics(
        CharacterClass characterClass = CharacterClass.Gladiator,
        long damageAmount = 1,
        double damagePerSecond = 1,
        double damageContribution = 1)
    {
        return new SceneCombatantMetrics
        {
            CharacterClass = characterClass,
            IsVisiblePlayerCombatant = true,
            DamageAmount = damageAmount,
            DamagePerSecond = damagePerSecond,
            DamageContribution = damageContribution
        };
    }
}
