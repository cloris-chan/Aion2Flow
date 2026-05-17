using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Canonicalization;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class CrossModuleProtocolParityHarnessTests
{
    [Fact]
    public void M4_12_VendoredStreamCorpus_ContainsProtocolEvidenceForMigratedModules()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var evidence = new ProtocolEvidence();

        foreach (var fileName in VendoredStreamLogNames())
        {
            var replay = ReplayWithSceneJournal(fileName);
            evidence.Observe(replay.SceneJournal);
        }

        var missing = evidence.GetMissingLabels();
        Assert.True(missing.Count == 0, evidence.FormatMissing(missing));
    }

    [Fact]
    public void M4_12_SelectedReplays_MigratedModuleFactsMatchSceneCorpusExpectations()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var diffs = new List<string>();

        AssertSystemRecoverySceneInvariant(diffs, "aion2flow.stream.20260426140354.log");
        AssertPeriodicLinkSceneInvariant(diffs, "aion2flow.stream.20260412103519.log");
        AssertCompactPrimarySceneInvariant(diffs, "aion2flow.stream.20260411174533.log");
        AssertCompactPrimarySceneInvariant(diffs, "aion2flow.stream.20260411215842.log");
        AssertMapIdentitySceneInvariant(diffs, "aion2flow.stream.20260419204630.log");
        AssertShieldAbsorbedSceneInvariant(diffs, "aion2flow.stream.20260411192501.log");

        Assert.True(diffs.Count == 0, string.Join(Environment.NewLine, diffs));
    }

    [Fact]
    public void M4_12_FullAggregateDiffHarness_ReportsNoSceneReplayOwnerDrift()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var unexpected = new List<string>();

        foreach (var fileName in VendoredStreamLogNames())
        {
            var replay = ReplayWithSceneJournal(fileName);
            var scene = ApplyScene(replay.SceneJournal);
            var diffs = BuildAggregateDiffs(replay, scene);

            foreach (var diff in diffs)
                unexpected.Add($"{fileName}|{diff}");
        }

        Assert.True(unexpected.Count == 0, string.Join(Environment.NewLine, unexpected.Take(80)));
    }

    private static void AssertSystemRecoverySceneInvariant(List<string> diffs, string fileName)
    {
        var replay = ReplayWithSceneJournal(fileName);
        var canonicalizer = new SystemPeriodicRecoveryCanonicalizer();
        var entries = CopyEntries(replay.SceneJournal);
        var scene = entries
            .Where(IsRawSystemPeriodicRecoveryEntry)
            .Select(entry =>
            {
                var stamp = entry.Stamp;
                var observation = entry.Combat!.Value;
                return canonicalizer.Normalize(entry.SourceEntityId, entry.TargetEntityId, in stamp, in observation);
            })
            .Where(static result => result.Observation.ValueKind == CombatValueKind.PeriodicHealing)
            .GroupBy(static result => result.SourceId)
            .ToDictionary(static group => group.Key, static group => group.Sum(static result => result.Observation.Damage));

        if (!scene.TryGetValue(10744, out var healing) || healing <= 0)
            diffs.Add($"{fileName}|system-recovery-healing|source=10744|expectedPositive|actual={healing}");
    }

    private static void AssertPeriodicLinkSceneInvariant(List<string> diffs, string fileName)
    {
        var replay = ReplayWithSceneJournal(fileName);
        var expected = SceneReplayTestView.Packets(replay)
            .Where(static packet => packet.EffectTag == PacketEffectTag.PeriodicLinkInvincible)
            .GroupBy(static packet => new PairKey(packet.SourceId, packet.TargetId))
            .ToDictionary(static group => group.Key, static group => group.Sum(static packet => packet.AttemptContribution));
        var scene = ApplyScene(replay.SceneJournal).Combat.Pairs
            .Where(static pair => pair.Value.InvincibleCount > 0)
            .ToDictionary(static pair => new PairKey(pair.Key.Source, pair.Key.Target), static pair => pair.Value.InvincibleCount);

        if (expected.Count == 0)
            diffs.Add($"{fileName}|periodic-link|missing scene periodic link evidence");

        foreach (var (pair, expectedCount) in expected)
        {
            scene.TryGetValue(pair, out var actual);
            if (actual < expectedCount)
                diffs.Add($"{fileName}|periodic-link|{pair.SourceId}->{pair.TargetId}|expectedAtLeast={expectedCount}|scene={actual}");
        }
    }

    private static void AssertCompactPrimarySceneInvariant(List<string> diffs, string fileName)
    {
        var replay = ReplayWithSceneJournal(fileName);
        var combat = ApplyScene(replay.SceneJournal).Combat;
        var primary = replay.Combatants
            .OrderByDescending(static summary => summary.IncomingEvades + summary.IncomingInvincibles)
            .ThenByDescending(static summary => summary.IncomingDamage)
            .First();

        if (!combat.TryGetCombatant(primary.CombatantId, out var scenePrimary))
        {
            diffs.Add($"{fileName}|compact-primary|missing scene combatant {primary.CombatantId}");
            return;
        }

        if (scenePrimary!.IncomingEvades != primary.IncomingEvades)
            diffs.Add($"{fileName}|compact-primary|incomingEvades|summary={primary.IncomingEvades}|scene={scenePrimary.IncomingEvades}");
    }

    private static void AssertMapIdentitySceneInvariant(List<string> diffs, string fileName)
    {
        var replay = ReplayWithSceneJournal(fileName);
        var metadata = ApplyScene(replay.SceneJournal).Boundary;
        if (metadata.CurrentMapId != replay.SceneOwner.Boundary.CurrentMapId)
            diffs.Add($"{fileName}|map-identity|mapId|owner={replay.SceneOwner.Boundary.CurrentMapId}|scene={metadata.CurrentMapId}");
        if (metadata.CurrentMapInstanceId != replay.SceneOwner.Boundary.CurrentMapInstanceId)
            diffs.Add($"{fileName}|map-identity|mapInstanceId|owner={replay.SceneOwner.Boundary.CurrentMapInstanceId}|scene={metadata.CurrentMapInstanceId}");
    }

    private static void AssertShieldAbsorbedSceneInvariant(List<string> diffs, string fileName)
    {
        var replay = ReplayWithSceneJournal(fileName);
        var scene = ApplyScene(replay.SceneJournal).Combat;
        var packetShieldAbsorbed = SceneReplayTestView.Packets(replay)
            .Where(static packet => packet.ValueKind == CombatValueKind.Shield && packet.EffectTag == PacketEffectTag.ShieldAbsorbed)
            .Sum(static packet => packet.Damage);
        var sceneShieldAbsorbed = scene.Combatants.Values.Sum(static combatant => combatant.OutgoingShieldAbsorbed);

        if (packetShieldAbsorbed <= 0)
            diffs.Add($"{fileName}|shield-absorbed-refinement|packet expected positive, got {packetShieldAbsorbed}");
        if (sceneShieldAbsorbed <= 0)
            diffs.Add($"{fileName}|shield-absorbed-refinement|scene expected positive, got {sceneShieldAbsorbed}");
    }

    private static AppliedScene ApplyScene(ObservedEventJournal journal)
    {
        var entities = new EntityStore();
        var metadata = new SceneBoundaryStore();
        var combat = new CombatStore();
        var applier = new DomainEventApplier(entities, metadata, combat);
        applier.ApplyJournal(journal);
        return new AppliedScene(entities, metadata, combat, applier);
    }

    private static ObservedEventEnvelope[] CopyEntries(ObservedEventJournal journal)
    {
        var entries = new ObservedEventEnvelope[journal.Count];
        var result = journal.CopyEntries(journal.CreateCursor(journal.FirstObservationOrdinal), entries);
        return entries.AsSpan(0, result.Count).ToArray();
    }

    private static PacketLogReplayResult ReplayWithSceneJournal(string fileName)
    {
        try
        {
            return PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{fileName}"));
        }
        finally
        {
        }
    }

    private static List<AggregateDiff> BuildAggregateDiffs(PacketLogReplayResult replay, AppliedScene scene)
    {
        var diffs = new List<AggregateDiff>();
        CompareCombatants(diffs, BuildSceneCombatants(replay.SceneOwner.Combat), BuildSceneCombatants(scene.Combat));
        ComparePairs(diffs, BuildScenePairs(replay.SceneOwner.Combat), BuildScenePairs(scene.Combat));
        CompareValue(diffs, "metadata", "current", "mapId", replay.SceneOwner.Boundary.CurrentMapId, scene.Boundary.CurrentMapId);
        CompareValue(diffs, "metadata", "current", "mapInstanceId", replay.SceneOwner.Boundary.CurrentMapInstanceId, scene.Boundary.CurrentMapInstanceId);
        return diffs;
    }

    private static Dictionary<int, CombatantTotals> BuildSceneCombatants(CombatStore combat)
    {
        var totals = new Dictionary<int, CombatantTotals>();
        foreach (var (id, combatant) in combat.Combatants)
        {
            totals[id] = new CombatantTotals
            {
                OutgoingDamage = combatant.OutgoingDamage,
                IncomingDamage = combatant.IncomingDamage,
                OutgoingHealing = combatant.OutgoingHealing,
                IncomingHealing = combatant.IncomingHealing,
                OutgoingShield = combatant.OutgoingShield,
                IncomingShield = combatant.IncomingShield,
                OutgoingShieldAbsorbed = combatant.OutgoingShieldAbsorbed,
                IncomingShieldAbsorbed = combatant.IncomingShieldAbsorbed,
                OutgoingHits = combatant.OutgoingHits,
                IncomingHits = combatant.IncomingHits,
                OutgoingAttempts = combatant.OutgoingAttempts,
                IncomingAttempts = combatant.IncomingAttempts,
                OutgoingEvades = combatant.OutgoingEvades,
                IncomingEvades = combatant.IncomingEvades,
                OutgoingInvincibles = combatant.OutgoingInvincibles,
                IncomingInvincibles = combatant.IncomingInvincibles,
                OutgoingMultiHits = combatant.OutgoingMultiHits,
                IncomingMultiHits = combatant.IncomingMultiHits
            };
        }
        return totals;
    }

    private static Dictionary<PairKey, PairTotals> BuildScenePairs(CombatStore combat)
    {
        var totals = new Dictionary<PairKey, PairTotals>();
        foreach (var (key, pair) in combat.Pairs)
        {
            totals[new PairKey(key.Source, key.Target)] = new PairTotals
            {
                Damage = pair.TotalDamage,
                Healing = pair.TotalHealing,
                Shield = pair.TotalShield,
                ShieldAbsorbed = pair.TotalShieldAbsorbed,
                Hits = pair.HitCount,
                Attempts = pair.AttemptCount,
                Evades = pair.EvadeCount,
                Invincibles = pair.InvincibleCount,
                MultiHits = pair.MultiHitCount
            };
        }
        return totals;
    }

    private static void CompareCombatants(List<AggregateDiff> diffs, Dictionary<int, CombatantTotals> legacy, Dictionary<int, CombatantTotals> scene)
    {
        foreach (var id in legacy.Keys.Concat(scene.Keys).Distinct().Order())
        {
            var legacyValue = legacy.TryGetValue(id, out var l) ? l : new CombatantTotals();
            var sceneValue = scene.TryGetValue(id, out var s) ? s : new CombatantTotals();
            var key = id.ToString();
            CompareValue(diffs, "combatant", key, "outgoingDamage", legacyValue.OutgoingDamage, sceneValue.OutgoingDamage);
            CompareValue(diffs, "combatant", key, "incomingDamage", legacyValue.IncomingDamage, sceneValue.IncomingDamage);
            CompareValue(diffs, "combatant", key, "outgoingHealing", legacyValue.OutgoingHealing, sceneValue.OutgoingHealing);
            CompareValue(diffs, "combatant", key, "incomingHealing", legacyValue.IncomingHealing, sceneValue.IncomingHealing);
            CompareValue(diffs, "combatant", key, "outgoingShield", legacyValue.OutgoingShield, sceneValue.OutgoingShield);
            CompareValue(diffs, "combatant", key, "incomingShield", legacyValue.IncomingShield, sceneValue.IncomingShield);
            CompareValue(diffs, "combatant", key, "outgoingShieldAbsorbed", legacyValue.OutgoingShieldAbsorbed, sceneValue.OutgoingShieldAbsorbed);
            CompareValue(diffs, "combatant", key, "incomingShieldAbsorbed", legacyValue.IncomingShieldAbsorbed, sceneValue.IncomingShieldAbsorbed);
            CompareValue(diffs, "combatant", key, "outgoingHits", legacyValue.OutgoingHits, sceneValue.OutgoingHits);
            CompareValue(diffs, "combatant", key, "incomingHits", legacyValue.IncomingHits, sceneValue.IncomingHits);
            CompareValue(diffs, "combatant", key, "outgoingAttempts", legacyValue.OutgoingAttempts, sceneValue.OutgoingAttempts);
            CompareValue(diffs, "combatant", key, "incomingAttempts", legacyValue.IncomingAttempts, sceneValue.IncomingAttempts);
            CompareValue(diffs, "combatant", key, "outgoingEvades", legacyValue.OutgoingEvades, sceneValue.OutgoingEvades);
            CompareValue(diffs, "combatant", key, "incomingEvades", legacyValue.IncomingEvades, sceneValue.IncomingEvades);
            CompareValue(diffs, "combatant", key, "outgoingInvincibles", legacyValue.OutgoingInvincibles, sceneValue.OutgoingInvincibles);
            CompareValue(diffs, "combatant", key, "incomingInvincibles", legacyValue.IncomingInvincibles, sceneValue.IncomingInvincibles);
            CompareValue(diffs, "combatant", key, "outgoingMultiHits", legacyValue.OutgoingMultiHits, sceneValue.OutgoingMultiHits);
            CompareValue(diffs, "combatant", key, "incomingMultiHits", legacyValue.IncomingMultiHits, sceneValue.IncomingMultiHits);
        }
    }

    private static void ComparePairs(List<AggregateDiff> diffs, Dictionary<PairKey, PairTotals> legacy, Dictionary<PairKey, PairTotals> scene)
    {
        foreach (var key in legacy.Keys.Concat(scene.Keys).Distinct().OrderBy(static key => key.SourceId).ThenBy(static key => key.TargetId))
        {
            var legacyValue = legacy.TryGetValue(key, out var l) ? l : new PairTotals();
            var sceneValue = scene.TryGetValue(key, out var s) ? s : new PairTotals();
            var label = $"{key.SourceId}->{key.TargetId}";
            CompareValue(diffs, "pair", label, "damage", legacyValue.Damage, sceneValue.Damage);
            CompareValue(diffs, "pair", label, "healing", legacyValue.Healing, sceneValue.Healing);
            CompareValue(diffs, "pair", label, "shield", legacyValue.Shield, sceneValue.Shield);
            CompareValue(diffs, "pair", label, "shieldAbsorbed", legacyValue.ShieldAbsorbed, sceneValue.ShieldAbsorbed);
            CompareValue(diffs, "pair", label, "hits", legacyValue.Hits, sceneValue.Hits);
            CompareValue(diffs, "pair", label, "attempts", legacyValue.Attempts, sceneValue.Attempts);
            CompareValue(diffs, "pair", label, "evades", legacyValue.Evades, sceneValue.Evades);
            CompareValue(diffs, "pair", label, "invincibles", legacyValue.Invincibles, sceneValue.Invincibles);
            CompareValue(diffs, "pair", label, "multiHits", legacyValue.MultiHits, sceneValue.MultiHits);
        }
    }

    private static void CompareValue(List<AggregateDiff> diffs, string scope, string key, string field, long legacy, long scene)
    {
        if (legacy != scene)
            diffs.Add(new AggregateDiff(scope, key, field, legacy, scene));
    }

    private static void CompareDictionaries(List<string> diffs, string fileName, string label, Dictionary<int, long> legacy, Dictionary<int, long> scene)
    {
        foreach (var key in legacy.Keys.Concat(scene.Keys).Distinct().Order())
        {
            legacy.TryGetValue(key, out var l);
            scene.TryGetValue(key, out var s);
            if (l != s)
                diffs.Add($"{fileName}|{label}|{key}|baseline={l}|scene={s}");
        }
    }

    private static IEnumerable<string> VendoredStreamLogNames()
    {
        var dir = FixtureHelper.GetPath("logs");
        foreach (var path in Directory.GetFiles(dir, "*.stream.*.log").Order(StringComparer.Ordinal))
            yield return Path.GetFileName(path);
    }

    private static bool IsRawSystemPeriodicRecoveryEntry(ObservedEventEnvelope entry)
    {
        if (entry.Domain != ObservedEventDomain.Combat || entry.SourceEntityId != entry.TargetEntityId || entry.Combat is not { } observation || observation.PeriodicRelation != PeriodicEffectRelation.Self || observation.PeriodicMode is not (1 or 2))
            return false;

        var originalSkillCode = observation.OriginalSkillCode != 0 ? observation.OriginalSkillCode : observation.SkillCode;
        return CombatResourceRegistry.ParseSkillVariant(originalSkillCode).BaseSkillCode == 190000000;
    }

    private static string BuildAggregateDiffReport(Dictionary<AggregateDiffClass, int> accepted, List<string> unexpected)
    {
        var acceptedLines = accepted.OrderBy(static pair => pair.Key).Select(static pair => $"{pair.Key}={pair.Value}");
        return $"accepted={string.Join(", ", acceptedLines)}{Environment.NewLine}{string.Join(Environment.NewLine, unexpected.Take(80))}";
    }

    private sealed class ProtocolEvidence
    {
        public int Combat;
        public int State;
        public int Aura;
        public int Resource;
        public int Scene;
        public int Summon;
        public int ExtendedNpcState;
        public int PeriodicChain;
        public int SystemRecovery;
        public int MultiHit;
        public int Compact0438;
        public int Compact0238;
        public int Compact0638;
        public int PeriodicLink;
        public int BossFocus;
        public int MapStaging;
        public int SceneArrival;

        public void Observe(ObservedEventJournal journal)
        {
            for (var i = 0; i < journal.Count; i++)
            {
                var entry = journal.Read(i);
                switch (entry.Domain)
                {
                    case ObservedEventDomain.Combat when entry.Combat is { } combat:
                        Combat++;
                        ObserveCombat(in entry, in combat);
                        break;
                    case ObservedEventDomain.State when entry.State is { } state:
                        State++;
                        ObserveState(in entry, in state);
                        break;
                    case ObservedEventDomain.Aura when entry.Aura is { } aura:
                        Aura++;
                        ObserveAura(in entry, in aura);
                        break;
                    case ObservedEventDomain.Resource:
                        Resource++;
                        BossFocus++;
                        break;
                    case ObservedEventDomain.Scene when entry.Scene is { } scene:
                        Scene++;
                        ObserveScene(in scene);
                        break;
                }
            }
        }

        public List<string> GetMissingLabels()
        {
            var missing = new List<string>();
            AddMissing(missing, "combat journal", Combat);
            AddMissing(missing, "state journal", State);
            AddMissing(missing, "aura journal", Aura);
            AddMissing(missing, "resource journal", Resource);
            AddMissing(missing, "scene journal", Scene);
            AddMissing(missing, "summon owner context", Summon);
            AddMissing(missing, "extended npc state", ExtendedNpcState);
            AddMissing(missing, "periodic chain 0538 mode 9/10/11", PeriodicChain);
            AddMissing(missing, "system periodic recovery seed/tick", SystemRecovery);
            AddMissing(missing, "multi-hit evidence", MultiHit);
            AddMissing(missing, "compact 0438 sidecar", Compact0438);
            AddMissing(missing, "compact 0238 control", Compact0238);
            AddMissing(missing, "compact 0638 control", Compact0638);
            AddMissing(missing, "periodic link mode 48", PeriodicLink);
            AddMissing(missing, "boss focus evidence", BossFocus);
            AddMissing(missing, "map staging", MapStaging);
            AddMissing(missing, "scene arrival", SceneArrival);
            return missing;
        }

        public string Format() =>
            $"evidence combat={Combat} state={State} aura={Aura} resource={Resource} scene={Scene} summon={Summon} extendedNpcState={ExtendedNpcState} periodicChain={PeriodicChain} systemRecovery={SystemRecovery} multiHit={MultiHit} compact0438={Compact0438} compact0238={Compact0238} compact0638={Compact0638} periodicLink={PeriodicLink} bossFocus={BossFocus} mapStaging={MapStaging} sceneArrival={SceneArrival}";

        public string FormatMissing(List<string> missing) =>
            $"{Format()}{Environment.NewLine}missing={string.Join(", ", missing)}";

        private void ObserveCombat(in ObservedEventEnvelope entry, in CombatObservation combat)
        {
            if (combat.PeriodicMode is 9 or 10 or 11)
                PeriodicChain++;
            if ((combat.OriginalSkillCode != 0 ? combat.OriginalSkillCode : combat.SkillCode) / 1000000 == 190)
                SystemRecovery++;
            if ((combat.Modifiers & DamageModifiers.MultiHit) != 0 || combat.MultiHitCount > 0)
                MultiHit++;
            if (entry.Raw.Opcode == 0x0438 && combat.Type is 1 or 2)
                Compact0438++;
            if (entry.Raw.Opcode == 0x0238)
                Compact0238++;
            if (entry.Raw.Opcode == 0x0638)
                Compact0638++;
            if (entry.Raw.Opcode == 0x0538 && combat.Type == 48)
                PeriodicLink++;
        }

        private void ObserveState(in ObservedEventEnvelope entry, in StateObservation state)
        {
            if (entry.TargetEntityId != 0 && state.EntityId == entry.TargetEntityId && entry.SourceEntityId != entry.TargetEntityId)
                Summon++;
            if (state.StateCode is 2136 or 0140 or 0240 or 4636)
                ExtendedNpcState++;
            if (state.StateCode is StateCodes.NpcKind or StateCodes.NpcBattle or StateCodes.NpcBattleToggle)
                BossFocus++;
        }

        private void ObserveAura(in ObservedEventEnvelope entry, in AuraObservation aura)
        {
            if (entry.Raw.Opcode == 0x2C38 && aura.ResultCode == 11)
                MultiHit++;
        }

        private void ObserveScene(in SceneObservation scene)
        {
            if (scene.DiagnosticKey is "stage-destination-map"
                or "stage-destination-instance"
                or "pending-destination-map"
                or "confirm-destination-map"
                or "confirm-destination-instance")
            {
                MapStaging++;
            }
            if (scene.DiagnosticKey == "scene-arrival")
                SceneArrival++;
        }

        private static void AddMissing(List<string> missing, string label, int count)
        {
            if (count == 0)
                missing.Add(label);
        }
    }

    private sealed class CombatantTotals
    {
        public long OutgoingDamage { get; set; }
        public long IncomingDamage { get; set; }
        public long OutgoingHealing { get; set; }
        public long IncomingHealing { get; set; }
        public long OutgoingShield { get; set; }
        public long IncomingShield { get; set; }
        public long OutgoingShieldAbsorbed { get; set; }
        public long IncomingShieldAbsorbed { get; set; }
        public int OutgoingHits { get; set; }
        public int IncomingHits { get; set; }
        public int OutgoingAttempts { get; set; }
        public int IncomingAttempts { get; set; }
        public int OutgoingEvades { get; set; }
        public int IncomingEvades { get; set; }
        public int OutgoingInvincibles { get; set; }
        public int IncomingInvincibles { get; set; }
        public int OutgoingMultiHits { get; set; }
        public int IncomingMultiHits { get; set; }
    }

    private sealed class PairTotals
    {
        public long Damage { get; set; }
        public long Healing { get; set; }
        public long Shield { get; set; }
        public long ShieldAbsorbed { get; set; }
        public int Hits { get; set; }
        public int Attempts { get; set; }
        public int Evades { get; set; }
        public int Invincibles { get; set; }
        public int MultiHits { get; set; }
    }

    private readonly record struct PairKey(int SourceId, int TargetId);

    private readonly record struct AggregateDiff(string Scope, string Key, string Field, long Baseline, long Scene)
    {
        public override string ToString() => $"{Scope}|{Key}|{Field}|baseline={Baseline}|scene={Scene}|delta={Scene - Baseline}";
    }

    private sealed record AppliedScene(EntityStore Entities, SceneBoundaryStore Boundary, CombatStore Combat, DomainEventApplier Applier);

    private enum AggregateDiffClass
    {
        BattleWindowAndSummonProjectionBoundary,
        OutcomeSidecarProjectionBoundary,
        ShieldAbsorbedProtocolRefinement,
        Unexpected
    }
}
