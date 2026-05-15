using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;

namespace Cloris.Aion2Flow.Tests;

internal sealed class SceneTestHarness : IDisposable
{
    private readonly ReplaySinkHolder _holder = SceneSinkFactory.CreateForReplay();
    private readonly IRuntimeObservationSink _sink;
    private long _timestamp = 1_000;
    private long _batchOrdinal;
    private long _completedBatchOrdinal;

    public SceneTestHarness()
    {
        _sink = new HarnessSink(this, _holder.Sink);
    }

    public IRuntimeObservationSink Sink => _sink;
    public SceneReadModelOwner Owner => _holder.Owner;

    public SceneCombatSnapshot CreateSnapshot()
    {
        CompletePendingBatches();
        return Owner.CreateSnapshot();
    }

    public CombatDetailDelta CreateDetailDelta(SceneCombatSnapshot snapshot, int combatantId, bool forceRefresh = false) =>
        Owner.CreateDetailDelta(snapshot, combatantId, forceRefresh);

    public CombatSkillBreakdownSnapshot CreateSkillBreakdown(SceneCombatSnapshot snapshot, int combatantId) =>
        Owner.CreateSkillBreakdown(snapshot, combatantId);

    public void AppendNickname(int uid, string nickname, int? originServerId = null) =>
        Sink.AppendNickname(uid, nickname, originServerId);

    public void AppendNpcCode(int instanceId, int npcCode) =>
        Sink.AppendNpcCode(instanceId, npcCode);

    public void AppendNpcName(int npcCode, string name) =>
        Sink.AppendNpcName(npcCode, name);

    public void AppendNpcKind(int instanceId, NpcKind kind) =>
        Sink.AppendNpcKind(instanceId, kind);

    public void AppendNpcHp(int instanceId, int hp, long observedAtMilliseconds) =>
        Sink.AppendNpcHp(instanceId, hp, observedAtMilliseconds);

    public void AppendNpcHp(int instanceId, int hp, int maxHp, long observedAtMilliseconds) =>
        Sink.AppendNpcHp(instanceId, hp, maxHp, observedAtMilliseconds);

    public void SetNpcBattle(int instanceId, bool isActive, long observedAtMilliseconds) =>
        Sink.SetNpcBattle(instanceId, isActive, observedAtMilliseconds);

    public void ToggleNpcBattle(int instanceId) =>
        Sink.ToggleNpcBattle(instanceId);

    public void AppendNpc2136State(int instanceId, uint sequence, uint value0) =>
        Sink.AppendNpc2136State(instanceId, sequence, value0);

    public void AppendNpc0140Value(int instanceId, uint value0) =>
        Sink.AppendNpc0140Value(instanceId, value0);

    public void AppendNpc0240Value(int instanceId, uint value0) =>
        Sink.AppendNpc0240Value(instanceId, value0);

    public void AppendNpc4636State(int instanceId, byte state0, byte state1) =>
        Sink.AppendNpc4636State(instanceId, state0, state1);

    public void AppendSummon(int ownerId, int summonInstanceId) =>
        Sink.AppendSummon(ownerId, summonInstanceId);

    public void AppendCombatPacket(ParsedCombatPacket packet)
    {
        packet = PreparePacket(packet);
        _holder.Sink.AppendCombatPacket(packet);
    }

    public bool TryGetNpcRuntimeState(int instanceId, out RuntimeNpcStateSnapshot state)
    {
        Owner.Refresh();
        if (Owner.Entities.TryGet(instanceId, out var entity))
        {
            state = new RuntimeNpcStateSnapshot(
                entity.NpcCode,
                entity.CurrentHp,
                entity.MaxHp,
                null,
                entity.NpcCombatActive,
                entity.Kind,
                entity.Value2136,
                entity.Sequence2136,
                entity.Value0140,
                entity.Value0240,
                entity.State4636,
                entity.Latest2C38);
            return true;
        }

        state = default;
        return false;
    }

    public void Dispose() => _holder.Dispose();

    private ParsedCombatPacket PreparePacket(ParsedCombatPacket packet)
    {
        var batchOrdinal = packet.BatchOrdinal > 0 ? packet.BatchOrdinal : ++_batchOrdinal;
        _batchOrdinal = Math.Max(_batchOrdinal, batchOrdinal);
        if (packet.Timestamp <= 0 || packet.Timestamp > 10_000_000_000)
        {
            packet = WithTimestamp(packet, _timestamp);
            _timestamp += 50;
        }
        else
        {
            _timestamp = Math.Max(_timestamp, packet.Timestamp + 50);
        }

        packet.BatchOrdinal = batchOrdinal;
        return packet;
    }

