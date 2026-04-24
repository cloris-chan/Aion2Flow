using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.PacketCapture.Streams;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.PacketCapture;

public sealed class PacketStreamProcessorNpcObservationTests
{
    private static readonly TcpConnection TestConnection = new(0x0100007f, 0x0100007f, 49820, 57080);

    [Fact]
    public void Uses_Recent_4536_Source_As_Fallback_For_SourceLess_Runtime_State_Frames()
    {
        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.FromFixture("state/4536-boss-observed-4370.hex"), TestConnection);
        processor.AppendAndProcess(HexHelper.FromFixture("state/2136-boss-scene-200003.hex"), TestConnection);
        processor.AppendAndProcess(HexHelper.FromFixture("state/0140-boss-tail-430d03.hex"), TestConnection);
        processor.AppendAndProcess(HexHelper.FromFixture("state/0240-boss-tail-430d03.hex"), TestConnection);

        Assert.True(store.TryGetNpcRuntimeState(4370, out var state));
        Assert.Equal((uint)6, state.Sequence2136);
        Assert.Equal((uint)200003, state.Value2136);
        Assert.Equal((uint)200003, state.Value0140);
        Assert.Equal((uint)200003, state.Value0240);
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
        CombatMetricsEngine.SetGameResources([], catalog);

        var store = new CombatMetricsStore
        {
            CurrentTarget = npcInstanceId
        };
        store.AppendNpcCode(npcInstanceId, npcCode);

        var processor = new PacketStreamProcessor(store);
        var parsed = processor.AppendAndProcess(HexHelper.FromFixture("state/2136-boss-scene-200003.hex"), TestConnection);

