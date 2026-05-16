using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Runtime;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketStreamProcessorNpcObservationTests
{
    private static readonly TcpConnection TestConnection = new(0x0100007f, 0x0100007f, 49820, 57080);

    [Fact]
    public void Runtime_Sink_Constructor_Writes_To_Scene_Journal()
    {
        var scene = new SceneLiveReadModel();
        using var processor = new PacketStreamProcessor(scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal)));

        var parsed = processor.AppendAndProcess(HexHelper.FromFixture("state/2136-boss-scene-200003.hex"), TestConnection);

        Assert.True(parsed);
        scene.Owner.Refresh();
        Assert.Equal((uint)200003, scene.Owner.Boundary.CurrentMapId);
        scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal)).MarkSceneArrival();
        scene.Owner.Refresh();
        Assert.Equal((uint)200003, scene.Owner.Boundary.CurrentMapId);
    }

    [Fact]
    public void Uses_Recent_4536_Source_As_Fallback_For_SourceLess_Runtime_State_Frames()
    {
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid()) { CurrentTarget = 4370 };
        var processor = new PacketStreamProcessor(sink);

        processor.AppendAndProcess(HexHelper.FromFixture("state/4536-boss-observed-4370.hex"), TestConnection);
        processor.AppendAndProcess(HexHelper.FromFixture("state/2136-boss-scene-200003.hex"), TestConnection);
        processor.AppendAndProcess(HexHelper.FromFixture("state/0140-boss-tail-430d03.hex"), TestConnection);
        processor.AppendAndProcess(HexHelper.FromFixture("state/0240-boss-tail-430d03.hex"), TestConnection);

        Assert.True(sink.TryGetNpcRuntimeState(4370, out var state));
        Assert.Equal((uint)6, state.Sequence2136);
        Assert.Equal((uint)200003, state.Value2136);
        Assert.Equal((uint)200003, state.Value0140);
        Assert.Equal((uint)200003, state.Value0240);
    }

    [Fact]
    public void Scene_Replay_Captures_Npc_Extended_State_From_State_Frames()
    {
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260419204630.log"));

        var journal = replay.SceneJournal;
        var entities = new EntityStore();
        var metadata = new SceneBoundaryStore();
        var combat = new CombatStore();
        var applier = new DomainEventApplier(entities, metadata, combat);

        applier.ApplyJournal(journal);

        var entity = entities.Entities.Values.FirstOrDefault(static entity => entity.Value2136.HasValue || entity.Value0140.HasValue || entity.Value0240.HasValue || entity.State4636.HasValue || entity.Latest2C38.HasValue);
        Assert.NotNull(entity);
    }

    [Theory]
    [InlineData("state/2136-boss-scene-1010.hex", 1010)]
    [InlineData("state/0140-boss-tail-f203.hex", 1010)]
    [InlineData("state/0240-boss-tail-f203.hex", 1010)]
    [InlineData("state/2136-boss-scene-200003.hex", 200003)]
    [InlineData("state/0140-boss-tail-430d03.hex", 200003)]
    [InlineData("state/0240-boss-tail-430d03.hex", 200003)]
    public void Scene_State_Frames_Commit_Map_Id_Immediately(string fixture, uint expectedMapId)
    {
        var scene = new SceneLiveReadModel();
        var processor = new PacketStreamProcessor(scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal)));

        var parsed = processor.AppendAndProcess(HexHelper.FromFixture(fixture), TestConnection);

        Assert.True(parsed);
        scene.Owner.Refresh();
        Assert.Equal(expectedMapId, scene.Owner.Boundary.CurrentMapId);
        scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal)).MarkSceneArrival();
        scene.Owner.Refresh();
        Assert.Equal(expectedMapId, scene.Owner.Boundary.CurrentMapId);
    }

    [Fact]
    public void Map_Instance_Frame_Stages_Instance_And_Is_Cleared_On_Confirmed_Map_Change()
    {
        var scene = new SceneLiveReadModel();
        var arrivalSink = scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal));
        var processor = new PacketStreamProcessor(scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal)));

        arrivalSink.StageDestinationMap(200003);
        arrivalSink.MarkSceneArrival();
        var parsed = processor.AppendAndProcess(HexHelper.FromFixture("state/2e92-bosschallenge-map-event.hex"), TestConnection);
        scene.Owner.Refresh();

        Assert.True(parsed);
        Assert.Equal((uint)200003, scene.Owner.Boundary.CurrentMapId);
        Assert.Equal((uint)113515, scene.Owner.Boundary.CurrentMapInstanceId);

        arrivalSink.MarkSceneArrival();
        scene.Owner.Refresh();
        Assert.Equal((uint)113515, scene.Owner.Boundary.CurrentMapInstanceId);

        parsed = processor.AppendAndProcess(HexHelper.FromFixture("state/2136-boss-scene-1010.hex"), TestConnection);
        scene.Owner.Refresh();

        Assert.True(parsed);
        Assert.Equal((uint)1010, scene.Owner.Boundary.CurrentMapId);
        Assert.Equal((uint)0, scene.Owner.Boundary.CurrentMapInstanceId);

        arrivalSink.MarkSceneArrival();
        scene.Owner.Refresh();
        Assert.Equal((uint)1010, scene.Owner.Boundary.CurrentMapId);
        Assert.Equal((uint)0, scene.Owner.Boundary.CurrentMapInstanceId);
    }

    [Fact]
    public void Redundant_State_2136_For_Same_Map_Does_Not_Stale_The_Already_Applied_Instance()
    {
        var scene = new SceneLiveReadModel();
        var sink = scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal));

        sink.StageDestinationMap(910035);
        sink.MarkSceneArrival();
        scene.Owner.Refresh();
        Assert.Equal((uint)910035, scene.Owner.Boundary.CurrentMapId);
        Assert.Equal((uint)0, scene.Owner.Boundary.CurrentMapInstanceId);

        sink.StageDestinationMap(910035);

        sink.StageDestinationMapInstance(516446);
        scene.Owner.Refresh();
        Assert.Equal((uint)516446, scene.Owner.Boundary.CurrentMapInstanceId);

        sink.StageDestinationMap(910035);
        sink.MarkSceneArrival();
        scene.Owner.Refresh();
        Assert.Equal((uint)910035, scene.Owner.Boundary.CurrentMapId);
        Assert.Equal((uint)516446, scene.Owner.Boundary.CurrentMapInstanceId);
    }

    [Fact]
    public void State_Catalog_Probe_Does_Not_Overwrite_Known_NpcCode_When_Value_Misses_Catalog()
    {
        const int npcInstanceId = 25664;
        const int npcCode = 2980049;
        const int sceneStateValue = 200003;

        var catalog = ResourceDatabase.LoadNpcCatalog("zh-TW");
        Assert.True(catalog.ContainsKey(npcCode));
        Assert.False(catalog.ContainsKey(sceneStateValue));
        CombatResourceRegistry.SetGameResources([], catalog);

        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid()) { CurrentTarget = npcInstanceId };
        sink.AppendNpcCode(npcInstanceId, npcCode);

        var processor = new PacketStreamProcessor(sink);
        var parsed = processor.AppendAndProcess(HexHelper.FromFixture("state/2136-boss-scene-200003.hex"), TestConnection);

        Assert.True(parsed);
        Assert.True(sink.TryGetNpcRuntimeState(npcInstanceId, out var state));
        Assert.Equal(npcCode, state.NpcCode);
        Assert.Equal((uint)sceneStateValue, state.Value2136);
    }

    [Fact]
    public void Synthesizes_Invincible_From_Mode48_Periodic_Link_Record()
    {
        CombatResourceRegistry.SetGameResources(
            [
                new Skill(1230000, "Fangs", SkillCategory.Npc, SkillSourceType.Unknown, "npc", null)
            ],
            new Dictionary<int, NpcCatalogEntry>());

        var scene = new SceneLiveReadModel();
        using var processor = new PacketStreamProcessor(scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal)));

        var parsed = processor.AppendAndProcess(HexHelper.FromFixture("combat/0538-mode48-link.hex"), TestConnection);

        Assert.True(parsed);
        scene.Owner.Refresh();
        var invincible = Assert.Single(scene.Owner.Combat.Events);
        Assert.Equal(29240, invincible.SourceId);
        Assert.Equal(16047, invincible.TargetId);
        Assert.Equal(608, invincible.Observation.Marker);
        Assert.Equal(1237540, invincible.Observation.OriginalSkillCode);
        Assert.Equal(1230000, invincible.Observation.SkillCode);
        Assert.Equal(1, invincible.InvincibleCount);
    }

    [Fact]
    public void Keeps_NonLink_0538_Periodic_Value_In_Combat_Metrics()
    {
        var scene = new SceneLiveReadModel();
        using var processor = new PacketStreamProcessor(scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal)));

        var parsed = processor.AppendAndProcess(HexHelper.FromFixture("combat/0538-dot.hex"), TestConnection);

        Assert.True(parsed);
        scene.Owner.Refresh();
        Assert.Single(scene.Owner.Combat.Events, static e => e.TargetId == 17640);
    }

    [Fact]
    public void Scans_Embedded_3336_OwnNickname_Record_From_Larger_Packet()
    {
        var scene = new SceneLiveReadModel();
        using var processor = new PacketStreamProcessor(scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal)));
        var packet = Convert.FromHexString("1EAA3336D70F5FB17904070750657269676565EF0306000000012D000000");

        var parsed = processor.AppendAndProcess(packet, TestConnection);

        Assert.True(parsed);
        scene.Owner.Refresh();
        Assert.True(scene.Owner.MetadataRegistry.TryGetPcMetadata(2007, out var metadata));
        Assert.Equal("Perigee", metadata.Nickname);
        Assert.Equal(495, metadata.OriginServerId);
    }

    [Fact]
    public async Task AppendAndProcess_Holds_Synchronization_Gate_For_Whole_Batch()
    {
        var sink = new BlockingSynchronizedSink();
        using var processor = new PacketStreamProcessor(sink);
        var packet = Convert.FromHexString("1EAA3336D70F5FB17904070750657269676565EF0306000000012D000000");
        var running = Task.Run(() => processor.AppendAndProcess(packet, TestConnection), TestContext.Current.CancellationToken);

        await sink.NicknameEntered.Task.WaitAsync(TestContext.Current.CancellationToken);

        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiter = Task.Run(() =>
        {
            lock (sink.Gate)
            {
                acquired.SetResult();
            }
        }, TestContext.Current.CancellationToken);

        Assert.NotSame(acquired.Task, await Task.WhenAny(acquired.Task, Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken)));
        sink.AllowNickname.SetResult();
        Assert.True(await running.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        await acquired.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await waiter.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.True(sink.SceneArrivalCalled);
    }

    [Fact]
    public void Parses_Compact_0438_Recovery_Frame_Without_Adding_Combat_Metrics()
    {
        var scene = new SceneLiveReadModel();
        using var processor = new PacketStreamProcessor(scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal)));

        var parsed = processor.AppendAndProcess(HexHelper.FromFixture("combat/0438-compact-other.hex"), TestConnection);

        Assert.True(parsed);
        scene.Owner.Refresh();
        Assert.Empty(scene.Owner.Combat.Events);
    }

    [Theory]
    [InlineData("state/0238-compact-control.hex")]
    [InlineData("state/0638-compact-control.hex")]
    public void Parses_Compact_Control_Frames_Without_Adding_Combat_Metrics(string path)
    {
        var scene = new SceneLiveReadModel();
        using var processor = new PacketStreamProcessor(scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal)));

        var parsed = processor.AppendAndProcess(HexHelper.FromFixture(path), TestConnection);

        Assert.True(parsed);
        scene.Owner.Refresh();
        Assert.Empty(scene.Owner.Combat.Events);
    }

    [Fact]
    public void Attributes_Heart_Gore_Sidecar_To_Preceding_Damage_Packet_As_MultiHit()
    {
        CombatResourceRegistry.SetGameResources(BuildMultiHitSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var scene = new SceneLiveReadModel();
        using var processor = new PacketStreamProcessor(scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal)));

        processor.AppendAndProcess(HexHelper.Parse("2B043892D5013604EB449A48C700040311005C02D84D01000000FC8901E8AA090101C1180100AC3E"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0E0638EB4478B4CB000500"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("1F0438EB440000EB4478B4CB000502EB7E924F01000000FC89010100"), TestConnection);
        scene.Owner.Refresh();

        var packets = scene.Owner.Combat.Events.Where(static e => e.SourceId == 8811).ToArray();

        var packet = Assert.Single(packets, static e => e.Observation.EventKind == CombatEventKind.Damage);
        Assert.Equal(4, packet.Observation.Marker);
        Assert.Equal(1, packet.Observation.HitCount);
        Assert.Equal(1, packet.Observation.MultiHitCount);
        Assert.True((packet.Observation.Modifiers & DamageModifiers.MultiHit) != 0);

        Assert.Contains(packets, static e => e.Observation.ValueKind == CombatValueKind.DrainHealing);
    }

    [Fact]
    public void Does_Not_Merge_SameMarker_Followup_Without_Authoritative_MultiHit_Signal()
    {
        CombatResourceRegistry.SetGameResources(BuildMultiHitSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        using var scene = new SceneTestHarness();
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = 4725,
            TargetId = 42995,
            OriginalSkillCode = 13060250,
            SkillCode = 13060250,
            Marker = 0xD7,
            Flag = 4,
            Type = 3,
            Unknown = 21957,
            Damage = 148403,
            Modifiers = DamageModifiers.Smite
        });
        scene.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = 4725,
            TargetId = 42995,
            OriginalSkillCode = 13060250,
            SkillCode = 13060250,
            Marker = 0xD7,
            Flag = 0,
            Type = 3,
            Unknown = 21957,
            Damage = 21992
        });

        scene.CreateSnapshot();

        var parsedPackets = scene.Owner.Combat.Events.Where(static e => e.SourceId == 4725).ToArray();
        Assert.Equal(2, parsedPackets.Length);
        Assert.Equal(parsedPackets[0].Observation.Marker, parsedPackets[1].Observation.Marker);
        Assert.Equal(1, parsedPackets[0].Observation.HitCount);
        Assert.Equal(0, parsedPackets[0].Observation.MultiHitCount);
        Assert.True((parsedPackets[0].Observation.Modifiers & DamageModifiers.MultiHit) == 0);
        Assert.Equal(1, parsedPackets[1].Observation.HitCount);
        Assert.Equal(0, parsedPackets[1].Observation.MultiHitCount);
    }

    [Fact]
    public void Does_Not_Attribute_Wrapped_8456_3642_Sidecars_Without_Explicit_MultiHit_Owner()
    {
        CombatResourceRegistry.SetGameResources(BuildMultiHitSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var scene = new SceneLiveReadModel();
        using var processor = new PacketStreamProcessor(scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal)));

        processor.AppendAndProcess(HexHelper.Parse("220438ADCB010400A507D1890E014402AFD5AD6901000000D88501FB1D0100"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("18845601383B4236040000000D69F36D9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("1884560148624236040000000D69F36D9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("18845601F4884236040000000D69F36D9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("1884560168B04236040000000D69F36D9D01000000"), TestConnection);
        scene.Owner.Refresh();

        var packets = scene.Owner.Combat.Events.Where(static e => e.SourceId == 933).ToArray();

        var packet = Assert.Single(packets);
        Assert.Equal(68, packet.Observation.Marker);
        Assert.Equal(0, packet.Observation.MultiHitCount);
        Assert.True((packet.Observation.Modifiers & DamageModifiers.MultiHit) == 0);
    }

    [Fact]
    public void Uses_TailEncoded_MultiHit_Count_Without_DoubleCounting_Wrapped_8456_Sidecars()
    {
        CombatResourceRegistry.SetGameResources(BuildMultiHitSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var scene = new SceneLiveReadModel();
        using var processor = new PacketStreamProcessor(scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal)));

        processor.AppendAndProcess(HexHelper.Parse("280438AFDD013600A507368E0301F1021800033F636501000000D88501A1550101DF010100"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("18845601383B423605000000D3EDFD6D9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("188456014862423605000000D3EDFD6D9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("18845601F488423605000000D3EDFD6D9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("1884560168B0423605000000D3EDFD6D9D01000000"), TestConnection);
        scene.Owner.Refresh();

        var packets = scene.Owner.Combat.Events.Where(static e => e.SourceId == 933).ToArray();

        var packet = Assert.Single(packets);
        Assert.Equal(241, packet.Observation.Marker);
        Assert.Equal(1, packet.Observation.MultiHitCount);
        Assert.True((packet.Observation.Modifiers & DamageModifiers.MultiHit) != 0);
    }

    [Fact]
    public void Does_Not_DoubleAttribute_MultiHit_To_Followup_Damage_When_Authoritative_0438_Owner_Already_Exists()
    {
        CombatResourceRegistry.SetGameResources(BuildMultiHitSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var scene = new SceneLiveReadModel();
        using var processor = new PacketStreamProcessor(scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal)));

        processor.AppendAndProcess(HexHelper.Parse("270438D0A10B3400EB3F368E03011003033F636501000000F07DD3470102950795070100"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("210438D0A10B0400EB3FD1890E011503AFD5AD6901000000F07DAB350100"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0C3538D0A10B00EB3F"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0B3538D0A10B0000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("18845601383B4236050000009A56C56E9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("1884560148624236050000009A56C56E9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("18845601F4884236050000009A56C56E9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("1884560168B04236050000009A56C56E9D01000000"), TestConnection);
        scene.Owner.Refresh();

        var parsedPackets = scene.Owner.Combat.Events.Where(static e => e.SourceId == 8171).OrderBy(static e => e.Observation.SkillCode).ToArray();
        Assert.Equal(2, parsedPackets.Length);

        Assert.Equal(17010230, parsedPackets[0].Observation.SkillCode);
        Assert.Equal(2, parsedPackets[0].Observation.MultiHitCount);
        Assert.True((parsedPackets[0].Observation.Modifiers & DamageModifiers.MultiHit) != 0);

        Assert.Equal(17730000, parsedPackets[1].Observation.SkillCode);
        Assert.Equal(0, parsedPackets[1].Observation.MultiHitCount);
        Assert.True((parsedPackets[1].Observation.Modifiers & DamageModifiers.MultiHit) == 0);
    }

    [Fact]
    public void Does_Not_Attribute_MultiHit_From_3538_Sidecar_Without_LayoutTag_Signal()
    {
        CombatResourceRegistry.SetGameResources(BuildMultiHitSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var scene = new SceneLiveReadModel();
        using var processor = new PacketStreamProcessor(scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal)));

        processor.AppendAndProcess(HexHelper.Parse("210438AFDD010400A507D1890E01C403AFD5AD6901000000F07DD6350100"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0C3538AFDD0100A507"), TestConnection);
        scene.Owner.Refresh();

        var packets = scene.Owner.Combat.Events.Where(static e => e.SourceId == 933).ToArray();

        var packet = Assert.Single(packets);
        Assert.Equal(196, packet.Observation.Marker);
        Assert.Equal(0, packet.Observation.MultiHitCount);
        Assert.False((packet.Observation.Modifiers & DamageModifiers.MultiHit) != 0);
    }

    private static SkillCollection BuildMultiHitSkillMap()
    {
        return
        [
            new Skill(13060250, "Ambush", SkillCategory.Assassin, SkillSourceType.PcSkill, "pc", null),
            new Skill(13350000, "Heart Gore", SkillCategory.Assassin, SkillSourceType.PcSkill, "pc", null),
            new Skill(17010230, "Earth's Retribution", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null),
            new Skill(17730000, "Empyrean Lord's Grace", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null)
        ];
    }

    private sealed class BlockingSynchronizedSink : IRuntimeObservationSink, IRuntimeObservationSynchronization
    {
        public Lock Gate { get; } = new();
        public TaskCompletionSource NicknameEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowNickname { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CurrentTarget { get; set; }
        public bool SceneArrivalCalled { get; private set; }

        public int ResolveLifecycleId(int rawInstanceId) => rawInstanceId;
        public int RebindInstanceLifecycle(int rawInstanceId) => rawInstanceId;
        public bool IsKnownEntity(int id) => false;
        public bool HasSummonOwner(int instanceId) => false;
        public bool TryGetNpcRuntimeState(int instanceId, out RuntimeNpcStateSnapshot state) { state = default; return false; }
        public int ResolveNpcObservationSource() => 0;
        public void RememberNpcObservationSource(int instanceId) { }
        public void StageDestinationMap(uint mapId) { }
        public void StageDestinationMap(uint mapId, bool allowSameMapReload) { }
        public void StageDestinationMapInstance(uint instanceId) { }
        public void MarkSceneArrival() => SceneArrivalCalled = true;
        public void MarkSceneTransportBoundary() { }
        public void AppendCombatObservation(int sourceId, int targetId, long timestamp, long frameOrdinal, long batchOrdinal, in CombatObservation observation, ushort opcode = 0, int payloadLength = 0, long captureSequence = 0) { }
        public void CompleteBatch(long batchOrdinal) { }
        public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, long timestamp, long frameOrdinal, long batchOrdinal) { }
        public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, int value, long timestamp, long frameOrdinal, long batchOrdinal) { }
        public void RegisterCompactControl0238(int sourceId, int skillCodeRaw, int marker, long batchOrdinal) { }
        public void RegisterCompactControl0638(int sourceId, int skillCodeRaw, int marker, long timestamp, long frameOrdinal, long batchOrdinal) { }
        public void RegisterPeriodicLink0538(int targetId, int sourceId, int linkId, int sequenceId, int tailRaw, long timestamp, long frameOrdinal, long batchOrdinal) { }
        public void RegisterObservation2A38(int sourceId, int mode, int groupCode, int sequenceId, ushort headValue, uint buffCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal) { }
        public void RegisterObservation2C38(int instanceId, int mode, int sequenceId, int resultCode, int tailSourceId, int tailSkillCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal) { }
        public void AppendNickname(int uid, string nickname, int? originServerId = null, Faction faction = Faction.Unknown)
        {
            NicknameEntered.SetResult();
            AllowNickname.Task.GetAwaiter().GetResult();
        }
        public void AppendNpcCode(int instanceId, int npcCode) { }
        public void AppendNpcName(int npcCode, string name) { }
        public void AppendNpcKind(int instanceId, NpcKind kind) { }
        public void AppendNpcHp(int instanceId, int hp, long observedAtMilliseconds) { }
        public void AppendNpcHp(int instanceId, int hp, int maxHp, long observedAtMilliseconds) { }
        public void SetNpcBattle(int instanceId, bool isActive, long observedAtMilliseconds) { }
        public void ToggleNpcBattle(int instanceId) { }
        public void AppendNpc2136State(int instanceId, uint sequence, uint value0) { }
        public void AppendNpc0140Value(int instanceId, uint value0) { }
        public void AppendNpc0240Value(int instanceId, uint value0) { }
        public void AppendNpc4636State(int instanceId, byte state0, byte state1) { }
        public void AppendSummon(int ownerId, int summonInstanceId) { }
    }
}