    private void CompleteBatch(long batchOrdinal)
    {
        if (batchOrdinal <= _completedBatchOrdinal)
            return;

        _holder.Sink.CompleteBatch(batchOrdinal);
        _completedBatchOrdinal = batchOrdinal;
    }

    private void CompletePendingBatches()
    {
        while (_completedBatchOrdinal < _batchOrdinal)
        {
            CompleteBatch(_completedBatchOrdinal + 1);
        }
    }

    private static ParsedCombatPacket WithTimestamp(ParsedCombatPacket packet, long timestamp)
    {
        var clone = new ParsedCombatPacket
        {
            SourceId = packet.SourceId,
            TargetId = packet.TargetId,
            Flag = packet.Flag,
            Damage = packet.Damage,
            OriginalSkillCode = packet.OriginalSkillCode,
            SkillCode = packet.SkillCode,
            BaseSkillCode = packet.BaseSkillCode,
            ChargeStage = packet.ChargeStage,
            SpecializationMask = packet.SpecializationMask,
            Marker = packet.Marker,
            Type = packet.Type,
            Unknown = packet.Unknown,
            LayoutTag = packet.LayoutTag,
            Loop = packet.Loop,
            HitContribution = packet.HitContribution,
            AttemptContribution = packet.AttemptContribution,
            MultiHitCount = packet.MultiHitCount,
            DrainHealAmount = packet.DrainHealAmount,
            RegenerationAmount = packet.RegenerationAmount,
            DetailRaw = packet.DetailRaw,
            ResourceKind = packet.ResourceKind,
            FrameOrdinal = packet.FrameOrdinal,
            BatchOrdinal = packet.BatchOrdinal,
            Timestamp = timestamp,
            Id = packet.Id,
            Modifiers = packet.Modifiers,
            EventKind = packet.EventKind,
            ValueKind = packet.ValueKind,
            IsNormalized = packet.IsNormalized
        };

        if (packet.IsPeriodicEffect)
            clone.SetPeriodicEffect(packet.PeriodicRelation, packet.PeriodicMode);

        if (packet.EffectTag != PacketEffectTag.None)
            clone.SetEffectTag(packet.EffectTag);

        return clone;
    }

    private sealed class HarnessSink(SceneTestHarness owner, IRuntimeObservationSink inner) : IRuntimeObservationSink
    {
        public int CurrentTarget
        {
            get => inner.CurrentTarget;
            set => inner.CurrentTarget = value;
        }

