using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Archive;
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
    public void Replay_20260809050345_EmitsCurrentRoundTripEchoes()
    {
        var observations = new List<ProtocolRoundTripObservation>();

        var replay = PacketLogReplayService.ReplayMany(
            [FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentChargeCooldown}")],
            observations.Add);

        Assert.Equal(2_145, replay.TotalLines);
        Assert.Equal(10, observations.Count);
        Assert.All(observations, static observation =>
            Assert.InRange(
                observation.ServerUnixMilliseconds - observation.ClientSentUnixMilliseconds,
                139,
                144));
        Assert.Contains(observations, static observation =>
            observation.ClientSentUnixMilliseconds == 1_786_223_034_263 &&
            observation.ServerUnixMilliseconds == 1_786_223_034_407);
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
    public void Replay_20260909150745_Resolves_Current4136_NpcIdentities()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentNpcIdentityLayout}"));

        AssertNpcEntity(replay, entityId: 17_489, npcCode: 2_301_006, NpcKind.Boss);
        AssertNpcEntity(replay, entityId: 17_544, npcCode: 2_301_014, NpcKind.Boss);
        Assert.Contains(2_301_006, replay.Snapshot.BossNpcCodes.AsSpan().ToArray());
        Assert.Contains(2_301_014, replay.Snapshot.BossNpcCodes.AsSpan().ToArray());

        Assert4136OwnedEntity(replay, entityId: 28_492, ownerId: 4_917);
        AssertNpcEntity(replay, entityId: 28_492, npcCode: 2_920_650, NpcKind.Summon);
    }

    [Fact]
    public void Replay_20260909214537_Resolves_Current4136_SummonOwnership()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentSummonOwnershipLayout}"));

        Assert4136OwnedNpc(replay, entityId: 37_116, ownerId: 7_778);
        Assert4136OwnedNpc(replay, entityId: 26_658, ownerId: 7_778);
        Assert4136OwnedNpc(replay, entityId: 18_902, ownerId: 7_778);
        Assert4136OwnedNpc(replay, entityId: 33_564, ownerId: 7_778);
        Assert4136OwnedNpc(replay, entityId: 22_801, ownerId: 7_778);
        Assert4136OwnedNpc(replay, entityId: 38_096, ownerId: 2_653);

        var ownedSummons = replay.SceneOwner.Entities.Entities.Values
            .Where(static entity => entity.OwnerKind == EntityOwnerKind.Summon)
            .ToArray();
        Assert.Equal(6, ownedSummons.Length);
        Assert.Equal(5, ownedSummons.Count(static summon => summon.OwnerEntityId == 7_778));
        Assert.Contains(ownedSummons, static summon => summon.EntityId == 38_096 && summon.OwnerEntityId == 2_653);
    }

    [Fact]
    public void Replay_20260909222525_Resolves_Current4136_NamedSummonOwnership()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentNamedSummonOwnershipLayout}"));

        Assert4136OwnedNpc(replay, entityId: 22_813, ownerId: 12_602);
    }

    [Fact]
    public void Replay_20260909232117_Resolves_Current4136_ElementalistSummonOwnership()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentElementalistSummonOwnershipLayout}"));

        Assert4136OwnedNpc(replay, entityId: 32_588, ownerId: 8_459);
        Assert4136OwnedNpc(replay, entityId: 36_986, ownerId: 8_459);
        Assert4136OwnedNpc(replay, entityId: 18_407, ownerId: 8_459);
        Assert4136OwnedNpc(replay, entityId: 26_593, ownerId: 8_459);
        Assert4136OwnedNpc(replay, entityId: 38_728, ownerId: 8_459);

        int[] expectedElementalistSummonIds =
        [
            18_328, 18_407, 18_952, 20_017, 21_753, 22_029, 22_859, 23_126, 23_312, 23_677,
            23_747, 26_593, 28_930, 28_933, 30_182, 30_395, 30_913, 31_673, 32_449, 32_588,
            36_036, 36_986, 37_611, 37_929, 38_053, 38_728, 39_374, 39_867, 40_026
        ];
        var actualElementalistSummonIds = SceneReplayTestView.SummonOwnerByInstance(replay)
            .Where(static pair => pair.Value == 8_459)
            .Select(static pair => pair.Key)
            .Order()
            .ToArray();
        Assert.Equal(expectedElementalistSummonIds, actualElementalistSummonIds);
    }

    [Fact]
    public void Replay_20260910001840_Resolves_Current4136_DirectMode1FSummonOwnership()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentDirectMode1FSummonOwnershipLayout}"));

        Assert4136OwnedNpc(replay, entityId: 33_185, ownerId: 8_966);
        Assert4136OwnedNpc(replay, entityId: 38_173, ownerId: 8_966);
    }

    [Fact]
    public void Replay_20260912203027_Resolves_Current4136_DirectMode1F10SummonOwnership()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentDirectMode1F10SummonOwnershipLayout}"));

        int[] expectedSummonIds =
        [
            21_846, 22_854, 23_191, 23_917, 24_274, 25_019, 28_058, 28_518, 28_520, 31_645,
            34_330, 34_991, 34_998, 35_220, 36_336, 37_447, 37_712, 37_741, 39_684,
            39_854, 40_682, 42_867, 42_871, 43_310, 44_397, 44_830, 44_843, 45_019
        ];
        var actualSummonIds = SceneReplayTestView.SummonOwnerByInstance(replay)
            .Where(static pair => pair.Value == 9_303)
            .Select(static pair => pair.Key)
            .Order()
            .ToArray();
        Assert.Equal(expectedSummonIds, actualSummonIds);
        Assert4136OwnedNpc(replay, entityId: 40_682, ownerId: 9_303);
        Assert.Equal(381_926_629, replay.Snapshot.Combatants[9_303].DamageAmount);
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
    public void Replay_20260809050345_ParsesChargeCooldownAndRowBaseUpdates()
    {
        SetResources();

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentChargeCooldown}"));
        var entries = ReadAllJournalEntries(replay);

        var combatAndControlEntries = entries.Where(static entry => entry.Raw.Opcode is 0x0238 or 0x0438).ToArray();
        Assert.NotEmpty(combatAndControlEntries);
        Assert.All(
            combatAndControlEntries,
            static entry => Assert.True(entry.Raw.CaptureSequence > 0));
        var combat0238Sequences = combatAndControlEntries
            .Where(static entry => entry.Raw.Opcode == 0x0238 && entry.State is null)
            .Select(static entry => entry.Raw.CaptureSequence)
            .ToHashSet();
        Assert.NotEmpty(combat0238Sequences);
        Assert.All(
            entries.Where(static entry => entry.Raw.Opcode == 0x0238 && entry.State is not null),
            entry => Assert.Contains(entry.Raw.CaptureSequence, combat0238Sequences));

        Assert.True(replay.SceneOwner.MetadataRegistry.TryGetPcMetadata(9_150, out var metadata));
        Assert.True(metadata.IsLocalPlayer);

        var flashSliceStarts = entries
            .Where(static entry => entry.State is { StateCode: StateCodes.CooldownStart0238, Value0: 13_050_240 })
            .Select(static entry => entry.State!.Value)
            .ToArray();
        Assert.Collection(
            flashSliceStarts,
            static state => AssertCooldown(state, 13_050_240, 7_450),
            static state => AssertCooldown(state, 13_050_240, 6_500));

        var flashSliceCharges = entries
            .Where(static entry => entry.State is { StateCode: StateCodes.CooldownCharge2238, Value0: 13_050_240 })
            .Select(static entry => entry.State!.Value)
            .ToArray();
        Assert.Collection(
            flashSliceCharges,
            static state => AssertChargeCooldown(state, 3, 1, 7_450),
            static state => AssertChargeCooldown(state, 1, 2, 0));

        Assert.Contains(
            entries,
            static entry => entry.SourceEntityId == 9_150 &&
                            entry.State is { StateCode: StateCodes.CooldownStart0238, Value0: 13_130_230, Value1: 7_450 });
        Assert.Contains(
            entries,
            static entry => entry.State is { StateCode: StateCodes.Cooldown4738, Value0: 13_130_000, Value1: 5_800 });
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
    public void Replay_20260712211428_ParsesFirstChargeCooldownWithTwoRemaining()
    {
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{ReplayScenarioCatalog.CurrentPartyStatusRelation}"));

        Assert.Contains(
            ReadAllJournalEntries(replay),
            static entry => entry.State is { StateCode: StateCodes.CooldownStart0238, Value0: 13_060_250, Value1: 7_500 } state &&
                            CooldownStartObservationDetail.TryDecode(state.DetailRaw, out var mode, out var availableCountAfterControl) &&
                            mode == 12 && availableCountAfterControl == 2);
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

    private static void AssertCooldown(StateObservation state, int rowBaseSkillId, int remainingMilliseconds)
    {
        Assert.Equal(rowBaseSkillId, state.Value0);
        Assert.Equal(remainingMilliseconds, state.Value1);
    }

    private static void AssertChargeCooldown(
        StateObservation state,
        byte expectedState,
        int expectedAvailableCount,
        int expectedRemainingMilliseconds)
    {
        Assert.Equal(13_050_240, state.Value0);
        Assert.Equal(expectedRemainingMilliseconds, state.Value1);
        Assert.True(CooldownChargeObservationDetail.TryDecode(state.DetailRaw, out var packetState, out var availableCount));
        Assert.Equal(expectedState, packetState);
        Assert.Equal(expectedAvailableCount, availableCount);
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

    private static void AssertNpcEntity(PacketLogReplayResult replay, int entityId, int npcCode, NpcKind kind)
    {
        Assert.True(replay.SceneOwner.Entities.TryGet(entityId, out var entity));
        Assert.Equal(npcCode, entity.NpcCode);
        Assert.Equal(kind, entity.Kind);
    }

    private static void Assert4136OwnedNpc(PacketLogReplayResult replay, int entityId, int ownerId)
    {
        Assert.True(replay.SceneOwner.Entities.TryGet(entityId, out var entity));
        Assert.Equal(EntityOwnerKind.Summon, entity.OwnerKind);
        Assert.Equal(ownerId, entity.OwnerEntityId);
        Assert.Contains(
            ReadAllJournalEntries(replay),
            entry => entry.Raw.Opcode == 0x4136 &&
                     entry.SourceEntityId == ownerId &&
                     entry.State is { EntityId: var stateEntityId, StateCode: 0, Value0: var stateOwnerId } &&
                     stateEntityId == entityId &&
                     stateOwnerId == ownerId);
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
