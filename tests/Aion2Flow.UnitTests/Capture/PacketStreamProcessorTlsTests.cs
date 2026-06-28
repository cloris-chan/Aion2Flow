using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketStreamProcessorTlsTests
{
    [Fact]
    public void AppendAndProcess_SkipsCompleteTlsRecord()
    {
        var sink = new RecordingRuntimeObservationSink();
        using var processor = new PacketStreamProcessor(sink);

        var parsed = processor.AppendAndProcess(BuildTlsRecordWithEmbedded0438Bytes(), default, 1_000);

        Assert.False(parsed);
        Assert.Equal(0, sink.CombatObservationCount);
    }

    [Fact]
    public void AppendAndProcess_ContinuesAfterTlsRecordInSamePayload()
    {
        var sink = new RecordingRuntimeObservationSink();
        using var processor = new PacketStreamProcessor(sink);
        var payload = BuildTlsRecordWithEmbedded0438Bytes().Concat(Build0438Frame()).ToArray();

        var parsed = processor.AppendAndProcess(payload, default, 1_000);

        Assert.True(parsed);
        Assert.Equal(1, sink.CombatObservationCount);
        Assert.Equal(16_140_000, sink.LastSkillCode);
    }

    [Fact]
    public void AppendAndProcess_ContinuesAfterSplitTlsRecord()
    {
        var sink = new RecordingRuntimeObservationSink();
        using var processor = new PacketStreamProcessor(sink);
        var tls = BuildTlsRecordWithEmbedded0438Bytes();
        var rest = tls[3..].Concat(Build0438Frame()).ToArray();

        Assert.False(processor.AppendAndProcess(tls[..3], default, 1_000));
        var parsed = processor.AppendAndProcess(rest, default, 1_050);

        Assert.True(parsed);
        Assert.Equal(1, sink.CombatObservationCount);
        Assert.Equal(16_140_000, sink.LastSkillCode);
    }

    private static byte[] BuildTlsRecordWithEmbedded0438Bytes()
    {
        var payload = new List<byte>
        {
            0x9D,
            0x80,
            0x50,
            0x04,
            0x38
        };
        Append0438Payload(payload, targetId: 200, sourceId: 100, skillCode: 815_805_088);
        payload.AddRange([
            0x61, 0x6D, 0x61, 0x7A, 0x6F, 0x6E, 0x74, 0x72,
            0x75, 0x73, 0x74, 0x2E, 0x63, 0x6F, 0x6D, 0x2F,
            0x72, 0x6F, 0x6F, 0x74, 0x63, 0x61, 0x31, 0x2E,
            0x63, 0x65, 0x72
        ]);

        var record = new List<byte>(payload.Count + 5)
        {
            0x17,
            0x03,
            0x03,
            (byte)(payload.Count >> 8),
            (byte)payload.Count
        };
        record.AddRange(payload);
        return [.. record];
    }

    private static byte[] Build0438Frame()
    {
        var payload = new List<byte>
        {
            0x04,
            0x38
        };
        Append0438Payload(payload, targetId: 200, sourceId: 100, skillCode: 16_140_000);

        var frame = new List<byte>(payload.Count + 1);
        AppendVarInt(frame, payload.Count + 4);
        frame.AddRange(payload);
        return [.. frame];
    }

    private static void Append0438Payload(List<byte> buffer, int targetId, int sourceId, int skillCode)
    {
        AppendVarInt(buffer, targetId);
        AppendVarInt(buffer, 4);
        AppendVarInt(buffer, 0);
        AppendVarInt(buffer, sourceId);
        AppendUInt32Le(buffer, skillCode);
        buffer.Add(1);
        AppendVarInt(buffer, 2);
        buffer.AddRange([0x57, 0, 0, 0, 0, 0, 0, 0]);
        AppendVarInt(buffer, 1);
        AppendVarInt(buffer, 1_395);
        AppendVarInt(buffer, 1);
    }

    private static void AppendVarInt(List<byte> buffer, int value)
    {
        var remaining = unchecked((uint)value);
        while (remaining > 0x7F)
        {
            buffer.Add((byte)((remaining & 0x7F) | 0x80));
            remaining >>= 7;
        }

        buffer.Add((byte)remaining);
    }

    private static void AppendUInt32Le(List<byte> buffer, int value)
    {
        var raw = unchecked((uint)value);
        buffer.Add((byte)raw);
        buffer.Add((byte)(raw >> 8));
        buffer.Add((byte)(raw >> 16));
        buffer.Add((byte)(raw >> 24));
    }

    private sealed class RecordingRuntimeObservationSink : IRuntimeObservationSink
    {
        public int CurrentTarget { get; set; }
        public int CombatObservationCount { get; private set; }
        public int LastSkillCode { get; private set; }

        public int ResolveLifecycleId(int rawInstanceId) => rawInstanceId;
        public int RebindInstanceLifecycle(int rawInstanceId) => rawInstanceId;
        public bool IsKnownEntity(int id) => id > 0;
        public bool HasSummonOwner(int instanceId) => false;
        public bool TryGetNpcRuntimeState(int instanceId, out RuntimeNpcStateSnapshot state)
        {
            state = default;
            return false;
        }

        public void SeedNpcRuntimeState(in PacketObservationSource packet, int instanceId, in RuntimeNpcStateSnapshot state)
        {
        }

        public int ResolveNpcObservationSource() => 0;
        public void RememberNpcObservationSource(int instanceId)
        {
        }

        public void StageDestinationMap(in PacketObservationSource packet, uint mapId)
        {
        }

        public void StageDestinationMap(in PacketObservationSource packet, uint mapId, bool allowSameMapReload)
        {
        }

        public void StagePendingDestinationMap(in PacketObservationSource packet, uint mapId, bool allowSameMapReload)
        {
        }

        public void ConfirmDestinationMap(in PacketObservationSource packet, uint mapId, bool allowSameMapReload)
        {
        }

        public void ConfirmPendingDestinationMapArrival(in PacketObservationSource packet)
        {
        }

        public void StageDestinationMapInstance(in PacketObservationSource packet, uint instanceId)
        {
        }

        public void ConfirmDestinationMapInstance(in PacketObservationSource packet, uint instanceId)
        {
        }

        public void MarkSceneTransportBoundary(in PacketObservationSource packet)
        {
        }

        public void AppendCombatObservation(in PacketObservationSource packet, int sourceId, int targetId, in CombatObservation observation)
        {
            CombatObservationCount++;
            LastSkillCode = observation.SkillCode;
        }

        public void CompleteBatch(long batchOrdinal)
        {
        }

        public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, int bodySkillVariantRaw, int marker, int layoutTag, int type)
            => RecordCombat(bodySkillVariantRaw);

        public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, int bodySkillVariantRaw, int marker, int layoutTag, int type, int value)
            => RecordCombat(bodySkillVariantRaw);

        public void RegisterCompactControl0238(in PacketObservationSource packet, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int echoSourceId)
            => RecordCombat(0);

        public void RegisterCompactControl0638(in PacketObservationSource packet, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int flag)
            => RecordCombat(0);

        public void RegisterObservation2A38(in PacketObservationSource packet, int entityId, int mode, int groupCode, int instanceSequenceId, uint headCode, ushort headValue, ulong headMiddleRaw, uint timelineValue, uint stableValue, int echoSourceId, int stackValue, ResourceEffectRef buffResourceEffectRef, int tailLength, ulong tailLow64, ulong tailHigh64)
        {
        }

        public void RegisterObservation2B38(in PacketObservationSource packet, int sourceId, int sourceIdCopy, int phase, int instanceSequenceId, ResourceEffectRef actionResourceEffectRef, int sequenceValue, int stateValue, int detailValue, int tailLength)
        {
        }

        public void RegisterObservation2C38(in PacketObservationSource packet, int entityId, scoped ReadOnlySpan<AuraResultRecord> results)
        {
        }

        public void AppendNickname(in PacketObservationSource packet, int uid, string nickname, Faction faction = Faction.Unknown, CharacterClass? characterClass = null, bool isLocalPlayer = false, int? originServerId = null, string legionName = "")
        {
        }

        public void AppendNpcCode(in PacketObservationSource packet, int instanceId, int npcCode)
        {
        }

        public void AppendNpcName(in PacketObservationSource packet, int npcCode, string name)
        {
        }

        public void AppendNpcKind(in PacketObservationSource packet, int instanceId, NpcKind kind)
        {
        }

        public void AppendNpcHp(in PacketObservationSource packet, int instanceId, int hp)
        {
        }

        public void AppendNpcHp(in PacketObservationSource packet, int instanceId, int hp, int maxHp)
        {
        }

        public void SetNpcBattle(in PacketObservationSource packet, int instanceId, bool isActive)
        {
        }

        public void ToggleNpcBattle(in PacketObservationSource packet, int instanceId)
        {
        }

        public void AppendNpc2136State(in PacketObservationSource packet, int instanceId, uint sequence, uint value0)
        {
        }

        public void AppendNpc0140Value(in PacketObservationSource packet, int instanceId, uint value0)
        {
        }

        public void AppendNpc0240Value(in PacketObservationSource packet, int instanceId, uint value0)
        {
        }

        public void AppendNpc4636State(in PacketObservationSource packet, int instanceId, byte state0, byte state1)
        {
        }

        public void AppendSummon(in PacketObservationSource packet, int ownerId, int summonInstanceId)
        {
        }

        private void RecordCombat(int skillCode)
        {
            CombatObservationCount++;
            LastSkillCode = skillCode;
        }
    }
}
