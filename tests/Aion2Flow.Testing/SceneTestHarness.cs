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
    private long _flushId;
    private long _completedFlushId;

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

    public void AppendNickname(int uid, string nickname, Faction faction = Faction.Unknown, CharacterClass? characterClass = null, bool isLocalPlayer = false, int? originServerId = null, string legionName = "") => Sink.AppendNickname(NextSource(), uid, nickname, faction, characterClass, isLocalPlayer, originServerId, legionName);

    public void AppendNpcCode(int instanceId, int npcCode) => Sink.AppendNpcCode(NextSource(), instanceId, npcCode);

    public void AppendNpcName(int npcCode, string name) => Sink.AppendNpcName(NextSource(), npcCode, name);

    public void AppendNpcKind(int instanceId, NpcKind kind) => Sink.AppendNpcKind(NextSource(), instanceId, kind);

    public void AppendNpcHp(int instanceId, long hp, long observedAtMilliseconds) => Sink.AppendNpcHp(SourceAt(observedAtMilliseconds), instanceId, hp);

    public void AppendNpcHp(int instanceId, long hp, long maxHp, long observedAtMilliseconds) => Sink.AppendNpcHp(SourceAt(observedAtMilliseconds), instanceId, hp, maxHp);

    public void SetNpcBattle(int instanceId, bool isActive, long observedAtMilliseconds) => Sink.SetNpcBattle(SourceAt(observedAtMilliseconds), instanceId, isActive);

    public void ToggleNpcBattle(int instanceId) => Sink.ToggleNpcBattle(NextSource(), instanceId);

    public void AppendNpc2136State(int instanceId, long sequence, long value0) => Sink.AppendNpc2136State(NextSource(), instanceId, sequence, value0);

    public void AppendNpc0140Value(int instanceId, long value0) => Sink.AppendNpc0140Value(NextSource(), instanceId, value0);

    public void AppendNpc0240Value(int instanceId, long value0) => Sink.AppendNpc0240Value(NextSource(), instanceId, value0);

    public void AppendNpc4636State(int instanceId, byte state0, byte state1) => Sink.AppendNpc4636State(NextSource(), instanceId, state0, state1);

    public void AppendSummon(int ownerId, int summonInstanceId) => Sink.AppendSummon(NextSource(), ownerId, summonInstanceId);

    public void AppendCombatWireObservation(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        long timestamp = 0,
        long flushId = 0)
    {
        var source = new PacketObservationSource(timestamp, flushId, 0, 0, 0, default);
        source = PrepareSource(in source);
        _holder.Sink.AppendCombatWireObservation(in source, sourceId, targetId, in observation);
    }

    public bool TryGetNpcRuntimeState(int instanceId, out RuntimeNpcStateSnapshot state)
    {
        Owner.Refresh();
        if (Owner.Entities.TryGet(instanceId, out var entity))
        {
            Owner.EntityVitals.TryGet(instanceId, out var vital);
            state = new RuntimeNpcStateSnapshot(entity.NpcCode, vital.EntityId > 0 ? vital.CurrentHp : null, vital.MaxHp, vital.EntityId > 0 ? vital.ObservedAtMilliseconds : null, entity.NpcCombatActive, entity.Kind, entity.Value2136, entity.Sequence2136, entity.Value0140, entity.Value0240, entity.State4636, entity.Latest2C38);
            return true;
        }

        state = default;
        return false;
    }

    public void Dispose() => _holder.Dispose();

    private PacketObservationSource PrepareSource(in PacketObservationSource source)
    {
        var resolvedFlushId = source.FlushId > 0 ? source.FlushId : ++_flushId;
        _flushId = Math.Max(_flushId, resolvedFlushId);
        var timestamp = source.CaptureTimestampMilliseconds;
        if (timestamp <= 0 || timestamp > 10_000_000_000)
        {
            timestamp = _timestamp;
            _timestamp += 50;
        }
        else
        {
            _timestamp = Math.Max(_timestamp, timestamp + 50);
        }

        return source with
        {
            CaptureTimestampMilliseconds = timestamp,
            FlushId = resolvedFlushId
        };
    }

    private void CompleteFlush(long flushId)
    {
        if (flushId <= _completedFlushId)
            return;

        _holder.Sink.CompleteFlush(flushId);
        _completedFlushId = flushId;
    }

    private PacketObservationSource NextSource()
    {
        var source = SourceAt(_timestamp);
        _timestamp += 50;
        return source;
    }

    private PacketObservationSource SourceAt(long timestamp)
        => new(timestamp, ++_flushId, 0, 0, 0, default);

    private void CompletePendingBatches()
    {
        while (_completedFlushId < _flushId)
        {
            CompleteFlush(_completedFlushId + 1);
        }
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
        public void SeedNpcRuntimeState(in PacketObservationSource packet, int instanceId, in RuntimeNpcStateSnapshot state) => inner.SeedNpcRuntimeState(in packet, instanceId, in state);
        public int ResolveNpcObservationSource() => inner.ResolveNpcObservationSource();
        public void RememberNpcObservationSource(int instanceId) => inner.RememberNpcObservationSource(instanceId);
        public void SetCurrentMap(in PacketObservationSource packet, uint mapId) => inner.SetCurrentMap(in packet, mapId);
        public void AnnounceDestinationMapTransition(in PacketObservationSource packet, uint mapId) => inner.AnnounceDestinationMapTransition(in packet, mapId);
        public void CommitDestinationMapTransition(in PacketObservationSource packet, uint mapId) => inner.CommitDestinationMapTransition(in packet, mapId);
        public void StageSceneMapCandidate(in PacketObservationSource packet, uint mapId) => inner.StageSceneMapCandidate(in packet, mapId);
        public void ConfirmSceneMap(in PacketObservationSource packet, uint mapId) => inner.ConfirmSceneMap(in packet, mapId);
        public void ConfirmDestinationMapArrival(in PacketObservationSource packet) => inner.ConfirmDestinationMapArrival(in packet);
        public void StageMapInstance(in PacketObservationSource packet, uint instanceId) => inner.StageMapInstance(in packet, instanceId);
        public void ConfirmMapInstance(in PacketObservationSource packet, uint instanceId) => inner.ConfirmMapInstance(in packet, instanceId);
        public void MarkSceneTransportBoundary(in PacketObservationSource packet) => inner.MarkSceneTransportBoundary(in packet);
        public void AppendCombatWireObservation(in PacketObservationSource source, int sourceId, int targetId, in CombatWireObservation observation)
        {
            var preparedSource = owner.PrepareSource(in source);
            inner.AppendCombatWireObservation(in preparedSource, sourceId, targetId, in observation);
        }
        public void CompleteFlush(long flushId) => owner.CompleteFlush(flushId);
        public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, int bodySkillVariantRaw, int marker, int layoutTag, int type) => inner.RegisterCompactValue0438(in packet, targetId, sourceId, bodySkillVariantRaw, marker, layoutTag, type);
        public void RegisterCompactValue0438(in PacketObservationSource packet, int targetId, int sourceId, int bodySkillVariantRaw, int marker, int layoutTag, int type, int value) => inner.RegisterCompactValue0438(in packet, targetId, sourceId, bodySkillVariantRaw, marker, layoutTag, type, value);
        public void RegisterCompactControl0238(in PacketObservationSource packet, int sourceId, int mode, uint bodyCodeRaw, int marker, int flag, int echoSourceId) => inner.RegisterCompactControl0238(in packet, sourceId, mode, bodyCodeRaw, marker, flag, echoSourceId);
        public void RegisterCompactControl0638(in PacketObservationSource packet, int sourceId, ResourceEffectRef bodyResourceEffectRef, int marker, int flag) => inner.RegisterCompactControl0638(in packet, sourceId, bodyResourceEffectRef, marker, flag);
        public void RegisterObservation2A38(in PacketObservationSource packet, int entityId, int mode, int groupCode, int instanceSequenceId, uint headCode, ushort headValue, ulong headMiddleRaw, uint timelineValue, uint stableValue, int echoSourceId, int stackValue, ResourceEffectRef buffResourceEffectRef, int tailLength, ulong tailLow64, ulong tailHigh64) => inner.RegisterObservation2A38(in packet, entityId, mode, groupCode, instanceSequenceId, headCode, headValue, headMiddleRaw, timelineValue, stableValue, echoSourceId, stackValue, buffResourceEffectRef, tailLength, tailLow64, tailHigh64);
        public void RegisterObservation2B38(in PacketObservationSource packet, int sourceId, int sourceIdCopy, int phase, int instanceSequenceId, ResourceEffectRef actionResourceEffectRef, int sequenceValue, int stateValue, int detailValue, int tailLength) => inner.RegisterObservation2B38(in packet, sourceId, sourceIdCopy, phase, instanceSequenceId, actionResourceEffectRef, sequenceValue, stateValue, detailValue, tailLength);
        public void RegisterObservation2C38(in PacketObservationSource packet, int entityId, scoped ReadOnlySpan<AuraResultRecord> results) => inner.RegisterObservation2C38(in packet, entityId, results);
        public void AppendNickname(in PacketObservationSource packet, int uid, string nickname, Faction faction = Faction.Unknown, CharacterClass? characterClass = null, bool isLocalPlayer = false, int? originServerId = null, string legionName = "") => inner.AppendNickname(in packet, uid, nickname, faction, characterClass, isLocalPlayer, originServerId, legionName);
        public void AppendPlayerGroupMember(in PacketObservationSource packet, int uid, in PlayerGroupMembership membership) => inner.AppendPlayerGroupMember(in packet, uid, in membership);
        public void AppendPlayerGroupProfile(in PacketObservationSource packet, int originServerId, string nickname, in PlayerGroupMembership membership) => inner.AppendPlayerGroupProfile(in packet, originServerId, nickname, in membership);
        public void AppendNpcCode(in PacketObservationSource packet, int instanceId, int npcCode) => inner.AppendNpcCode(in packet, instanceId, npcCode);
        public void AppendNpcName(in PacketObservationSource packet, int npcCode, string name) => inner.AppendNpcName(in packet, npcCode, name);
        public void AppendNpcKind(in PacketObservationSource packet, int instanceId, NpcKind kind) => inner.AppendNpcKind(in packet, instanceId, kind);
        public void AppendNpcHp(in PacketObservationSource packet, int instanceId, long hp) => inner.AppendNpcHp(in packet, instanceId, hp);
        public void AppendNpcHp(in PacketObservationSource packet, int instanceId, long hp, long maxHp) => inner.AppendNpcHp(in packet, instanceId, hp, maxHp);
        public void SetNpcBattle(in PacketObservationSource packet, int instanceId, bool isActive) => inner.SetNpcBattle(in packet, instanceId, isActive);
        public void ToggleNpcBattle(in PacketObservationSource packet, int instanceId) => inner.ToggleNpcBattle(in packet, instanceId);
        public void AppendNpc2136State(in PacketObservationSource packet, int instanceId, long sequence, long value0) => inner.AppendNpc2136State(in packet, instanceId, sequence, value0);
        public void AppendNpc0140Value(in PacketObservationSource packet, int instanceId, long value0) => inner.AppendNpc0140Value(in packet, instanceId, value0);
        public void AppendNpc0240Value(in PacketObservationSource packet, int instanceId, long value0) => inner.AppendNpc0240Value(in packet, instanceId, value0);
        public void AppendNpc4636State(in PacketObservationSource packet, int instanceId, byte state0, byte state1) => inner.AppendNpc4636State(in packet, instanceId, state0, state1);
        public void AppendSummon(in PacketObservationSource packet, int ownerId, int summonInstanceId) => inner.AppendSummon(in packet, ownerId, summonInstanceId);
    }
}
