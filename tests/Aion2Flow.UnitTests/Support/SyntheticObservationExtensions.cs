using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.Support;

internal static class SyntheticObservationExtensions
{
    public static void ApplyCombat(this CombatStore store, int sourceId, int targetId, long damage, int hitCount, int attemptCount, int skillCode)
    {
        var observation = new CombatObservation
        {
            SkillCode = skillCode,
            Damage = damage,
            HitCount = hitCount,
            AttemptCount = attemptCount,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };
        store.ApplyCombat(sourceId, targetId, in observation, store.Revision + 1);
    }

    public static void ApplyCombat(this CombatStore store, int sourceId, int targetId, in CombatObservation observation) => store.ApplyCombat(sourceId, targetId, in observation, store.Revision + 1);

    public static PacketObservationSource Source(long timestamp = 0, ushort opcode = 0) => new(timestamp, 0, 0, opcode, 0, 0, default);

    public static void AppendCombatPacket(this IRuntimeObservationSink sink, ParsedCombatPacket packet)
    {
        var observation = packet.ToObservation();
        var source = new PacketObservationSource(packet.Timestamp, packet.FrameOrdinal, packet.BatchOrdinal, 0, 0, 0, default);
        sink.AppendCombatObservation(in source, packet.SourceId, packet.TargetId, in observation);
    }

    public static void StageDestinationMap(this IRuntimeObservationSink sink, uint mapId)
    {
        var source = Source();
        sink.StageDestinationMap(in source, mapId);
    }

    public static void StageDestinationMapInstance(this IRuntimeObservationSink sink, uint instanceId)
    {
        var source = Source();
        sink.StageDestinationMapInstance(in source, instanceId);
    }

    public static void AppendNickname(this IRuntimeObservationSink sink, int uid, string nickname, int? originServerId = null, Faction faction = Faction.Unknown, CharacterClass? characterClass = null, bool isLocalPlayer = false)
    {
        var source = Source();
        sink.AppendNickname(in source, uid, nickname, originServerId, faction, characterClass, isLocalPlayer);
    }

    public static void AppendNpcCode(this IRuntimeObservationSink sink, int instanceId, int npcCode)
    {
        var source = Source();
        sink.AppendNpcCode(in source, instanceId, npcCode);
    }

    public static void AppendNpcName(this IRuntimeObservationSink sink, int npcCode, string name)
    {
        var source = Source();
        sink.AppendNpcName(in source, npcCode, name);
    }

    public static void AppendNpcKind(this IRuntimeObservationSink sink, int instanceId, NpcKind kind)
    {
        var source = Source();
        sink.AppendNpcKind(in source, instanceId, kind);
    }

    public static void AppendNpcHp(this IRuntimeObservationSink sink, int instanceId, int hp, long observedAtMilliseconds)
    {
        var source = Source(observedAtMilliseconds, 0x008D);
        sink.AppendNpcHp(in source, instanceId, hp);
    }

    public static void AppendNpcHp(this IRuntimeObservationSink sink, int instanceId, int hp, int maxHp, long observedAtMilliseconds)
    {
        var source = Source(observedAtMilliseconds, 0x008D);
        sink.AppendNpcHp(in source, instanceId, hp, maxHp);
    }

    public static void SetNpcBattle(this IRuntimeObservationSink sink, int instanceId, bool isActive, long observedAtMilliseconds)
    {
        var source = Source(observedAtMilliseconds, 0x218D);
        sink.SetNpcBattle(in source, instanceId, isActive);
    }

    public static void ToggleNpcBattle(this IRuntimeObservationSink sink, int instanceId)
    {
        var source = Source(opcode: 0x218D);
        sink.ToggleNpcBattle(in source, instanceId);
    }

    public static void AppendNpc2136State(this IRuntimeObservationSink sink, int instanceId, uint sequence, uint value0)
    {
        var source = Source(opcode: 0x2136);
        sink.AppendNpc2136State(in source, instanceId, sequence, value0);
    }

    public static void AppendNpc0140Value(this IRuntimeObservationSink sink, int instanceId, uint value0)
    {
        var source = Source(opcode: 0x0140);
        sink.AppendNpc0140Value(in source, instanceId, value0);
    }

    public static void AppendNpc0240Value(this IRuntimeObservationSink sink, int instanceId, uint value0)
    {
        var source = Source(opcode: 0x0240);
        sink.AppendNpc0240Value(in source, instanceId, value0);
    }

    public static void AppendNpc4636State(this IRuntimeObservationSink sink, int instanceId, byte state0, byte state1)
    {
        var source = Source(opcode: 0x4636);
        sink.AppendNpc4636State(in source, instanceId, state0, state1);
    }

    public static void AppendSummon(this IRuntimeObservationSink sink, int ownerId, int summonInstanceId)
    {
        var source = Source(opcode: 0x4036);
        sink.AppendSummon(in source, ownerId, summonInstanceId);
    }

    public static void RegisterObservation2C38(this IRuntimeObservationSink sink, int entityId, int instanceSequenceId, int resultCode, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        var source = new PacketObservationSource(timestamp, frameOrdinal, batchOrdinal, 0x2C38, 0, 0, default);
        Span<AuraResultRecord> results = stackalloc AuraResultRecord[1];
        results[0] = new AuraResultRecord(0, instanceSequenceId, resultCode, 0, 0, 0);
        sink.RegisterObservation2C38(in source, entityId, results);
    }
}
