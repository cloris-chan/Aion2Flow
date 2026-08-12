using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Archive;
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
    public void Replay_Skips_Stream_Entry_When_Length_Does_Not_Match_Payload()
    {
        const string line =
            "2026-07-01T19:10:11.8450000+00:00|dir=inbound|16777343:52475->16777343:54260|seq=1|len=2|data=00";

        using var reader = new StringReader(line);
        var replay = PacketLogReplayService.Replay(reader, "invalid-length.stream.log");

        Assert.Equal(1, replay.TotalLines);
        Assert.Equal(0, replay.ReplayedLines);
        Assert.Equal(1, replay.SkippedLines);
        Assert.Equal(1, replay.SkippedEventCounts["<invalid>"]);
    }

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
    public void Replay_20260725192105_IgnoresMapEventMapIdsForSceneScope()
    {
        var replay = PacketLogReplayService.Replay(
            FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentUnknownToSunkenTempleTransition}"));
        Assert.Empty(ReadDirectMapEventObservations(replay, 0x0061, 0x0161));

        Assert.Collection(
            ReadDirectMapEventObservations(replay, 0x2136),
            static candidate =>
            {
                Assert.Equal(610010u, candidate.MapId);
                Assert.Equal(SceneObservationKind.CurrentMap, candidate.Kind);
            },
            static candidate =>
            {
                Assert.Equal(610010u, candidate.MapId);
                Assert.Equal(SceneObservationKind.CurrentMap, candidate.Kind);
            });
        Assert.Collection(
            ReadSceneTransitions(replay),
            static transition =>
            {
                Assert.Equal((ushort)0x0140, transition.Opcode);
                Assert.Equal(610010u, transition.MapId);
                Assert.Equal(0u, transition.MapInstanceId);
                Assert.Equal(SceneObservationKind.MapContextStarted, transition.Kind);
            });

        var archive = Assert.Single(replay.MapTransitionArchives);
        Assert.Equal(0u, archive.Snapshot.MapId);
        Assert.Equal(610010u, replay.Snapshot.MapId);
        Assert.Equal(173415u, replay.Snapshot.MapInstanceId);
    }

    [Fact]
    public void Replay_20260726065616_UsesQualifiedArrivalsForInitialMapAndSameMapReload()
    {
        var replay = PacketLogReplayService.Replay(
            FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentSameMapInstanceReload}"));
        var transitions = ReadSceneTransitions(replay);

        var arrivals = transitions
            .Where(static transition => transition.Kind == SceneObservationKind.MapContextStarted)
            .ToArray();
        Assert.Equal(2, arrivals.Length);
        Assert.Equal((ushort)0x2136, arrivals[0].Opcode);
        Assert.Equal((ushort)0x2336, arrivals[1].Opcode);
        Assert.All(arrivals, static arrival => Assert.Equal(910055u, arrival.MapId));
        Assert.Equal(910055u, replay.Snapshot.MapId);
        Assert.Equal(229838u, replay.Snapshot.MapInstanceId);
        Assert.Single(replay.MapTransitionArchives);
    }

    [Fact]
    public void Replay_20260728234348_And_20260728234353_ArchivesUnknownMapBeforeMorheim()
    {
        var preludePath = FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentUnknownMapTransportPrelude}");
        var arrivalPath = FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentUnknownMapArrival}");
        using var reader = new StringReader(string.Concat(File.ReadAllText(preludePath), File.ReadAllText(arrivalPath)));
        var replay = PacketLogReplayService.Replay(reader, "20260728234348+20260728234353.stream.log");
        var transitions = ReadSceneTransitions(replay);

        Assert.Collection(
            transitions,
            static arrival =>
            {
                Assert.Equal((ushort)0x2136, arrival.Opcode);
                Assert.Equal(1_111u, arrival.MapId);
                Assert.Equal(0u, arrival.MapInstanceId);
                Assert.Equal(SceneObservationKind.MapContextStarted, arrival.Kind);
            });

        var candidate = Assert.Single(ReadDirectMapEventObservations(replay, 0x2136));
        Assert.Equal(1_111u, candidate.MapId);
        Assert.Equal(SceneObservationKind.MapContextStarted, candidate.Kind);

        var archive = Assert.Single(replay.MapTransitionArchives);
        Assert.Equal(0u, archive.Snapshot.MapId);
        Assert.Equal(627_522, archive.Snapshot.Combatants[3_793].DamageAmount);
        Assert.Equal(1_111u, replay.Snapshot.MapId);
        Assert.Equal(0u, replay.Snapshot.MapInstanceId);
    }

    [Fact]
    public void Replay_20260729002254_And_20260729002341_KeepsOldMapCombatUntilArrival()
    {
        var transferPath = FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentOldMapCombatDuringTransfer}");
        var arrivalPath = FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentArrivalAfterOldMapCombat}");
        using var reader = new StringReader(string.Concat(File.ReadAllText(transferPath), File.ReadAllText(arrivalPath)));
        var replay = PacketLogReplayService.Replay(reader, "20260729002254+20260729002341.stream.log");

        var candidate = Assert.Single(
            ReadDirectMapEventObservations(replay, 0x2136),
            static observation => observation.MapId == 1_010);
        Assert.Equal(SceneObservationKind.MapCandidateObserved, candidate.Kind);

        var arrival = Assert.Single(
            ReadSceneTransitions(replay),
            static transition => transition.Opcode == 0x2336);
        Assert.Equal(1_010u, arrival.MapId);
        Assert.Equal(SceneObservationKind.MapContextStarted, arrival.Kind);

        var transferDamage = ReadCombatWireEntries(replay)
            .Where(entry =>
                entry.Stamp.ObservationOrdinal > candidate.ObservationOrdinal &&
                entry.Stamp.ObservationOrdinal < arrival.ObservationOrdinal &&
                entry.SourceId == 6_393 &&
                entry.TargetId == 34_654 &&
                entry.Observation.Damage > 0)
            .OrderBy(static entry => entry.Stamp.ObservationOrdinal)
            .Select(static entry => entry.Observation.Damage)
            .ToArray();
        Assert.Equal([107_024, 285_584, 16_346, 16_508], transferDamage);

        var archive = Assert.Single(replay.MapTransitionArchives);
        Assert.True(archive.Snapshot.Combatants.TryGetValue(6_393, out var oldPlayer));
        Assert.True(oldPlayer.DamageAmount >= transferDamage.Sum());
        Assert.DoesNotContain(6_393, replay.Snapshot.Combatants.Keys);
        Assert.Equal(1_010u, replay.Snapshot.MapId);
    }

    [Fact]
    public void ReplayMany_20260728234348_And_20260728234353_PreservesOneTransportSession()
    {
        var preludePath = FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentUnknownMapTransportPrelude}");
        var arrivalPath = FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentUnknownMapArrival}");

        var replay = PacketLogReplayService.ReplayMany([preludePath, arrivalPath]);

        Assert.Equal(1_111u, replay.Snapshot.MapId);
        Assert.Single(replay.MapTransitionArchives);
        Assert.Contains(
            ReadSceneTransitions(replay),
            static transition => transition.Kind == SceneObservationKind.MapContextStarted &&
                                 transition.MapId == 1_111u);
    }

    [Fact]
    public void ReplayDirectory_ConcatenatesChronologicalDumpSegments()
    {
        var preludePath = FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentUnknownMapTransportPrelude}");
        var arrivalPath = FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentUnknownMapArrival}");
        var root = Path.Combine(Path.GetTempPath(), $"aion2flow-replay-{Guid.NewGuid():N}");
        var dumps = Path.Combine(root, "dumps");
        Directory.CreateDirectory(Path.Combine(dumps, "20260728234348"));
        Directory.CreateDirectory(Path.Combine(dumps, "20260728234353"));

        try
        {
            File.Copy(preludePath, Path.Combine(dumps, "20260728234348", "stream.log"));
            File.Copy(arrivalPath, Path.Combine(dumps, "20260728234353", "stream.log"));

            var replay = PacketLogReplayService.ReplayDirectory(root);

            Assert.True(replay.ReplayedLines > 0);
            Assert.Single(replay.MapTransitionArchives);
            Assert.Equal(1_111u, replay.Snapshot.MapId);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReplayMany_20260810_CrossServerLifecycle_PreservesMapContextsAndCombat()
    {
        SetResources();

        var paths = ReplayScenarioCatalog.CurrentCrossServerLifecycle
            .Select(static fileName => FixtureHelper.GetPath($"logs/{fileName}"))
            .ToArray();

        var replay = PacketLogReplayService.ReplayMany(paths);
        var transitions = ReadSceneTransitions(replay)
            .Select(static transition => transition.MapId)
            .ToArray();
        var combat = ReadCombatWireEntries(replay);
        var damage = combat
            .Where(static entry => entry.Observation.Damage > 0)
            .ToArray();

        Assert.Equal([600132u, 600142u, 1110u, 600132u, 1110u], transitions);
        Assert.Equal(726, combat.Count);
        Assert.Equal(15, damage.Length);
        Assert.Equal(103_966, damage.Sum(static entry => entry.Observation.Damage));

        var archive = Assert.Single(replay.MapTransitionArchives);
        Assert.Equal(600132u, archive.Snapshot.MapId);
        Assert.Equal(23, archive.CombatEvents.Count);
        Assert.Equal(1110u, replay.Snapshot.MapId);
    }

    [Fact]
    public void ReplayMany_20260812_RoundTripLifecycle_EmitsCurrentEchoes()
    {
        var paths = ReplayScenarioCatalog.CurrentRoundTripLifecycle
            .Select(static fileName => FixtureHelper.GetPath($"logs/{fileName}"))
            .ToArray();
        var observations = new List<ProtocolRoundTripObservation>();

        var replay = PacketLogReplayService.ReplayMany(paths, observations.Add);

        Assert.Equal(3_314, replay.TotalLines);
        Assert.Equal(8, observations.Count);
        Assert.All(observations, static observation =>
            Assert.InRange(
                observation.ServerUnixMilliseconds - observation.ClientSentUnixMilliseconds,
                129,
                132));
        Assert.Contains(observations, static observation =>
            observation.ClientSentUnixMilliseconds == 1_786_515_131_286 &&
            observation.ServerUnixMilliseconds == 1_786_515_131_416);
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
        Assert.True(TryGetLatestEntityVitalObservation(replay, 18_551, out var npcVital));
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

        var ownerRows = AssertOwnerTargetCandidateRows(replay, expectedCount: 236);
        AssertDirectSemanticHealsPassOwnerPostParseGate(replay);
        Assert.All(ownerRows, static row => Assert.Equal((ushort)0x0438, row.Raw.Opcode));
    }

    [Fact]
    public void Replay_20260722171214_And_171240_Attributes_Current4136_OwnedEntityLayouts()
    {
        SetResources();

        var headerReplay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentOwnedEntityHeaderLayout}"));
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentOwnedEntityLayouts}"));

        AssertOwnedEntity(headerReplay, entityId: 30_471, ownerId: 15_233);

        var ownedEntityCount = replay.SceneOwner.Entities.Entities.Values.Count(
            static entity => entity.OwnerKind == EntityOwnerKind.Summon && entity.OwnerEntityId is > 0);
        ownedEntityCount += replay.MapTransitionArchives.Sum(
            static archive => archive.Entities.Count(
                static entity => entity.OwnerKind == EntityOwnerKind.Summon && entity.OwnerEntityId is > 0));
        Assert.Equal(51, ownedEntityCount);

        AssertOwnedEntities(
            replay,
            ownerId: 1_073,
            17_712,
            20_681,
            21_430,
            22_310,
            22_547,
            23_096,
            23_394,
            25_157,
            26_638,
            27_643,
            28_960,
            30_181,
            30_805,
            31_000,
            31_139,
            32_835,
            33_493);
        AssertOwnedEntities(
            replay,
            ownerId: 15_233,
            18_424,
            24_860,
            25_106,
            26_354,
            26_991,
            27_511,
            28_300,
            28_800,
            29_414,
            29_941,
            30_249,
            30_269,
            32_331,
            35_882,
            36_258,
            40_094,
            40_099,
            41_142,
            41_882,
            41_990,
            42_395);
        AssertOwnedEntities(replay, ownerId: 10_060, 22_542, 26_695, 30_009, 33_064, 37_896);
        AssertOwnedEntities(replay, ownerId: 14_604, 32_155, 32_846);
        AssertOwnedEntities(replay, ownerId: 16_199, 17_437, 26_156);
        AssertOwnedEntities(replay, ownerId: 3_386, 22_438, 24_700, 29_142, 32_146);

        AssertUnownedPlayer(replay, entityId: 1_073);
        AssertUnownedPlayer(replay, entityId: 3_386);
        AssertUnownedPlayer(replay, entityId: 10_060);
        AssertUnownedPlayer(replay, entityId: 14_604);
        AssertUnownedPlayer(replay, entityId: 16_199);
        AssertUnownedPlayer(replay, entityId: 15_233);
    }

    [Fact]
    public void Replay_20260726011355_Attributes_Mode5F0000_4136OwnedEntities()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentOwnedEntityMode5F0000}"));

        var snapshot = AssertOwnedEntity(replay, entityId: 29_060, ownerId: 9_537);
        AssertOwnedEntity(replay, entityId: 41_891, ownerId: 9_537);
        Assert.Equal(1_232_299, snapshot.Combatants[9_537].DamageAmount);
    }

    [Fact]
    public void Replay_Current4136_Attributes_AdditionalOwnedEntityLayouts()
    {
        SetResources();

        var namedMode5F0001 = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentOwnedEntityNamedMode5F0001}"));
        var directMode5D1000 = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentOwnerTargetCanonicalizationEdge}"));
        var namedMode1F0001 = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentOwnerSystemCanonicalization}"));
        var npcOwnedMode170000 = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentTenPlayerForceRoster}"));

        Assert4136OwnedEntity(namedMode5F0001, entityId: 35_524, ownerId: 10_476);
        Assert4136OwnedEntityObservation(directMode5D1000, entityId: 35_785, ownerId: 8_876);
        Assert4136OwnedEntity(namedMode1F0001, entityId: 30_197, ownerId: 8_748);
        Assert4136OwnedEntity(npcOwnedMode170000, entityId: 43_045, ownerId: 41_711);
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
        Assert.True(TryGetPcMetadata(replay, playerId, out var metadata));
        Assert.Equal("綠豆冰糕", metadata.Nickname);
        Assert.Equal(Faction.Light, metadata.Faction);
        Assert.Equal(1007, metadata.OriginServerId);
        Assert.True(metadata.IsLocalPlayer);
    }

    [Fact]
    public void Replay_20260809021609_ParsesFixed32CrossServerIdentityAndCombat()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentFixed32CrossServerCombat}"));

        const int playerId = 2300;
        Assert.Contains(
            ReadAllJournalEntries(replay),
            static entry => entry.Raw.Opcode == 0x3336 &&
                            entry.State is { EntityId: playerId, StateCode: StateCodes.PlayerIdentity, Text: "코자", IsLocalPlayer: true, OriginServerId: 2007, Faction: Faction.Dark });

        Assert.True(replay.SceneOwner.MetadataRegistry.TryGetPcMetadata(playerId, out var metadata));
        Assert.Equal("코자", metadata.Nickname);
        Assert.Equal(Faction.Dark, metadata.Faction);
        Assert.Equal(2007, metadata.OriginServerId);
        Assert.True(metadata.IsLocalPlayer);

        var combatant = Assert.Single(replay.Combatants, static combatant => combatant.CombatantId == playerId);
        Assert.Equal(886_473, combatant.OutgoingDamage);
        Assert.Equal(269, combatant.OutgoingHits);
        Assert.Equal(269, combatant.OutgoingAttempts);
        Assert.Equal(138, combatant.OutgoingCriticals);
        Assert.Equal(34_217, combatant.IncomingDamage);
        Assert.Equal(37_170, combatant.OutgoingHealing);
        Assert.Equal(96_889, combatant.IncomingHealing);
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

        AssertLiveGroupRelations(replay, PlayerGroupRelation.PartyMember, 4327, 9183, 9429, 16102);
        AssertLiveGroupRelation(replay, 13028, PlayerGroupRelation.Unknown);
    }

    [Fact]
    public void Replay_20260715000443_ParsesCompleteTenPlayerForceRosterSnapshot()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentTenPlayerForceRoster}"));
        var entries = ReadAllJournalEntries(replay);

        AssertLiveGroupRelations(replay, PlayerGroupRelation.PartyMember, 3446, 3817, 13319, 15591);
        AssertLiveGroupRelations(replay, PlayerGroupRelation.ForceMember, 1307, 1549, 5142, 5193, 5927);
        AssertLiveGroupRelation(replay, 2204, PlayerGroupRelation.Unknown);

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

        AssertLiveGroupRelations(replay, PlayerGroupRelation.PartyMember, 1285, 2664, 9551, 15547);
        AssertLiveGroupRelations(replay, PlayerGroupRelation.ForceMember, 870, 6538, 8108, 9301, 15480);
        AssertLiveGroupRelation(replay, 9142, PlayerGroupRelation.Unknown);
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
        AssertLiveGroupRelations(replay, PlayerGroupRelation.Unknown, 4520, 4990, 7048, 7329, 9974, 10532);
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
        var contexts = CreateOwnerTargetCanonicalizationContexts(replay);
        var matches = new List<CanonicalizationProbeRow>();
        foreach (var entry in ReadCombatWireEntries(replay))
        {
            var canonicalizer = ResolveOwnerTargetCanonicalizer(contexts, entry.Stamp.ObservationOrdinal);
            if (canonicalizer is null)
                continue;

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
        var contexts = CreateOwnerTargetCanonicalizationContexts(replay);
        var admitted = new List<CombatContribution>();
        foreach (var row in ReadCombatWireEntries(replay))
        {
            var canonicalizer = ResolveOwnerTargetCanonicalizer(contexts, row.Stamp.ObservationOrdinal);
            if (canonicalizer is null)
                continue;

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

    private static OwnerTargetCanonicalizationContext[] CreateOwnerTargetCanonicalizationContexts(PacketLogReplayResult replay)
    {
        var contexts = new List<OwnerTargetCanonicalizationContext>(replay.MapTransitionArchives.Count + 1);
        foreach (var archive in replay.MapTransitionArchives)
        {
            var entities = new EntityStore();
            foreach (var entity in archive.Entities)
            {
                if (entity is { OwnerKind: EntityOwnerKind.Summon, OwnerEntityId: > 0 })
                    entities.ApplySummon(entity.OwnerEntityId.Value, entity.EntityId);
            }

            contexts.Add(new OwnerTargetCanonicalizationContext(
                archive.TimelineSegment.StartObservationOrdinal,
                archive.TimelineSegment.EndObservationOrdinalExclusive,
                new OwnerTargetSummonResourceCanonicalizer(entities)));
        }

        contexts.Add(new OwnerTargetCanonicalizationContext(
            replay.SceneOwner.SceneStartObservationOrdinal,
            replay.SceneJournal.NextObservationOrdinal,
            new OwnerTargetSummonResourceCanonicalizer(replay.SceneOwner.Entities)));
        return contexts.ToArray();
    }

    private static OwnerTargetSummonResourceCanonicalizer? ResolveOwnerTargetCanonicalizer(
        IReadOnlyList<OwnerTargetCanonicalizationContext> contexts,
        long observationOrdinal)
    {
        foreach (var context in contexts)
        {
            if (observationOrdinal >= context.StartObservationOrdinal &&
                observationOrdinal < context.EndObservationOrdinalExclusive)
            {
                return context.Canonicalizer;
            }
        }

        return null;
    }

    private static string CreateSystemRecoveryPairKey(in CanonicalizationProbeRow row)
        => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{row.SourceId}|{row.TargetId}|{row.Observation.SkillCode}|{row.Observation.BodyResourceEffectRef.RawId}|{row.Observation.DetailResourceEffectRef.RawId}|{row.Observation.ChainId}|{row.Observation.Damage}");

    private static IReadOnlyList<DirectMapEventObservation> ReadDirectMapEventObservations(
        PacketLogReplayResult replay,
        params ushort[] opcodes)
    {
        var observations = new List<DirectMapEventObservation>();
        var cursor = replay.SceneJournal.CreateCursor(replay.SceneJournal.FirstObservationOrdinal);
        while (true)
        {
            var result = replay.SceneJournal.ReadEntries(cursor, 1024, entries =>
            {
                foreach (var entry in entries)
                {
                    if (entry.Domain != ObservedEventDomain.Scene)
                    {
                        continue;
                    }

                    if (!opcodes.Contains(entry.Raw.Opcode))
                    {
                        continue;
                    }

                    observations.Add(new DirectMapEventObservation(
                        entry.Raw.Opcode,
                        entry.Scene.MapId,
                        entry.Scene.Kind,
                        entry.ObservedAtMilliseconds,
                        entry.Stamp.ObservationOrdinal));
                }
            });

            if (result.Count == 0)
            {
                return observations;
            }

            cursor = result.Cursor;
        }
    }

    private static IReadOnlyList<SceneTransitionObservation> ReadSceneTransitions(PacketLogReplayResult replay)
    {
        var transitions = new List<SceneTransitionObservation>();
        var cursor = replay.SceneJournal.CreateCursor(replay.SceneJournal.FirstObservationOrdinal);
        while (true)
        {
            var result = replay.SceneJournal.ReadEntries(cursor, 1024, entries =>
            {
                foreach (var entry in entries)
                {
                    if (entry.Domain != ObservedEventDomain.Scene)
                        continue;

                    if (entry.Scene.Kind == SceneObservationKind.MapContextStarted)
                    {
                        transitions.Add(new SceneTransitionObservation(
                            entry.Raw.Opcode,
                            entry.Scene.MapId,
                            entry.Scene.MapInstanceId,
                            entry.Scene.Kind,
                            entry.Stamp.ObservationOrdinal));
                    }
                }
            });

            if (result.Count == 0)
                return transitions;

            cursor = result.Cursor;
        }
    }

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
                            entry.Stamp,
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

    private static void AssertLiveGroupRelations(
        PacketLogReplayResult replay,
        PlayerGroupRelation expectedRelation,
        params int[] entityIds)
    {
        foreach (var entityId in entityIds)
            AssertLiveGroupRelation(replay, entityId, expectedRelation);
    }

    private static void AssertGroupMembershipObservations(
        IReadOnlyList<ReplayJournalEntrySnapshot> entries,
        ushort opcode,
        PlayerGroupKind kind,
        params int[] entityIds)
    {
        foreach (var entityId in entityIds)
        {
            Assert.Contains(
                entries,
                entry => entry.Raw.Opcode == opcode &&
                         entry.SourceEntityId == entityId &&
                         entry.State is
                         {
                             EntityId: var stateEntityId,
                             StateCode: StateCodes.PlayerGroupMembership,
                             GroupMembership.Kind: var stateKind
                         } &&
                         stateEntityId == entityId &&
                         stateKind == kind);
        }
    }

    private static void AssertLiveGroupRelation(
        PacketLogReplayResult replay,
        int entityId,
        PlayerGroupRelation expectedRelation)
    {
        Assert.True(
            replay.SceneOwner.MetadataRegistry.TryGetPcMetadata(entityId, out var metadata),
            $"missing live PC metadata for {entityId}");
        Assert.Equal(expectedRelation, metadata.GroupRelation);
    }

    private static void AssertArchivedGroupRelation(
        in SceneIdentityScope identityScope,
        int entityId,
        PlayerGroupRelation expectedRelation)
    {
        Assert.True(
            identityScope.TryGetPcMetadata(entityId, out var metadata),
            $"missing archived PC metadata for {entityId}");
        Assert.Equal(expectedRelation, metadata.GroupRelation);
    }

    private static void AssertPcMetadata(PacketLogReplayResult replay, int entityId, string nickname, CharacterClass characterClass, Faction faction)
    {
        Assert.True(TryGetPcMetadata(replay, entityId, out var metadata), $"missing PC metadata for {entityId}");
        Assert.Equal(nickname, metadata.Nickname);
        Assert.Equal(characterClass, metadata.CharacterClass);
        Assert.Equal(faction, metadata.Faction);
    }

    private static bool TryGetPcMetadata(PacketLogReplayResult replay, int entityId, out PcMetadata metadata)
    {
        if (replay.SceneOwner.MetadataRegistry.TryGetPcMetadata(entityId, out metadata))
            return true;

        foreach (var archive in replay.MapTransitionArchives)
        {
            if (archive.IdentityScope.TryGetPcMetadata(entityId, out metadata))
                return true;
        }

        metadata = default;
        return false;
    }

    private static bool TryGetLatestEntityVitalObservation(
        PacketLogReplayResult replay,
        int entityId,
        out EntityVitalObservation observation)
    {
        EntityVitalObservation? latest = null;
        var cursor = replay.SceneJournal.CreateCursor(replay.SceneJournal.FirstObservationOrdinal);
        while (true)
        {
            var result = replay.SceneJournal.ReadEntries(cursor, 1024, entries =>
            {
                foreach (var entry in entries)
                {
                    if (entry.Domain == ObservedEventDomain.EntityVital &&
                        entry.EntityVital.EntityId == entityId)
                    {
                        latest = entry.EntityVital;
                    }
                }
            });

            if (result.Count == 0)
                break;

            cursor = result.Cursor;
        }

        observation = latest.GetValueOrDefault();
        return latest.HasValue;
    }

    private static SceneCombatSnapshot AssertOwnedEntity(PacketLogReplayResult replay, int entityId, int ownerId)
    {
        if (replay.SceneOwner.Entities.TryGet(entityId, out var currentEntity) &&
            currentEntity.OwnerKind == EntityOwnerKind.Summon &&
            currentEntity.OwnerEntityId == ownerId)
        {
            Assert.Equal(NpcKind.Summon, currentEntity.Kind);
            Assert.DoesNotContain(entityId, replay.Snapshot.Combatants.Keys);
            return replay.Snapshot;
        }

        foreach (var archive in replay.MapTransitionArchives)
        {
            var archivedEntity = archive.Entities.FirstOrDefault(
                candidate => candidate.EntityId == entityId &&
                             candidate.OwnerKind == EntityOwnerKind.Summon &&
                             candidate.OwnerEntityId == ownerId);
            if (archivedEntity.EntityId == 0)
                continue;

            Assert.Equal(NpcKind.Summon, archivedEntity.Kind);
            Assert.DoesNotContain(entityId, archive.Snapshot.Combatants.Keys);
            return archive.Snapshot;
        }

        Assert.Fail($"missing owned entity {entityId} for owner {ownerId} in retained map contexts");
        return default!;
    }

    private static void AssertOwnedEntity(SceneArchivePayload archive, int entityId, int ownerId)
    {
        var entity = Assert.Single(archive.Entities, candidate => candidate.EntityId == entityId);
        Assert.Equal(EntityOwnerKind.Summon, entity.OwnerKind);
        Assert.Equal(ownerId, entity.OwnerEntityId);
        Assert.Equal(NpcKind.Summon, entity.Kind);
        Assert.DoesNotContain(entityId, archive.Snapshot.Combatants.Keys);
    }

    private static void Assert4136OwnedEntity(PacketLogReplayResult replay, int entityId, int ownerId)
    {
        AssertOwnedEntity(replay, entityId, ownerId);
        Assert4136OwnedEntityObservation(replay, entityId, ownerId);
    }

    private static void Assert4136OwnedEntityObservation(PacketLogReplayResult replay, int entityId, int ownerId)
    {
        Assert.Contains(
            ReadAllJournalEntries(replay),
            entry => entry.Raw.Opcode == 0x4136 &&
                     entry.SourceEntityId == ownerId &&
                     entry.State is { EntityId: var stateEntityId, StateCode: 0, Value0: var stateOwnerId } &&
                     stateEntityId == entityId &&
                     stateOwnerId == ownerId);
    }

    private static void AssertOwnedEntities(PacketLogReplayResult replay, int ownerId, params int[] entityIds)
    {
        foreach (var entityId in entityIds)
            AssertOwnedEntity(replay, entityId, ownerId);
    }

    private static void AssertOwnedEntities(SceneArchivePayload archive, int ownerId, params int[] entityIds)
    {
        foreach (var entityId in entityIds)
            AssertOwnedEntity(archive, entityId, ownerId);
    }

    private static void AssertUnownedPlayer(PacketLogReplayResult replay, int entityId)
    {
        if (replay.SceneOwner.Entities.TryGet(entityId, out var currentEntity) &&
            currentEntity.OwnerKind == EntityOwnerKind.None &&
            currentEntity.OwnerEntityId is null &&
            replay.Snapshot.Combatants.ContainsKey(entityId))
        {
            return;
        }

        Assert.Contains(
            replay.MapTransitionArchives,
            archive => archive.Entities.Any(
                           entity => entity.EntityId == entityId &&
                                     entity.OwnerKind == EntityOwnerKind.None &&
                                     entity.OwnerEntityId is null) &&
                       archive.Snapshot.Combatants.ContainsKey(entityId));
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

    private readonly record struct DirectMapEventObservation(
        ushort Opcode,
        uint MapId,
        SceneObservationKind Kind,
        long ObservedAtMilliseconds,
        long ObservationOrdinal);

    private readonly record struct SceneTransitionObservation(
        ushort Opcode,
        uint MapId,
        uint MapInstanceId,
        SceneObservationKind Kind,
        long ObservationOrdinal);

    private readonly record struct ReplayJournalEntrySnapshot(
        TimelineStamp Stamp,
        int SourceEntityId,
        RawPacketReference Raw,
        StateObservation? State);

    private readonly record struct CombatWireEntrySnapshot(
        TimelineStamp Stamp,
        int SourceId,
        int TargetId,
        CombatWireObservation Observation,
        RawPacketReference Raw);

    private sealed record OwnerTargetCanonicalizationContext(
        long StartObservationOrdinal,
        long EndObservationOrdinalExclusive,
        OwnerTargetSummonResourceCanonicalizer Canonicalizer);

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

    private static void AssertMetric(long actual, long expected, string name, string dump)
        => Assert.True(actual == expected, $"{name}={actual} expected={expected}\n{dump}");
}
