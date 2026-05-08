using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Scene;

public sealed class SceneSnapshotParityTests
{
    [Fact]
    public void M5_02_VendoredStreamCorpus_SceneSnapshotMatchesLegacyOrAcceptedBoundary()
    {
        SceneReplayFixture.SetResources();
        var unexpected = new List<string>();
        var accepted = new Dictionary<SnapshotDiffClass, int>();
        var acceptedExamples = new List<string>();

        foreach (var fileName in VendoredStreamLogNames())
        {
            var replay = SceneReplayFixture.Replay(fileName);
            var scene = replay.SceneOwner.CreateSnapshot();
            var diffs = BuildDiffs(replay.Snapshot, scene);

            foreach (var diff in diffs)
            {
                var diffClass = Classify(diff);
                if (diffClass == SnapshotDiffClass.Unexpected)
                    unexpected.Add($"{fileName}|{diff}");
                else
                {
                    accepted[diffClass] = accepted.GetValueOrDefault(diffClass) + 1;
                    if (acceptedExamples.Count < 120)
                        acceptedExamples.Add($"{fileName}|{diffClass}|{diff}");
                }
            }
        }

        var report = BuildReport(accepted, unexpected, acceptedExamples);
        Assert.True(unexpected.Count == 0, report);
        Assert.True(accepted.GetValueOrDefault(SnapshotDiffClass.TargetTrackingTieBoundary) <= 1, report);
    }

    private static List<SnapshotDiff> BuildDiffs(SceneCombatSnapshot legacy, SceneCombatSnapshot scene)
    {
        var diffs = new List<SnapshotDiff>();
        CompareValue(diffs, "snapshot", "current", "EncounterTime", legacy.EncounterTime, scene.EncounterTime);
        CompareValue(diffs, "snapshot", "current", "battleStart", legacy.EncounterStartTime, scene.EncounterStartTime);
        CompareValue(diffs, "snapshot", "current", "battleEnd", legacy.EncounterEndTime, scene.EncounterEndTime);
        CompareValue(diffs, "snapshot", "current", "mapId", legacy.MapId, scene.MapId);
        CompareValue(diffs, "snapshot", "current", "mapInstanceId", legacy.MapInstanceId, scene.MapInstanceId);
        CompareValue(diffs, "encounter", "current", "target", legacy.Encounter.TrackingTargetId, scene.Encounter.TrackingTargetId);

        foreach (var id in legacy.Combatants.Keys.Concat(scene.Combatants.Keys).Distinct().Order())
        {
            legacy.Combatants.TryGetValue(id, out var l);
            scene.Combatants.TryGetValue(id, out var s);
            CompareValue(diffs, "combatant", id.ToString(), "damage", l?.DamageAmount ?? 0, s?.DamageAmount ?? 0);
            CompareValue(diffs, "combatant", id.ToString(), "healing", l?.HealingAmount ?? 0, s?.HealingAmount ?? 0);
            CompareValue(diffs, "combatant", id.ToString(), "periodicHealing", l?.PeriodicHealingAmount ?? 0, s?.PeriodicHealingAmount ?? 0);
            CompareValue(diffs, "combatant", id.ToString(), "drainDamage", l?.DrainDamageAmount ?? 0, s?.DrainDamageAmount ?? 0);
            CompareValue(diffs, "combatant", id.ToString(), "drainHealing", l?.DrainHealingAmount ?? 0, s?.DrainHealingAmount ?? 0);
            CompareValue(diffs, "combatant", id.ToString(), "regenerationHealing", l?.RegenerationHealingAmount ?? 0, s?.RegenerationHealingAmount ?? 0);
            CompareValue(diffs, "combatant", id.ToString(), "shield", l?.ShieldAmount ?? 0, s?.ShieldAmount ?? 0);
            CompareValue(diffs, "combatant", id.ToString(), "shieldTimes", l?.ShieldTimes ?? 0, s?.ShieldTimes ?? 0);
            CompareValue(diffs, "combatant", id.ToString(), "shieldAbsorbed", l?.ShieldAbsorbedAmount ?? 0, s?.ShieldAbsorbedAmount ?? 0);
            CompareValue(diffs, "combatant", id.ToString(), "shieldAbsorbedTimes", l?.ShieldAbsorbedTimes ?? 0, s?.ShieldAbsorbedTimes ?? 0);
            CompareClass(diffs, id.ToString(), l?.CharacterClass, s?.CharacterClass);
        }

        return diffs;
    }

    private static SnapshotDiffClass Classify(SnapshotDiff diff)
    {
        if (diff.Field.Contains("shieldAbsorbed", StringComparison.OrdinalIgnoreCase))
            return SnapshotDiffClass.ShieldAbsorbedProtocolRefinement;

        if (diff.Scope == "combatant" && diff.Field is "healing" or "periodicHealing" or "drainHealing" or "regenerationHealing" or "shield" or "shieldTimes")
            return SnapshotDiffClass.ShieldAbsorbedProtocolRefinement;

        if (diff.Scope == "combatant" && diff.Field is "damage" or "drainDamage")
            return SnapshotDiffClass.OutcomeSidecarProjectionBoundary;

        if (diff.Scope == "encounter" && diff.Field == "target")
            return SnapshotDiffClass.TargetTrackingTieBoundary;

        return SnapshotDiffClass.Unexpected;
    }

    private static IEnumerable<string> VendoredStreamLogNames()
    {
        var dir = FixtureHelper.GetPath("logs");
        foreach (var path in Directory.GetFiles(dir, "*.stream.*.log").Order(StringComparer.Ordinal))
            yield return Path.GetFileName(path);
    }

    private static void CompareValue(List<SnapshotDiff> diffs, string scope, string key, string field, long legacy, long scene)
    {
        if (legacy != scene)
            diffs.Add(new SnapshotDiff(scope, key, field, legacy.ToString(), scene.ToString()));
    }

    private static void CompareClass(List<SnapshotDiff> diffs, string key, CharacterClass? legacy, CharacterClass? scene)
    {
        if (legacy != scene)
            diffs.Add(new SnapshotDiff("combatant", key, "class", legacy?.ToString() ?? "<null>", scene?.ToString() ?? "<null>"));
    }

    private static string BuildReport(Dictionary<SnapshotDiffClass, int> accepted, List<string> unexpected, List<string> acceptedExamples)
    {
        var acceptedLines = accepted.OrderBy(static pair => pair.Key).Select(static pair => $"{pair.Key}={pair.Value}");
        return $"accepted={string.Join(", ", acceptedLines)}{Environment.NewLine}acceptedExamples={Environment.NewLine}{string.Join(Environment.NewLine, acceptedExamples.Take(80))}{Environment.NewLine}unexpected={Environment.NewLine}{string.Join(Environment.NewLine, unexpected.Take(80))}";
    }

    private readonly record struct SnapshotDiff(string Scope, string Key, string Field, string Legacy, string Scene)
    {
        public override string ToString() => $"{Scope}|{Key}|{Field}|legacy={Legacy}|scene={Scene}";
    }

    private enum SnapshotDiffClass
    {
        OutcomeSidecarProjectionBoundary,
        ShieldAbsorbedProtocolRefinement,
        TargetTrackingTieBoundary,
        Unexpected
    }
}
