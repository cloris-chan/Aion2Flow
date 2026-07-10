using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;
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
    public void Replay_20260705051242_Covers_OwnerTarget_And_SystemPeriodic_Canonicalization()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentOwnerSystemCanonicalization}"));

        var ownerRows = AssertCanonicalizedRows(replay, CombatContributionCanonicalization.OwnerTargetSummonResource, expectedCount: 148);
        Assert.All(ownerRows, static row =>
        {
            Assert.Equal(CombatEventKind.Support, row.Observation.EventKind);
            Assert.Equal(CombatValueKind.Support, row.Observation.ValueKind);
        });
        AssertDirectSemanticHealsBypassOwnerCanonicalization(replay);
        var systemSeedRows = AssertCanonicalizedRows(replay, CombatContributionCanonicalization.SystemPeriodicRecoverySeed, expectedCount: 9);
        var systemHealingRows = AssertCanonicalizedRows(replay, CombatContributionCanonicalization.SystemPeriodicRecoveryHealing, expectedCount: 9);
        Assert.All(systemSeedRows, static e =>
        {
            Assert.Equal(CombatEventKind.Support, e.Observation.EventKind);
            Assert.Equal(CombatValueKind.Support, e.Observation.ValueKind);
        });
        Assert.All(systemHealingRows, static e =>
        {
            Assert.Equal(CombatEventKind.Healing, e.Observation.EventKind);
            Assert.Equal(CombatValueKind.PeriodicHealing, e.Observation.ValueKind);
        });
        AssertBalancedSystemPeriodicRecoveryPairs(systemSeedRows, systemHealingRows);
    }

    [Fact]
    public void Replay_20260704053009_Covers_OwnerTarget_CompactTwoContribution_Edge()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentOwnerTargetCanonicalizationEdge}"));

        var ownerRows = AssertCanonicalizedRows(replay, CombatContributionCanonicalization.OwnerTargetSummonResource, expectedCount: 8);
        Assert.All(ownerRows, static row =>
        {
            Assert.Equal(CombatEventKind.Support, row.Observation.EventKind);
            Assert.Equal(CombatValueKind.Support, row.Observation.ValueKind);
        });
        AssertDirectSemanticHealsBypassOwnerCanonicalization(replay);
        Assert.All(ownerRows, static row => Assert.Equal((ushort)0x0438, row.Raw.Opcode));
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

    [Fact]
    public void Replay_20260703041828_Applies_Extended3336_SelfIdentity_Without4536()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentBrawlerExtendedSelfIdentity}"));
        var entries = ReadAllJournalEntries(replay);

        const int playerId = 4233;
        Assert.DoesNotContain(entries, static entry => entry.Raw.Opcode == 0x4536);
        Assert.Contains(
            entries,
            static entry => entry.Raw.Opcode == 0x3336 &&
                            entry.State is { EntityId: playerId, StateCode: StateCodes.PlayerIdentity, Text: "dfdyhj", IsLocalPlayer: true, OriginServerId: 1014, Faction: Faction.Light });

        Assert.True(replay.SceneOwner.MetadataRegistry.TryGetPcMetadata(playerId, out var metadata));
        Assert.Equal("dfdyhj", metadata.Nickname);
        Assert.Equal(Faction.Light, metadata.Faction);
        Assert.Equal(1014, metadata.OriginServerId);
    }

    [Fact]
    public void Replay_20260705015611_Applies_CrossServer3336_SelfIdentityMarker3F()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentCrossServerSelfIdentityMarker3F}"));
        var entries = ReadAllJournalEntries(replay);

        const int playerId = 5905;
        var selfIdentityEntries = entries
            .Where(static entry => entry.Raw.Opcode == 0x3336 &&
                                   entry.State is
                                   {
                                       EntityId: playerId,
                                       StateCode: StateCodes.PlayerIdentity,
                                       Text: "綠豆冰糕",
                                       IsLocalPlayer: true,
                                       OriginServerId: 1007,
                                       Faction: Faction.Light
                                   })
            .ToArray();

        Assert.True(selfIdentityEntries.Length >= 3, $"self 3336 entries={selfIdentityEntries.Length}");
        Assert.True(replay.SceneOwner.MetadataRegistry.TryGetPcMetadata(playerId, out var metadata));
        Assert.Equal("綠豆冰糕", metadata.Nickname);
        Assert.Equal(Faction.Light, metadata.Faction);
        Assert.Equal(1007, metadata.OriginServerId);
        Assert.True(metadata.IsLocalPlayer);
    }

    [Fact]
    public void Replay_20260704202443_Applies_Current4536_Marker17PcProfiles()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentNearbyPcProfilesMarker17}"));

        AssertPcMetadata(replay, 15683, "血焰", CharacterClass.Assassin, Faction.Light);
        AssertPcMetadata(replay, 865, "侯爷丶", CharacterClass.Ranger, Faction.Light);
        AssertPcMetadata(replay, 13932, "啵里哩啵", CharacterClass.Elementalist, Faction.Light);
    }

    [Fact]
    public void Replay_20260702200648_Parses_CurrentPartyAndForceMemberRelations()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentPartyForceRelation}"));

        const int selfId = 11531;
        const int targetId = 5515;
        const uint forceGroupId = 690_480_796;
        var entries = ReadAllJournalEntries(replay);

        Assert.Contains(
            entries,
            static entry => entry.Raw.Opcode == 0x3336 &&
                            entry.State is { EntityId: selfId, StateCode: StateCodes.PlayerIdentity, IsLocalPlayer: true, Text: "謝謝惠顧" });

        Assert.Contains(
            entries,
            static entry => entry.Raw.Opcode == 0x0D92 &&
                            entry.State is { EntityId: targetId, StateCode: StateCodes.PlayerGroupMembership, GroupMembership.Kind: PlayerGroupKind.Party, GroupMembership.SubPartyIndex: 0, GroupMembership.MemberSlotIndex: 2 });

        Assert.Contains(
            entries,
            static entry => entry.Raw.Opcode == 0x1E96 &&
                            entry.State is { EntityId: selfId, StateCode: StateCodes.PlayerGroupMembership, GroupMembership.Kind: PlayerGroupKind.Force, GroupMembership.GroupId: forceGroupId, GroupMembership.SubPartyIndex: 1, GroupMembership.MemberSlotIndex: 1 });

        Assert.Contains(
            entries,
            static entry => entry.Raw.Opcode == 0x1D96 &&
                            entry.State is { EntityId: targetId, StateCode: StateCodes.PlayerGroupMembership, GroupMembership.Kind: PlayerGroupKind.Force, GroupMembership.GroupId: forceGroupId, GroupMembership.SubPartyIndex: 4 });

        Assert.True(replay.SceneOwner.MetadataRegistry.TryGetPcMetadata(targetId, out var targetMetadata));
        Assert.Equal("星昂", targetMetadata.Nickname);
        Assert.Equal(PlayerGroupRelation.PartyMember, targetMetadata.GroupRelation);
    }

    [Fact]
    public void Replay_20260704002230_Parses_CrossServerMatchedPartyRelationsFromExplicitPackets()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentCrossServerMatchedPartyRelation}"));
        var entries = ReadAllJournalEntries(replay);

        Assert.Contains(
            entries,
            static entry => entry.Raw.Opcode == 0x0092 &&
                            entry.State is { EntityId: 10780, StateCode: StateCodes.PlayerGroupMembership, GroupMembership.Kind: PlayerGroupKind.Party });

        Assert.DoesNotContain(
            entries,
            static entry => entry.Raw.Opcode == 0x048D &&
                            entry.State is { StateCode: StateCodes.PlayerGroupMembership });

        AssertGroupRelations(replay, PlayerGroupRelation.PartyMember, 9975, 10780, 14819);
        AssertGroupRelations(replay, PlayerGroupRelation.Unknown, 12478, 14000);
    }

    [Fact]
    public void Replay_20260704005035_Parses_ForceDungeonInitialRelations()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentForceDungeonInitialRelation}"));
        var entries = ReadAllJournalEntries(replay);

        Assert.Contains(
            entries,
            static entry => entry.Raw.Opcode == 0x1B96 &&
                            entry.State is { EntityId: 8108, StateCode: StateCodes.PlayerGroupMembership, GroupMembership.Kind: PlayerGroupKind.Force, GroupMembership.GroupId: 0 });

        Assert.Contains(
            entries,
            static entry => entry.Raw.Opcode == 0x0D92 &&
                            entry.SourceEntityId == 0 &&
                            entry.State is { EntityId: 0, StateCode: StateCodes.PlayerGroupMembership, Text: "浮屠", OriginServerId: 2002, GroupMembership.Kind: PlayerGroupKind.Party, GroupMembership.MemberSlotIndex: 5 });

        Assert.Contains(
            entries,
            static entry => entry.Raw.Opcode == 0x0092 &&
                            entry.SourceEntityId == 0 &&
                            entry.State is { EntityId: 0, StateCode: StateCodes.PlayerGroupMembership, Text: "浮屠", OriginServerId: 2002, GroupMembership.Kind: PlayerGroupKind.Party, GroupMembership.MemberSlotIndex: 5 });

        AssertGroupRelations(replay, PlayerGroupRelation.PartyMember, 1285, 2664, 9551, 15547);
        AssertGroupRelations(replay, PlayerGroupRelation.ForceMember, 870, 6538, 8108, 9301, 15480);
        AssertGroupRelation(replay, 9142, PlayerGroupRelation.Unknown);
    }

    [Fact]
    public void Replay_20260704010004_KeepsActivityDungeonPlayersIndependent()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentActivityDungeonIndependentPlayers}"));
        var entries = ReadAllJournalEntries(replay);

        Assert.DoesNotContain(
            entries,
            static entry => entry.Raw.Opcode == 0x0A96 &&
                            entry.State is { StateCode: StateCodes.PlayerGroupMembership });
        AssertGroupRelations(replay, PlayerGroupRelation.Unknown, 4520, 4990, 7048, 7329, 9974, 10532);
    }

    [Fact]
    public void Replay_20260704153057_Parses_ForceRosterProfilesWithoutSceneIds()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentForceDungeonPreInstanceRoster}"));
        var entries = ReadAllJournalEntries(replay);

        AssertForceRosterProfile(entries, "拳X", 2001, 1);
        AssertForceRosterProfile(entries, "折柳", 2005, 2);
        AssertForceRosterProfile(entries, "大奶的诱惑", 1004, 3);
        AssertForceRosterProfile(entries, "Apple苹果", 2010, 4);
        AssertForceRosterProfile(entries, "娜烏西卡", 2006, 5);
        AssertForceRosterProfile(entries, "韭艾", 2012, 5);
    }

    [Fact]
    public void Replay_20260704155002_DoesNotPromoteCombat048DFramesToGroupRelations()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentForceDungeonWithoutExplicitRelationPackets}"));
        var entries = ReadAllJournalEntries(replay);

        Assert.DoesNotContain(
            entries,
            static entry => entry.Raw.Opcode == 0x048D &&
                            entry.State is { StateCode: StateCodes.PlayerGroupMembership });

        AssertGroupRelations(replay, PlayerGroupRelation.Unknown, 1339, 3316, 4110, 4909, 7740, 10984, 11101, 12588, 15338, 15481);
    }

    [Fact]
    public void Replay_20260704153057_And_20260704155002_ResolvesForceDungeonRosterProfilesThroughGlobalRegistry()
    {
        SetResources();

        var replay = ReplayCombinedFixtures(
            ReplayScenarioCatalog.CurrentForceDungeonPreInstanceRoster,
            ReplayScenarioCatalog.CurrentForceDungeonWithoutExplicitRelationPackets);

        AssertGroupRelations(replay, PlayerGroupRelation.PartyMember, 3316, 4909, 7740, 15338);
        AssertGroupRelations(replay, PlayerGroupRelation.ForceMember, 1339, 4110, 10984, 11101, 12588);
        AssertGroupRelation(replay, 15481, PlayerGroupRelation.Unknown);
    }

    [Fact]
    public void Replay_20260702181554_Parses_Current0438_Defensive_Result_Modifiers()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentClericDefensiveModifiers}"));

        const int playerId = 11190;
        var player = Assert.Single(replay.Combatants, static combatant => combatant.CombatantId == playerId);
        Assert.Equal(6, player.IncomingDamage);
        Assert.Equal(6, player.IncomingHits);
        Assert.Equal(14, player.IncomingAttempts);
        Assert.Equal(8, player.IncomingEvades);

        var packets = SceneReplayTestView.Packets(replay);
        var incomingHits = packets
            .Where(static packet => packet.SourceId == 18722 && packet.TargetId == playerId && packet.SkillCode == 1_100_020 && packet.LayoutTag == 0x46 && packet.HitContribution > 0)
            .OrderBy(static packet => packet.Timestamp)
            .ThenBy(static packet => packet.Marker)
            .ToArray();

        Assert.Equal(6, incomingHits.Length);
        AssertDefensiveHit(incomingHits[0], 1_526_857_856, DamageModifiers.Front, DamageModifiers.Block | DamageModifiers.Parry | DamageModifiers.DefensivePerfect | DamageModifiers.Endurance);
        AssertDefensiveHit(incomingHits[1], 1_526_857_858, DamageModifiers.Front | DamageModifiers.Parry, DamageModifiers.Block | DamageModifiers.DefensivePerfect | DamageModifiers.Endurance);
        AssertDefensiveHit(incomingHits[2], 1_526_857_938, DamageModifiers.Front | DamageModifiers.Parry | DamageModifiers.DefensivePerfect | DamageModifiers.Endurance, DamageModifiers.Block);
        AssertDefensiveHit(incomingHits[3], 1_526_857_857, DamageModifiers.Front | DamageModifiers.Block, DamageModifiers.Parry | DamageModifiers.DefensivePerfect | DamageModifiers.Endurance);
        AssertDefensiveHit(incomingHits[4], 1_526_857_922, DamageModifiers.Front | DamageModifiers.Parry | DamageModifiers.DefensivePerfect, DamageModifiers.Block | DamageModifiers.Endurance);
        AssertDefensiveHit(incomingHits[5], 1_526_857_888, DamageModifiers.Front, DamageModifiers.Block | DamageModifiers.Parry | DamageModifiers.DefensivePerfect | DamageModifiers.Endurance);
    }

    [Fact]
    public void Replay_20260702183936_Parses_Current0438_Defensive_Modifier_Totals()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentClericDefensiveModifierTotals}"));

        const int playerId = 10408;
        var player = Assert.Single(replay.Combatants, static combatant => combatant.CombatantId == playerId);
        Assert.Equal(32, player.IncomingDamage);
        Assert.Equal(32, player.IncomingHits);
        Assert.Equal(61, player.IncomingAttempts);
        Assert.Equal(29, player.IncomingEvades);
        Assert.Equal(0, player.IncomingInvincibles);

        var incomingHits = SceneReplayTestView.Packets(replay)
            .Where(static packet => packet.TargetId == playerId && packet.LayoutTag == 0x46 && packet.HitContribution > 0)
            .ToArray();
        var dump = string.Join(
            Environment.NewLine,
            incomingHits.Select(static packet => $"t={packet.Timestamp} detailRef={packet.DetailResourceEffectRef.RawId} damage={packet.Damage} mods={packet.Modifiers}"));

        AssertMetric(incomingHits.Sum(static packet => packet.HitContribution), 32, "hits", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.Front)), 32, "fronts", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.Back)), 0, "backs", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWithAny(packet, DamageModifiers.Block | DamageModifiers.Parry)), 23, "defensiveBlocks", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.Block)), 17, "shieldBlocks", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.Parry)), 6, "weaponParries", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.DefensivePerfect)), 2, "defensivePerfects", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.Endurance)), 4, "endurance", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.Regeneration)), 6, "regeneration", dump);
    }

    [Fact]
    public void Replay_20260702190835_Parses_Current0438_Damage_Without_Identity_Metadata()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentClericDamageWithoutIdentity}"));

        const int playerId = 10408;
        var player = Assert.Single(replay.Combatants, static combatant => combatant.CombatantId == playerId);
        Assert.Equal(813_802, player.OutgoingDamage);
        Assert.Equal(2, player.OutgoingHits);
        Assert.Equal(2, player.OutgoingAttempts);
        Assert.Equal(6_565, player.OutgoingHealing);
        Assert.Equal(1_025, player.OutgoingShield);
        Assert.Equal(7, player.IncomingDamage);
        Assert.Equal(7, player.IncomingHits);
        Assert.Equal(15, player.IncomingAttempts);
        Assert.Equal(8, player.IncomingEvades);
        Assert.Equal(0, player.IncomingInvincibles);

        Assert.False(replay.SceneOwner.MetadataRegistry.TryGetPcMetadata(playerId, out _));
        Assert.True(replay.SceneOwner.Entities.TryGet(playerId, out var entity));
        Assert.Equal(CharacterClass.Cleric, entity.CharacterClass);

        var packets = SceneReplayTestView.Packets(replay);
        Assert.Equal(813_802, packets.Where(static packet => packet.SourceId == playerId && packet.SkillCode == 17_060_233 && packet.ContributesDamage).Sum(static packet => packet.Damage));
        Assert.Equal(719, packets.Where(static packet => packet.SourceId == playerId && packet.SkillCode == 17_720_001 && packet.ValueKind == CombatValueKind.Healing).Sum(static packet => packet.Damage));
        Assert.Equal(5_846, packets.Where(static packet => packet.SourceId == playerId && packet.SkillCode == 17_800_001 && packet.ValueKind == CombatValueKind.Healing).Sum(static packet => packet.Damage));

        var incomingHits = packets
            .Where(static packet => packet.TargetId == playerId && packet.LayoutTag == 0x46 && packet.HitContribution > 0)
            .ToArray();
        var dump = string.Join(
            Environment.NewLine,
            incomingHits.Select(static packet => $"t={packet.Timestamp} detailRef={packet.DetailResourceEffectRef.RawId} damage={packet.Damage} mods={packet.Modifiers}"));

        AssertMetric(incomingHits.Sum(static packet => packet.HitContribution), 7, "hits", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.Front)), 6, "fronts", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWithAny(packet, DamageModifiers.Block | DamageModifiers.Parry)), 6, "defensiveBlocks", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.Block)), 4, "shieldBlocks", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.Parry)), 2, "weaponParries", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.DefensivePerfect)), 1, "defensivePerfects", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.Endurance)), 4, "endurance", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.Regeneration)), 1, "regeneration", dump);
    }

    private static void SetResources() => CombatResourceRegistry.SetGameResources(ResourceCatalog.Load(ResourceLanguage.TraditionalChinese));

    private static CombatEventRecord[] AssertCanonicalizedRows(PacketLogReplayResult replay, CombatContributionCanonicalization flag, int expectedCount)
    {
        var rows = replay.SceneOwner.Combat.Events
            .Where(e => HasCanonicalization(in e, flag))
            .ToArray();
        var dump = string.Join(
            Environment.NewLine,
            replay.SceneOwner.Combat.Events
                .Where(static e => e.Canonicalization != CombatContributionCanonicalization.None)
                .GroupBy(static e => e.Canonicalization)
                .OrderBy(static group => group.Key)
                .Select(static group => $"{group.Key}: {group.Count()}"));

        Assert.True(rows.Length == expectedCount, $"{flag} rows={rows.Length} expected={expectedCount}\n{dump}");
        return rows;
    }

    private static void AssertBalancedSystemPeriodicRecoveryPairs(IReadOnlyList<CombatEventRecord> seedRows, IReadOnlyList<CombatEventRecord> healingRows)
    {
        var seeds = seedRows.Select(static row => CreateSystemRecoveryPairKey(in row)).Order().ToArray();
        var healing = healingRows.Select(static row => CreateSystemRecoveryPairKey(in row)).Order().ToArray();
        Assert.Equal(seeds, healing);
    }

    private static void AssertDirectSemanticHealsBypassOwnerCanonicalization(PacketLogReplayResult replay)
    {
        var rows = replay.SceneOwner.Combat.Events
            .Where(static row => IsDirectSemanticHeal(in row))
            .ToArray();

        Assert.NotEmpty(rows);
        Assert.All(rows, static row =>
        {
            Assert.Equal(CombatEventKind.Healing, row.Observation.EventKind);
            Assert.Equal(CombatValueKind.Healing, row.Observation.ValueKind);
            Assert.False(HasCanonicalization(in row, CombatContributionCanonicalization.OwnerTargetSummonResource));
        });
    }

    private static bool IsDirectSemanticHeal(in CombatEventRecord row)
    {
        var observation = row.Observation;
        return observation.PeriodicRelation == PeriodicEffectRelation.None &&
            observation.ResourceKind != CombatResourceKind.Mana &&
            CombatResourceRegistry.TryResolveDirectCombatEffectSemantics(in observation, out var semantics) &&
            (semantics.DirectFacets & SkillSemanticFacet.Healing) != 0;
    }

    private static string CreateSystemRecoveryPairKey(in CombatEventRecord row)
        => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{row.SourceId}|{row.TargetId}|{row.EventKey.SkillCode}|{row.EventKey.BodyResourceEffectRef.RawId}|{row.EventKey.DetailResourceEffectRef.RawId}|{row.Observation.ChainId}|{row.Observation.Damage}");

    private static bool HasCanonicalization(in CombatEventRecord row, CombatContributionCanonicalization flag) => (row.Canonicalization & flag) == flag;

    private static PacketLogReplayResult ReplayCombinedFixtures(params string[] fixtureNames)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aion2flow-replay-{Guid.NewGuid():N}.stream.log");
        try
        {
            using (var writer = File.CreateText(path))
            {
                for (var i = 0; i < fixtureNames.Length; i++)
                {
                    foreach (var line in File.ReadLines(FixtureHelper.GetPath($"logs/{fixtureNames[i]}")))
                        writer.WriteLine(line);
                }
            }

            return PacketLogReplayService.Replay(path);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

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

    private static void AssertGroupRelations(PacketLogReplayResult replay, PlayerGroupRelation expectedRelation, params int[] entityIds)
    {
        foreach (var entityId in entityIds)
            AssertGroupRelation(replay, entityId, expectedRelation);
    }

    private static void AssertGroupRelation(PacketLogReplayResult replay, int entityId, PlayerGroupRelation expectedRelation)
    {
        Assert.True(replay.SceneOwner.MetadataRegistry.TryGetPcMetadata(entityId, out var metadata), $"missing PC metadata for {entityId}");
        Assert.Equal(expectedRelation, metadata.GroupRelation);
    }

    private static void AssertPcMetadata(PacketLogReplayResult replay, int entityId, string nickname, CharacterClass characterClass, Faction faction)
    {
        Assert.True(replay.SceneOwner.MetadataRegistry.TryGetPcMetadata(entityId, out var metadata), $"missing PC metadata for {entityId}");
        Assert.Equal(nickname, metadata.Nickname);
        Assert.Equal(characterClass, metadata.CharacterClass);
        Assert.Equal(faction, metadata.Faction);
    }

    private static void AssertForceRosterProfile(IReadOnlyList<ObservedEventEnvelope> entries, string nickname, int originServerId, byte memberSlotIndex)
    {
        Assert.Contains(
            entries,
            entry => entry.Raw.Opcode == 0x0A96 &&
                     entry.SourceEntityId == 0 &&
                     entry.State is
                     {
                         EntityId: 0,
                         StateCode: StateCodes.PlayerGroupMembership,
                         Text: var text,
                         OriginServerId: var stateOriginServerId,
                         GroupMembership.Kind: PlayerGroupKind.Force,
                         GroupMembership.GroupId: 0,
                         GroupMembership.SubPartyIndex: 0,
                         GroupMembership.MemberSlotIndex: var stateMemberSlotIndex
                     } &&
                     text == nickname &&
                     stateOriginServerId == originServerId &&
                     stateMemberSlotIndex == memberSlotIndex);
    }

    private static int CountHitsWith(SceneReplayPacket packet, DamageModifiers modifier)
        => (packet.Modifiers & modifier) != 0 ? packet.HitContribution : 0;

    private static int CountHitsWithAny(SceneReplayPacket packet, DamageModifiers modifiers)
        => (packet.Modifiers & modifiers) != 0 ? packet.HitContribution : 0;

    private static void AssertDefensiveHit(SceneReplayPacket packet, uint expectedDetailRef, DamageModifiers expectedPresent, DamageModifiers expectedAbsent)
    {
        var dump = $"detailRef={packet.DetailResourceEffectRef.RawId} marker={packet.Marker} mods={packet.Modifiers} layout={packet.LayoutTag} flag={packet.Flag} type={packet.Type} detail=0x{packet.DetailRaw:X16}";
        Assert.Equal(expectedDetailRef, packet.DetailResourceEffectRef.RawId);
        Assert.True((packet.Modifiers & expectedPresent) == expectedPresent, $"missing expected modifiers {expectedPresent}\n{dump}");
        Assert.True((packet.Modifiers & expectedAbsent) == 0, $"unexpected modifiers {packet.Modifiers & expectedAbsent}\n{dump}");
    }

    private static void AssertMetric(long actual, long expected, string name, string dump)
        => Assert.True(actual == expected, $"{name}={actual} expected={expected}\n{dump}");
}
