using System.Globalization;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketLogReplayServiceTests
{
    [Fact]
    public void Stream_Replay_Runtime_Sink_Path_Matches_Direct_Scene_Processor()
    {
        CombatResourceRegistry.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var entries = new[]
        {
            CreateStreamReplayEntry("2026-05-02T15:52:39.1861829+08:00", "state/2136-boss-scene-200003.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:40.0000000+08:00", "nickname/3336-own-thanks.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:41.0000000+08:00", "combat/0438-damage.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:42.0000000+08:00", "combat/0538-dot.hex")
        };

        var path = WriteTempReplayLog("stream", [.. entries.Select(BuildStreamReplayLine)]);
        try
        {
            var sinkReplay = PacketLogReplayService.Replay(path);
            var directReplay = ReplayStreamEntriesWithSceneProcessor(entries);

            AssertStreamReplayParity(directReplay, sinkReplay);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Scene_Replay_Is_Deterministic()
    {
        CombatResourceRegistry.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var entries = new[]
        {
            CreateStreamReplayEntry("2026-05-02T15:52:39.1861829+08:00", "state/2136-boss-scene-200003.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:40.0000000+08:00", "nickname/3336-own-thanks.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:41.0000000+08:00", "combat/0438-damage.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:42.0000000+08:00", "combat/0538-dot.hex")
        };

        var path = WriteTempReplayLog("stream", [.. entries.Select(BuildStreamReplayLine)]);
        try
        {
            var first = PacketLogReplayService.Replay(path);
            var second = PacketLogReplayService.Replay(path);

            AssertSnapshotParity(first.Snapshot, second.Snapshot);
            Assert.Equal(first.ReplayedLines, second.ReplayedLines);
            Assert.Equal(first.SkippedLines, second.SkippedLines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Replay_Records_Baseline_Counters_For_Ingest_Snapshot_And_Summary()
    {
        CombatResourceRegistry.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var entries = new[]
        {
            CreateStreamReplayEntry("2026-05-02T15:52:39.1861829+08:00", "state/2136-boss-scene-200003.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:40.0000000+08:00", "nickname/3336-own-thanks.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:41.0000000+08:00", "combat/0438-damage.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:42.0000000+08:00", "combat/0538-dot.hex")
        };

        var path = WriteTempReplayLog("stream", [.. entries.Select(BuildStreamReplayLine)]);
        try
        {
            var replay = PacketLogReplayService.Replay(path);

            Assert.NotSame(PacketLogReplayBaselineCounters.Empty, replay.BaselineCounters);
            AssertBaselineCounter(replay.BaselineCounters.ReplayIngest);
            AssertBaselineCounter(replay.BaselineCounters.SnapshotCreation);
            AssertBaselineCounter(replay.BaselineCounters.CombatantSummaryCreation);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("aion2flow.stream.20260411174533.log", 3, 0)]
    [InlineData("aion2flow.stream.20260411174739.log", 0, 3)]
    [InlineData("aion2flow.stream.20260411184521.log", 2, 2)]
    [InlineData("aion2flow.stream.20260411192501.log", 6, 1)]
    [InlineData("aion2flow.stream.20260411205158.log", 3, 2)]
    [InlineData("aion2flow.stream.20260411210634.log", 5, 0)]
    [InlineData("aion2flow.stream.20260411212441.log", 1, 0)]
    [InlineData("aion2flow.stream.20260411215842.log", 7, 0)]
    [InlineData("aion2flow.stream.20260411232425.log", 10, 3)]
    [InlineData("aion2flow.stream.20260411235759.log", 1, 1)]
    [InlineData("aion2flow.stream.20260412103519.log", 18, 7)]
    [InlineData("aion2flow.stream.20260412110721.log", 10, 7)]
    public void Replay_Reconstructs_April11_Incoming_Avoidance_Ground_Truth_From_Stream_Log(string fileName, int expectedEvades, int expectedInvincibles)
    {
        CombatResourceRegistry.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{fileName}"));

        Assert.True(replay.ReplayedLines > 0);

        var primary = replay.Combatants
            .OrderByDescending(static summary => summary.IncomingEvades + summary.IncomingInvincibles)
            .ThenByDescending(static summary => summary.IncomingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        Assert.True(primary.IncomingEvades == expectedEvades, summaryDump);
        Assert.True(primary.IncomingInvincibles == expectedInvincibles, summaryDump);
    }

    [Theory]
    [InlineData("aion2flow.stream.20260412103519.log", 18, 7)]
    [InlineData("aion2flow.stream.20260412110721.log", 10, 7)]
    public void Replay_Reconstructs_Reported_MultiSource_Invincibles_With_Full_Skill_Map(string fileName, int expectedEvades, int expectedInvincibles)
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{fileName}"));

        Assert.True(replay.ReplayedLines > 0);

        var primary = replay.Combatants
            .OrderByDescending(static summary => summary.IncomingEvades + summary.IncomingInvincibles)
            .ThenByDescending(static summary => summary.IncomingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        Assert.True(primary.IncomingEvades == expectedEvades, summaryDump);
        Assert.True(primary.IncomingInvincibles == expectedInvincibles, summaryDump);
    }

    [Fact]
    public void Replay_20260415_Outgoing_Combat_Stats_Match_PacketOnly_Avoidance()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260415211500.log"));

        Assert.True(replay.ReplayedLines > 0);

        var player = replay.Combatants
            .OrderByDescending(static s => s.OutgoingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        var diagDump = $"Player: id={player.CombatantId} hits={player.OutgoingHits} att={player.OutgoingAttempts} inv={player.OutgoingInvincibles}\n{summaryDump}";

        Assert.True(player.OutgoingDamage == 19969423, $"OutgoingDamage={player.OutgoingDamage}\n{diagDump}");
        Assert.True(player.OutgoingInvincibles == 8, $"OutgoingInvincibles={player.OutgoingInvincibles}\n{diagDump}");
        Assert.True(player.OutgoingHits == 1304, $"OutgoingHits={player.OutgoingHits}\n{diagDump}");
        Assert.True(player.OutgoingAttempts == 1312, $"OutgoingAttempts={player.OutgoingAttempts}\n{diagDump}");
    }

    [Fact]
    public void Replay_20260416021557_Outgoing_Combat_Stats_Match_PacketOnly_Avoidance()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260416021557.log"));

        Assert.True(replay.ReplayedLines > 0);

        var player = replay.Combatants
            .OrderByDescending(static s => s.OutgoingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        var diagDump = $"Player: id={player.CombatantId} hits={player.OutgoingHits} att={player.OutgoingAttempts} inv={player.OutgoingInvincibles}\n{summaryDump}";

        Assert.True(player.OutgoingDamage == 7866922, $"OutgoingDamage={player.OutgoingDamage}\n{diagDump}");
        Assert.True(player.OutgoingHits == 1166, $"OutgoingHits={player.OutgoingHits}\n{diagDump}");
        Assert.True(player.OutgoingAttempts == 1166, $"OutgoingAttempts={player.OutgoingAttempts}\n{diagDump}");

        const int gameReportedHealing021557 = 583068;
        Assert.True(player.IncomingHealing == gameReportedHealing021557, $"IncomingHealing={player.IncomingHealing} expected={gameReportedHealing021557}\n{diagDump}");
    }

    [Fact]
    public void Replay_20260416021406_Outgoing_Combat_Stats_Match_PacketOnly_Avoidance()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260416021406.log"));

        Assert.True(replay.ReplayedLines > 0);

        var player = replay.Combatants
            .OrderByDescending(static s => s.OutgoingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        var diagDump = $"Player: id={player.CombatantId} hits={player.OutgoingHits} att={player.OutgoingAttempts} inv={player.OutgoingInvincibles} dmg={player.OutgoingDamage}\n{summaryDump}";

        Assert.True(player.OutgoingDamage == 3954053, $"OutgoingDamage={player.OutgoingDamage}\n{diagDump}");
        Assert.True(player.OutgoingHits == 524, $"OutgoingHits={player.OutgoingHits}\n{diagDump}");
        Assert.True(player.OutgoingAttempts == 524, $"OutgoingAttempts={player.OutgoingAttempts}\n{diagDump}");

        const int gameReportedHealing021406 = 141564;
        Assert.True(player.IncomingHealing == gameReportedHealing021406, $"IncomingHealing={player.IncomingHealing} expected={gameReportedHealing021406}\n{diagDump}");
    }

    [Fact]
    public void Replay_20260417003456_Ground_AoE_Entities_Attributed_To_Owning_Player()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260417003456.log"));

        Assert.True(replay.ReplayedLines > 0);

        var snapshot = replay.Snapshot;
        var combatantDump = string.Join("\n", snapshot.Combatants
            .OrderByDescending(c => c.Value.DamageAmount)
            .Select(c => $"id={c.Key} class={c.Value.CharacterClass} dmg={c.Value.DamageAmount} heal={c.Value.HealingAmount}"));

        Assert.False(snapshot.Combatants.ContainsKey(99306), $"Ground AoE entity 99306 should not appear separately.\n{combatantDump}");
        Assert.False(snapshot.Combatants.ContainsKey(39022), $"Ground AoE entity 39022 should not appear separately.\n{combatantDump}");

        Assert.True(snapshot.Combatants.TryGetValue(664, out var cleric), $"Cleric 664 not found.\n{combatantDump}");
        Assert.True(cleric.DamageAmount == 3323254, $"Cleric damage={cleric.DamageAmount} expected=3323254\n{combatantDump}");
    }

    [Fact]
    public void Replay_20260417023559_Cleric_Healing_No_False_Drain()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260417023559.log"));
        Assert.True(replay.ReplayedLines > 0);

        var snapshot = replay.Snapshot;
        var player = snapshot.Combatants
            .OrderByDescending(c => c.Value.DamageAmount)
            .First();

        var metrics = player.Value;
        var skills = replay.SceneOwner.CreateSkillBreakdown(snapshot, player.Key).Skills;

        var combatantDump = string.Join("\n", snapshot.Combatants
            .OrderByDescending(c => c.Value.DamageAmount)
            .Select(c => $"id={c.Key} class={c.Value.CharacterClass} dmg={c.Value.DamageAmount} heal={c.Value.HealingAmount}"));

        var divineAuraSkillCode = 17150340;
        if (skills.TryGetValue(divineAuraSkillCode, out var divineAura))
        {
            Assert.True(divineAura.DrainHealingAmount == 0,
                $"Divine Aura drain={divineAura.DrainHealingAmount} should be 0\n{combatantDump}");
        }

        const int gameReportedHealing023559 = 70963;
        Assert.Equal(gameReportedHealing023559, metrics.HealingAmount);
    }

    [Fact]
    public void Replay_20260417141813_Light_Of_Regeneration_Periodic_Healing()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260417141813.log"));
        Assert.True(replay.ReplayedLines > 0);

        var snapshot = replay.Snapshot;
        var player = snapshot.Combatants
            .OrderByDescending(c => c.Value.DamageAmount)
            .First();

        var metrics = player.Value;
        var skills = replay.SceneOwner.CreateSkillBreakdown(snapshot, player.Key).Skills;

        var lightOfRegenBaseSkill = 17090000;
        var lightOfRegenSkills = skills
            .Where(kvp => kvp.Key / 10000 * 10000 == lightOfRegenBaseSkill || kvp.Key == lightOfRegenBaseSkill)
            .ToList();

        var skillDump = string.Join("\n", lightOfRegenSkills
            .Select(kvp => $"skill={kvp.Key} heal={kvp.Value.HealingAmount} periodic={kvp.Value.PeriodicHealingAmount} drain={kvp.Value.DrainHealingAmount}"));

        const int expectedLightOfRegenHealing = 4599;
        var totalLightOfRegenHealing = lightOfRegenSkills.Sum(kvp => kvp.Value.HealingAmount);
        Assert.True(totalLightOfRegenHealing == expectedLightOfRegenHealing,
            $"LightOfRegen total={totalLightOfRegenHealing} expected={expectedLightOfRegenHealing}\n{skillDump}");
    }

    [Fact]
    public void Replay_20260419204630_Instance_Clear_Restore_And_Incoming_Damage()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));
        Assert.True(replay.ReplayedLines > 0);

        var player = replay.Combatants
            .OrderByDescending(static s => s.OutgoingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        var diagDump = $"Player: id={player.CombatantId} dmg={player.OutgoingDamage} heal={player.IncomingHealing} inDmg={player.IncomingDamage} inHits={player.IncomingHits}\n{summaryDump}";

        Assert.True(player.IncomingDamage == 946, $"IncomingDamage={player.IncomingDamage} expected=946\n{diagDump}");
        Assert.True(player.IncomingHits == 2, $"IncomingHits={player.IncomingHits} expected=2\n{diagDump}");

        Assert.True(player.IncomingHealing == 48630, $"IncomingHealing={player.IncomingHealing} expected=48630 (HP instance-clear restore + Radiant Benediction, excludes MP restore)\n{diagDump}");
    }

    [Fact]
    public void Replay_20260423001617_Visible_Combatant_Damage_Contribution_Does_Not_Exceed_One_Hundred_Percent()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260423001617.log"));
        Assert.True(replay.ReplayedLines > 0);

        var visibleCombatants = replay.Snapshot.Combatants
            .Where(static pair => pair.Value.CharacterClass is not null)
            .OrderByDescending(static pair => pair.Value.DamageAmount)
            .ToArray();

        var visibleContributionTotal = visibleCombatants.Sum(static pair => pair.Value.DamageContribution);
        var combatantDump = string.Join(
            Environment.NewLine,
            visibleCombatants.Select(static pair =>
                $"id={pair.Key} class={pair.Value.CharacterClass} dmg={pair.Value.DamageAmount} dps={pair.Value.DamagePerSecond:F2} share={pair.Value.DamageContribution:P4}"));

        Assert.True(visibleCombatants.Length >= 2, combatantDump);
        Assert.True(visibleContributionTotal <= 1.0000000001d,
            $"visibleContributionTotal={visibleContributionTotal:P8}\n{combatantDump}");
    }

    [Fact]
    public void Replay_20260426110459_Templar_DirectSelfHpRecovery_Packets_Are_Healing()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260426110459.log"));

        Assert.True(replay.ReplayedLines > 0);

        const int playerId = 6774;
        const int hpAbsorptionEffectSkillCode = 10000013;
        const int wardingStrikeSkillCode = 12351450;
        const int punishingStrikeSkillCode = 12060240;
        var selfHealingPackets = SceneReplayTestView.BySource(replay)[playerId]
            .Where(packet =>
                packet.SourceId == playerId &&
                packet.TargetId == playerId &&
                packet.EventKind == CombatEventKind.Healing)
            .ToArray();
        var packetDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.BySource(replay)[playerId]
                .Where(static packet => packet.SourceId == packet.TargetId)
                .Select(static packet =>
                    $"skill={packet.SkillCode} raw={packet.OriginalSkillCode} damage={packet.Damage} event={packet.EventKind} value={packet.ValueKind} periodic={packet.PeriodicRelation}:{packet.PeriodicMode} marker={packet.Marker} detail={packet.DetailRaw}"));

        var hpAbsorptionRecovery = selfHealingPackets
            .Where(static packet => packet.SkillCode == hpAbsorptionEffectSkillCode)
            .Sum(static packet => packet.Damage);
        var wardingStrikeRecovery = selfHealingPackets
            .Where(static packet => packet.SkillCode == wardingStrikeSkillCode)
            .Sum(static packet => packet.Damage);
        var punishingStrikeRecovery = selfHealingPackets
            .Where(static packet =>
                packet.OriginalSkillCode == punishingStrikeSkillCode &&
                packet.ValueKind == CombatValueKind.DrainHealing)
            .Sum(static packet => packet.Damage);
        Assert.True(hpAbsorptionRecovery == 5372, packetDump);
        Assert.True(wardingStrikeRecovery == 2492, packetDump);
        Assert.True(punishingStrikeRecovery == 1563, packetDump);

        var recognizedSelfRecovery = hpAbsorptionRecovery + wardingStrikeRecovery + punishingStrikeRecovery;
        Assert.Equal(9427, recognizedSelfRecovery);

        var combatantDump = string.Join(
            Environment.NewLine,
            replay.Snapshot.Combatants
                .OrderByDescending(static pair => pair.Value.HealingAmount)
                .Select(static pair => $"id={pair.Key} heal={pair.Value.HealingAmount} damage={pair.Value.DamageAmount}"));
        Assert.True(replay.Snapshot.Combatants.TryGetValue(playerId, out var playerMetrics), combatantDump);
        Assert.True(playerMetrics.HealingAmount == recognizedSelfRecovery,
            $"HealingAmount={playerMetrics.HealingAmount} expected={recognizedSelfRecovery}\n{packetDump}\n{combatantDump}");
    }

    [Fact]
    public void Replay_20260426121726_Templar_Healing_Matches_Game_Ground_Truth()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260426121726.log"));

        Assert.True(replay.ReplayedLines > 0);

        const int playerId = 15980;
        var combatantDump = string.Join(
            Environment.NewLine,
            replay.Snapshot.Combatants
                .OrderByDescending(static pair => pair.Value.HealingAmount)
                .Select(static pair => $"id={pair.Key} heal={pair.Value.HealingAmount} damage={pair.Value.DamageAmount}"));
        Assert.True(replay.Snapshot.Combatants.TryGetValue(playerId, out var playerMetrics), combatantDump);
        var playerSkills = replay.SceneOwner.CreateSkillBreakdown(replay.Snapshot, playerId).Skills;

        var skillDump = string.Join(
            Environment.NewLine,
            playerSkills
                .Where(static pair => pair.Value.HealingAmount > 0 || pair.Value.DrainHealingAmount > 0)
                .OrderByDescending(static pair => pair.Value.HealingAmount)
                .Select(static pair =>
                    $"skill={pair.Key} heal={pair.Value.HealingAmount} periodic={pair.Value.PeriodicHealingAmount} drain={pair.Value.DrainHealingAmount} times={pair.Value.HealingTimes}"));
        var packetDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.BySource(replay)[playerId]
                .Where(static packet => packet.SourceId == packet.TargetId || packet.DrainHealAmount > 0)
                .Select(static packet =>
                    $"skill={packet.SkillCode} raw={packet.OriginalSkillCode} damage={packet.Damage} drain={packet.DrainHealAmount} event={packet.EventKind} value={packet.ValueKind} periodic={packet.PeriodicRelation}:{packet.PeriodicMode} marker={packet.Marker} type={packet.Type} detail={packet.DetailRaw}"));

        long SkillDrainHealing(int skillCode) =>
            playerSkills.TryGetValue(skillCode, out var metrics)
                ? metrics.DrainHealingAmount
                : 0;

        Assert.True(SkillDrainHealing(12010250) == 858, skillDump);
        Assert.True(SkillDrainHealing(12020250) == 911, skillDump);
        Assert.True(SkillDrainHealing(12030250) == 897, skillDump);
        Assert.True(SkillDrainHealing(12440250) == 2395, skillDump);
        Assert.True(SkillDrainHealing(12060240) == 784, skillDump);
        Assert.True(playerMetrics.HealingAmount == 31531,
            $"HealingAmount={playerMetrics.HealingAmount} expected=31531\n{skillDump}\n{packetDump}\n{combatantDump}");
    }

    [Fact]
    public void Replay_20260426140354_SummonRestores_And_TargetSupport_Are_Classified_From_PacketShape()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), ResourceDatabase.LoadNpcCatalog("zh-TW"));

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260426140354.log"));

        const int playerId = 4156;
        var playerOwnedIds = SceneReplayTestView.SummonOwnerByInstance(replay)
            .Where(static pair => pair.Value == playerId)
            .Select(static pair => pair.Key)
            .Append(playerId)
            .ToHashSet();
        var combatantDump = string.Join(
            Environment.NewLine,
            replay.Snapshot.Combatants
                .OrderByDescending(static pair => pair.Value.DamageAmount + pair.Value.HealingAmount)
                .Select(static pair =>
                    $"id={pair.Key} class={pair.Value.CharacterClass} damage={pair.Value.DamageAmount} heal={pair.Value.HealingAmount} shield={pair.Value.ShieldAmount}"));
        var summaryDump = string.Join(
            Environment.NewLine,
            replay.Combatants
                .OrderByDescending(static summary => summary.OutgoingDamage + summary.OutgoingHealing + summary.IncomingDamage)
                .Select(static summary =>
                    $"id={summary.CombatantId} name={summary.DisplayName} outDmg={summary.OutgoingDamage} inDmg={summary.IncomingDamage} outHeal={summary.OutgoingHealing} inHeal={summary.IncomingHealing} outShield={summary.OutgoingShield} inShield={summary.IncomingShield} attempts={summary.OutgoingAttempts}/{summary.IncomingAttempts} hits={summary.OutgoingHits}/{summary.IncomingHits}"));
        var summonDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.SummonOwnerByInstance(replay)
                .OrderBy(static pair => pair.Key)
                .Select(static pair => $"summon={pair.Key} owner={pair.Value}"));
        var targetDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.ByTarget(replay)
                .Select(static pair => new
                {
                    Target = pair.Key,
                    Damage = pair.Value.Where(static packet => packet.EventKind == CombatEventKind.Damage).Sum(static packet => packet.Damage),
                    pair.Value.Count
                })
                .OrderByDescending(static entry => entry.Damage)
                .Select(entry => $"target={entry.Target} damage={entry.Damage} packets={entry.Count}"));
        var playerIncomingDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.ByTarget(replay)[playerId]
                .Where(static packet => packet.EventKind == CombatEventKind.Damage)
                .GroupBy(static packet => new { packet.SourceId, packet.SkillCode, packet.OriginalSkillCode })
                .Select(static group => new
                {
                    group.Key.SourceId,
                    group.Key.SkillCode,
                    group.Key.OriginalSkillCode,
                    Damage = group.Sum(static packet => packet.Damage),
                    Attempts = group.Sum(static packet => packet.AttemptContribution),
                    Hits = group.Sum(static packet => packet.HitContribution),
                    Count = group.Count()
                })
                .OrderByDescending(static entry => entry.Damage)
                .Select(entry => $"source={entry.SourceId} skill={entry.SkillCode} raw={entry.OriginalSkillCode} damage={entry.Damage} attempts={entry.Attempts} hits={entry.Hits} packets={entry.Count}"));
        var playerHealingGroupDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.Packets(replay)
                .Where(packet => packet.SourceId == playerId &&
                                 packet.Timestamp >= replay.Snapshot.EncounterStartTime &&
                                 packet.Timestamp <= replay.Snapshot.EncounterEndTime &&
                                 packet.ValueKind is CombatValueKind.Healing or CombatValueKind.PeriodicHealing or CombatValueKind.DrainHealing)
                .GroupBy(packet => new
                {
                    packet.SkillCode,
                    packet.OriginalSkillCode,
                    packet.ValueKind,
                    InWindow = true,
                    RawSource = packet.SourceId,
                    IsSelfTarget = packet.TargetId == playerId,
                    IsSummonTarget = playerOwnedIds.Contains(packet.TargetId) && packet.TargetId != playerId
                })
                .Select(group => new
                {
                    group.Key.SkillCode,
                    group.Key.OriginalSkillCode,
                    group.Key.ValueKind,
                    group.Key.InWindow,
                    group.Key.RawSource,
                    group.Key.IsSelfTarget,
                    group.Key.IsSummonTarget,
                    Damage = group.Sum(static packet => packet.Damage),
                    Count = group.Count()
                })
                .OrderByDescending(entry => entry.Damage)
                .Select(entry =>
                    $"skill={entry.SkillCode} raw={entry.OriginalSkillCode} value={entry.ValueKind} inWindow={entry.InWindow} rawSource={entry.RawSource} self={entry.IsSelfTarget} summonTarget={entry.IsSummonTarget} damage={entry.Damage} count={entry.Count}"));
        var spiritDescentPacketDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.BySource(replay).Values
                .SelectMany(static queue => queue)
                .Where(static packet => packet.SkillCode == 16990004 || packet.OriginalSkillCode == 16990004)
                .OrderBy(static packet => packet.Timestamp)
                .Select(packet =>
                    $"t={packet.Timestamp} src={packet.SourceId} tgt={packet.TargetId} dmg={packet.Damage} kind={packet.EventKind}/{packet.ValueKind} layout={packet.LayoutTag} flag={packet.Flag} type={packet.Type} loop={packet.Loop} detail=0x{packet.DetailRaw:X16} marker={packet.Marker} unknown={packet.Unknown} sourceSummon={SceneReplayTestView.SummonOwnerByInstance(replay).ContainsKey(packet.SourceId)} targetSummon={SceneReplayTestView.SummonOwnerByInstance(replay).ContainsKey(packet.TargetId)}"));
        var diagnostics =
            $"target={replay.Snapshot.TargetObservation?.InstanceId} encounter={replay.Snapshot.EncounterStartTime}-{replay.Snapshot.EncounterEndTime}\ncombatants:\n{combatantDump}\nsummaries:\n{summaryDump}\nsummons:\n{summonDump}\ntargets:\n{targetDump}\nplayer-healing-groups:\n{playerHealingGroupDump}\nspirit-descent-packets:\n{spiritDescentPacketDump}\nplayer-incoming:\n{playerIncomingDump}";

        Assert.True(replay.Snapshot.Combatants.TryGetValue(playerId, out var playerMetrics), diagnostics);
        var playerSkills = replay.SceneOwner.CreateSkillBreakdown(replay.Snapshot, playerId).Skills;
        Assert.False(
            playerSkills.TryGetValue(16990004, out var spiritDescentRestore) && spiritDescentRestore.HealingAmount > 0,
            diagnostics);

        var playerSummary = Assert.Single(replay.Combatants, static summary => summary.CombatantId == playerId);
        Assert.True(playerSummary.IncomingDamage == 13_347, diagnostics);

        foreach (var summonId in playerOwnedIds.Where(static id => id != playerId))
        {
            var summonSummary = Assert.Single(replay.Combatants, summary => summary.CombatantId == summonId);
            Assert.True(summonSummary.IncomingDamage == 0, diagnostics);
        }
    }

    [Fact]
    public void Replay_20260426031332_EnhanceSpiritBenediction_Self_And_Summon_Healing_Match_Game_Ground_Truth()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260426031332.log"));

        Assert.True(replay.ReplayedLines > 0);

        const int playerId = 10277;
        const int summonId = 37299;
        const int enhanceSpiritBenedictionSkillCode = 16190000;
        var healingPackets = SceneReplayTestView.BySource(replay)[playerId]
            .Where(packet =>
                packet.SkillCode == enhanceSpiritBenedictionSkillCode &&
                packet.ValueKind == CombatValueKind.PeriodicHealing)
            .ToArray();
        var packetDump = string.Join(
            Environment.NewLine,
            healingPackets.Select(static packet =>
                $"target={packet.TargetId} damage={packet.Damage} mode={packet.PeriodicRelation}:{packet.PeriodicMode} value={packet.ValueKind}"));

        Assert.Equal(18, healingPackets.Length);
        Assert.True(healingPackets.Sum(static packet => packet.Damage) == 3438, packetDump);
        Assert.True(healingPackets.Where(static packet => packet.TargetId == playerId).Sum(static packet => packet.Damage) == 1737, packetDump);
        Assert.True(healingPackets.Where(static packet => packet.TargetId == summonId).Sum(static packet => packet.Damage) == 1701, packetDump);
        Assert.All(healingPackets, static packet => Assert.Equal(CombatEventKind.Healing, packet.EventKind));

        var combatantDump = string.Join(
            Environment.NewLine,
            replay.Snapshot.Combatants
                .OrderByDescending(static pair => pair.Value.HealingAmount)
                .Select(static pair => $"id={pair.Key} heal={pair.Value.HealingAmount} damage={pair.Value.DamageAmount}"));
        Assert.True(replay.Snapshot.Combatants.TryGetValue(playerId, out var playerMetrics), combatantDump);
        var playerSkills = replay.SceneOwner.CreateSkillBreakdown(replay.Snapshot, playerId).Skills;
        var skillDump = string.Join(
            Environment.NewLine,
            playerSkills
                .Where(static pair => pair.Value.HealingAmount > 0)
                .OrderByDescending(static pair => pair.Value.HealingAmount)
                .Select(static pair =>
                    $"skill={pair.Key} heal={pair.Value.HealingAmount} periodic={pair.Value.PeriodicHealingAmount} drain={pair.Value.DrainHealingAmount} times={pair.Value.HealingTimes}"));
        var spiritDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.BySource(replay).Values
                .SelectMany(static queue => queue)
                .Where(static packet => packet.SkillCode == 16990004 || packet.OriginalSkillCode == 16990004)
                .OrderBy(static packet => packet.Timestamp)
                .Select(packet =>
                    $"t={packet.Timestamp} src={packet.SourceId} tgt={packet.TargetId} dmg={packet.Damage} kind={packet.EventKind}/{packet.ValueKind} layout={packet.LayoutTag} flag={packet.Flag} type={packet.Type} loop={packet.Loop} detail=0x{packet.DetailRaw:X16} marker={packet.Marker} unknown={packet.Unknown} periodic={packet.PeriodicRelation}:{packet.PeriodicMode} sourceSummon={SceneReplayTestView.SummonOwnerByInstance(replay).ContainsKey(packet.SourceId)} targetSummon={SceneReplayTestView.SummonOwnerByInstance(replay).ContainsKey(packet.TargetId)}"));
        Assert.True(playerMetrics.HealingAmount == 3438, $"HealingAmount={playerMetrics.HealingAmount} expected=3438 encounter={replay.Snapshot.EncounterStartTime}-{replay.Snapshot.EncounterEndTime}\n{skillDump}\n{spiritDump}\n{combatantDump}");
        Assert.True(playerSkills.TryGetValue(enhanceSpiritBenedictionSkillCode, out var skill), combatantDump);
        Assert.Equal(3438, skill.HealingAmount);
        Assert.Equal(3438, skill.PeriodicHealingAmount);

        var summaryDump = string.Join(
            Environment.NewLine,
            replay.Combatants.Select(static summary =>
                $"id={summary.CombatantId} outgoingHealing={summary.OutgoingHealing} incomingHealing={summary.IncomingHealing} outgoingDamage={summary.OutgoingDamage} incomingDamage={summary.IncomingDamage}"));
        var playerSummary = Assert.Single(replay.Combatants, static summary => summary.CombatantId == playerId);
        var summonSummary = Assert.Single(replay.Combatants, static summary => summary.CombatantId == summonId);
        Assert.True(playerSummary.OutgoingHealing == 3438, summaryDump);
        Assert.True(playerSummary.IncomingHealing == 1737, summaryDump);
        Assert.True(summonSummary.IncomingHealing == 1701, summaryDump);
    }

    [Fact]
    public void SceneReplay_VendoredLog_IsDeterministic_20260415()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var path = FixtureHelper.GetPath("logs/aion2flow.stream.20260415211500.log");
        var first = PacketLogReplayService.Replay(path);
        var second = PacketLogReplayService.Replay(path);

        AssertSnapshotParity(first.Snapshot, second.Snapshot);
        Assert.Equal(first.ReplayedLines, second.ReplayedLines);
        Assert.True(second.SceneJournal.Count > 0);
    }

    [Fact]
    public void SceneReplay_VendoredLog_IsDeterministic_20260419()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var path = FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log");
        var first = PacketLogReplayService.Replay(path);
        var second = PacketLogReplayService.Replay(path);

        AssertSnapshotParity(first.Snapshot, second.Snapshot);
        Assert.Equal(first.ReplayedLines, second.ReplayedLines);
        Assert.True(second.SceneJournal.Count > 0);
    }

    [Fact]
    public void SceneReplay_JournalOrdinals_AreMonotonicallyIncreasing()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var path = FixtureHelper.GetPath("logs/aion2flow.stream.20260415211500.log");
        var replay = PacketLogReplayService.Replay(path);

        var journal = replay.SceneJournal;
        Assert.True(journal.Count > 0);

        long prevOrdinal = -1;
        for (int i = 0; i < journal.Count; i++)
        {
            var entry = journal.Read(i);
            Assert.True(entry.Stamp.ObservationOrdinal > prevOrdinal, $"Ordinal {entry.Stamp.ObservationOrdinal} at index {i} not greater than {prevOrdinal}");
            prevOrdinal = entry.Stamp.ObservationOrdinal;
        }
    }

    [Fact]
    public void SceneReplay_BaselineCounters_AreRecorded()
    {
        CombatResourceRegistry.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var entries = new[]
        {
            CreateStreamReplayEntry("2026-05-02T15:52:39.1861829+08:00", "state/2136-boss-scene-200003.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:40.0000000+08:00", "nickname/3336-own-thanks.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:41.0000000+08:00", "combat/0438-damage.hex"),
            CreateStreamReplayEntry("2026-05-02T15:52:42.0000000+08:00", "combat/0538-dot.hex")
        };

        var path = WriteTempReplayLog("stream", [.. entries.Select(BuildStreamReplayLine)]);
        try
        {
            var replay = PacketLogReplayService.Replay(path);

            Assert.NotSame(PacketLogReplayBaselineCounters.Empty, replay.BaselineCounters);
            AssertBaselineCounter(replay.BaselineCounters.ReplayIngest);
            AssertBaselineCounter(replay.BaselineCounters.SnapshotCreation);
            AssertBaselineCounter(replay.BaselineCounters.CombatantSummaryCreation);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SceneReplay_AlwaysExposesSceneJournal()
    {
        CombatResourceRegistry.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var entries = new[]
        {
            CreateStreamReplayEntry("2026-05-02T15:52:41.0000000+08:00", "combat/0438-damage.hex"),
        };

        var path = WriteTempReplayLog("stream", [.. entries.Select(BuildStreamReplayLine)]);
        try
        {
            var replay = PacketLogReplayService.Replay(path);
            Assert.True(replay.SceneJournal.Count > 0);
            Assert.NotNull(replay.SceneOwner);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempReplayLog(string logKind, params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.{logKind}.log");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static StreamReplayEntry CreateStreamReplayEntry(string timestamp, string fixture)
    {
        return new StreamReplayEntry(
            DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            HexHelper.FromFixture(fixture));
    }

    private static string BuildStreamReplayLine(StreamReplayEntry entry)
        => $"{entry.Timestamp:O}|dir=inbound|16777343:57080->16777343:49820|len={entry.Payload.Length}|data={Convert.ToHexString(entry.Payload)}";

    private static StreamProcessorReplayResult ReplayStreamEntriesWithSceneProcessor(IReadOnlyList<StreamReplayEntry> entries)
    {
        using var holder = SceneSinkFactory.CreateForReplay();
        var replayedLines = 0;
        var skippedLines = 0;
        using var processor = new PacketStreamProcessor(holder.Sink);
        var connection = new TcpConnection(16777343, 16777343, 57080, 49820);

        foreach (var entry in entries)
        {
            if (processor.AppendAndProcess(entry.Payload, connection, entry.Timestamp.ToUnixTimeMilliseconds()))
            {
                replayedLines++;
            }
            else
            {
                skippedLines++;
            }
        }

        holder.Sink.CompleteBatch(long.MaxValue);
        return new StreamProcessorReplayResult(
            entries.Count,
            replayedLines,
            skippedLines,
            holder.Owner.CreateSnapshot(),
            SceneReplayTestView.Packets(new PacketLogReplayResult(
                string.Empty,
                entries.Count,
                replayedLines,
                skippedLines,
                holder.Owner.CreateSnapshot(),
                holder.Journal,
                holder.Owner,
                [],
                new Dictionary<string, int>(),
                new Dictionary<string, int>())));
    }

    private static void AssertStreamReplayParity(StreamProcessorReplayResult expected, PacketLogReplayResult actual)
    {
        Assert.Equal(expected.TotalLines, actual.TotalLines);
        Assert.Equal(expected.ReplayedLines, actual.ReplayedLines);
        Assert.Equal(expected.SkippedLines, actual.SkippedLines);
        AssertSnapshotParity(expected.Snapshot, actual.Snapshot);
        Assert.Equal(
            BuildEncounterPacketFacts(expected.Packets, expected.Snapshot),
            BuildEncounterPacketFacts(SceneReplayTestView.Packets(actual), actual.Snapshot));
    }

    private static void AssertBaselineCounter(PacketLogReplayBaselineCounter counter)
    {
        Assert.True(counter.Elapsed >= TimeSpan.Zero);
        Assert.True(counter.AllocatedBytes >= 0);
    }

    private static void AssertSnapshotParity(SceneCombatSnapshot expected, SceneCombatSnapshot actual)
    {
        Assert.Equal(expected.EncounterTime, actual.EncounterTime);
        Assert.Equal(expected.EncounterStartTime, actual.EncounterStartTime);
        Assert.Equal(expected.EncounterEndTime, actual.EncounterEndTime);
        Assert.Equal(expected.MapId, actual.MapId);
        Assert.Equal(expected.MapInstanceId, actual.MapInstanceId);
        Assert.Equal(expected.TargetObservation?.InstanceId, actual.TargetObservation?.InstanceId);
        Assert.Equal(
            expected.Combatants.Keys.Order().ToArray(),
            actual.Combatants.Keys.Order().ToArray());

        foreach (var id in expected.Combatants.Keys)
        {
            AssertCombatantParity(expected.Combatants[id], actual.Combatants[id]);
        }
    }

    private static void AssertCombatantParity(SceneCombatantMetrics expected, SceneCombatantMetrics actual)
    {
        Assert.Equal(expected.CharacterClass, actual.CharacterClass);
        Assert.Equal(expected.DamageAmount, actual.DamageAmount);
        Assert.Equal(expected.HealingAmount, actual.HealingAmount);
        Assert.Equal(expected.PeriodicHealingAmount, actual.PeriodicHealingAmount);
        Assert.Equal(expected.DrainDamageAmount, actual.DrainDamageAmount);
        Assert.Equal(expected.DrainHealingAmount, actual.DrainHealingAmount);
        Assert.Equal(expected.RegenerationHealingAmount, actual.RegenerationHealingAmount);
        Assert.Equal(expected.ShieldAmount, actual.ShieldAmount);
        Assert.Equal(expected.ShieldTimes, actual.ShieldTimes);
        Assert.Equal(expected.ShieldAbsorbedAmount, actual.ShieldAbsorbedAmount);
        Assert.Equal(expected.ShieldAbsorbedTimes, actual.ShieldAbsorbedTimes);
    }

    private static void AssertSkillParity(SkillMetrics expected, SkillMetrics actual)
    {
        Assert.Equal(expected.SkillCode, actual.SkillCode);
        Assert.Equal(expected.EventKind, actual.EventKind);
        Assert.Equal(expected.PrimaryValueKind, actual.PrimaryValueKind);
        Assert.Equal(expected.DamageAmount, actual.DamageAmount);
        Assert.Equal(expected.PeriodicDamageAmount, actual.PeriodicDamageAmount);
        Assert.Equal(expected.PeriodicDamageTimes, actual.PeriodicDamageTimes);
        Assert.Equal(expected.HealingAmount, actual.HealingAmount);
        Assert.Equal(expected.HealingTimes, actual.HealingTimes);
        Assert.Equal(expected.SupportTimes, actual.SupportTimes);
        Assert.Equal(expected.PeriodicHealingAmount, actual.PeriodicHealingAmount);
        Assert.Equal(expected.PeriodicHealingTimes, actual.PeriodicHealingTimes);
        Assert.Equal(expected.DrainDamageAmount, actual.DrainDamageAmount);
        Assert.Equal(expected.DrainDamageTimes, actual.DrainDamageTimes);
        Assert.Equal(expected.DrainHealingAmount, actual.DrainHealingAmount);
        Assert.Equal(expected.DrainHealingTimes, actual.DrainHealingTimes);
        Assert.Equal(expected.RegenerationHealingAmount, actual.RegenerationHealingAmount);
        Assert.Equal(expected.RegenerationHealingTimes, actual.RegenerationHealingTimes);
        Assert.Equal(expected.ShieldAmount, actual.ShieldAmount);
        Assert.Equal(expected.ShieldTimes, actual.ShieldTimes);
        Assert.Equal(expected.ShieldAbsorbedAmount, actual.ShieldAbsorbedAmount);
        Assert.Equal(expected.ShieldAbsorbedTimes, actual.ShieldAbsorbedTimes);
        Assert.Equal(expected.CriticalTimes, actual.CriticalTimes);
        Assert.Equal(expected.Times, actual.Times);
        Assert.Equal(expected.AttemptTimes, actual.AttemptTimes);
        Assert.Equal(expected.EvadeTimes, actual.EvadeTimes);
        Assert.Equal(expected.InvincibleTimes, actual.InvincibleTimes);
        Assert.Equal(expected.MultiHitTimes, actual.MultiHitTimes);
        Assert.Equal(expected.BackTimes, actual.BackTimes);
        Assert.Equal(expected.PerfectTimes, actual.PerfectTimes);
        Assert.Equal(expected.SmiteTimes, actual.SmiteTimes);
        Assert.Equal(expected.ParryTimes, actual.ParryTimes);
        Assert.Equal(expected.BlockTimes, actual.BlockTimes);
        Assert.Equal(expected.PerfectParryTimes, actual.PerfectParryTimes);
        Assert.Equal(expected.PerfectBlockTimes, actual.PerfectBlockTimes);
        Assert.Equal(expected.EnduranceTimes, actual.EnduranceTimes);
        Assert.Equal(expected.RegenerationTimes, actual.RegenerationTimes);
    }

    private static string[] BuildEncounterPacketFacts(IReadOnlyList<SceneReplayPacket> packets, SceneCombatSnapshot snapshot)
    {
        return packets
            .Where(packet => packet.Timestamp >= snapshot.EncounterStartTime && packet.Timestamp <= snapshot.EncounterEndTime)
            .Select(static packet =>
            {
                return string.Join(
                    "|",
                    packet.SourceId,
                    packet.TargetId,
                    packet.SourceId,
                    packet.TargetId,
                    packet.OriginalSkillCode,
                    packet.SkillCode,
                    packet.Damage,
                    packet.Modifiers,
                    packet.EventKind,
                    packet.ValueKind,
                    packet.EffectTag,
                    packet.Timestamp);
            })
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string BuildSummaryDump(IEnumerable<PacketLogCombatantSummary> summaries)
    {
        return string.Join(
            Environment.NewLine,
            summaries
                .OrderByDescending(static summary => summary.IncomingEvades + summary.IncomingInvincibles)
                .ThenByDescending(static summary => summary.IncomingDamage)
                .Select(static summary =>
                    $"id={summary.CombatantId} incoming(evade={summary.IncomingEvades}, invincible={summary.IncomingInvincibles}, damage={summary.IncomingDamage}, hits={summary.IncomingHits}, attempts={summary.IncomingAttempts}) outgoing(damage={summary.OutgoingDamage}, hits={summary.OutgoingHits}, attempts={summary.OutgoingAttempts})"));
    }

    private static SkillCollection BuildReplaySkillMap()
    {
        return
        [
            new Skill(1230000, "Fangs", SkillCategory.Npc, SkillSourceType.Unknown, "npc", null),
            new Skill(17000100, "Dodge", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null),
            new Skill(17010230, "Earth's Retribution", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null),
            new Skill(17730000, "Empyrean Lord's Grace", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null)
        ];
    }

    private sealed record StreamProcessorReplayResult(
        int TotalLines,
        int ReplayedLines,
        int SkippedLines,
        SceneCombatSnapshot Snapshot,
        IReadOnlyList<SceneReplayPacket> Packets);

    private sealed record StreamReplayEntry(DateTimeOffset Timestamp, byte[] Payload);
}
