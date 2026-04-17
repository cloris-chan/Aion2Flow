using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.PacketCapture.Protocol;
using Cloris.Aion2Flow.PacketCapture.Readers;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.PacketCapture;

public sealed class PacketLogReplayServiceTests
{
    [Fact]
    public void Replay_Reconstructs_Battle_Snapshot_And_Combatant_Summaries_From_Frame_Log()
    {
        CombatMetricsEngine.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var firstLine = "2026-04-10T16:15:36.2148073+08:00|damage|16777343:62420->16777343:52250|target=200287|source=16039|skillRaw=17010230|damage=3593|skill=17010230|baseSkill=17010000|charge=0|specs=2+3|skillName=Earth's Retribution|skillKind=Damage|skillSemantics=Damage, Support|valueKind=Damage|data=230438DF9C0C1400A77D368E03014E02033F636501000000D88501891C01";
        var secondLine = "2026-04-10T16:15:36.3112138+08:00|damage|16777343:62420->16777343:52250|target=200287|source=16039|skillRaw=17730001|damage=2875|skill=17730000|baseSkill=17730000|charge=1|skillName=Empyrean Lord's Grace|skillKind=Damage|skillSemantics=Damage, Support|valueKind=Damage|data=220438DF9C0C0400A77DD1890E015002AFD5AD6901000000D88501BB1601";
        var metaLine = "2026-04-10T16:15:36.1000000+08:00|frame-batch|16777343:62420->16777343:52250|offset=0|frameLength=35|data=230438DF9C0C1400A77D368E03014E02033F636501000000D88501891C01";

        var firstPacket = ParseDamagePacket("230438DF9C0C1400A77D368E03014E02033F636501000000D88501891C01");
        var secondPacket = ParseDamagePacket("220438DF9C0C0400A77DD1890E015002AFD5AD6901000000D88501BB1601");
        var expectedBattleTime =
            DateTimeOffset.Parse("2026-04-10T16:15:36.3112138+08:00").ToUnixTimeMilliseconds() -
            DateTimeOffset.Parse("2026-04-10T16:15:36.2148073+08:00").ToUnixTimeMilliseconds();

        var path = WriteTempReplayLog("frame", metaLine, firstLine, secondLine);
        try
        {
            var replay = PacketLogReplayService.Replay(path);

            Assert.Equal(3, replay.TotalLines);
            Assert.Equal(2, replay.ReplayedLines);
            Assert.Equal(1, replay.SkippedLines);
            Assert.Equal(2, replay.ReplayedEventCounts["damage"]);
            Assert.Equal(1, replay.SkippedEventCounts["frame-batch"]);
            Assert.Equal(expectedBattleTime, replay.Snapshot.BattleTime);
            Assert.Equal(200287, replay.Snapshot.TargetObservation?.InstanceId);

            var source = Assert.Single(replay.Combatants, static summary => summary.CombatantId == 16039);
            Assert.Equal(firstPacket.Damage + secondPacket.Damage, source.OutgoingDamage);
            Assert.Equal((firstPacket.IsCritical ? 1 : 0) + (secondPacket.IsCritical ? 1 : 0), source.OutgoingCriticals);
            Assert.Equal(2, source.OutgoingHits);
            Assert.Equal(2, source.OutgoingAttempts);

            var target = Assert.Single(replay.Combatants, static summary => summary.CombatantId == 200287);
            Assert.Equal(firstPacket.Damage + secondPacket.Damage, target.IncomingDamage);
            Assert.Equal(source.OutgoingCriticals, target.IncomingCriticals);
            Assert.Equal(2, target.IncomingHits);
            Assert.Equal(2, target.IncomingAttempts);
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
        CombatMetricsEngine.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());

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
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

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
    public void Replay_20260415_Outgoing_Combat_Stats_Match_Game_Ground_Truth()
    {
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260415211500.log"));

        Assert.True(replay.ReplayedLines > 0);

        var player = replay.Combatants
            .OrderByDescending(static s => s.OutgoingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        var diagDump = $"Player: id={player.CombatantId} hits={player.OutgoingHits} att={player.OutgoingAttempts} inv={player.OutgoingInvincibles}\n{summaryDump}";

        Assert.True(player.OutgoingDamage == 20211224, $"OutgoingDamage={player.OutgoingDamage}\n{diagDump}");
        Assert.True(player.OutgoingInvincibles == 8, $"OutgoingInvincibles={player.OutgoingInvincibles}\n{diagDump}");
        Assert.True(player.OutgoingHits == 1305, $"OutgoingHits={player.OutgoingHits}\n{diagDump}");
        Assert.True(player.OutgoingAttempts == 1313, $"OutgoingAttempts={player.OutgoingAttempts}\n{diagDump}");
    }

    [Fact]
    public void Replay_20260416021557_Outgoing_Combat_Stats_Match_Game_Ground_Truth()
    {
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260416021557.log"));

        Assert.True(replay.ReplayedLines > 0);

        var player = replay.Combatants
            .OrderByDescending(static s => s.OutgoingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        var diagDump = $"Player: id={player.CombatantId} hits={player.OutgoingHits} att={player.OutgoingAttempts} inv={player.OutgoingInvincibles}\n{summaryDump}";

        Assert.True(player.OutgoingDamage == 7920567, $"OutgoingDamage={player.OutgoingDamage}\n{diagDump}");
        Assert.True(player.OutgoingHits == 1170, $"OutgoingHits={player.OutgoingHits}\n{diagDump}");
        Assert.True(player.OutgoingAttempts == 1170, $"OutgoingAttempts={player.OutgoingAttempts}\n{diagDump}");

        const int gameReportedHealing021557 = 583068;
        Assert.True(player.IncomingHealing == gameReportedHealing021557, $"IncomingHealing={player.IncomingHealing} expected={gameReportedHealing021557}\n{diagDump}");
    }

    [Fact]
    public void Replay_20260416021406_Outgoing_Combat_Stats_Match_Game_Ground_Truth()
    {
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260416021406.log"));

        Assert.True(replay.ReplayedLines > 0);

        var player = replay.Combatants
            .OrderByDescending(static s => s.OutgoingDamage)
            .First();

        var summaryDump = BuildSummaryDump(replay.Combatants);
        var diagDump = $"Player: id={player.CombatantId} hits={player.OutgoingHits} att={player.OutgoingAttempts} inv={player.OutgoingInvincibles} dmg={player.OutgoingDamage}\n{summaryDump}";

        Assert.True(player.OutgoingDamage == 3961239, $"OutgoingDamage={player.OutgoingDamage}\n{diagDump}");
        Assert.True(player.OutgoingHits == 525, $"OutgoingHits={player.OutgoingHits}\n{diagDump}");
        Assert.True(player.OutgoingAttempts == 525, $"OutgoingAttempts={player.OutgoingAttempts}\n{diagDump}");

        const int gameReportedHealing021406 = 141564;
        Assert.True(player.IncomingHealing == gameReportedHealing021406, $"IncomingHealing={player.IncomingHealing} expected={gameReportedHealing021406}\n{diagDump}");
    }

    [Fact]
    public void Replay_20260417003456_Ground_AoE_Entities_Attributed_To_Owning_Player()
    {
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260417003456.log"));

        Assert.True(replay.ReplayedLines > 0);

        var snapshot = replay.Snapshot;
        var combatantDump = string.Join("\n", snapshot.Combatants
            .OrderByDescending(c => c.Value.DamageAmount)
            .Select(c => $"id={c.Key} class={c.Value.CharacterClass} dmg={c.Value.DamageAmount} heal={c.Value.HealingAmount} name={c.Value.Nickname}"));

        Assert.False(snapshot.Combatants.ContainsKey(99306), $"Ground AoE entity 99306 should not appear separately.\n{combatantDump}");
        Assert.False(snapshot.Combatants.ContainsKey(39022), $"Ground AoE entity 39022 should not appear separately.\n{combatantDump}");

        Assert.True(snapshot.Combatants.TryGetValue(664, out var cleric), $"Cleric 664 not found.\n{combatantDump}");
        Assert.True(cleric.DamageAmount == 3421060, $"Cleric damage={cleric.DamageAmount} expected=3421060\n{combatantDump}");
    }

    [Fact]
    public void Replay_20260417023559_Cleric_Healing_No_False_Drain()
    {
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260417023559.log"));
        Assert.True(replay.ReplayedLines > 0);

        var snapshot = replay.Snapshot;
        var player = snapshot.Combatants
            .OrderByDescending(c => c.Value.DamageAmount)
            .First();

        var metrics = player.Value;

        var combatantDump = string.Join("\n", snapshot.Combatants
            .OrderByDescending(c => c.Value.DamageAmount)
            .Select(c => $"id={c.Key} class={c.Value.CharacterClass} dmg={c.Value.DamageAmount} heal={c.Value.HealingAmount} name={c.Value.Nickname}"));

        var divineAuraSkillCode = 17150340;
        if (metrics.Skills.TryGetValue(divineAuraSkillCode, out var divineAura))
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
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260417141813.log"));
        Assert.True(replay.ReplayedLines > 0);

        var snapshot = replay.Snapshot;
        var player = snapshot.Combatants
            .OrderByDescending(c => c.Value.DamageAmount)
            .First();

        var metrics = player.Value;

        var lightOfRegenBaseSkill = 17090000;
        var lightOfRegenSkills = metrics.Skills
            .Where(kvp => kvp.Key / 10000 * 10000 == lightOfRegenBaseSkill || kvp.Key == lightOfRegenBaseSkill)
            .ToList();

        var skillDump = string.Join("\n", lightOfRegenSkills
            .Select(kvp => $"skill={kvp.Key} heal={kvp.Value.HealingAmount} periodic={kvp.Value.PeriodicHealingAmount} drain={kvp.Value.DrainHealingAmount}"));

        const int expectedLightOfRegenHealing = 4599;
        var totalLightOfRegenHealing = lightOfRegenSkills.Sum(kvp => kvp.Value.HealingAmount);
        Assert.True(totalLightOfRegenHealing == expectedLightOfRegenHealing,
            $"LightOfRegen total={totalLightOfRegenHealing} expected={expectedLightOfRegenHealing}\n{skillDump}");
    }

    private static string WriteTempReplayLog(string logKind, params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.{logKind}.log");
        File.WriteAllLines(path, lines);
        return path;
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

    private static ParsedCombatPacket ParseDamagePacket(string hex)
    {
        var packet = HexHelper.Parse(hex);
        var ok = Packet0438DamageParser.TryParse(packet, out var parsed);
        if (!ok)
        {
            var reader = new PacketSpanReader(packet);
            Assert.True(reader.TryReadVarInt(out _));
            ok = Packet0438DamageParser.TryParsePayload(packet.AsSpan()[reader.Offset..], out parsed, out _);
        }

        Assert.True(ok);

        return new ParsedCombatPacket
        {
            SourceId = parsed.SourceId,
            TargetId = parsed.TargetId,
            OriginalSkillCode = parsed.SkillCodeRaw,
            SkillCode = parsed.SkillCodeRaw,
            Marker = parsed.Marker,
            Type = parsed.Type,
            Damage = parsed.Damage,
            Modifiers = parsed.Modifiers
        };
    }

    private static SkillCollection BuildReplaySkillMap()
    {
        return
        [
            new Skill(1230000, "Fangs", SkillCategory.Npc, SkillSourceType.Unknown, "npc", SkillKind.Damage, SkillSemantics.Damage, null),
            new Skill(17000100, "Dodge", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", SkillKind.Support, SkillSemantics.Support, null),
            new Skill(17010230, "Earth's Retribution", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage | SkillSemantics.Support, null),
            new Skill(17730000, "Empyrean Lord's Grace", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage | SkillSemantics.Support, null)
        ];
    }
}
