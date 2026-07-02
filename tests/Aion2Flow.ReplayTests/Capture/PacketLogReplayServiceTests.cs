using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketLogReplayServiceTests
{
    [Fact]
    public void Replay_20260702031011_Parses_Current0438_Damage_And_Modifier_Layout()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentAssassinDirectDamage}"));

        Assert.True(replay.ReplayedLines > 0);

        const int playerId = 6455;
        var player = Assert.Single(replay.Combatants, static combatant => combatant.CombatantId == playerId);
        Assert.Equal(4_636_957, player.OutgoingDamage);
        Assert.Equal(246, player.OutgoingHits);
        Assert.Equal(246, player.OutgoingAttempts);
        Assert.Equal(215, player.OutgoingCriticals);
        Assert.Equal(38_083, player.OutgoingHealing);

        var packets = SceneReplayTestView.Packets(replay);
        AssertDamageSkill(packets, playerId, 13_040_250, 878_254, 64, 64, 13, 24, 0, 64, 20);
        AssertDamageSkill(packets, playerId, 13_030_250, 759_018, 48, 48, 15, 20, 0, 48, 14);
        AssertDamageSkill(packets, playerId, 13_800_007, 749_727, 28, 18, 11, 17, 0, 28, 0);
        AssertDamageSkill(packets, playerId, 13_010_250, 748_340, 34, 34, 6, 16, 0, 34, 10);
        AssertDamageSkill(packets, playerId, 13_351_450, 401_454, 4, 4, 0, 1, 0, 4, 4);
        AssertDamageSkill(packets, playerId, 13_730_007, 22_706, 0, 0, 0, 0, 0, 0, 0);
    }

    [Fact]
    public void Replay_20260702054027_Parses_Current0438_Regeneration_And_InlineRecovery()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentBrawlerRegenerationRecovery}"));

        Assert.True(replay.ReplayedLines > 0);

        const int playerId = 2141;
        var player = Assert.Single(replay.Combatants, static combatant => combatant.CombatantId == playerId);
        Assert.Equal(4_960_083, player.OutgoingDamage);
        Assert.Equal(489, player.OutgoingHits);
        Assert.Equal(489, player.OutgoingAttempts);
        Assert.Equal(329_163, player.OutgoingHealing);
        Assert.Equal(43_170, player.IncomingDamage);
        Assert.Equal(17, player.IncomingHits);
        Assert.Equal(24, player.IncomingAttempts);
        Assert.Equal(3, player.IncomingEvades);
        Assert.Equal(4, player.IncomingInvincibles);

        var packets = SceneReplayTestView.Packets(replay);
        var regenerationHealing = packets
            .Where(static packet => packet.SourceId == playerId && packet.TargetId == playerId && packet.EffectTag == PacketEffectTag.RegenerationHealing)
            .ToArray();
        Assert.Equal(2, regenerationHealing.Length);
        Assert.Equal(1_209, regenerationHealing.Sum(static packet => packet.Damage));

        AssertSkillValueKind(replay, skillCode: 12_350_150, CombatEventKind.Healing, CombatValueKind.Healing, expectedCount: 3, expectedAmount: 7_808);
        Assert.Equal(48_912, packets.Where(static packet => packet.SourceId == playerId && packet.SkillCode == 1_900_911 && packet.ValueKind == CombatValueKind.Healing).Sum(static packet => packet.Damage));
        Assert.Equal(6_329, packets.Where(static packet => packet.SourceId == playerId && packet.SkillCode == 1_900_911 && packet.ValueKind == CombatValueKind.Support).Sum(static packet => packet.Damage));
    }

    [Fact]
    public void Replay_20260702054027_Applies_Current3336_SelfIdentity()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentBrawlerRegenerationRecovery}"));

        const int playerId = 2141;
        Assert.Contains(
            ReadAllJournalEntries(replay),
            static entry => entry.Raw.Opcode == 0x3336 &&
                            entry.State is { EntityId: playerId, StateCode: StateCodes.PlayerIdentity, Text: "綠豆冰糕", IsLocalPlayer: true, OriginServerId: 1007, Faction: Faction.Light });

        Assert.True(replay.SceneOwner.MetadataRegistry.TryGetPcMetadata(playerId, out var metadata));
        Assert.Equal("綠豆冰糕", metadata.Nickname);
        Assert.Equal(Faction.Light, metadata.Faction);
    }

    private static void SetResources() => CombatResourceRegistry.SetGameResources(ResourceCatalog.Load(ResourceLanguage.TraditionalChinese));

    private static IReadOnlyList<ObservedEventEnvelope> ReadAllJournalEntries(PacketLogReplayResult replay)
    {
        var entries = new List<ObservedEventEnvelope>(replay.SceneJournal.Count);
        var cursor = replay.SceneJournal.CreateCursor(0);
        while (true)
        {
            var result = replay.SceneJournal.ReadEntries(cursor, 1024, batch =>
            {
                foreach (var entry in batch)
                {
                    entries.Add(entry);
                }
            });

            if (result.Count == 0)
            {
                return entries;
            }

            cursor = result.Cursor;
        }
    }

    private static void AssertDamageSkill(
        IReadOnlyList<SceneReplayPacket> packets,
        int sourceId,
        int skillCode,
        long expectedDamage,
        int expectedHits,
        int expectedCriticals,
        int expectedPerfects,
        int expectedSmites,
        int expectedFronts,
        int expectedBacks,
        int expectedMultiHits)
    {
        var matching = packets
            .Where(packet => packet.SourceId == sourceId && packet.SkillCode == skillCode && packet.ContributesDamage)
            .ToArray();
        var dump = string.Join(
            Environment.NewLine,
            matching.Select(static packet =>
                $"t={packet.Timestamp} skill={packet.SkillCode} damage={packet.Damage} hits={packet.HitContribution} mods={packet.Modifiers} multi={packet.MultiHitCount} layout={packet.LayoutTag} type={packet.Type} loop={packet.Loop} detail=0x{packet.DetailRaw:X16}"));

        AssertMetric(matching.Sum(static packet => packet.Damage), expectedDamage, "damage", dump);
        AssertMetric(matching.Sum(static packet => packet.HitContribution), expectedHits, "hits", dump);
        AssertMetric(matching.Sum(static packet => CountHitsWith(packet, DamageModifiers.Critical)), expectedCriticals, "criticals", dump);
        AssertMetric(matching.Sum(static packet => CountHitsWith(packet, DamageModifiers.Perfect)), expectedPerfects, "perfects", dump);
        AssertMetric(matching.Sum(static packet => CountHitsWith(packet, DamageModifiers.Smite)), expectedSmites, "smites", dump);
        AssertMetric(matching.Sum(static packet => CountHitsWith(packet, DamageModifiers.Front)), expectedFronts, "fronts", dump);
        AssertMetric(matching.Sum(static packet => CountHitsWith(packet, DamageModifiers.Back)), expectedBacks, "backs", dump);
        AssertMetric(matching.Sum(static packet => CountHitsWith(packet, DamageModifiers.MultiHit)), expectedMultiHits, "multiHits", dump);
    }

    private static void AssertSkillValueKind(PacketLogReplayResult replay, int skillCode, CombatEventKind eventKind, CombatValueKind valueKind, int expectedCount, long expectedAmount)
    {
        var matching = replay.SceneOwner.Combat.Events
            .Where(e => e.Observation.SkillCode == skillCode &&
                        e.Observation.BodySkillVariantRaw == skillCode &&
                        e.Observation.EventKind == eventKind &&
                        e.Observation.ValueKind == valueKind)
            .ToArray();
        var skillDump = string.Join(
            Environment.NewLine,
            replay.SceneOwner.Combat.Events
                .Where(e => e.Observation.SkillCode == skillCode || e.Observation.BodySkillVariantRaw == skillCode)
                .GroupBy(e => new { e.Observation.SkillCode, e.Observation.BodySkillVariantRaw, e.Observation.EventKind, e.Observation.ValueKind, e.ContributesDamage, e.ContributesHealing })
                .OrderByDescending(group => group.Sum(e => e.Observation.Damage))
                .Select(group =>
                    $"skill={group.Key.SkillCode} body={group.Key.BodySkillVariantRaw} event={group.Key.EventKind} value={group.Key.ValueKind} contribD={group.Key.ContributesDamage} contribH={group.Key.ContributesHealing} count={group.Count()} amount={group.Sum(e => e.Observation.Damage)}"));

        Assert.True(matching.Length == expectedCount, $"count={matching.Length} expected={expectedCount}\n{skillDump}");
        Assert.True(matching.Sum(e => e.Observation.Damage) == expectedAmount, $"amount={matching.Sum(e => e.Observation.Damage)} expected={expectedAmount}\n{skillDump}");
        Assert.All(matching, e => Assert.False(e.ContributesDamage));
        Assert.All(matching, e => Assert.True(e.ContributesHealing));
    }

    private static int CountHitsWith(SceneReplayPacket packet, DamageModifiers modifier)
        => (packet.Modifiers & modifier) != 0 ? packet.HitContribution : 0;

    private static void AssertMetric(long actual, long expected, string name, string dump)
        => Assert.True(actual == expected, $"{name}={actual} expected={expected}\n{dump}");
}