        public int ResolveLifecycleId(int rawInstanceId) => inner.ResolveLifecycleId(rawInstanceId);
        public int RebindInstanceLifecycle(int rawInstanceId) => inner.RebindInstanceLifecycle(rawInstanceId);
        public bool IsKnownEntity(int id) => inner.IsKnownEntity(id);
        public bool HasSummonOwner(int instanceId) => inner.HasSummonOwner(instanceId);
        public bool TryGetNpcRuntimeState(int instanceId, out RuntimeNpcStateSnapshot state) => inner.TryGetNpcRuntimeState(instanceId, out state);
        public int ResolveNpcObservationSource() => inner.ResolveNpcObservationSource();
        public void RememberNpcObservationSource(int instanceId) => inner.RememberNpcObservationSource(instanceId);
        public void StageDestinationMap(uint mapId) => inner.StageDestinationMap(mapId);
        public void StageDestinationMap(uint mapId, bool allowSameMapReload) => inner.StageDestinationMap(mapId, allowSameMapReload);
        public void StageDestinationMapInstance(uint instanceId) => inner.StageDestinationMapInstance(instanceId);
        public void MarkSceneArrival() => inner.MarkSceneArrival();
        public void MarkSceneTransportBoundary() => inner.MarkSceneTransportBoundary();
        public void AppendCombatObservation(
            int sourceId,
            int targetId,
            long timestamp,
            long frameOrdinal,
            long batchOrdinal,
            in CombatObservation observation,
            ushort opcode = 0,
            int payloadLength = 0,
            long captureSequence = 0)
        {
            var packet = ParsedCombatPacket.FromObservation(sourceId, targetId, in observation, timestamp, frameOrdinal, batchOrdinal);
            packet = owner.PreparePacket(packet);
            var prepared = packet.ToObservation();
            inner.AppendCombatObservation(packet.SourceId, packet.TargetId, packet.Timestamp, packet.FrameOrdinal, packet.BatchOrdinal, in prepared, opcode, payloadLength, captureSequence);
        }
        public void CompleteBatch(long batchOrdinal) => owner.CompleteBatch(batchOrdinal);
        public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, long timestamp, long frameOrdinal, long batchOrdinal) => inner.RegisterCompactValue0438(targetId, sourceId, skillCodeRaw, marker, layoutTag, type, timestamp, frameOrdinal, batchOrdinal);
        public void RegisterCompactValue0438(int targetId, int sourceId, int skillCodeRaw, int marker, int layoutTag, int type, int value, long timestamp, long frameOrdinal, long batchOrdinal) => inner.RegisterCompactValue0438(targetId, sourceId, skillCodeRaw, marker, layoutTag, type, value, timestamp, frameOrdinal, batchOrdinal);
        public void RegisterCompactControl0238(int sourceId, int skillCodeRaw, int marker, long batchOrdinal) => inner.RegisterCompactControl0238(sourceId, skillCodeRaw, marker, batchOrdinal);
        public void RegisterCompactControl0638(int sourceId, int skillCodeRaw, int marker, long timestamp, long frameOrdinal, long batchOrdinal) => inner.RegisterCompactControl0638(sourceId, skillCodeRaw, marker, timestamp, frameOrdinal, batchOrdinal);
        public void RegisterPeriodicLink0538(int targetId, int sourceId, int linkId, int sequenceId, int tailRaw, long timestamp, long frameOrdinal, long batchOrdinal) => inner.RegisterPeriodicLink0538(targetId, sourceId, linkId, sequenceId, tailRaw, timestamp, frameOrdinal, batchOrdinal);
        public void RegisterObservation2A38(int sourceId, int mode, int groupCode, int sequenceId, ushort headValue, uint buffCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal) => inner.RegisterObservation2A38(sourceId, mode, groupCode, sequenceId, headValue, buffCodeRaw, timestamp, frameOrdinal, batchOrdinal);
        public void RegisterObservation2C38(int instanceId, int mode, int sequenceId, int resultCode, int tailSourceId, int tailSkillCodeRaw, long timestamp, long frameOrdinal, long batchOrdinal) => inner.RegisterObservation2C38(instanceId, mode, sequenceId, resultCode, tailSourceId, tailSkillCodeRaw, timestamp, frameOrdinal, batchOrdinal);
        public void AppendNickname(int uid, string nickname, int? originServerId = null) => inner.AppendNickname(uid, nickname, originServerId);
        public void AppendNpcCode(int instanceId, int npcCode) => inner.AppendNpcCode(instanceId, npcCode);
        public void AppendNpcName(int npcCode, string name) => inner.AppendNpcName(npcCode, name);
        public void AppendNpcKind(int instanceId, NpcKind kind) => inner.AppendNpcKind(instanceId, kind);
        public void AppendNpcHp(int instanceId, int hp, long observedAtMilliseconds) => inner.AppendNpcHp(instanceId, hp, observedAtMilliseconds);
        public void AppendNpcHp(int instanceId, int hp, int maxHp, long observedAtMilliseconds) => inner.AppendNpcHp(instanceId, hp, maxHp, observedAtMilliseconds);
        public void SetNpcBattle(int instanceId, bool isActive, long observedAtMilliseconds) => inner.SetNpcBattle(instanceId, isActive, observedAtMilliseconds);
        public void ToggleNpcBattle(int instanceId) => inner.ToggleNpcBattle(instanceId);
        public void AppendNpc2136State(int instanceId, uint sequence, uint value0) => inner.AppendNpc2136State(instanceId, sequence, value0);
        public void AppendNpc0140Value(int instanceId, uint value0) => inner.AppendNpc0140Value(instanceId, value0);
        public void AppendNpc0240Value(int instanceId, uint value0) => inner.AppendNpc0240Value(instanceId, value0);
        public void AppendNpc4636State(int instanceId, byte state0, byte state1) => inner.AppendNpc4636State(instanceId, state0, state1);
        public void AppendSummon(int ownerId, int summonInstanceId) => inner.AppendSummon(ownerId, summonInstanceId);
    }
}
