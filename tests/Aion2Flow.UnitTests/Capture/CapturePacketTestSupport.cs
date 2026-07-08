using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Tests.Capture;

internal static class CapturePacketTestData
{
    public const int GameSkillCode = 16_140_000;
    public const int EmbeddedTlsSkillCode = 815_805_088;

    public static byte[] BuildTlsRecordWithEmbedded0438Bytes()
    {
        var payload = new List<byte>
        {
            0x9D,
            0x80,
            0x50,
            0x04,
            0x38
        };
        Append0438Payload(payload, targetId: 200, sourceId: 100, skillCode: EmbeddedTlsSkillCode);
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

    public static byte[] Build0438Frame(int skillCode = GameSkillCode)
    {
        var payload = new List<byte>
        {
            0x04,
            0x38
        };
        Append0438Payload(payload, targetId: 200, sourceId: 100, skillCode);

        var frame = new List<byte>(payload.Count + 1);
        AppendVarInt(frame, payload.Count + 4);
        frame.AddRange(payload);
        return [.. frame];
    }

    public static byte[] BuildMapEvent0061Frame(uint mapId)
    {
        var payload = new List<byte> { 0x00, 0x61 };
        AppendUInt32Le(payload, mapId);
        payload.Add(0x03);
        AppendUInt64Le(payload, 0x0000019F21876F5C);
        AppendUInt64Le(payload, 0x0000019F2190C0E8);
        return BuildFrame(payload);
    }

    public static byte[] BuildMapEvent0161Frame(uint mapId)
    {
        var payload = new List<byte> { 0x01, 0x61, 0x00 };
        AppendUInt32Le(payload, mapId);
        payload.AddRange([0x01, 0x00]);
        return BuildFrame(payload);
    }

    public static byte[] BuildState0240Frame(uint value0)
    {
        var payload = new List<byte> { 0x02, 0x40 };
        AppendUInt32Le(payload, value0);
        AppendUInt16Le(payload, 0);
        return BuildFrame(payload);
    }

    public static byte[] BuildRemainHp008DFrame(int npcId, uint hp)
    {
        var payload = new List<byte> { 0x00, 0x8D };
        AppendVarInt(payload, npcId);
        AppendVarInt(payload, 2);
        AppendVarInt(payload, 1);
        AppendVarInt(payload, 0);
        AppendUInt32Le(payload, hp);
        return BuildFrame(payload);
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

    private static void AppendUInt32Le(List<byte> buffer, uint value)
    {
        buffer.Add((byte)value);
        buffer.Add((byte)(value >> 8));
        buffer.Add((byte)(value >> 16));
        buffer.Add((byte)(value >> 24));
    }

    private static void AppendUInt16Le(List<byte> buffer, ushort value)
    {
        buffer.Add((byte)value);
        buffer.Add((byte)(value >> 8));
    }

    private static void AppendUInt64Le(List<byte> buffer, ulong value)
    {
        buffer.Add((byte)value);
        buffer.Add((byte)(value >> 8));
        buffer.Add((byte)(value >> 16));
        buffer.Add((byte)(value >> 24));
        buffer.Add((byte)(value >> 32));
        buffer.Add((byte)(value >> 40));
        buffer.Add((byte)(value >> 48));
        buffer.Add((byte)(value >> 56));
    }

    private static byte[] BuildFrame(List<byte> payload)
    {
        var frame = new List<byte>(payload.Count + 1);
        AppendVarInt(frame, payload.Count + 4);
        frame.AddRange(payload);
        return [.. frame];
    }
}

internal sealed class RecordingRuntimeObservationSink : IRuntimeObservationSink
{
    public int CurrentTarget { get; set; }
    public int CombatObservationCount { get; private set; }
    public int LastSkillCode { get; private set; }
    public int ConfirmedMapCount { get; private set; }
    public uint LastConfirmedMapId { get; private set; }
    public int NpcHpObservationCount { get; private set; }
    public int LastNpcHpInstanceId { get; private set; }
    public long LastNpcHp { get; private set; }
    public bool ThrowOnNpcHp { get; set; }

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
        ConfirmedMapCount++;
        LastConfirmedMapId = mapId;
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
        RecordCombat(observation.SkillCode);
    }

    public void CompleteFlush(long flushId)
    {
    }

    public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, int bodySkillVariantRaw, int marker, int layoutTag, int type)
        => RecordCombat(bodySkillVariantRaw);

    public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, int bodySkillVariantRaw, int marker, int layoutTag, int type, int value)
        => RecordCombat(bodySkillVariantRaw);

    public void RegisterCompactControl0238(in PacketObservationSource packet, int sourceId, int mode, uint bodyCodeRaw, int marker, int flag, int echoSourceId)
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

    public void AppendPlayerGroupMember(in PacketObservationSource packet, int uid, in PlayerGroupMembership membership)
    {
    }

    public void AppendPlayerGroupProfile(in PacketObservationSource packet, int originServerId, string nickname, in PlayerGroupMembership membership)
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

    public void AppendNpcHp(in PacketObservationSource packet, int instanceId, long hp)
    {
        if (ThrowOnNpcHp)
            throw new InvalidOperationException("npc hp append failed");

        NpcHpObservationCount++;
        LastNpcHpInstanceId = instanceId;
        LastNpcHp = hp;
    }

    public void AppendNpcHp(in PacketObservationSource packet, int instanceId, long hp, long maxHp)
    {
        AppendNpcHp(in packet, instanceId, hp);
    }

    public void SetNpcBattle(in PacketObservationSource packet, int instanceId, bool isActive)
    {
    }

    public void ToggleNpcBattle(in PacketObservationSource packet, int instanceId)
    {
    }

    public void AppendNpc2136State(in PacketObservationSource packet, int instanceId, long sequence, long value0)
    {
    }

    public void AppendNpc0140Value(in PacketObservationSource packet, int instanceId, long value0)
    {
    }

    public void AppendNpc0240Value(in PacketObservationSource packet, int instanceId, long value0)
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
