using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketLogReplayServiceTests
{
    [Theory]
    [MemberData(nameof(ReplayScenarioCatalog.April11IncomingAvoidance), MemberType = typeof(ReplayScenarioCatalog))]
    public void Replay_Reconstructs_April11_Incoming_Avoidance_Ground_Truth_From_Stream_Log(ReplayAvoidanceScenario scenario)
    {
        CombatResourceRegistry.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{scenario.FileName}"));

        Assert.True(replay.ReplayedLines > 0);

        var primary = replay.Combatants
            .OrderByDescending(static summary => summary.IncomingEvades + summary.IncomingInvincibles)
            .ThenByDescending(static summary => summary.IncomingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        Assert.True(primary.IncomingEvades == scenario.ExpectedEvades, summaryDump);
        Assert.True(primary.IncomingInvincibles == scenario.ExpectedInvincibles, summaryDump);
    }

    [Theory]
    [MemberData(nameof(ReplayScenarioCatalog.ReportedMultiSourceInvincibles), MemberType = typeof(ReplayScenarioCatalog))]
    public void Replay_Reconstructs_Reported_MultiSource_Invincibles_With_Full_Skill_Map(ReplayAvoidanceScenario scenario)
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{scenario.FileName}"));

        Assert.True(replay.ReplayedLines > 0);

        var primary = replay.Combatants
            .OrderByDescending(static summary => summary.IncomingEvades + summary.IncomingInvincibles)
            .ThenByDescending(static summary => summary.IncomingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        Assert.True(primary.IncomingEvades == scenario.ExpectedEvades, summaryDump);
        Assert.True(primary.IncomingInvincibles == scenario.ExpectedInvincibles, summaryDump);
    }

    [Theory]
    [MemberData(nameof(ReplayScenarioCatalog.OutgoingCombatStats), MemberType = typeof(ReplayScenarioCatalog))]
    public void Replay_Outgoing_Combat_Stats_Match_PacketOnly_GroundTruth(ReplayOutgoingCombatStatsScenario scenario)
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{scenario.FileName}"));

        Assert.True(replay.ReplayedLines > 0);

        var player = replay.Combatants
            .OrderByDescending(static s => s.OutgoingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        var diagDump = $"Player: id={player.CombatantId} hits={player.OutgoingHits} att={player.OutgoingAttempts} inv={player.OutgoingInvincibles} dmg={player.OutgoingDamage}\n{summaryDump}";

        Assert.True(player.OutgoingDamage == scenario.ExpectedOutgoingDamage, $"OutgoingDamage={player.OutgoingDamage}\n{diagDump}");
        Assert.True(player.OutgoingHits == scenario.ExpectedOutgoingHits, $"OutgoingHits={player.OutgoingHits}\n{diagDump}");
        Assert.True(player.OutgoingAttempts == scenario.ExpectedOutgoingAttempts, $"OutgoingAttempts={player.OutgoingAttempts}\n{diagDump}");
        if (scenario.ExpectedOutgoingInvincibles is { } expectedOutgoingInvincibles)
            Assert.True(player.OutgoingInvincibles == expectedOutgoingInvincibles, $"OutgoingInvincibles={player.OutgoingInvincibles}\n{diagDump}");
        if (scenario.ExpectedIncomingHealing is { } expectedIncomingHealing)
            Assert.True(player.IncomingHealing == expectedIncomingHealing, $"IncomingHealing={player.IncomingHealing} expected={expectedIncomingHealing}\n{diagDump}");
    }

    [Fact]
    public void Replay_20260417003456_Ground_AoE_Entities_Attributed_To_Owning_Player()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.GroundAoeAttribution}"));

        Assert.True(replay.ReplayedLines > 0);

        var snapshot = replay.Snapshot;
        var combatantDump = string.Join("\n", snapshot.Combatants
            .OrderByDescending(c => c.Value.DamageAmount)
            .Select(c => $"id={c.Key} class={c.Value.CharacterClass} dmg={c.Value.DamageAmount} heal={c.Value.HealingAmount}"));

        Assert.False(snapshot.Combatants.ContainsKey(99306), $"Ground AoE entity 99306 should not appear separately.\n{combatantDump}");
        Assert.False(snapshot.Combatants.ContainsKey(39022), $"Ground AoE entity 39022 should not appear separately.\n{combatantDump}");

        Assert.True(snapshot.Combatants.TryGetValue(664, out var cleric), $"Cleric 664 not found.\n{combatantDump}");
        Assert.True(cleric.DamageAmount == 3465070, $"Cleric damage={cleric.DamageAmount} expected=3465070\n{combatantDump}");
    }

    [Fact]
    public void Replay_20260417023559_Cleric_Healing_No_False_Drain()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.ClericHealingNoFalseDrain}"));
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
        if (skills.TryGetBySkillCode(divineAuraSkillCode, out var divineAura))
        {
            Assert.True(divineAura.DrainHealingAmount == 0,
                $"Divine Aura drain={divineAura.DrainHealingAmount} should be 0\n{combatantDump}");
        }

        const int expectedPacketOnlyHealing023559 = 13345;
        Assert.Equal(expectedPacketOnlyHealing023559, metrics.HealingAmount);
    }

    [Fact]
    public void Replay_20260417141813_Light_Of_Regeneration_Periodic_Healing()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.LightOfRegenerationPeriodicHealing}"));
        Assert.True(replay.ReplayedLines > 0);

        var snapshot = replay.Snapshot;
        var player = snapshot.Combatants
            .OrderByDescending(c => c.Value.DamageAmount)
            .First();

        var metrics = player.Value;
        var skills = replay.SceneOwner.CreateSkillBreakdown(snapshot, player.Key).Skills;

        var lightOfRegenBaseSkill = 17090000;
        var lightOfRegenSkills = skills
            .Where(kvp => kvp.Key.SkillCode / 10000 * 10000 == lightOfRegenBaseSkill || kvp.Key.SkillCode == lightOfRegenBaseSkill)
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

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.InstanceClearRestoreAndMapBoundary}"));
        Assert.True(replay.ReplayedLines > 0);

        var player = replay.Combatants
            .OrderByDescending(static s => s.OutgoingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        var diagDump = $"Player: id={player.CombatantId} dmg={player.OutgoingDamage} heal={player.IncomingHealing} inDmg={player.IncomingDamage} inHits={player.IncomingHits}\n{summaryDump}";

        Assert.True(player.IncomingDamage == 946, $"IncomingDamage={player.IncomingDamage} expected=946\n{diagDump}");
        Assert.True(player.IncomingHits == 2, $"IncomingHits={player.IncomingHits} expected=2\n{diagDump}");

        Assert.True(player.IncomingHealing == 42616, $"IncomingHealing={player.IncomingHealing} expected=42616 (known HP restore only; ambiguous direct detail families excluded)\n{diagDump}");
    }

    [Fact]
    public void Replay_20260423001617_Visible_Combatant_Damage_Contribution_Does_Not_Exceed_One_Hundred_Percent()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.VisibleDamageContributionBoundary}"));
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

        Assert.True(visibleContributionTotal <= 1.0000000001d,
            $"visibleContributionTotal={visibleContributionTotal:P8}\n{combatantDump}");
    }

    [Theory]
    [MemberData(nameof(ReplayScenarioCatalog.Mode10PacketOnlyDamage), MemberType = typeof(ReplayScenarioCatalog))]
    public void Replay_Counts_PacketOnly_Mode10_TargetDamage(ReplayMode10DamageScenario scenario)
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), ResourceDatabase.LoadNpcCatalog("zh-TW"));

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{scenario.FileName}"));

        Assert.True(replay.ReplayedLines > 0);

        AssertMode10PacketOnlyDamage(
            replay,
            scenario.SourceId,
            scenario.TargetId,
            scenario.CombatantId,
            scenario.TailSkillCode,
            scenario.ExpectedPacketCount,
            scenario.ExpectedDamage);
    }

    [Fact]
    public void Replay_Recovers_After_Split_Transport_Frames()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), ResourceDatabase.LoadNpcCatalog("zh-TW"));

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.SplitTransportFrameRecovery}"));

        Assert.True(replay.ReplayedLines >= 600, $"ReplayedLines={replay.ReplayedLines}");
        Assert.True(replay.Snapshot.EncounterStartTime > 0, $"EncounterStartTime={replay.Snapshot.EncounterStartTime}");
        Assert.True(replay.Snapshot.EncounterEndTime >= replay.Snapshot.EncounterStartTime, $"EncounterEndTime={replay.Snapshot.EncounterEndTime}");

        var combatantDump = BuildSummaryDump(replay.Combatants);
        var player = Assert.Single(replay.Combatants, static summary => summary.CombatantId == 4679);
        Assert.Equal("cloris", player.DisplayName);
        Assert.True(player.OutgoingDamage > 500_000, combatantDump);
        Assert.True(player.IncomingHealing > 18_000, combatantDump);
    }

    [Fact]
    public void Replay_Recovers_PcMetadata_From_048D_Metadata_Packets()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), ResourceDatabase.LoadNpcCatalog("zh-TW"));

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.PcMetadata048D}"));

        Assert.True(replay.ReplayedLines > 0);
        var summaryDump = BuildSummaryDump(replay.Combatants);
        AssertPcName(replay, 2359, "风栖", summaryDump);
        AssertPcName(replay, 5324, "星勇敢呦", summaryDump);
        AssertPcName(replay, 6045, "發表", summaryDump);
        AssertPcName(replay, 8179, "脸红红", summaryDump);
        AssertPcName(replay, 12698, "成員術士", summaryDump);
        AssertPcName(replay, 16199, "雾中看山河", summaryDump);
    }

    [Fact]
    public void Replay_Recovers_PcMetadata_From_4536_Metadata_Packets()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), ResourceDatabase.LoadNpcCatalog("zh-TW"));

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.PcMetadata4536}"));

        Assert.True(replay.ReplayedLines > 0);
        var summaryDump = BuildSummaryDump(replay.Combatants);
        AssertPcNameAndClass(replay, 11518, "鲍鲍龙", CharacterClass.Ranger, summaryDump);
        AssertPcNameAndClass(replay, 14727, "沐雨橙风", CharacterClass.Ranger, summaryDump);
        AssertPcNameAndClass(replay, 16199, "雾中看山河", CharacterClass.Elementalist, summaryDump);
        AssertPcNameAndClass(replay, 12562, "楊狼噠", CharacterClass.Elementalist, summaryDump);
        AssertPcNameAndClass(replay, 11898, "无名氏", CharacterClass.Elementalist, summaryDump);
        AssertPcNameAndClass(replay, 6045, "發表", CharacterClass.Elementalist, summaryDump);
        AssertPcNameAndClass(replay, 8001, "習慣了孤單", CharacterClass.Elementalist, summaryDump);
        AssertPcNameAndClass(replay, 14091, "Sissi", CharacterClass.Cleric, summaryDump);
    }

    [Fact]
    public void Replay_Uses_Direct0438_Body_As_SkillVariant_For_ActionGrouping()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), ResourceDatabase.LoadNpcCatalog("zh-TW"));

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.Direct0438BodySkillVariant}"));

        const int playerId = 4679;
        const int combustionSkillCode = 16040030;
        var packets = SceneReplayTestView.BySource(replay)[playerId];
        var packetDump = string.Join(
            Environment.NewLine,
            packets
                .Where(static packet => packet.BodySkillVariantRaw is combustionSkillCode or 16010020 or 16770001)
                .Select(static packet =>
                    $"skill={packet.SkillCode} bodySkill={packet.BodySkillVariantRaw} bodyRef={packet.BodyResourceEffectRef.RawId} detail={packet.DetailResourceEffectRef.RawId} damage={packet.Damage} event={packet.EventKind} value={packet.ValueKind}"));

        var directCombustionRows = packets
            .Where(static packet =>
                packet.SourceId == playerId &&
                packet.BodySkillVariantRaw == combustionSkillCode &&
                packet.BodyResourceEffectRef.IsEmpty &&
                packet.SkillCode == combustionSkillCode)
            .ToArray();
        Assert.NotEmpty(directCombustionRows);
        Assert.DoesNotContain(packets, static packet =>
            packet.BodySkillVariantRaw == combustionSkillCode &&
            packet.SkillCode == 0);

        var skills = replay.SceneOwner.CreateSkillBreakdown(replay.Snapshot, playerId).Skills;
        Assert.True(skills.TryGetBySkillCode(combustionSkillCode, out var combustion), packetDump);
        Assert.True(combustion.DamageAmount > 100_000, packetDump);
        Assert.False(skills.ContainsKey(new CombatActionKey(0, ResourceEffectRef.FromRaw(combustionSkillCode), default)), packetDump);
    }

    [Fact]
    public void Replay_Recovers_NpcCatalog_From_4136_State_Packets()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), ResourceDatabase.LoadNpcCatalog("zh-TW"));

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.NpcCatalogState4136}"));

        Assert.True(replay.ReplayedLines > 0);
        var summaryDump = BuildSummaryDump(replay.Combatants);
        AssertNpcNameAndKind(replay, 31812, "庭院蜘蛛", NpcKind.Monster, summaryDump);
        AssertNpcNameAndKind(replay, 29327, "大葉格拉比", NpcKind.Monster, summaryDump);
        AssertNpcNameAndKind(replay, 26373, "徬徨的風精靈", NpcKind.Monster, summaryDump);
        AssertNpcNameAndKind(replay, 25464, "幻影魔法格拉比", NpcKind.Monster, summaryDump);
    }

    [Fact]
    public void Replay_Recovers_BossCatalog_From_4136_State_Packets()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), ResourceDatabase.LoadNpcCatalog("zh-TW"));

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.BossCatalogState4136}"));

        Assert.True(replay.ReplayedLines > 0);
        var summaryDump = BuildSummaryDump(replay.Combatants);
        AssertNpcNameAndKind(replay, 22315, "狂暴的佩爾克", NpcKind.Boss, summaryDump);
        AssertNpcNameAndNotBoss(replay, 16737, "地之精靈", summaryDump);
        AssertNpcNameAndNotBoss(replay, 24740, "水之精靈", summaryDump);
    }

    [Fact]
    public void Replay_Recovers_SummonOwner_And_Catalog_From_4136_Create_State()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), ResourceDatabase.LoadNpcCatalog("zh-TW"));

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.SummonCreateState4136}"));

        Assert.True(replay.ReplayedLines > 0);
        var summaryDump = BuildSummaryDump(replay.Combatants);
        const int ownerId = 1795;
        var summonOwnerByInstance = SceneReplayTestView.SummonOwnerByInstance(replay);

        Assert.Equal(ownerId, summonOwnerByInstance[30110]);
        Assert.Equal(ownerId, summonOwnerByInstance[20255]);
        AssertSummonNpc(replay, 30110, 2920650, "神聖氣息", summaryDump);
        AssertSummonNpc(replay, 20255, 2920650, "神聖氣息", summaryDump);
        Assert.DoesNotContain(replay.Combatants, static combatant => combatant.CombatantId is 30110 or 20255);
        Assert.Contains(replay.Combatants, static combatant => combatant.CombatantId == ownerId && combatant.OutgoingDamage > 6_500_000);
    }

    [Fact]
    public void Replay_20260426140354_SummonRestores_And_TargetSupport_Are_Classified_From_PacketShape()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), ResourceDatabase.LoadNpcCatalog("zh-TW"));

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.SummonRestoresAndTargetSupport}"));

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
                .GroupBy(static packet => new { packet.SourceId, packet.SkillCode, packet.BodyResourceEffectRef, packet.DetailResourceEffectRef })
                .Select(static group => new
                {
                    group.Key.SourceId,
                    group.Key.SkillCode,
                    BodyRef = group.Key.BodyResourceEffectRef.RawId,
                    DetailRef = group.Key.DetailResourceEffectRef.RawId,
                    Damage = group.Sum(static packet => packet.Damage),
                    Attempts = group.Sum(static packet => packet.AttemptContribution),
                    Hits = group.Sum(static packet => packet.HitContribution),
                    Count = group.Count()
                })
                .OrderByDescending(static entry => entry.Damage)
                .Select(entry => $"source={entry.SourceId} skill={entry.SkillCode} bodyRef={entry.BodyRef} detailRef={entry.DetailRef} damage={entry.Damage} attempts={entry.Attempts} hits={entry.Hits} packets={entry.Count}"));
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
                    packet.BodyResourceEffectRef,
                    packet.DetailResourceEffectRef,
                    packet.ValueKind,
                    InWindow = true,
                    RawSource = packet.SourceId,
                    IsSelfTarget = packet.TargetId == playerId,
                    IsSummonTarget = playerOwnedIds.Contains(packet.TargetId) && packet.TargetId != playerId
                })
                .Select(group => new
                {
                    group.Key.SkillCode,
                    BodyRef = group.Key.BodyResourceEffectRef.RawId,
                    DetailRef = group.Key.DetailResourceEffectRef.RawId,
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
                    $"skill={entry.SkillCode} bodyRef={entry.BodyRef} detailRef={entry.DetailRef} value={entry.ValueKind} inWindow={entry.InWindow} rawSource={entry.RawSource} self={entry.IsSelfTarget} summonTarget={entry.IsSummonTarget} damage={entry.Damage} count={entry.Count}"));
        var spiritDescentPacketDump = string.Join(
            Environment.NewLine,
            SceneReplayTestView.BySource(replay).Values
                .SelectMany(static queue => queue)
                .Where(static packet => packet.SkillCode == 16990004)
                .OrderBy(static packet => packet.Timestamp)
                .Select(packet =>
                    $"t={packet.Timestamp} src={packet.SourceId} tgt={packet.TargetId} dmg={packet.Damage} kind={packet.EventKind}/{packet.ValueKind} layout={packet.LayoutTag} flag={packet.Flag} type={packet.Type} loop={packet.Loop} detail=0x{packet.DetailRaw:X16} marker={packet.Marker} unknown={packet.Unknown} sourceSummon={SceneReplayTestView.SummonOwnerByInstance(replay).ContainsKey(packet.SourceId)} targetSummon={SceneReplayTestView.SummonOwnerByInstance(replay).ContainsKey(packet.TargetId)}"));
        var diagnostics =
            $"target={replay.Snapshot.TargetObservation?.InstanceId} encounter={replay.Snapshot.EncounterStartTime}-{replay.Snapshot.EncounterEndTime}\ncombatants:\n{combatantDump}\nsummaries:\n{summaryDump}\nsummons:\n{summonDump}\ntargets:\n{targetDump}\nplayer-healing-groups:\n{playerHealingGroupDump}\nspirit-descent-packets:\n{spiritDescentPacketDump}\nplayer-incoming:\n{playerIncomingDump}";

        Assert.True(replay.Snapshot.Combatants.TryGetValue(playerId, out var playerMetrics), diagnostics);
        var playerSkills = replay.SceneOwner.CreateSkillBreakdown(replay.Snapshot, playerId).Skills;
        Assert.False(
            playerSkills.TryGetBySkillCode(16990004, out var spiritDescentRestore) && spiritDescentRestore.HealingAmount > 0,
            diagnostics);

        var summonOwnerByInstance = SceneReplayTestView.SummonOwnerByInstance(replay);
        var ownerTargetResourceValues = SceneReplayTestView.Packets(replay)
            .Where(packet =>
                summonOwnerByInstance.TryGetValue(packet.SourceId, out var ownerId) &&
                ownerId == packet.TargetId &&
                packet.Damage > 0 &&
                packet.PeriodicRelation == PeriodicEffectRelation.None &&
                packet.LayoutTag == 4 &&
                packet.Flag == 0 &&
                packet.Type == 2 &&
                packet.Loop == 1 &&
                (packet.HitContribution > 0 || packet.AttemptContribution > 0))
            .ToArray();
        Assert.NotEmpty(ownerTargetResourceValues);
        Assert.All(ownerTargetResourceValues, static packet =>
        {
            Assert.Equal(CombatEventKind.Support, packet.EventKind);
            Assert.Equal(CombatValueKind.Support, packet.ValueKind);
            Assert.False(packet.ContributesDamage);
            Assert.False(packet.ContributesHealing);
        });

        foreach (var summonId in playerOwnedIds.Where(static id => id != playerId))
        {
            var summonSummary = Assert.Single(replay.Combatants, summary => summary.CombatantId == summonId);
            Assert.True(summonSummary.IncomingDamage == 0, diagnostics);
        }
    }

    [Fact]
    public void Replay_20260512223507_CompactType2SidecarsCancelPendingEvadesByPacketStructure()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CompactSidecarCancellation}"));
        var expected = new[]
        {
            new { SourceId = 119157, TargetId = 2681, Marker = 4, Timestamp = 1778596536127L },
            new { SourceId = 145776, TargetId = 2681, Marker = 1, Timestamp = 1778596672584L }
        };

        foreach (var key in expected)
        {
            var raw = Enumerable.Range(0, replay.SceneJournal.Count)
                .Select(index => replay.SceneJournal.Read(index))
                .Where(entry =>
                    entry.Raw.Opcode == 0x0438 &&
                    entry.SourceEntityId == key.SourceId &&
                    entry.TargetEntityId == key.TargetId &&
                    entry.Raw.TimestampMilliseconds == key.Timestamp &&
                    entry.Combat is { } observation &&
                    observation.Marker == key.Marker &&
                    observation.HitCount == 0 &&
                    observation.AttemptCount == 0 &&
                    observation.Type is 1 or 2)
                .Select(static entry => entry.Combat!.Value.Type)
                .ToArray();

            Assert.Contains(1, raw);
            Assert.Contains(2, raw);
            Assert.DoesNotContain(
                replay.SceneOwner.Combat.Events,
                combat =>
                    combat.SourceId == key.SourceId &&
                    combat.TargetId == key.TargetId &&
                    combat.ObservedAtMilliseconds == key.Timestamp &&
                    combat.Observation.Marker == key.Marker &&
                    combat.Observation.EffectTag == PacketEffectTag.CompactEvade);
        }
    }

    [Fact]
    public void Replay_20260426031332_EnhanceSpiritBenediction_Self_And_Summon_Healing_Match_Game_Ground_Truth()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.EnhanceSpiritBenedictionSelfAndSummonHealing}"));

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
                .Where(static packet => packet.SkillCode == 16990004)
                .OrderBy(static packet => packet.Timestamp)
                .Select(packet =>
                    $"t={packet.Timestamp} src={packet.SourceId} tgt={packet.TargetId} dmg={packet.Damage} kind={packet.EventKind}/{packet.ValueKind} layout={packet.LayoutTag} flag={packet.Flag} type={packet.Type} loop={packet.Loop} detail=0x{packet.DetailRaw:X16} marker={packet.Marker} unknown={packet.Unknown} periodic={packet.PeriodicRelation}:{packet.PeriodicMode} sourceSummon={SceneReplayTestView.SummonOwnerByInstance(replay).ContainsKey(packet.SourceId)} targetSummon={SceneReplayTestView.SummonOwnerByInstance(replay).ContainsKey(packet.TargetId)}"));
        Assert.True(playerMetrics.HealingAmount == 3438, $"HealingAmount={playerMetrics.HealingAmount} expected=3438 encounter={replay.Snapshot.EncounterStartTime}-{replay.Snapshot.EncounterEndTime}\n{skillDump}\n{spiritDump}\n{combatantDump}");
        Assert.True(playerSkills.TryGetBySkillCode(enhanceSpiritBenedictionSkillCode, out var skill), combatantDump);
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

    [Theory]
    [MemberData(nameof(ReplayScenarioCatalog.DeterministicReplayLogs), MemberType = typeof(ReplayScenarioCatalog))]
    public void SceneReplay_VendoredLog_IsDeterministic(string fileName)
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());
        var path = FixtureHelper.GetPath($"logs/{fileName}");
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
        var path = FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CanonicalSceneReplayLog}");
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
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CanonicalSceneReplayLog}"));

        Assert.NotSame(PacketLogReplayBaselineCounters.Empty, replay.BaselineCounters);
        AssertBaselineCounter(replay.BaselineCounters.ReplayIngest);
        AssertBaselineCounter(replay.BaselineCounters.SnapshotCreation);
        AssertBaselineCounter(replay.BaselineCounters.CombatantSummaryCreation);
    }

    [Fact]
    public void SceneReplay_AlwaysExposesSceneJournal()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CanonicalSceneReplayLog}"));

        Assert.True(replay.SceneJournal.Count > 0);
        Assert.NotNull(replay.SceneOwner);
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
        Assert.Equal(expected.Combatants.Keys.Order().ToArray(), actual.Combatants.Keys.Order().ToArray());

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

    private static void AssertMode10PacketOnlyDamage(
        PacketLogReplayResult replay,
        int sourceId,
        int targetId,
        int combatantId,
        int tailSkillCode,
        int expectedPacketCount,
        long expectedDamage)
    {
        var packets = ReadRawMode10Packets(replay, sourceId, targetId, tailSkillCode);
        var packetDump = string.Join(
            Environment.NewLine,
            packets.Select(static packet =>
                $"skill={packet.SkillCode} tail={packet.PeriodicTailSkillCodeRaw} damage={packet.Damage} tailLen={packet.PeriodicTailLength} tailPrefix={packet.PeriodicTailPrefixValue} value={packet.ValueKind}"));

        Assert.Equal(expectedPacketCount, packets.Length);
        Assert.Equal(expectedDamage, packets.Sum(static packet => packet.Damage));
        Assert.All(packets, static packet => Assert.Equal(4, packet.PeriodicTailLength));
        Assert.All(packets, static packet => Assert.Equal(0, packet.PeriodicTailPrefixValue));
        Assert.All(packets, static packet => Assert.True(packet.Damage > 0));

        Assert.Contains(replay.Combatants, combatant => combatant.CombatantId == combatantId && combatant.OutgoingDamage >= expectedDamage);

        var skills = replay.SceneOwner.CreateSkillBreakdown(replay.Snapshot, combatantId).Skills;
        var skillDump = BuildSkillDump(skills);
        Assert.True(skills.TryGetBySkillCode(tailSkillCode, out var skill), $"{packetDump}\n{skillDump}");
        Assert.True(skill.PeriodicDamageAmount == expectedDamage, $"PeriodicDamageAmount={skill.PeriodicDamageAmount} expected={expectedDamage}\n{packetDump}\n{skillDump}");
        Assert.True(skill.PeriodicDamageTimes == expectedPacketCount, $"PeriodicDamageTimes={skill.PeriodicDamageTimes} expected={expectedPacketCount}\n{packetDump}\n{skillDump}");
    }

    private static void AssertPcName(PacketLogReplayResult replay, int combatantId, string expectedName, string summaryDump)
    {
        var combatant = Assert.Single(replay.Combatants, summary => summary.CombatantId == combatantId);
        Assert.Equal(expectedName, combatant.DisplayName);
        Assert.True(replay.SceneOwner.MetadataRegistry.TryGetPcMetadata(combatantId, out var metadata), summaryDump);
        Assert.Equal(expectedName, metadata.Nickname);
    }

    private static void AssertPcNameAndClass(PacketLogReplayResult replay, int combatantId, string expectedName, CharacterClass expectedClass, string summaryDump)
    {
        AssertPcName(replay, combatantId, expectedName, summaryDump);
        Assert.True(replay.SceneOwner.MetadataRegistry.TryGetPcMetadata(combatantId, out var metadata), summaryDump);
        Assert.Equal(expectedClass, metadata.CharacterClass);
        Assert.True(replay.SceneOwner.Entities.TryGet(combatantId, out var entity), summaryDump);
        Assert.Equal(expectedClass, entity.CharacterClass);
    }

    private static void AssertNpcNameAndKind(PacketLogReplayResult replay, int combatantId, string expectedName, NpcKind expectedKind, string summaryDump)
    {
        var combatant = Assert.Single(replay.Combatants, summary => summary.CombatantId == combatantId);
        Assert.Equal(expectedName, combatant.DisplayName);
        Assert.True(replay.SceneOwner.MetadataRegistry.TryGetNpcCode(combatantId, out var npcCode), summaryDump);
        Assert.True(replay.SceneOwner.Entities.TryGet(combatantId, out var entity), summaryDump);
        Assert.Equal(expectedKind, entity.Kind);
        Assert.True(CombatResourceRegistry.TryResolveNpcCatalogEntry(npcCode, out var entry), summaryDump);
        Assert.Equal(expectedName, entry.Name);
    }

    private static void AssertSummonNpc(PacketLogReplayResult replay, int combatantId, int expectedNpcCode, string expectedName, string summaryDump)
    {
        Assert.True(replay.SceneOwner.MetadataRegistry.TryGetNpcCode(combatantId, out var npcCode), summaryDump);
        Assert.Equal(expectedNpcCode, npcCode);
        Assert.Equal(expectedName, SceneReplayTestView.ResolveDisplayName(replay, combatantId));
        Assert.True(replay.SceneOwner.Entities.TryGet(combatantId, out var entity), summaryDump);
        Assert.Equal(NpcKind.Summon, entity.Kind);
        Assert.Equal(expectedNpcCode, entity.NpcCode);
        Assert.True(CombatResourceRegistry.TryResolveNpcCatalogEntry(npcCode, out var entry), summaryDump);
        Assert.Equal(expectedName, entry.Name);
        Assert.Equal(NpcKind.Summon, CombatResourceRegistry.ResolveNpcKind(entry.Kind));
    }

    private static void AssertNpcNameAndNotBoss(PacketLogReplayResult replay, int combatantId, string expectedName, string summaryDump)
    {
        var combatant = Assert.Single(replay.Combatants, summary => summary.CombatantId == combatantId);
        Assert.Equal(expectedName, combatant.DisplayName);
        Assert.True(replay.SceneOwner.MetadataRegistry.TryGetNpcCode(combatantId, out var npcCode), summaryDump);
        Assert.True(replay.SceneOwner.Entities.TryGet(combatantId, out var entity), summaryDump);
        Assert.NotEqual(NpcKind.Boss, entity.Kind);
        Assert.True(CombatResourceRegistry.TryResolveNpcCatalogEntry(npcCode, out var entry), summaryDump);
        Assert.Equal(expectedName, entry.Name);
    }

    private static CombatObservation[] ReadRawMode10Packets(PacketLogReplayResult replay, int sourceId, int targetId, int tailSkillCode)
    {
        var packets = new List<CombatObservation>();
        for (var ordinal = replay.SceneJournal.FirstObservationOrdinal; ordinal < replay.SceneJournal.NextObservationOrdinal; ordinal++)
        {
            var entry = replay.SceneJournal.Read(ordinal);
            if (entry.SourceEntityId != sourceId ||
                entry.TargetEntityId != targetId ||
                entry.Combat is not { } observation ||
                observation.PeriodicRelation != PeriodicEffectRelation.Target ||
                observation.PeriodicMode != 10 ||
                observation.PeriodicTailSkillCodeRaw != tailSkillCode)
            {
                continue;
            }

            packets.Add(observation);
        }

        return [.. packets];
    }

    private static string BuildSkillDump(SkillMetricsSnapshotMap skills)
    {
        return string.Join(
            Environment.NewLine,
            skills
                .OrderByDescending(static pair => pair.Value.DamageAmount + pair.Value.PeriodicDamageAmount)
                .Select(static pair => $"skill={pair.Key} direct={pair.Value.DamageAmount} periodic={pair.Value.PeriodicDamageAmount} times={pair.Value.PeriodicDamageTimes}"));
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

}
