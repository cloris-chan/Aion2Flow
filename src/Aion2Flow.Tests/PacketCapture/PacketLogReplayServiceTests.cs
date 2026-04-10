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
            var replay = new PacketLogReplayService().Replay(path);

            Assert.Equal(3, replay.TotalLines);
            Assert.Equal(2, replay.ReplayedLines);
            Assert.Equal(1, replay.SkippedLines);
            Assert.Equal(2, replay.ReplayedEventCounts["damage"]);
            Assert.Equal(1, replay.SkippedEventCounts["frame-batch"]);
            Assert.Equal(expectedBattleTime, replay.Snapshot.BattleTime);
            Assert.Equal(200287, replay.Snapshot.TargetObservation?.InstanceId);

            var source = Assert.Single(replay.Combatants, static summary => summary.ActorId == 16039);
            Assert.Equal(firstPacket.Damage + secondPacket.Damage, source.OutgoingDamage);
            Assert.Equal((firstPacket.IsCritical ? 1 : 0) + (secondPacket.IsCritical ? 1 : 0), source.OutgoingCriticals);
            Assert.Equal(2, source.OutgoingHits);
            Assert.Equal(2, source.OutgoingAttempts);

            var target = Assert.Single(replay.Combatants, static summary => summary.ActorId == 200287);
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

    [Fact]
    public void Replay_Reconstructs_Incoming_Invincible_Summary_From_Frame_Log()
    {
        CombatMetricsEngine.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var damageLine = "2026-04-10T16:15:41.4121194+08:00|damage|16777343:62420->16777343:52250|target=16039|source=200287|skillRaw=1230180|damage=1|skill=1230000|baseSkill=1230000|charge=0|specs=1|skillKind=Unknown|skillSemantics=None|valueKind=Damage|data=260438A77D4410DF9C0C64C5120002021B1B550701000000904E0101";
        var dodgeControlLine = "2026-04-10T16:15:41.6149585+08:00|compact-0638|16777343:62420->16777343:52250|source=16039|skillRaw=17000101|marker=87|flag=0|skill=17000100|baseSkill=17000000|charge=1|specs=1|skillName=Dodge|skillKind=Support|skillSemantics=Support|data=0E0638A77DA56603015700";
        var dodgeOutcomeLine = "2026-04-10T16:15:41.6155335+08:00|aux-2c38|16777343:62420->16777343:52250|source=16039|mode=2|state=0|seq=143|result=7|family=mode-2-state-0-result-7|tailLen=4|data=112C38A77D02008D0107008F0107";

        var path = WriteTempReplayLog("frame", damageLine, dodgeControlLine, dodgeOutcomeLine);
        try
        {
            var replay = new PacketLogReplayService().Replay(path);

            Assert.Equal(3, replay.ReplayedLines);
            Assert.Equal(0, replay.SkippedLines);

            var player = Assert.Single(replay.Combatants, static summary => summary.ActorId == 16039);
            Assert.Equal(1, player.IncomingDamage);
            Assert.Equal(1, player.IncomingInvincibles);
            Assert.Equal(2, player.IncomingAttempts);

            var monster = Assert.Single(replay.Combatants, static summary => summary.ActorId == 200287);
            Assert.Equal(1, monster.OutgoingDamage);
            Assert.Equal(1, monster.OutgoingAttempts);

            Assert.True(replay.Store.CombatPacketsByTarget.TryGetValue(16039, out var packets));
            Assert.Contains(packets, static packet => packet.SkillCode == SyntheticCombatSkillCodes.UnresolvedInvincible);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Replay_Reconstructs_Incoming_Invincible_Summary_From_Stream_Log()
    {
        CombatMetricsEngine.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var firstTimestamp = DateTimeOffset.Parse("2026-04-10T16:15:41.1000000+08:00");
        var lastTimestamp = DateTimeOffset.Parse("2026-04-10T16:15:41.6155335+08:00");
        var damagePacket = ParseDamagePacket("220438ADCB010400A507D1890E014402AFD5AD6901000000D88501FB1D0100");
        var damageLine = "2026-04-10T16:15:41.1000000+08:00|dir=inbound|16777343:52250->16777343:62420|seq=101|len=31|data=220438ADCB010400A507D1890E014402AFD5AD6901000000D88501FB1D0100";
        var outboundNoiseLine = "2026-04-10T16:15:41.5000000+08:00|dir=outbound|16777343:62420->16777343:52250|seq=77|len=1|data=00";
        var dodgeControlLine = "2026-04-10T16:15:41.6149585+08:00|dir=inbound|16777343:52250->16777343:62420|seq=102|len=11|data=0E0638F922A56603011600";
        var dodgeOutcomeLine = "2026-04-10T16:15:41.6155335+08:00|dir=inbound|16777343:52250->16777343:62420|seq=103|len=14|data=112C38F9220200DF140700E01407";

        var path = WriteTempReplayLog("stream", damageLine, outboundNoiseLine, dodgeControlLine, dodgeOutcomeLine);
        try
        {
            var replay = new PacketLogReplayService().Replay(path);

            Assert.Equal(4, replay.TotalLines);
            Assert.Equal(3, replay.ReplayedLines);
            Assert.Equal(1, replay.SkippedLines);
            Assert.Equal(3, replay.ReplayedEventCounts["inbound"]);
            Assert.Equal(1, replay.SkippedEventCounts["outbound-ignored"]);
            Assert.Equal(lastTimestamp.ToUnixTimeMilliseconds() - firstTimestamp.ToUnixTimeMilliseconds(), replay.Snapshot.BattleTime);

            var source = Assert.Single(replay.Combatants, summary => summary.ActorId == damagePacket.SourceId);
            Assert.Equal(damagePacket.Damage, source.OutgoingDamage);
            Assert.Equal(1, source.OutgoingHits);
            Assert.Equal(1, source.OutgoingAttempts);

            var target = Assert.Single(replay.Combatants, summary => summary.ActorId == damagePacket.TargetId);
            Assert.Equal(damagePacket.Damage, target.IncomingDamage);
            Assert.Equal(1, target.IncomingHits);
            Assert.Equal(1, target.IncomingAttempts);

            var player = Assert.Single(replay.Combatants, static summary => summary.ActorId == 4473);
            Assert.Equal(1, player.IncomingInvincibles);
            Assert.Equal(1, player.IncomingAttempts);

            Assert.True(replay.Store.CombatPacketsByTarget.TryGetValue(4473, out var packets));
            Assert.Contains(packets, static packet => packet.SkillCode == SyntheticCombatSkillCodes.UnresolvedInvincible);
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
