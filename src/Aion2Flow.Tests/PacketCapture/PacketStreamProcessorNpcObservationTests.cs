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
    public void Uses_Recent_4536_Actor_As_Fallback_For_Actorless_Runtime_State_Frames()
    {
        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.FromFixture("state/4536-boss-observed-4370.hex"), TestConnection);
        processor.AppendAndProcess(HexHelper.FromFixture("state/2136-boss-scene-200003.hex"), TestConnection);
        processor.AppendAndProcess(HexHelper.FromFixture("state/0140-boss-tail-430d03.hex"), TestConnection);
        processor.AppendAndProcess(HexHelper.FromFixture("state/0240-boss-tail-430d03.hex"), TestConnection);

        Assert.Equal((uint)6, store.Npc2136SequenceByInstance[4370]);
        Assert.Equal((uint)200003, store.Npc2136ValueByInstance[4370]);
        Assert.Equal((uint)200003, store.Npc0140ValueByInstance[4370]);
        Assert.Equal((uint)200003, store.Npc0240ValueByInstance[4370]);
    }

    [Fact]
    public void Skips_Mode48_Periodic_Link_Record_From_Combat_Metrics()
    {
        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        var parsed = processor.AppendAndProcess(HexHelper.FromFixture("combat/0538-mode48-link.hex"), TestConnection);

        Assert.True(parsed);
        Assert.Empty(store.CombatPacketsByTarget);
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
        Assert.Equal(2007, store.LocalActorId);
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
    public void Attributes_SameMarker_Followup_Damage_To_Primary_Hit_As_MultiHit()
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
        Assert.Equal(1, parsedPackets[0].MultiHitCount);
        Assert.True((parsedPackets[0].Modifiers & DamageModifiers.MultiHit) != 0);
        Assert.Equal(0, parsedPackets[1].HitContribution);
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
        store.RememberLocalActor(933);
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
        store.RememberLocalActor(8171);
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
    public void Attributes_3538_DualActor_Sidecar_To_Preceding_Damage_Packet_Without_LocalActor_As_Single_MultiHit()
    {
        CombatMetricsEngine.SetGameResources(BuildMultiHitSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("210438AFDD010400A507D1890E01C403AFD5AD6901000000F07DD6350100"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0C3538AFDD0100A507"), TestConnection);

        Assert.True(store.CombatPacketsBySource.TryGetValue(933, out var packets));

        var packet = Assert.Single(packets);
        Assert.Equal(196, packet.Marker);
        Assert.Equal(1, packet.MultiHitCount);
        Assert.False(packet.HasAuthoritativeMultiHitCount);
        Assert.True((packet.Modifiers & DamageModifiers.MultiHit) != 0);
    }

    [Fact]
    public void Does_Not_Synthesize_Evade_From_Standalone_Compact0638_Without_CompactValue()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("290438A395014610A0B907F4C81000590244005B7F8E0601000000904E0101"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0F0638A0B907F4C810005900"), TestConnection);

        Assert.True(store.CombatPacketsBySource.TryGetValue(122016, out var packets));

        var parsedPackets = packets.ToArray();
        var landed = Assert.Single(parsedPackets);
        Assert.Equal(89, landed.Marker);
        Assert.True((landed.Modifiers & DamageModifiers.Evade) == 0);
    }

    [Fact]
    public void Synthesizes_Evade_From_Generic_Type1_CompactValue_When_Matching_0638_Arrives()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("1F0438A5070000ADCB01368F1200060123F13F0701000000904E0100"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0F0638ADCB01368F12000600"), TestConnection);

        Assert.True(store.CombatPacketsBySource.TryGetValue(26029, out var packets));

        var evade = Assert.Single(packets);
        Assert.Equal(933, evade.TargetId);
        Assert.Equal(6, evade.Marker);
        Assert.Equal(0, evade.Damage);
        Assert.Equal(0, evade.HitContribution);
        Assert.Equal(1, evade.AttemptContribution);
        Assert.True((evade.Modifiers & DamageModifiers.Evade) != 0);
    }

    [Fact]
    public void Synthesizes_Evade_From_Layout2_Type1_CompactOutcome_When_Matching_0638_Arrives()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("210438A5070200AFDD01368F12000601200023F13F0701000000904E0100"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0F0638AFDD01368F12000600"), TestConnection);

        Assert.True(store.CombatPacketsBySource.TryGetValue(28335, out var packets));

        var evade = Assert.Single(packets);
        Assert.Equal(933, evade.TargetId);
        Assert.Equal(6, evade.Marker);
        Assert.Equal(0, evade.Damage);
        Assert.Equal(0, evade.HitContribution);
        Assert.Equal(1, evade.AttemptContribution);
        Assert.True((evade.Modifiers & DamageModifiers.Evade) != 0);
    }

    [Fact]
    public void Synthesizes_Evade_From_Generic_Type1_CompactValue_Even_When_Same_Marker_Landed_Damage_Exists()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("280438A5074610EB99015E8F120004024200C300400701000000904E0101EF3E2F0D010001"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("1F0438A5070000EB99015E8F12000401C300400701000000904E0100"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0F0638EB99015E8F12000400"), TestConnection);

        Assert.True(store.CombatPacketsBySource.TryGetValue(19691, out var packets));

        var parsedPackets = packets.OrderBy(static x => x.Timestamp).ToArray();
        Assert.Equal(2, parsedPackets.Length);
        Assert.Equal(4, parsedPackets[0].Marker);
        Assert.Equal(1, parsedPackets[0].Damage);
        Assert.Equal(4, parsedPackets[1].Marker);
        Assert.Equal(0, parsedPackets[1].Damage);
        Assert.Equal(1, parsedPackets[1].AttemptContribution);
        Assert.True((parsedPackets[1].Modifiers & DamageModifiers.Evade) != 0);
    }

    [Fact]
    public void Synthesizes_Evade_From_Layout2_Type1_CompactOutcome_Even_When_Same_Marker_Landed_Damage_Exists()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("280438A5074610EB99015E8F120009020400C300400701000000904E0101EF3E2F0D010001"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("210438A5070200EB99015E8F120009014000C300400702000000904E0200"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0F0638EB99015E8F12000900"), TestConnection);

        Assert.True(store.CombatPacketsBySource.TryGetValue(19691, out var packets));

        var parsedPackets = packets.OrderBy(static x => x.Timestamp).ToArray();
        Assert.Equal(2, parsedPackets.Length);
        Assert.Equal(9, parsedPackets[0].Marker);
        Assert.Equal(1, parsedPackets[0].Damage);
        Assert.Equal(9, parsedPackets[1].Marker);
        Assert.Equal(0, parsedPackets[1].Damage);
        Assert.Equal(1, parsedPackets[1].AttemptContribution);
        Assert.True((parsedPackets[1].Modifiers & DamageModifiers.Evade) != 0);
    }

    [Fact]
    public void Flushes_Evade_From_Generic_Type1_CompactValue_When_Matching_0638_Is_Missing()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        store.RegisterCompactValue0438(933, 26029, 1216310, 6, 1, 1_000);
        store.FlushPendingOutcomeSidecars(5_000);

        Assert.True(store.CombatPacketsBySource.TryGetValue(26029, out var packets));

        var evade = Assert.Single(packets);
        Assert.Equal(933, evade.TargetId);
        Assert.Equal(6, evade.Marker);
        Assert.Equal(0, evade.Damage);
        Assert.True((evade.Modifiers & DamageModifiers.Evade) != 0);
    }

    [Fact]
    public void Does_Not_Synthesize_Evade_From_Flag2_Compact0638_Without_Entity_Relation()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        store.RememberLocalActor(933);
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("0F0638ADCB01368F12001302"), TestConnection);

        Assert.False(store.CombatPacketsBySource.TryGetValue(26029, out _));
    }

    [Fact]
    public void Does_Not_Synthesize_Evade_From_Flag2_Compact0638_When_DirectDamage_Already_Exists_For_Marker()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        store.RememberLocalActor(933);
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("280438A5074610AFDD01368F12000F02420023F13F0701000000904E0101EF3E2F0D010001"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0F0638AFDD01368F12000F02"), TestConnection);

        Assert.True(store.CombatPacketsBySource.TryGetValue(28335, out var packets));

        var packet = Assert.Single(packets);
        Assert.Equal(15, packet.Marker);
        Assert.Equal(1, packet.Damage);
        Assert.True((packet.Modifiers & DamageModifiers.Evade) == 0);
    }

    [Fact]
    public void Does_Not_Synthesize_Invincible_From_Charged_Dodge_Mode1_Result7()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        store.RegisterCompactControl0638(9990, 17000101, 9, 1_000);
        store.RegisterObservation2C38(9990, 1, 65, 7, 1_050, 2);

        Assert.False(store.CombatPacketsByTarget.TryGetValue(9990, out var packets) &&
                     packets.Any(packet => packet.SkillCode == SyntheticCombatSkillCodes.UnresolvedInvincible));
    }

    [Fact]
    public void Does_Not_Synthesize_Invincible_From_CurrentTarget_Alone_Without_Authoritative_Avoided_Hit_Outcome()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore
        {
            CurrentTarget = 9990
        };
        store.RegisterCompactControl0638(2007, 12000101, 8, 1_000);
        store.RegisterObservation2C38(2007, 73, 7, 1_050);

        Assert.False(store.CombatPacketsBySource.TryGetValue(9990, out var packets) &&
                     packets.Any(packet => packet.SkillCode == SyntheticCombatSkillCodes.UnresolvedInvincible));
    }

    [Fact]
    public void Synthesizes_Invincible_From_Charged_Dodge_Mode2_Result7()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        store.RegisterCompactControl0638(8171, 17000101, 72, 1_100);
        store.RegisterObservation2C38(8171, 2, 1652, 7, 1_150, 2);

        Assert.True(store.CombatPacketsByTarget.TryGetValue(8171, out var packets));

        var outcome = Assert.Single(packets, packet => packet.SkillCode == SyntheticCombatSkillCodes.UnresolvedInvincible);
        Assert.Equal(0, outcome.SourceId);
        Assert.Equal(8171, outcome.TargetId);
        Assert.Equal(72, outcome.Marker);
        Assert.Equal(0, outcome.Damage);
        Assert.Equal(0, outcome.HitContribution);
        Assert.Equal(1, outcome.AttemptContribution);
        Assert.True((outcome.Modifiers & DamageModifiers.Invincible) != 0);
    }

    [Fact]
    public void Does_Not_Synthesize_Invincible_From_Charged_Dodge_Mode1_Result7_After_Compact_Control()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        store.RegisterCompactControl0638(8171, 17000101, 114, 10_150);
        store.RegisterObservation2C38(8171, 1, 2477, 7, 10_300, 2);

        Assert.False(store.CombatPacketsByTarget.TryGetValue(8171, out var packets) &&
                     packets.Any(packet => packet.SkillCode == SyntheticCombatSkillCodes.UnresolvedInvincible));
    }

    [Fact]
    public void Does_Not_Synthesize_Invincible_From_Current_Logs_False_Charged_Dodge_Lane_Without_Incoming_Evade()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("0E0638F922A5660301DE00"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0D2C38F92201009A1407"), TestConnection);

        Assert.False(store.CombatPacketsByTarget.TryGetValue(4473, out var packets) &&
                     packets.Any(packet => packet.SkillCode == SyntheticCombatSkillCodes.UnresolvedInvincible));
    }

    [Fact]
    public void Synthesizes_Invincible_From_Current_Logs_Real_Charged_Dodge_Lane_With_Incoming_Avoided_Hit_Outcome()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("0E0638F922A56603011600"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("112C38F9220200DF140700E01407"), TestConnection);

        Assert.True(store.CombatPacketsByTarget.TryGetValue(4473, out var packets));

        var outcome = Assert.Single(packets, packet => packet.SkillCode == SyntheticCombatSkillCodes.UnresolvedInvincible);
        Assert.Equal(0, outcome.SourceId);
        Assert.Equal(4473, outcome.TargetId);
        Assert.Equal(22, outcome.Marker);
        Assert.Equal(0, outcome.Damage);
        Assert.Equal(0, outcome.HitContribution);
        Assert.Equal(1, outcome.AttemptContribution);
        Assert.True((outcome.Modifiers & DamageModifiers.Invincible) != 0);
    }

    [Fact]
    public void Does_Not_Synthesize_Invincible_From_Base_Dodge_SelfEcho_That_Only_Resembles_Shift_Iframe()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();

        store.RegisterCompactValue0438(5957, 5957, 17000100, 236, 2, 1_100);
        store.RegisterCompactControl0638(5957, 17000100, 236, 1_140);
        store.RegisterObservation2C38(5957, 3608, 7, 1_150);

        Assert.False(store.CombatPacketsByTarget.TryGetValue(5957, out var packets) &&
                     packets.Any(packet => packet.SkillCode == SyntheticCombatSkillCodes.UnresolvedInvincible));
    }

    [Fact]
    public void Does_Not_Synthesize_Invincible_From_Group17_2A38_Without_Dodge_Signal()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("302A38C52E01119009C1EA2101FFFFFFFFFFFFFFFF8075D52ABB030000C52E010094BF1847015A4447B86C1F47"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0D2C38C52E0100900907"), TestConnection);

        Assert.False(store.CombatPacketsByTarget.TryGetValue(5957, out var packets) &&
                     packets.Any(packet => packet.SkillCode == SyntheticCombatSkillCodes.UnresolvedInvincible));
    }

    [Fact]
    public void Does_Not_Synthesize_Invincible_From_Standalone_2C38_Result7()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("0D2C38C52E0100900907"), TestConnection);

        Assert.False(store.CombatPacketsByTarget.TryGetValue(5957, out _));
        Assert.Empty(store.CombatPacketsBySource);
    }

    [Fact]
    public void Does_Not_Synthesize_Invincible_From_Uncharged_Dodge_Result7_Without_Charged_Variant()
    {
        CombatMetricsEngine.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var processor = new PacketStreamProcessor(store);

        processor.AppendAndProcess(HexHelper.Parse("0E0638F922A46603011000"), TestConnection);
        processor.AppendAndProcess(HexHelper.Parse("0D2C38F9220100D51407"), TestConnection);

        Assert.False(store.CombatPacketsByTarget.TryGetValue(4473, out var packets) &&
                     packets.Any(packet => packet.SkillCode == SyntheticCombatSkillCodes.UnresolvedInvincible));
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
