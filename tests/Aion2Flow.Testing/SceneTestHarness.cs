using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;

namespace Cloris.Aion2Flow.Tests;

public sealed class SceneTestHarness : IDisposable
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

    public CombatDetailDelta CreateDetailDelta(SceneCombatSnapshot snapshot, int combatantId, bool forceRefresh = false) => Owner.CreateDetailDelta(snapshot, combatantId, forceRefresh);

    public CombatSkillBreakdownSnapshot CreateSkillBreakdown(SceneCombatSnapshot snapshot, int combatantId) => Owner.CreateSkillBreakdown(snapshot, combatantId);

    public void AppendNickname(int uid, string nickname, int? originServerId = null, Faction faction = Faction.Unknown, CharacterClass? characterClass = null) => Sink.AppendNickname(NextSource(), uid, nickname, originServerId, faction, characterClass);

    public void AppendNpcCode(int instanceId, int npcCode) => Sink.AppendNpcCode(NextSource(), instanceId, npcCode);

    public void AppendNpcName(int npcCode, string name) => Sink.AppendNpcName(NextSource(), npcCode, name);

    public void AppendNpcKind(int instanceId, NpcKind kind) => Sink.AppendNpcKind(NextSource(), instanceId, kind);

    public void AppendNpcHp(int instanceId, int hp, long observedAtMilliseconds) => Sink.AppendNpcHp(SourceAt(observedAtMilliseconds), instanceId, hp);

    public void AppendNpcHp(int instanceId, int hp, int maxHp, long observedAtMilliseconds) => Sink.AppendNpcHp(SourceAt(observedAtMilliseconds), instanceId, hp, maxHp);

    public void SetNpcBattle(int instanceId, bool isActive, long observedAtMilliseconds) => Sink.SetNpcBattle(SourceAt(observedAtMilliseconds), instanceId, isActive);

    public void ToggleNpcBattle(int instanceId) => Sink.ToggleNpcBattle(NextSource(), instanceId);

    public void AppendNpc2136State(int instanceId, uint sequence, uint value0) => Sink.AppendNpc2136State(NextSource(), instanceId, sequence, value0);

    public void AppendNpc0140Value(int instanceId, uint value0) => Sink.AppendNpc0140Value(NextSource(), instanceId, value0);

    public void AppendNpc0240Value(int instanceId, uint value0) => Sink.AppendNpc0240Value(NextSource(), instanceId, value0);

    public void AppendNpc4636State(int instanceId, byte state0, byte state1) => Sink.AppendNpc4636State(NextSource(), instanceId, state0, state1);

    public void AppendSummon(int ownerId, int summonInstanceId) => Sink.AppendSummon(NextSource(), ownerId, summonInstanceId);

    public void AppendCombatPacket(ParsedCombatPacket packet)
    {
        packet = PreparePacket(packet);
        var observation = packet.ToObservation();
        var source = new PacketObservationSource(packet.Timestamp, packet.FrameOrdinal, packet.BatchOrdinal, 0, 0, 0, default);
        _holder.Sink.AppendCombatObservation(in source, packet.SourceId, packet.TargetId, in observation);
    }

    public bool TryGetNpcRuntimeState(int instanceId, out RuntimeNpcStateSnapshot state)
    {
        Owner.Refresh();
        if (Owner.Entities.TryGet(instanceId, out var entity))
        {
            state = new RuntimeNpcStateSnapshot(entity.NpcCode, entity.CurrentHp, entity.MaxHp, null, entity.NpcCombatActive, entity.Kind, entity.Value2136, entity.Sequence2136, entity.Value0140, entity.Value0240, entity.State4636, entity.Latest2C38);
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

    private PacketObservationSource NextSource()
    {
        var source = SourceAt(_timestamp);
        _timestamp += 50;
        return source;
    }

    private PacketObservationSource SourceAt(long timestamp)
        => new(timestamp, 0, ++_batchOrdinal, 0, 0, 0, default);

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
            SkillCode = packet.SkillCode,
            BodySkillVariantRaw = packet.BodySkillVariantRaw,
            BodyResourceEffectRef = packet.BodyResourceEffectRef,
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
            DetailResourceEffectRef = packet.DetailResourceEffectRef,
            ResourceKind = packet.ResourceKind,
            FrameOrdinal = packet.FrameOrdinal,
            BatchOrdinal = packet.BatchOrdinal,
            Timestamp = timestamp,
            Id = packet.Id,
            Modifiers = packet.Modifiers,
            EventKind = packet.EventKind,
            ValueKind = packet.ValueKind,
            PeriodicTailSkillCodeRaw = packet.PeriodicTailSkillCodeRaw,
            PeriodicTailPrefixValue = packet.PeriodicTailPrefixValue,
            PeriodicTailLength = packet.PeriodicTailLength,
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
        public void StageDestinationMap(in PacketObservationSource packet, uint mapId) => inner.StageDestinationMap(in packet, mapId);
        public void StageDestinationMap(in PacketObservationSource packet, uint mapId, bool allowSameMapReload) => inner.StageDestinationMap(in packet, mapId, allowSameMapReload);
        public void StagePendingDestinationMap(in PacketObservationSource packet, uint mapId, bool allowSameMapReload) => inner.StagePendingDestinationMap(in packet, mapId, allowSameMapReload);
        public void ConfirmDestinationMap(in PacketObservationSource packet, uint mapId, bool allowSameMapReload) => inner.ConfirmDestinationMap(in packet, mapId, allowSameMapReload);
        public void ConfirmPendingDestinationMapArrival(in PacketObservationSource packet) => inner.ConfirmPendingDestinationMapArrival(in packet);
        public void StageDestinationMapInstance(in PacketObservationSource packet, uint instanceId) => inner.StageDestinationMapInstance(in packet, instanceId);
        public void ConfirmDestinationMapInstance(in PacketObservationSource packet, uint instanceId) => inner.ConfirmDestinationMapInstance(in packet, instanceId);
        public void MarkSceneTransportBoundary(in PacketObservationSource packet) => inner.MarkSceneTransportBoundary(in packet);
        public void AppendCombatObservation(in PacketObservationSource source, int sourceId, int targetId, in CombatObservation observation)
        {
            var packet = ParsedCombatPacket.FromObservation(sourceId, targetId, in observation, source.CaptureTimestampMilliseconds, source.FrameOrdinal, source.BatchOrdinal);
            packet = owner.PreparePacket(packet);
            var prepared = packet.ToObservation();
            var preparedSource = source with
            {
                CaptureTimestampMilliseconds = packet.Timestamp,
                FrameOrdinal = packet.FrameOrdinal,
                BatchOrdinal = packet.BatchOrdinal
            };
            inner.AppendCombatObservation(in preparedSource, packet.SourceId, packet.TargetId, in prepared);
        }
        public void CompleteBatch(long batchOrdinal) => owner.CompleteBatch(batchOrdinal);
        public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int layoutTag, int type) => inner.RegisterCompactValue0438(in packet, targetId, sourceId, bodyResourceEffectRef, marker, layoutTag, type);
        public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int layoutTag, int type, int value) => inner.RegisterCompactValue0438(in packet, targetId, sourceId, bodyResourceEffectRef, marker, layoutTag, type, value);
        public void RegisterCompactControl0238(in PacketObservationSource packet, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker) => inner.RegisterCompactControl0238(in packet, sourceId, bodyResourceEffectRef, marker);
        public void RegisterCompactControl0638(in PacketObservationSource packet, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int flag) => inner.RegisterCompactControl0638(in packet, sourceId, bodyResourceEffectRef, marker, flag);
        public void RegisterObservation2A38(in PacketObservationSource packet, int entityId, int mode, int groupCode, int instanceSequenceId, uint headCode, ushort headValue, ulong headMiddleRaw, uint timelineValue, uint stableValue, int echoSourceId, int stackValue, ResourceEffectRef buffResourceEffectRef, int tailLength, ulong tailLow64, ulong tailHigh64) => inner.RegisterObservation2A38(in packet, entityId, mode, groupCode, instanceSequenceId, headCode, headValue, headMiddleRaw, timelineValue, stableValue, echoSourceId, stackValue, buffResourceEffectRef, tailLength, tailLow64, tailHigh64);
        public void RegisterObservation2B38(in PacketObservationSource packet, int sourceId, int sourceIdCopy, int phase, int instanceSequenceId, ResourceEffectRef actionResourceEffectRef, int sequenceValue, int stateValue, int detailValue, int tailLength) => inner.RegisterObservation2B38(in packet, sourceId, sourceIdCopy, phase, instanceSequenceId, actionResourceEffectRef, sequenceValue, stateValue, detailValue, tailLength);
        public void RegisterObservation2C38(in PacketObservationSource packet, int entityId, scoped ReadOnlySpan<AuraResultRecord> results) => inner.RegisterObservation2C38(in packet, entityId, results);
        public void AppendNickname(in PacketObservationSource packet, int uid, string nickname, int? originServerId = null, Faction faction = Faction.Unknown, CharacterClass? characterClass = null) => inner.AppendNickname(in packet, uid, nickname, originServerId, faction, characterClass);
        public void AppendNpcCode(in PacketObservationSource packet, int instanceId, int npcCode) => inner.AppendNpcCode(in packet, instanceId, npcCode);
        public void AppendNpcName(in PacketObservationSource packet, int npcCode, string name) => inner.AppendNpcName(in packet, npcCode, name);
        public void AppendNpcKind(in PacketObservationSource packet, int instanceId, NpcKind kind) => inner.AppendNpcKind(in packet, instanceId, kind);
        public void AppendNpcHp(in PacketObservationSource packet, int instanceId, int hp) => inner.AppendNpcHp(in packet, instanceId, hp);
        public void AppendNpcHp(in PacketObservationSource packet, int instanceId, int hp, int maxHp) => inner.AppendNpcHp(in packet, instanceId, hp, maxHp);
        public void SetNpcBattle(in PacketObservationSource packet, int instanceId, bool isActive) => inner.SetNpcBattle(in packet, instanceId, isActive);
        public void ToggleNpcBattle(in PacketObservationSource packet, int instanceId) => inner.ToggleNpcBattle(in packet, instanceId);
        public void AppendNpc2136State(in PacketObservationSource packet, int instanceId, uint sequence, uint value0) => inner.AppendNpc2136State(in packet, instanceId, sequence, value0);
        public void AppendNpc0140Value(in PacketObservationSource packet, int instanceId, uint value0) => inner.AppendNpc0140Value(in packet, instanceId, value0);
        public void AppendNpc0240Value(in PacketObservationSource packet, int instanceId, uint value0) => inner.AppendNpc0240Value(in packet, instanceId, value0);
        public void AppendNpc4636State(in PacketObservationSource packet, int instanceId, byte state0, byte state1) => inner.AppendNpc4636State(in packet, instanceId, state0, state1);
        public void AppendSummon(in PacketObservationSource packet, int ownerId, int summonInstanceId) => inner.AppendSummon(in packet, ownerId, summonInstanceId);
    }
}
