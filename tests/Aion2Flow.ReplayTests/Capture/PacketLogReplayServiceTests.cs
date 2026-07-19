using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Canonicalization;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketLogReplayServiceTests
{
    [Fact]
    public void Replay_Observer_Receives_Production_Occurrences_After_Initial_Reset()
    {
        SetResources();
        var observer = new RecordingSceneEventObserver();

        var replay = PacketLogReplayService.Replay(
            FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentBrawlerRegenerationRecovery}"),
            observer);

        Assert.True(replay.ReplayedLines > 0);
        Assert.NotEmpty(observer.Contexts);
        Assert.NotEmpty(observer.AuraContexts);
        Assert.Contains(
            observer.AuraContexts,
            static context => AuraPacketEvidenceResolver.Evaluate(in context).HasLifecycleEvidence);
        Assert.Contains(
            observer.Contexts,
            static context =>
                context.Resolution.PacketRule == CombatPacketRule.RegenerationSecondary &&
                context.ProductionMaterialization.Contribution is
                {
                    Metric: CombatMetricKind.Healing,
                    Delivery: CombatDeliveryKind.Regeneration
                });
        Assert.All(observer.Contexts, static context =>
        {
            Assert.True(context.SourceObservationOrdinal >= 0);
            Assert.True(context.FlushId >= 0);
        });
    }

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
        Assert.Equal(200_003u, replay.Snapshot.MapId);
        Assert.Equal(644u, replay.Snapshot.MapInstanceId);
        Assert.True(replay.SceneOwner.EntityVitals.TryGet(18_551, out var npcVital));
        Assert.Equal(20_000_000, npcVital.CurrentHp);
        Assert.Equal(20_000_000, npcVital.MaxHp);

        var packets = SceneReplayTestView.Packets(replay);
        var regenerationHealing = packets
            .Where(static packet =>
                packet.SourceId == playerId &&
                packet.TargetId == playerId &&
                packet.Metric == CombatMetricKind.Healing &&
                packet.Delivery == CombatDeliveryKind.Regeneration)
            .ToArray();
        Assert.Equal(2, regenerationHealing.Length);
        Assert.Equal(1_209, regenerationHealing.Sum(static packet => packet.Amount));

        AssertSkillContribution(replay, skillCode: 12_350_150, CombatMetricKind.Healing, CombatDeliveryKind.Direct, expectedCount: 3, expectedAmount: 7_808);
        var skill1900911 = packets.Where(static packet => packet.SourceId == playerId && packet.SkillCode == 1_900_911).ToArray();
        Assert.Equal(48_912, skill1900911.Where(static packet => packet.Metric == CombatMetricKind.Healing).Sum(static packet => packet.Amount));
        Assert.Equal(48_912, skill1900911.Sum(static packet => packet.Amount));
    }

    [Fact]
    public void Replay_20260705051242_Covers_OwnerTarget_And_SystemPeriodic_Canonicalization()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentOwnerSystemCanonicalization}"));

        var ownerRows = AssertOwnerTargetCandidateRows(replay, expectedCount: 345);
        AssertDirectSemanticHealsPassOwnerPostParseGate(replay);
        var (systemSeedRows, systemHealingRows) = AssertSystemPeriodicRecoveryRows(replay, expectedCount: 9);
        AssertBalancedSystemPeriodicRecoveryPairs(systemSeedRows, systemHealingRows);
    }

    [Fact]
    public void Replay_20260704053009_Covers_OwnerTarget_CompactTwoContribution_Edge()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentOwnerTargetCanonicalizationEdge}"));

        var ownerRows = AssertOwnerTargetCandidateRows(replay, expectedCount: 127);
        AssertDirectSemanticHealsPassOwnerPostParseGate(replay);
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
    public void Replay_20260704002230_Parses_CrossServerMatchedPartyRelationsFromCurrentPackets()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentCrossServerMatchedPartyRelation}"));
        var entries = ReadAllJournalEntries(replay);

        Assert.Contains(
            entries,
            static entry => entry.Raw.Opcode == 0x0092 &&
                            entry.State is { EntityId: 10780, StateCode: StateCodes.PlayerGroupMembership, GroupMembership.Kind: PlayerGroupKind.Party });

        Assert.Contains(
            entries,
            static entry => entry.Raw.Opcode == 0x1B92 &&
                            entry.State is { EntityId: 14000, StateCode: StateCodes.PlayerGroupMembership, GroupMembership.Kind: PlayerGroupKind.Party, GroupMembership.MemberSlotIndex: 0 });

        Assert.DoesNotContain(
            entries,
            static entry => entry.Raw.Opcode == 0x048D &&
                            entry.State is { StateCode: StateCodes.PlayerGroupMembership });

        AssertGroupRelations(replay, PlayerGroupRelation.PartyMember, 9975, 10780, 14000, 14819);
        AssertGroupRelation(replay, 12478, PlayerGroupRelation.Unknown);
    }

    [Fact]
    public void Replay_20260712211428_RecognizesPartyMembersFromEarlyStatusFrames()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentPartyStatusRelation}"));
        var entries = ReadAllJournalEntries(replay);
        var statusMembers = entries
            .Where(static entry => entry.Raw.Opcode == 0x1B92 && entry.State is { StateCode: StateCodes.PlayerGroupMembership })
            .OrderBy(static entry => entry.SourceEntityId)
            .ToArray();

        Assert.Equal([4327, 9183, 9429, 16102], statusMembers.Select(static entry => entry.SourceEntityId));
        Assert.All(
            statusMembers,
            static entry =>
            {
                Assert.True(entry.Stamp.OffsetTicks < TimeSpan.FromSeconds(30).Ticks, $"party member {entry.SourceEntityId} was first recognized at {TimeSpan.FromTicks(entry.Stamp.OffsetTicks)}");
                Assert.Equal(PlayerGroupKind.Party, entry.State!.Value.GroupMembership.Kind);
                Assert.Equal(0, entry.State.Value.GroupMembership.MemberSlotIndex);
            });

        var firstRosterRelation = Assert.Single(
            entries.Where(static entry => entry.Raw.Opcode == 0x0092 && entry.State is { StateCode: StateCodes.PlayerGroupMembership }),
            static entry => entry.SourceEntityId == 16102);
        Assert.True(firstRosterRelation.Stamp.OffsetTicks > TimeSpan.FromMinutes(3).Ticks);

        AssertGroupRelations(replay, PlayerGroupRelation.PartyMember, 4327, 9183, 9429, 16102);
        AssertGroupRelation(replay, 13028, PlayerGroupRelation.Unknown);
    }

    [Fact]
    public void Replay_20260715000443_ParsesCompleteTenPlayerForceRosterSnapshot()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentTenPlayerForceRoster}"));
        var entries = ReadAllJournalEntries(replay);

        AssertGroupRelations(replay, PlayerGroupRelation.PartyMember, 3446, 3817, 13319, 15591);
        AssertGroupRelations(replay, PlayerGroupRelation.ForceMember, 1307, 1549, 5142, 5193, 5927);
        AssertGroupRelation(replay, 2204, PlayerGroupRelation.Unknown);

        Assert.All(
            new[] { 1549, 5142, 5927 },
            entityId => Assert.Contains(
                entries,
                entry => entry.Raw.Opcode == 0x0296 &&
                         entry.SourceEntityId == entityId &&
                         entry.State is { StateCode: StateCodes.PlayerGroupMembership, GroupMembership.Kind: PlayerGroupKind.Force }));
        Assert.Contains(
            entries,
            static entry => entry.Raw.Opcode == 0x0296 &&
                            entry.SourceEntityId == 0 &&
                            entry.State is { StateCode: StateCodes.PlayerGroupMembership, Text: "艾小露", OriginServerId: 2003, GroupMembership.Kind: PlayerGroupKind.Force });
    }

    [Fact]
    public void Replay_20260715002239_RecognizesCompleteForceFromCurrentStatusFrames()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentTenPlayerForceStatus}"));
        var entries = ReadAllJournalEntries(replay);

        AssertGroupRelations(replay, PlayerGroupRelation.PartyMember, 7771, 9372, 10544, 12203);
        AssertGroupRelations(replay, PlayerGroupRelation.ForceMember, 1656, 8088, 10250, 10550, 14375);
        AssertGroupRelation(replay, 1134, PlayerGroupRelation.Unknown);

        Assert.All(
            new[] { 1656, 8088, 10250, 10550, 14375 },
            entityId => Assert.Contains(
                entries,
                entry => entry.Raw.Opcode == 0x2B96 &&
                         entry.SourceEntityId == entityId &&
                         entry.State is { StateCode: StateCodes.PlayerGroupMembership, GroupMembership.Kind: PlayerGroupKind.Force }));
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
    public void Replay_20260704155002_UsesPartyAndForceStatusFramesWithoutPromotingCombat048D()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentForceDungeonPartyStatusRelations}"));
        var entries = ReadAllJournalEntries(replay);

        Assert.DoesNotContain(
            entries,
            static entry => entry.Raw.Opcode == 0x048D &&
                            entry.State is { StateCode: StateCodes.PlayerGroupMembership });

        Assert.Contains(
            entries,
            static entry => entry.Raw.Opcode == 0x1B92 &&
                            entry.State is { EntityId: 3316, StateCode: StateCodes.PlayerGroupMembership, GroupMembership.Kind: PlayerGroupKind.Party });
        Assert.Contains(
            entries,
            static entry => entry.Raw.Opcode == 0x2B96 &&
                            entry.State is { EntityId: 1339, StateCode: StateCodes.PlayerGroupMembership, GroupMembership.Kind: PlayerGroupKind.Force });

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
        var mechanicDump = string.Join(
            Environment.NewLine,
            replay.SceneOwner.Mechanics.Events
                .Where(static e => e.TargetId == playerId)
                .Select(static e => $"t={e.ObservedAtMilliseconds} source={e.SourceId} skill={e.Observation.SkillCode} damage={e.Observation.Damage} hits={e.Mechanic.HitCount} attempts={e.Mechanic.AttemptCount} evades={e.Mechanic.EvadeCount} invincibles={e.Mechanic.InvincibleCount} mods={e.Mechanic.Modifiers} rule={e.Mechanic.Resolution.PacketRule} materialization={e.Mechanic.Resolution.Materialization} association={e.Mechanic.Resolution.Association}"));
        Assert.Equal(186_174, replay.Snapshot.EncounterStartTime);
        Assert.Equal(6, player.IncomingDamage);
        Assert.Equal(6, player.IncomingHits);
        AssertMetric(player.IncomingAttempts, 14, "attempts", mechanicDump);
        AssertMetric(player.IncomingEvades, 8, "evades", mechanicDump);

        var packets = SceneReplayTestView.Packets(replay);
        var incomingHits = packets
            .Where(static packet => packet.SourceId == 18722 && packet.TargetId == playerId && packet.SkillCode == 1_100_020 && packet.LayoutTag == 0x46 && packet.HitCount > 0)
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
            .Where(static packet => packet.TargetId == playerId && packet.LayoutTag == 0x46 && packet.HitCount > 0)
            .ToArray();
        var dump = string.Join(
            Environment.NewLine,
            incomingHits.Select(static packet => $"t={packet.Timestamp} detailRef={packet.DetailResourceEffectRef.RawId} amount={packet.Amount} mods={packet.Modifiers}"));

        AssertMetric(incomingHits.Sum(static packet => packet.HitCount), 32, "hits", dump);
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
        Assert.Equal(813_802, packets.Where(static packet => packet.SourceId == playerId && packet.SkillCode == 17_060_233 && packet.Metric == CombatMetricKind.Damage).Sum(static packet => packet.Amount));
        Assert.Equal(719, packets.Where(static packet => packet.SourceId == playerId && packet.SkillCode == 17_720_001 && packet.Metric == CombatMetricKind.Healing).Sum(static packet => packet.Amount));
        Assert.Equal(5_846, packets.Where(static packet => packet.SourceId == playerId && packet.SkillCode == 17_800_001 && packet.Metric == CombatMetricKind.Healing).Sum(static packet => packet.Amount));

        var incomingHits = packets
            .Where(static packet => packet.TargetId == playerId && packet.LayoutTag == 0x46 && packet.HitCount > 0)
            .ToArray();
        var dump = string.Join(
            Environment.NewLine,
            incomingHits.Select(static packet => $"t={packet.Timestamp} detailRef={packet.DetailResourceEffectRef.RawId} amount={packet.Amount} mods={packet.Modifiers}"));

        AssertMetric(incomingHits.Sum(static packet => packet.HitCount), 7, "hits", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.Front)), 6, "fronts", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWithAny(packet, DamageModifiers.Block | DamageModifiers.Parry)), 6, "defensiveBlocks", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.Block)), 4, "shieldBlocks", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.Parry)), 2, "weaponParries", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.DefensivePerfect)), 1, "defensivePerfects", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.Endurance)), 4, "endurance", dump);
        AssertMetric(incomingHits.Sum(static packet => CountHitsWith(packet, DamageModifiers.Regeneration)), 1, "regeneration", dump);
    }

    private static void SetResources() => CombatResourceRegistry.SetGameResources(ResourceCatalog.Load(ResourceLanguage.TraditionalChinese));

    private static CanonicalizationProbeRow[] AssertOwnerTargetCandidateRows(PacketLogReplayResult replay, int expectedCount)
    {
        var canonicalizer = new OwnerTargetSummonResourceCanonicalizer(replay.SceneOwner.Entities);
        var matches = new List<CanonicalizationProbeRow>();
        foreach (var entry in ReadCombatWireEntries(replay))
        {
            var observation = entry.Observation;
            var result = canonicalizer.Normalize(entry.SourceId, entry.TargetId, in observation);
            if (result.Resolution.Suppression != CombatSuppressionReason.OwnerTargetSummonResource)
                continue;

            matches.Add(new CanonicalizationProbeRow(
                entry.SourceId,
                entry.TargetId,
                observation,
                entry.Raw,
                result.Resolution));
        }

        var rows = matches.ToArray();
        Assert.Equal(expectedCount, rows.Length);
        return rows;
    }

    private static (CanonicalizationProbeRow[] Seeds, CanonicalizationProbeRow[] Healing) AssertSystemPeriodicRecoveryRows(
        PacketLogReplayResult replay,
        int expectedCount)
    {
        var canonicalizer = new SystemPeriodicRecoveryCanonicalizer();
        var seeds = new List<CanonicalizationProbeRow>();
        var healing = new List<CanonicalizationProbeRow>();
        foreach (var entry in ReadCombatWireEntries(replay))
        {
            var observation = entry.Observation;
            var result = canonicalizer.Normalize(entry.SourceId, entry.TargetId, in observation);
            var row = new CanonicalizationProbeRow(entry.SourceId, entry.TargetId, result.Observation, entry.Raw, result.Resolution);
            if (result.Resolution.Suppression == CombatSuppressionReason.SystemPeriodicRecoverySeed)
                seeds.Add(row);
            else if (result.Resolution.PacketRule == CombatPacketRule.PeriodicRecovery)
                healing.Add(row);
        }

        Assert.Equal(expectedCount, seeds.Count);
        Assert.Equal(expectedCount, healing.Count);
        return (seeds.ToArray(), healing.ToArray());
    }

    private static void AssertBalancedSystemPeriodicRecoveryPairs(IReadOnlyList<CanonicalizationProbeRow> seedRows, IReadOnlyList<CanonicalizationProbeRow> healingRows)
    {
        var seeds = seedRows.Select(static row => CreateSystemRecoveryPairKey(in row)).Order().ToArray();
        var healing = healingRows.Select(static row => CreateSystemRecoveryPairKey(in row)).Order().ToArray();
        Assert.Equal(seeds, healing);
    }

    private static void AssertDirectSemanticHealsPassOwnerPostParseGate(PacketLogReplayResult replay)
    {
        var canonicalizer = new OwnerTargetSummonResourceCanonicalizer(replay.SceneOwner.Entities);
        var admitted = new List<CombatContribution>();
        foreach (var row in ReadCombatWireEntries(replay))
        {
            var observation = row.Observation;
            var result = canonicalizer.Normalize(row.SourceId, row.TargetId, in observation);
            if (result.Resolution.Suppression != CombatSuppressionReason.OwnerTargetSummonResource)
                continue;

            var occurrence = result.Resolution;
            var materialization = CombatOccurrenceMaterializer.Resolve(
                row.SourceId,
                row.TargetId,
                in observation,
                in occurrence);
            if (materialization.Contribution is not { Metric: CombatMetricKind.Healing, Resolution.Authority: CombatResolutionAuthority.SkillSemantic } contribution)
                continue;

            Assert.True(materialization.IsAdmitted);
            Assert.Equal(CombatPacketRule.DirectValue, contribution.Resolution.PacketRule);
            Assert.True(contribution.Resolution.SemanticMatch is CombatSemanticMatchKind.ExactNode or CombatSemanticMatchKind.UnambiguousSlot);
            admitted.Add(contribution);
        }

        Assert.NotEmpty(admitted);
    }

    private static string CreateSystemRecoveryPairKey(in CanonicalizationProbeRow row)
        => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{row.SourceId}|{row.TargetId}|{row.Observation.SkillCode}|{row.Observation.BodyResourceEffectRef.RawId}|{row.Observation.DetailResourceEffectRef.RawId}|{row.Observation.ChainId}|{row.Observation.Damage}");

    private static IReadOnlyList<ReplayJournalEntrySnapshot> ReadAllJournalEntries(PacketLogReplayResult replay)
    {
        var entries = new List<ReplayJournalEntrySnapshot>(replay.SceneJournal.Count);
        var cursor = replay.SceneJournal.CreateCursor(0);
        while (true)
        {
            var result = replay.SceneJournal.ReadEntries(cursor, 1024, batch =>
            {
                foreach (var entry in batch)
                {
                    entries.Add(new ReplayJournalEntrySnapshot(
                        entry.Stamp,
                        entry.SourceEntityId,
                        entry.Raw,
                        entry.Domain == ObservedEventDomain.State ? entry.State : null));
                }
            });

            if (result.Count == 0)
            {
                return entries;
            }

            cursor = result.Cursor;
        }
    }

    private static IReadOnlyList<CombatWireEntrySnapshot> ReadCombatWireEntries(PacketLogReplayResult replay)
    {
        var entries = new List<CombatWireEntrySnapshot>();
        var cursor = replay.SceneJournal.CreateCursor(0);
        while (true)
        {
            var result = replay.SceneJournal.ReadEntries(cursor, 1024, batch =>
            {
                foreach (var entry in batch)
                {
                    if (entry.Domain == ObservedEventDomain.Combat)
                    {
                        entries.Add(new CombatWireEntrySnapshot(
                            entry.SourceEntityId,
                            entry.TargetEntityId,
                            entry.Combat,
                            entry.Raw));
                    }
                }
            });

            if (result.Count == 0)
                return entries;

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
            .Where(packet => packet.SourceId == sourceId && packet.SkillCode == skillCode && packet.Metric == CombatMetricKind.Damage)
            .ToArray();
        var dump = string.Join(
            Environment.NewLine,
            matching.Select(static packet =>
                $"t={packet.Timestamp} skill={packet.SkillCode} amount={packet.Amount} hits={packet.HitCount} mods={packet.Modifiers} multi={packet.MultiHitCount} layout={packet.LayoutTag} type={packet.Type} loop={packet.Loop} detail=0x{packet.DetailRaw:X16}"));

        AssertMetric(matching.Sum(static packet => packet.Amount), expectedDamage, "damage", dump);
        AssertMetric(matching.Sum(static packet => packet.HitCount), expectedHits, "hits", dump);
        AssertMetric(matching.Sum(static packet => CountHitsWith(packet, DamageModifiers.Critical)), expectedCriticals, "criticals", dump);
        AssertMetric(matching.Sum(static packet => CountHitsWith(packet, DamageModifiers.Perfect)), expectedPerfects, "perfects", dump);
        AssertMetric(matching.Sum(static packet => CountHitsWith(packet, DamageModifiers.Smite)), expectedSmites, "smites", dump);
        AssertMetric(matching.Sum(static packet => CountHitsWith(packet, DamageModifiers.Front)), expectedFronts, "fronts", dump);
        AssertMetric(matching.Sum(static packet => CountHitsWith(packet, DamageModifiers.Back)), expectedBacks, "backs", dump);
        AssertMetric(matching.Sum(static packet => CountHitsWith(packet, DamageModifiers.MultiHit)), expectedMultiHits, "multiHits", dump);
    }

    private static void AssertSkillContribution(
        PacketLogReplayResult replay,
        int skillCode,
        CombatMetricKind metric,
        CombatDeliveryKind delivery,
        int expectedCount,
        long expectedAmount)
    {
        var matching = replay.SceneOwner.Combat.Events
            .Where(e => e.Observation.SkillCode == skillCode &&
                        e.Observation.BodySkillVariantRaw == skillCode &&
                        e.Contribution.Metric == metric &&
                        e.Contribution.Delivery == delivery)
            .ToArray();
        var skillDump = string.Join(
            Environment.NewLine,
            replay.SceneOwner.Combat.Events
                .Where(e => e.Observation.SkillCode == skillCode || e.Observation.BodySkillVariantRaw == skillCode)
                .GroupBy(e => new { e.Observation.SkillCode, e.Observation.BodySkillVariantRaw, e.Contribution.Metric, e.Contribution.Delivery })
                .OrderByDescending(group => group.Sum(e => e.Contribution.Amount))
                .Select(group =>
                    $"skill={group.Key.SkillCode} body={group.Key.BodySkillVariantRaw} metric={group.Key.Metric} delivery={group.Key.Delivery} count={group.Count()} amount={group.Sum(e => e.Contribution.Amount)}"));

        Assert.True(matching.Length == expectedCount, $"count={matching.Length} expected={expectedCount}\n{skillDump}");
        Assert.True(matching.Sum(e => e.Contribution.Amount) == expectedAmount, $"amount={matching.Sum(e => e.Contribution.Amount)} expected={expectedAmount}\n{skillDump}");
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

    private static void AssertForceRosterProfile(IReadOnlyList<ReplayJournalEntrySnapshot> entries, string nickname, int originServerId, byte memberSlotIndex)
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
        => (packet.Modifiers & modifier) != 0 ? packet.HitCount : 0;

    private readonly record struct ReplayJournalEntrySnapshot(
        TimelineStamp Stamp,
        int SourceEntityId,
        RawPacketReference Raw,
        StateObservation? State);

    private readonly record struct CombatWireEntrySnapshot(
        int SourceId,
        int TargetId,
        CombatWireObservation Observation,
        RawPacketReference Raw);

    private readonly record struct CanonicalizationProbeRow(
        int SourceId,
        int TargetId,
        CombatWireObservation Observation,
        RawPacketReference Raw,
        CombatOccurrenceResolution Resolution);

    private sealed class RecordingSceneEventObserver : ISceneEventObserver
    {
        public List<CombatOccurrenceContext> Contexts { get; } = [];
        public List<AuraLifecycleObservationContext> AuraContexts { get; } = [];

        public void Observe(in CombatOccurrenceContext context) => Contexts.Add(context);

        public void Observe(in AuraLifecycleObservationContext context) => AuraContexts.Add(context);
    }

    private static int CountHitsWithAny(SceneReplayPacket packet, DamageModifiers modifiers)
        => (packet.Modifiers & modifiers) != 0 ? packet.HitCount : 0;

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
