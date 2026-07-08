using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public sealed class ScenePlaybackSession
{
    private const int DefaultRecentMarkerCapacity = 512;
    private readonly IScenePlaybackSource _source;
    private FrameProjector? _projector;
    private long _nextLoadedObservationOrdinal;
    private long _positionMilliseconds;

    public ScenePlaybackSession(IScenePlaybackSource source)
    {
        _source = source;
        var segment = _source.CreateTimelineSegment();
        _nextLoadedObservationOrdinal = segment.StartObservationOrdinal;
    }

    public IScenePlaybackSource Source => _source;

    public long NextLoadedObservationOrdinal => _nextLoadedObservationOrdinal;

    public long PositionMilliseconds => _positionMilliseconds;

    public void ResetLoadedCursor()
    {
        var segment = _source.CreateTimelineSegment();
        _nextLoadedObservationOrdinal = segment.StartObservationOrdinal;
        _projector = null;
        _positionMilliseconds = 0;
    }

    public JournalReadResult ReadNextTimelineBatch(int maxCount, JournalEntriesReader reader)
    {
        var segment = _source.CreateTimelineSegment();
        var cursor = new JournalCursor(Math.Max(segment.StartObservationOrdinal, _nextLoadedObservationOrdinal));
        var result = segment.ReadEntries(cursor, maxCount, reader);
        _nextLoadedObservationOrdinal = result.Cursor.NextObservationOrdinal;
        return result;
    }

    public ScenePlaybackFrame Seek(long positionMilliseconds)
    {
        var projector = CreateProjector();
        _projector = projector;
        return ApplyFrame(projector.AdvanceTo(positionMilliseconds, projector.Segment, projector.TimeRange));
    }

    public ScenePlaybackFrame AdvanceTo(long positionMilliseconds)
    {
        var projector = _projector ??= CreateProjector();
        if (positionMilliseconds < _positionMilliseconds)
            return Seek(positionMilliseconds);

        var segment = _source.CreateTimelineSegment();
        var timeRange = segment.IsLiveGrowing ? ScenePlaybackTimeline.ResolveTimeRange(segment, _source.CreateSnapshot()) : projector.TimeRange;
        return ApplyFrame(projector.AdvanceTo(positionMilliseconds, segment, timeRange));
    }

    internal ScenePlaybackFrame SeekObservationOrdinal(long endObservationOrdinalExclusive)
    {
        var segment = _source.CreateTimelineSegment();
        var target = Math.Clamp(endObservationOrdinalExclusive, segment.StartObservationOrdinal, segment.CurrentEndObservationOrdinalExclusive);
        var projector = _projector;
        if (projector is null || target < _nextLoadedObservationOrdinal)
        {
            projector = CreateProjector();
            _projector = projector;
        }

        var timeRange = segment.IsLiveGrowing ? ScenePlaybackTimeline.ResolveTimeRange(segment, _source.CreateSnapshot()) : projector.TimeRange;
        return ApplyFrame(projector.AdvanceToObservationOrdinal(target, segment, timeRange));
    }

    private ScenePlaybackFrame ApplyFrame(ScenePlaybackFrame frame)
    {
        _positionMilliseconds = frame.PositionMilliseconds;
        _nextLoadedObservationOrdinal = frame.AppliedSegment.EndObservationOrdinalExclusive;
        return frame;
    }

    internal ScenePlaybackCheckpoint CreateCheckpoint()
    {
        if (_projector is null)
        {
            var frame = Seek(_positionMilliseconds);
            return new ScenePlaybackCheckpoint(frame.PositionMilliseconds, new JournalCursor(frame.AppliedSegment.EndObservationOrdinalExclusive));
        }

        return new ScenePlaybackCheckpoint(_positionMilliseconds, new JournalCursor(_nextLoadedObservationOrdinal));
    }

    internal ScenePlaybackCombatantDetail CreateCombatantDetail(int combatantId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(combatantId);
        var projector = _projector ??= CreateProjector();
        return projector.CreateCombatantDetail(combatantId);
    }

    private FrameProjector CreateProjector()
    {
        var segment = _source.CreateTimelineSegment();
        var baseSnapshot = _source.CreateSnapshot();
        var timeRange = ScenePlaybackTimeline.ResolveTimeRange(segment, baseSnapshot);
        return new FrameProjector(_source.EncounterId, baseSnapshot.Kind, segment, timeRange);
    }

    private sealed class FrameProjector
    {
        private readonly Guid _encounterId;
        private readonly SceneKind _kind;
        private readonly EntityStore _entities;
        private readonly SceneBoundaryStore _boundary;
        private readonly RuntimeMetadataRegistry _metadata;
        private readonly CombatStore _combat;
        private readonly DomainEventApplier _applier;
        private readonly SceneCombatSnapshotAdapter _adapter;
        private readonly Dictionary<int, ScenePlaybackResourceState> _resources;
        private readonly Dictionary<ScenePlaybackAuraInstanceKey, ScenePlaybackAuraState> _auraInstances;
        private readonly Dictionary<ScenePlaybackTrack, TrackAccumulator> _tracks;
        private readonly Queue<ScenePlaybackTrackMarker> _recentMarkers;
        private ScenePlaybackTrackMarker[] _recentMarkerSnapshot = [];
        private bool _recentMarkerSnapshotDirty;
        private SceneJournalSegment _segment;
        private ScenePlaybackTimeRange _timeRange;
        private JournalCursor _cursor;
        private long _targetOffsetMilliseconds;
        private long _positionMilliseconds;
        private long _appliedEndOrdinal;
        private long _currentFlushId = -1;
        private long _completedFlushId = -1;
        private int _detailCombatantId;
        private CombatDetailSubscription? _detailSubscription;

        public FrameProjector(Guid encounterId, SceneKind kind, SceneJournalSegment segment, ScenePlaybackTimeRange timeRange)
        {
            _encounterId = encounterId;
            _kind = kind;
            _segment = segment;
            _timeRange = timeRange;
            _entities = new EntityStore();
            _boundary = new SceneBoundaryStore();
            _metadata = new RuntimeMetadataRegistry();
            _combat = new CombatStore();
            _resources = [];
            _auraInstances = [];
            _tracks = [];
            _recentMarkers = new Queue<ScenePlaybackTrackMarker>(DefaultRecentMarkerCapacity);
            _cursor = segment.CreateCursor();
            _targetOffsetMilliseconds = timeRange.HasTiming ? timeRange.StartOffsetMilliseconds : 0;
            _positionMilliseconds = 0;
            _appliedEndOrdinal = segment.StartObservationOrdinal;
            _currentFlushId = -1;
            _completedFlushId = -1;
            _applier = new DomainEventApplier(_entities, _boundary, _metadata, _combat);
            _adapter = new SceneCombatSnapshotAdapter(_entities, _combat, _boundary, _applier.BossFocus, _encounterId);
        }

        public SceneJournalSegment Segment => _segment;

        public ScenePlaybackTimeRange TimeRange => _timeRange;

        public ScenePlaybackCombatantDetail CreateCombatantDetail(int combatantId)
        {
            var snapshot = _adapter.CreateSnapshot(_kind);
            if (_detailSubscription is null || _detailCombatantId != combatantId)
            {
                _detailCombatantId = combatantId;
                _detailSubscription = new CombatDetailSubscription(_combat, combatantId);
            }

            var writer = new PlaybackDetailEventWriter();
            var update = _detailSubscription!.CreateSnapshotUpdate(_adapter, snapshot, CombatDetailProjectionScope.CurrentFrame, writer);
            return new ScenePlaybackCombatantDetail(_positionMilliseconds, _appliedEndOrdinal, snapshot, update, writer.Events);
        }

        public ScenePlaybackFrame AdvanceTo(long positionMilliseconds, SceneJournalSegment segment, ScenePlaybackTimeRange timeRange)
        {
            _segment = segment;
            _timeRange = timeRange;
            _positionMilliseconds = ScenePlaybackTimeline.ClampPosition(positionMilliseconds, _timeRange.DurationMilliseconds);
            _targetOffsetMilliseconds = _timeRange.HasTiming
                ? _timeRange.StartOffsetMilliseconds + _positionMilliseconds
                : _positionMilliseconds;
            if (_cursor.NextObservationOrdinal < _segment.StartObservationOrdinal)
                _cursor = _segment.CreateCursor();
            ApplyEntries();
            return BuildFrame();
        }

        public ScenePlaybackFrame AdvanceToObservationOrdinal(long endObservationOrdinalExclusive, SceneJournalSegment segment, ScenePlaybackTimeRange timeRange)
        {
            _segment = segment;
            _timeRange = timeRange;
            var target = Math.Clamp(endObservationOrdinalExclusive, _segment.StartObservationOrdinal, _segment.CurrentEndObservationOrdinalExclusive);
            if (_cursor.NextObservationOrdinal < _segment.StartObservationOrdinal)
                _cursor = _segment.CreateCursor();
            ApplyEntriesToObservationOrdinal(target);
            ResolvePositionAtObservationBoundary(target);
            return BuildFrame();
        }

        private ScenePlaybackFrame BuildFrame()
        {
            var snapshot = _adapter.CreateSnapshot(_kind);
            var totals = CreateTotals(snapshot);
            return new ScenePlaybackFrame
            {
                EncounterId = _encounterId,
                PositionMilliseconds = _positionMilliseconds,
                TimeRange = _timeRange,
                AppliedSegment = new SceneJournalSegment(_segment.Journal, _segment.StartObservationOrdinal, _appliedEndOrdinal, IsLiveGrowing: false),
                Snapshot = snapshot,
                CombatTotals = totals,
                Resources = CreateResourceSnapshot(),
                ActiveAuras = CreateActiveAuraSnapshot(),
                Tracks = CreateTrackSnapshot(),
                RecentMarkers = CreateRecentMarkerSnapshot()
            };
        }

        private void ApplyEntries()
        {
            while (true)
            {
                if (_cursor.NextObservationOrdinal >= _segment.CurrentEndObservationOrdinalExclusive)
                    break;

                var stoppedAtTarget = false;
                var appliedAny = false;
                var result = _segment.ReadEntries(_cursor, ScenePlaybackTimeline.DefaultReadBatchSize, entries =>
                {
                    foreach (ref readonly var entry in entries)
                    {
                        var offset = ScenePlaybackTimeline.ResolveOffsetMilliseconds(in entry);
                        if (_timeRange.HasTiming && offset > _targetOffsetMilliseconds)
                        {
                            stoppedAtTarget = true;
                            return;
                        }

                        ApplyEntry(in entry, offset);
                        appliedAny = true;
                    }
                });

                if (result.Count == 0 || stoppedAtTarget || !appliedAny)
                    break;
            }

            TryCompleteCurrentFlushAtSegmentEnd();
        }

        private void ApplyEntriesToObservationOrdinal(long endObservationOrdinalExclusive)
        {
            while (_cursor.NextObservationOrdinal < endObservationOrdinalExclusive)
            {
                var stoppedAtTarget = false;
                var appliedAny = false;
                var result = _segment.ReadEntries(_cursor, ScenePlaybackTimeline.DefaultReadBatchSize, entries =>
                {
                    foreach (ref readonly var entry in entries)
                    {
                        if (entry.Stamp.ObservationOrdinal >= endObservationOrdinalExclusive)
                        {
                            stoppedAtTarget = true;
                            return;
                        }

                        var offset = ScenePlaybackTimeline.ResolveOffsetMilliseconds(in entry);
                        ApplyEntry(in entry, offset);
                        appliedAny = true;
                    }
                });

                if (result.Count == 0 || stoppedAtTarget || !appliedAny)
                    break;
            }

            TryCompleteCurrentFlushAtSegmentEnd();
        }

        private void ResolvePositionAtObservationBoundary(long endObservationOrdinalExclusive)
        {
            if (endObservationOrdinalExclusive <= _segment.StartObservationOrdinal)
            {
                _positionMilliseconds = 0;
                _targetOffsetMilliseconds = _timeRange.HasTiming ? _timeRange.StartOffsetMilliseconds : 0;
                return;
            }

            var offset = 0L;
            _segment.ReadEntries(new JournalCursor(endObservationOrdinalExclusive - 1), 1, entries =>
            {
                if (entries.Length > 0)
                    offset = ScenePlaybackTimeline.ResolveOffsetMilliseconds(in entries[0]);
            });
            _targetOffsetMilliseconds = offset;
            _positionMilliseconds = Math.Max(0, offset);
        }

        private void ApplyEntry(in ObservedEventEnvelope entry, long offset)
        {
            var flushId = entry.Stamp.FlushId;
            if (_currentFlushId >= 0 && flushId != _currentFlushId)
                CompleteFlush(_currentFlushId);

            _currentFlushId = flushId;
            _applier.ApplyEntry(in entry);
            ApplyFrameTracks(in entry, offset);
            _appliedEndOrdinal = entry.Stamp.ObservationOrdinal + 1;
            _cursor = new JournalCursor(_appliedEndOrdinal);
        }

        private void CompleteFlush(long flushId)
        {
            if (flushId <= 0 || flushId <= _completedFlushId)
                return;

            _applier.CompleteFlush();
            _completedFlushId = flushId;
        }

        private void TryCompleteCurrentFlushAtSegmentEnd()
        {
            if (_currentFlushId <= 0 || _cursor.NextObservationOrdinal < _segment.CurrentEndObservationOrdinalExclusive)
                return;

            if (_segment.IsLiveGrowing && (_segment.Journal?.LastCompletedFlushId ?? -1) < _currentFlushId)
                return;

            CompleteFlush(_currentFlushId);
        }

        private void ApplyFrameTracks(in ObservedEventEnvelope entry, long offset)
        {
            var lifecycleEventKind = ResolveLifecycleEventKind(in entry);
            var marker = ScenePlaybackTrackProjection.CreateMarker(in entry, offset, Math.Max(0, offset), lifecycleEventKind);
            var track = marker.Track;
            ref var accumulator = ref CollectionsMarshal.GetValueRefOrAddDefault(_tracks, track, out var exists);
            if (!exists)
                accumulator = new TrackAccumulator(entry.Stamp.ObservationOrdinal);
            accumulator.Apply(entry.Stamp.ObservationOrdinal);

            if (entry.Domain == ObservedEventDomain.Resource && entry.Resource is { } resource)
            {
                var maximumValue = ResolveResourceMaximum(in resource);
                marker = marker with { MaximumValue = maximumValue };
                _resources[resource.EntityId] = new ScenePlaybackResourceState(
                    resource.EntityId,
                    resource.CurrentValue,
                    maximumValue,
                    resource.Delta,
                    resource.ResourceKind,
                    offset,
                    entry.Stamp.ObservationOrdinal);
            }
            else if (entry.Domain == ObservedEventDomain.Aura && entry.Aura is { } aura)
            {
                var key = new ScenePlaybackAuraInstanceKey(aura.EntityId, aura.InstanceSequenceId);
                if (lifecycleEventKind == ScenePlaybackLifecycleEventKind.Result)
                {
                    _auraInstances.Remove(key);
                }
                else if (lifecycleEventKind == ScenePlaybackLifecycleEventKind.Open)
                {
                    _auraInstances[key] = new ScenePlaybackAuraState(aura.EntityId, aura.EchoSourceEntityId, aura.InstanceSequenceId, aura.StackCount, aura.OpenMode, aura.GroupCode, aura.HeadValue, aura.BuffResourceEffectRef, offset, offset, ResolveExpiration(offset, aura.HeadValue), entry.Stamp.ObservationOrdinal, entry.Stamp.ObservationOrdinal);
                }
                else if (aura.Kind == AuraObservationKind.Open)
                {
                    _auraInstances.Remove(key);
                }
            }
            else if (lifecycleEventKind == ScenePlaybackLifecycleEventKind.Renew && entry.Action is { } renewal)
            {
                var key = new ScenePlaybackAuraInstanceKey(renewal.SourceEntityId, renewal.InstanceSequenceId);
                var active = _auraInstances[key];
                _auraInstances[key] = active with
                {
                    RenewedAtMilliseconds = offset,
                    ExpiresAtMilliseconds = ResolveExpiration(offset, active.DurationMilliseconds),
                    LastObservationOrdinal = entry.Stamp.ObservationOrdinal
                };
            }

            AddRecentMarker(marker);
        }

        private ScenePlaybackLifecycleEventKind ResolveLifecycleEventKind(in ObservedEventEnvelope entry)
        {
            if (entry.Aura is { } aura)
            {
                if (aura.Kind == AuraObservationKind.Open)
                    return ScenePlaybackAuraProtocol.IsTrackableOpen(in aura)
                        ? ScenePlaybackLifecycleEventKind.Open
                        : ScenePlaybackLifecycleEventKind.None;

                return _auraInstances.ContainsKey(new ScenePlaybackAuraInstanceKey(aura.EntityId, aura.InstanceSequenceId))
                    ? ScenePlaybackLifecycleEventKind.Result
                    : ScenePlaybackLifecycleEventKind.None;
            }

            if (entry.Action is not { } action ||
                !ScenePlaybackAuraProtocol.IsRenewal(in action))
                return ScenePlaybackLifecycleEventKind.None;

            return _auraInstances.ContainsKey(new ScenePlaybackAuraInstanceKey(action.SourceEntityId, action.InstanceSequenceId))
                ? ScenePlaybackLifecycleEventKind.Renew
                : ScenePlaybackLifecycleEventKind.None;
        }

        private long? ResolveResourceMaximum(in ResourceObservation resource)
        {
            var maximumValue = resource.MaximumValue;
            if (_entities.TryGet(resource.EntityId, out var entity) && entity.MaxHp is long entityMaxHp)
                maximumValue = maximumValue.HasValue ? Math.Max(maximumValue.Value, entityMaxHp) : entityMaxHp;

            return maximumValue;
        }

        private void AddRecentMarker(ScenePlaybackTrackMarker marker)
        {
            _recentMarkers.Enqueue(marker);
            while (_recentMarkers.Count > DefaultRecentMarkerCapacity)
                _recentMarkers.Dequeue();
            _recentMarkerSnapshotDirty = true;
        }

        private static long? ResolveExpiration(long renewedAtMilliseconds, ushort durationMilliseconds)
            => durationMilliseconds == ushort.MaxValue ? null : renewedAtMilliseconds + durationMilliseconds;

        private ScenePlaybackCombatTotals CreateTotals(SceneCombatSnapshot snapshot)
        {
            var totalDamage = 0L;
            var totalHealing = 0L;
            var totalShield = 0L;
            var totalShieldAbsorbed = 0L;
            var combatants = snapshot.Combatants.AsSpan();
            foreach (ref readonly var combatant in combatants)
            {
                totalDamage += combatant.Metrics.DamageAmount;
                totalHealing += combatant.Metrics.HealingAmount;
                totalShield += combatant.Metrics.ShieldAmount;
                totalShieldAbsorbed += combatant.Metrics.ShieldAbsorbedAmount;
            }

            var elapsed = snapshot.EncounterTime > 0 ? snapshot.EncounterTime : Math.Max(0, _targetOffsetMilliseconds - _timeRange.StartOffsetMilliseconds);
            var dps = elapsed > 0 ? (double)totalDamage / elapsed * 1000 : 0d;
            var hps = elapsed > 0 ? (double)totalHealing / elapsed * 1000 : 0d;
            return new ScenePlaybackCombatTotals(totalDamage, totalHealing, totalShield, totalShieldAbsorbed, dps, hps, elapsed);
        }

        private ScenePlaybackResourceState[] CreateResourceSnapshot()
        {
            if (_resources.Count == 0)
                return [];

            var result = new ScenePlaybackResourceState[_resources.Count];
            var index = 0;
            foreach (var state in _resources.Values)
                result[index++] = state;
            Array.Sort(result, static (left, right) => left.EntityId.CompareTo(right.EntityId));
            return result;
        }

        private ScenePlaybackAuraState[] CreateAuraInstanceSnapshot()
        {
            if (_auraInstances.Count == 0)
                return [];

            var result = new ScenePlaybackAuraState[_auraInstances.Count];
            var index = 0;
            foreach (var state in _auraInstances.Values)
                result[index++] = state;
            Array.Sort(result, static (left, right) =>
            {
                var cmp = left.EntityId.CompareTo(right.EntityId);
                if (cmp != 0)
                    return cmp;
                return left.InstanceSequenceId.CompareTo(right.InstanceSequenceId);
            });
            return result;
        }

        private ScenePlaybackAuraState[] CreateActiveAuraSnapshot()
        {
            if (_auraInstances.Count == 0)
                return [];

            var count = 0;
            foreach (var pair in _auraInstances)
            {
                if (IsActive(pair.Value))
                    count++;
            }

            if (count == 0)
                return [];

            var result = new ScenePlaybackAuraState[count];
            var index = 0;
            foreach (var state in _auraInstances.Values)
            {
                if (IsActive(state))
                    result[index++] = state;
            }
            Array.Sort(result, static (left, right) =>
            {
                var cmp = left.EntityId.CompareTo(right.EntityId);
                return cmp != 0 ? cmp : left.InstanceSequenceId.CompareTo(right.InstanceSequenceId);
            });
            return result;
        }

        private bool IsActive(ScenePlaybackAuraState state) => state.ExpiresAtMilliseconds is not long expiresAt || expiresAt > _targetOffsetMilliseconds;

        private ScenePlaybackTrackWindow[] CreateTrackSnapshot()
        {
            if (_tracks.Count == 0)
                return [];

            var result = new ScenePlaybackTrackWindow[_tracks.Count];
            var index = 0;
            foreach (var (track, accumulator) in _tracks)
                result[index++] = accumulator.ToWindow(track);
            Array.Sort(result, static (left, right) => left.Track.CompareTo(right.Track));
            return result;
        }

        private ScenePlaybackTrackMarker[] CreateRecentMarkerSnapshot()
        {
            if (_recentMarkers.Count == 0)
                return [];

            if (!_recentMarkerSnapshotDirty)
                return _recentMarkerSnapshot;

            _recentMarkerSnapshot = _recentMarkers.ToArray();
            _recentMarkerSnapshotDirty = false;
            return _recentMarkerSnapshot;
        }

        private sealed class PlaybackDetailEventWriter : ICombatDetailEventWriter
        {
            private readonly List<CombatDetailEvent> _events = [];

            public IReadOnlyList<CombatDetailEvent> Events => _events;

            public void Clear() => _events.Clear();

            public void Add(in CombatDetailEvent detailEvent) => _events.Add(detailEvent);
        }
    }

    private struct TrackAccumulator(long firstOrdinal)
    {
        private long _firstOrdinal = firstOrdinal;
        private long _lastOrdinal = firstOrdinal;
        private int _count;

        public void Apply(long ordinal)
        {
            _firstOrdinal = Math.Min(_firstOrdinal, ordinal);
            _lastOrdinal = Math.Max(_lastOrdinal, ordinal);
            _count++;
        }

        public readonly ScenePlaybackTrackWindow ToWindow(ScenePlaybackTrack track) => new(track, _firstOrdinal, _lastOrdinal + 1, _count);

    }
}