        Assert.True(parsed);
        Assert.True(store.TryGetNpcRuntimeState(npcInstanceId, out var state));
        Assert.Equal(npcCode, state.NpcCode);
        Assert.Equal((uint)sceneStateValue, state.Value2136);
    }

    [Fact]
    public void Synthesizes_Invincible_From_Mode48_Periodic_Link_Record()
    {
        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        var parsed = processor.AppendAndProcess(HexHelper.FromFixture("combat/0538-mode48-link.hex"), TestConnection);

        Assert.True(parsed);
        Assert.True(store.CombatPacketsByTarget.TryGetValue(16047, out var packets));

        var invincible = Assert.Single(packets);
        Assert.Equal(29240, invincible.SourceId);
        Assert.Equal(16047, invincible.TargetId);
        Assert.Equal(608, invincible.Marker);
        Assert.Equal(1237540, invincible.OriginalSkillCode);
        Assert.Equal(1230000, invincible.SkillCode);
        Assert.True((invincible.Modifiers & DamageModifiers.Invincible) != 0);
    }

    [Fact]
    public void Keeps_NonLink_0538_Periodic_Value_In_Combat_Metrics()
    {
        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        var parsed = processor.AppendAndProcess(HexHelper.FromFixture("combat/0538-dot.hex"), TestConnection);

        Assert.True(parsed);
        Assert.True(store.CombatPacketsByTarget.TryGetValue(17640, out var packets));
        Assert.Single(packets);
    }

    [Fact]
    public void Scans_Embedded_3336_OwnNickname_Record_From_Larger_Packet()
    {
        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);
        var packet = Convert.FromHexString("1EAA3336D70F5FB17904070750657269676565EF0306000000012D000000");

        var parsed = processor.AppendAndProcess(packet, TestConnection);

        Assert.True(parsed);
        Assert.True(store.Nicknames.TryGetValue(2007, out var nickname));
        Assert.Equal("Perigee", nickname);
    }

    [Fact]
    public void Parses_Compact_0438_Recovery_Frame_Without_Adding_Combat_Metrics()
    {
        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        var parsed = processor.AppendAndProcess(HexHelper.FromFixture("combat/0438-compact-other.hex"), TestConnection);

        Assert.True(parsed);
        Assert.Empty(store.CombatPacketsByTarget);
    }

    [Theory]
    [InlineData("state/0238-compact-control.hex")]
    [InlineData("state/0638-compact-control.hex")]
    public void Parses_Compact_Control_Frames_Without_Adding_Combat_Metrics(string path)
    {
        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        var parsed = processor.AppendAndProcess(HexHelper.FromFixture(path), TestConnection);

        Assert.True(parsed);
        Assert.Empty(store.CombatPacketsByTarget);
    }

    [Fact]
    public void Attributes_Heart_Gore_Sidecar_To_Preceding_Damage_Packet_As_MultiHit()
    {
        CombatMetricsEngine.SetGameResources(BuildMultiHitSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("2B043892D5013604EB449A48C700040311005C02D84D01000000FC8901E8AA090101C1180100AC3E"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0E0638EB4478B4CB000500"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("1F0438EB440000EB4478B4CB000502EB7E924F01000000FC89010100"), TestConnection);

        Assert.True(store.CombatPacketsBySource.TryGetValue(8811, out var packets));

        var packet = Assert.Single(packets);
        Assert.Equal(4, packet.Marker);
        Assert.Equal(1, packet.HitContribution);
        Assert.Equal(1, packet.MultiHitCount);
        Assert.True((packet.Modifiers & DamageModifiers.MultiHit) != 0);
    }

    [Fact]
    public void Does_Not_Merge_SameMarker_Followup_Without_Authoritative_MultiHit_Signal()
    {
        CombatMetricsEngine.SetGameResources(BuildMultiHitSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        store.AppendCombatPacket(new Cloris.Aion2Flow.Combat.Metrics.ParsedCombatPacket
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
        store.AppendCombatPacket(new Cloris.Aion2Flow.Combat.Metrics.ParsedCombatPacket
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

        Assert.True(store.CombatPacketsBySource.TryGetValue(4725, out var packets));

        var parsedPackets = packets.ToArray();
        Assert.Equal(2, parsedPackets.Length);
        Assert.Equal(parsedPackets[0].Marker, parsedPackets[1].Marker);
        Assert.Equal(1, parsedPackets[0].HitContribution);
        Assert.Equal(0, parsedPackets[0].MultiHitCount);
        Assert.True((parsedPackets[0].Modifiers & DamageModifiers.MultiHit) == 0);
        Assert.Equal(1, parsedPackets[1].HitContribution);
        Assert.Equal(0, parsedPackets[1].MultiHitCount);
    }

    [Fact]
    public void Does_Not_Attribute_Wrapped_8456_3642_Sidecars_Without_Explicit_MultiHit_Owner()
    {
        CombatMetricsEngine.SetGameResources(BuildMultiHitSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("220438ADCB010400A507D1890E014402AFD5AD6901000000D88501FB1D0100"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("18845601383B4236040000000D69F36D9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("1884560148624236040000000D69F36D9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("18845601F4884236040000000D69F36D9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("1884560168B04236040000000D69F36D9D01000000"), TestConnection);

        Assert.True(store.CombatPacketsBySource.TryGetValue(933, out var packets));

        var packet = Assert.Single(packets);
        Assert.Equal(68, packet.Marker);
        Assert.Equal(0, packet.MultiHitCount);
        Assert.True((packet.Modifiers & DamageModifiers.MultiHit) == 0);
    }

    [Fact]
    public void Uses_TailEncoded_MultiHit_Count_Without_DoubleCounting_Wrapped_8456_Sidecars()
    {
        CombatMetricsEngine.SetGameResources(BuildMultiHitSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("280438AFDD013600A507368E0301F1021800033F636501000000D88501A1550101DF010100"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("18845601383B423605000000D3EDFD6D9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("188456014862423605000000D3EDFD6D9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("18845601F488423605000000D3EDFD6D9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("1884560168B0423605000000D3EDFD6D9D01000000"), TestConnection);

        Assert.True(store.CombatPacketsBySource.TryGetValue(933, out var packets));

        var packet = Assert.Single(packets);
        Assert.Equal(241, packet.Marker);
        Assert.Equal(1, packet.MultiHitCount);
        Assert.True(packet.HasAuthoritativeMultiHitCount);
        Assert.True((packet.Modifiers & DamageModifiers.MultiHit) != 0);
    }

    [Fact]
    public void Does_Not_DoubleAttribute_MultiHit_To_Followup_Damage_When_Authoritative_0438_Owner_Already_Exists()
    {
        CombatMetricsEngine.SetGameResources(BuildMultiHitSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("270438D0A10B3400EB3F368E03011003033F636501000000F07DD3470102950795070100"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("210438D0A10B0400EB3FD1890E011503AFD5AD6901000000F07DAB350100"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0C3538D0A10B00EB3F"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0B3538D0A10B0000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("18845601383B4236050000009A56C56E9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("1884560148624236050000009A56C56E9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("18845601F4884236050000009A56C56E9D01000000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("1884560168B04236050000009A56C56E9D01000000"), TestConnection);

        Assert.True(store.CombatPacketsBySource.TryGetValue(8171, out var packets));

        var parsedPackets = packets.OrderBy(packet => packet.SkillCode).ToArray();
        Assert.Equal(2, parsedPackets.Length);

        Assert.Equal(17010230, parsedPackets[0].SkillCode);
        Assert.Equal(2, parsedPackets[0].MultiHitCount);
        Assert.True(parsedPackets[0].HasAuthoritativeMultiHitCount);
        Assert.True((parsedPackets[0].Modifiers & DamageModifiers.MultiHit) != 0);

        Assert.Equal(17730000, parsedPackets[1].SkillCode);
        Assert.Equal(0, parsedPackets[1].MultiHitCount);
        Assert.False(parsedPackets[1].HasAuthoritativeMultiHitCount);
        Assert.True((parsedPackets[1].Modifiers & DamageModifiers.MultiHit) == 0);
    }

    [Fact]
    public void Does_Not_Attribute_MultiHit_From_3538_Sidecar_Without_LayoutTag_Signal()
    {
        CombatMetricsEngine.SetGameResources(BuildMultiHitSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("210438AFDD010400A507D1890E01C403AFD5AD6901000000F07DD6350100"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0C3538AFDD0100A507"), TestConnection);

        Assert.True(store.CombatPacketsBySource.TryGetValue(933, out var packets));

        var packet = Assert.Single(packets);
        Assert.Equal(196, packet.Marker);
        Assert.Equal(0, packet.MultiHitCount);
        Assert.False(packet.HasAuthoritativeMultiHitCount);
        Assert.False((packet.Modifiers & DamageModifiers.MultiHit) != 0);
    }

    [Fact]
    public void Flushes_Pending_Compact_Type1_Avoid_As_Evade_At_Batch_End()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        store.RegisterCompactValue0438(933, 26029, 1216310, 6, 0, 1, 1_000, 1, 100);
        store.FlushPendingOutcomeSidecars();

        Assert.True(store.CombatPacketsBySource.TryGetValue(26029, out var packets));

        var evade = Assert.Single(packets);
        Assert.Equal(933, evade.TargetId);
        Assert.Equal(6, evade.Marker);
        Assert.Equal(100, evade.BatchOrdinal);
        Assert.Equal(0, evade.Damage);
        Assert.Equal(0, evade.HitContribution);
        Assert.Equal(1, evade.AttemptContribution);
        Assert.True((evade.Modifiers & DamageModifiers.Evade) != 0);
    }

    [Fact]
    public void Keeps_Pending_Compact_Type1_Avoid_As_Evade_When_Group17_Arrives_Without_PeriodicLink()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        store.RegisterCompactValue0438(933, 26029, 1216310, 6, 0, 1, 1_000, 1, 100);
        store.RegisterObservation2A38(933, 1, 17, 44, 0x1388, 0x1ab57000, 1_001, 2, 100);
        store.FlushPendingOutcomeSidecars();

        Assert.True(store.CombatPacketsBySource.TryGetValue(26029, out var packets));

        var evade = Assert.Single(packets);
        Assert.Equal(933, evade.TargetId);
        Assert.Equal(6, evade.Marker);
        Assert.Equal(100, evade.BatchOrdinal);
        Assert.Equal(1216310, evade.SkillCode);
        Assert.Equal(0, evade.Damage);
        Assert.Equal(0, evade.HitContribution);
        Assert.Equal(1, evade.AttemptContribution);
        Assert.True((evade.Modifiers & DamageModifiers.Evade) != 0);
        Assert.True((evade.Modifiers & DamageModifiers.Invincible) == 0);
    }

    [Fact]
    public void Keeps_Direct_Blocked_Damage_As_Hit_When_Group17_Arrives_Without_PeriodicLink()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = 26029,
            TargetId = 933,
            OriginalSkillCode = 1216310,
            SkillCode = 1216310,
            Marker = 6,
            Damage = 1,
            Timestamp = 1_000,
            FrameOrdinal = 1,
            BatchOrdinal = 100,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });
        store.RegisterObservation2A38(933, 1, 17, 44, 0x1388, 0x1ab57000, 1_001, 2, 100);
        store.FlushPendingOutcomeSidecars();

        Assert.True(store.CombatPacketsByTarget.TryGetValue(933, out var packets));

        var packet = Assert.Single(packets);
        Assert.Equal(26029, packet.SourceId);
        Assert.Equal(933, packet.TargetId);
        Assert.Equal(6, packet.Marker);
        Assert.Equal(100, packet.BatchOrdinal);
        Assert.Equal(1, packet.Damage);
        Assert.Equal(1, packet.HitContribution);
        Assert.Equal(1, packet.AttemptContribution);
        Assert.True((packet.Modifiers & DamageModifiers.Invincible) == 0);
        Assert.True((packet.Modifiers & DamageModifiers.Evade) == 0);
    }

    [Fact]
    public void Prefers_Evade_Over_Invincible_When_Same_Batch_Dodge_Arrives()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = 26029,
            TargetId = 933,
            OriginalSkillCode = 1216310,
            SkillCode = 1216310,
            Marker = 8,
            Damage = 1,
            Timestamp = 1_000,
            FrameOrdinal = 1,
            BatchOrdinal = 100,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });
        store.RegisterCompactControl0238(933, 17000100, 72, 100);
        store.FlushPendingOutcomeSidecars();

        Assert.True(store.CombatPacketsByTarget.TryGetValue(933, out var packets));

        var evade = Assert.Single(packets);
        Assert.Equal(26029, evade.SourceId);
        Assert.Equal(933, evade.TargetId);
        Assert.Equal(8, evade.Marker);
        Assert.Equal(100, evade.BatchOrdinal);
        Assert.Equal(0, evade.Damage);
        Assert.Equal(0, evade.HitContribution);
        Assert.Equal(1, evade.AttemptContribution);
        Assert.True((evade.Modifiers & DamageModifiers.Evade) != 0);
        Assert.True((evade.Modifiers & DamageModifiers.Invincible) == 0);
    }

    [Fact]
    public void Synthesizes_Invincible_Alongside_Compact_Evade_When_PeriodicLink_Arrives()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        store.RegisterCompactValue0438(933, 26029, 1216310, 6, 0, 1, 1_000, 1, 100);
        store.FlushPendingOutcomeSidecars();
        store.RegisterPeriodicLink0538(933, 933, 26029, 45, 1216310, 1_020, 4, 102);

        Assert.True(store.CombatPacketsBySource.TryGetValue(26029, out var packets));

        var parsedPackets = packets.OrderBy(packet => packet.Modifiers).ThenBy(packet => packet.Marker).ToArray();
        Assert.Equal(2, parsedPackets.Length);

        var evade = Assert.Single(parsedPackets, static packet => (packet.Modifiers & DamageModifiers.Evade) != 0);
        Assert.Equal(6, evade.Marker);
        Assert.Equal(1216310, evade.SkillCode);

        var invincible = Assert.Single(parsedPackets, static packet => (packet.Modifiers & DamageModifiers.Invincible) != 0);
        Assert.Equal(26029, invincible.SourceId);
        Assert.Equal(933, invincible.TargetId);
        Assert.Equal(1216310, invincible.SkillCode);
        Assert.Equal(45, invincible.Marker);
        Assert.Equal(0, invincible.Damage);
        Assert.Equal(0, invincible.HitContribution);
        Assert.Equal(1, invincible.AttemptContribution);
        Assert.True((invincible.Modifiers & DamageModifiers.Invincible) != 0);
    }

    [Fact]
    public void Leaves_DefensivePerfect_Blocked_Damage_Unchanged_Without_Explicit_Avoided_Evidence()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        store.AppendCombatPacket(new ParsedCombatPacket
        {
            SourceId = 26029,
            TargetId = 933,
            OriginalSkillCode = 1216310,
            SkillCode = 1216310,
            Marker = 7,
            Damage = 1,
            Modifiers = DamageModifiers.Perfect | DamageModifiers.Block,
            Timestamp = 2_000,
            FrameOrdinal = 3,
            BatchOrdinal = 101,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        });
        store.FlushPendingOutcomeSidecars();

        Assert.True(store.CombatPacketsByTarget.TryGetValue(933, out var packets));

        var packet = Assert.Single(packets);
        Assert.Equal(26029, packet.SourceId);
        Assert.Equal(7, packet.Marker);
        Assert.Equal(1, packet.Damage);
        Assert.Equal(1, packet.HitContribution);
        Assert.Equal(1, packet.AttemptContribution);
        Assert.True((packet.Modifiers & DamageModifiers.Evade) == 0);
        Assert.True((packet.Modifiers & DamageModifiers.Invincible) == 0);
    }

    [Fact]
    public void Does_Not_Synthesize_Invincible_From_Group17_Without_Avoided_Hit()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        store.RegisterObservation2A38(933, 1, 17, 44, 0x1388, 0x1ab57000, 1_001, 2, 100);
        store.FlushPendingOutcomeSidecars();

        Assert.False(store.CombatPacketsByTarget.TryGetValue(933, out _));
        Assert.Empty(store.CombatPacketsBySource);
    }

    [Fact]
    public void Does_Not_Synthesize_Invincible_From_Standalone_2C38_Result7()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        store.RegisterObservation2C38(5957, 1, 3608, 7, 1_150, 2, 100);

        Assert.False(store.CombatPacketsByTarget.TryGetValue(5957, out _));
        Assert.Empty(store.CombatPacketsBySource);
    }

    private static SkillCollection BuildMultiHitSkillMap()
    {
        return
        [
            new Skill(13060250, "Ambush", SkillCategory.Assassin, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage, null),
            new Skill(13350000, "Heart Gore", SkillCategory.Assassin, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage, null),
            new Skill(17010230, "Earth's Retribution", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage | SkillSemantics.Support, null),
            new Skill(17730000, "Empyrean Lord's Grace", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", SkillKind.Damage, SkillSemantics.Damage | SkillSemantics.Support, null)
        ];
    }

    private static SkillCollection BuildCompactEvadeSkillMap()
    {
        return
        [
            new Skill(1216310, "Attack", SkillCategory.Npc, SkillSourceType.Unknown, "npc", SkillKind.Damage, SkillSemantics.Damage, null),
            new Skill(1216350, "Vine Swipe", SkillCategory.Npc, SkillSourceType.Unknown, "npc", SkillKind.Damage, SkillSemantics.Damage, null),
            new Skill(1100020, "Croka Light Beam", SkillCategory.Npc, SkillSourceType.Unknown, "npc", SkillKind.Damage, SkillSemantics.Damage, null),
            new Skill(12000100, "Dodge", SkillCategory.Templar, SkillSourceType.PcSkill, "pc", SkillKind.Support, SkillSemantics.Support, null),
            new Skill(17000100, "Dodge", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", SkillKind.Support, SkillSemantics.Support, null)
        ];
    }
}
