using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

public sealed class ScenePlaybackSession
{
    private const int DefaultReadBatchSize = 512;
    private const int DefaultRecentMarkerCapacity = 512;
    private readonly IScenePlaybackSource _source;
    private FrameProjector? _projector;
    private long _nextLoadedObservationOrdinal;
    private long _positionMilliseconds;
    private double _speed = 1d;
    private bool _isPlaying;

    public ScenePlaybackSession(IScenePlaybackSource source)
    {
        _source = source;
        var segment = _source.CreateTimelineSegment();
        _nextLoadedObservationOrdinal = segment.StartObservationOrdinal;
    }

    public IScenePlaybackSource Source => _source;

    public long NextLoadedObservationOrdinal => _nextLoadedObservationOrdinal;

    public long PositionMilliseconds => _positionMilliseconds;

    public double Speed => _speed;

    public bool IsPlaying => _isPlaying;

    public void Play() => _isPlaying = true;

    public void Pause() => _isPlaying = false;

    public void SetSpeed(double speed)
    {
        if (!double.IsFinite(speed) || speed <= 0)
            throw new ArgumentOutOfRangeException(nameof(speed));

        _speed = speed;
    }

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
        var timeRange = segment.IsLiveGrowing ? ResolveTimeRange(segment, _source.CreateSnapshot()) : projector.TimeRange;
        return ApplyFrame(projector.AdvanceTo(positionMilliseconds, segment, timeRange));
    }

    private ScenePlaybackFrame ApplyFrame(ScenePlaybackFrame frame)
    {
        _positionMilliseconds = frame.PositionMilliseconds;
        _nextLoadedObservationOrdinal = frame.AppliedSegment.EndObservationOrdinalExclusive;
        return frame;
    }

    private FrameProjector CreateProjector()
    {
        var segment = _source.CreateTimelineSegment();
        var baseSnapshot = _source.CreateSnapshot();
        var timeRange = ResolveTimeRange(segment, baseSnapshot);
        return new FrameProjector(_source.EncounterId, segment, timeRange);
    }

    private static long ClampPosition(long positionMilliseconds, long durationMilliseconds)
    {
        if (positionMilliseconds <= 0)
            return 0;

        return durationMilliseconds > 0 ? Math.Min(positionMilliseconds, durationMilliseconds) : positionMilliseconds;
    }

    private static ScenePlaybackTimeRange ResolveTimeRange(SceneJournalSegment segment, SceneCombatSnapshot snapshot)
    {
        var start = long.MaxValue;
        var end = long.MinValue;
        var cursor = segment.CreateCursor();
        while (true)
        {
            var result = segment.ReadEntries(cursor, DefaultReadBatchSize, entries =>
            {
                foreach (ref readonly var entry in entries)
                {
                    var timestamp = ResolveTimestampMilliseconds(in entry);
                    if (timestamp <= 0)
                        continue;

                    start = Math.Min(start, timestamp);
                    end = Math.Max(end, timestamp);
                }
            });

            if (result.Count == 0)
                break;

            cursor = result.Cursor;
        }

        if (start != long.MaxValue && end != long.MinValue)
            return new ScenePlaybackTimeRange(start, end, Math.Max(0, end - start), true);

        if (snapshot.EncounterStartTime > 0)
        {
            var fallbackEnd = snapshot.EncounterEndTime >= snapshot.EncounterStartTime ? snapshot.EncounterEndTime : snapshot.EncounterStartTime;
            return new ScenePlaybackTimeRange(snapshot.EncounterStartTime, fallbackEnd, Math.Max(0, fallbackEnd - snapshot.EncounterStartTime), true);
        }

        return default;
    }

    private static long ResolveTimestampMilliseconds(in ObservedEventEnvelope entry)
    {
        if (entry.Raw.TimestampMilliseconds > 0)
            return entry.Raw.TimestampMilliseconds;

        return entry.Stamp.OffsetTicks > 0 ? entry.Stamp.OffsetTicks / TimeSpan.TicksPerMillisecond : 0;
    }

    private sealed class FrameProjector
    {
        private readonly Guid _encounterId;
        private readonly EntityStore _entities = new();
        private readonly SceneBoundaryStore _boundary = new();
        private readonly RuntimeMetadataRegistry _metadata = new();
        private readonly CombatStore _combat = new();
        private readonly DomainEventApplier _applier;
        private readonly SceneCombatSnapshotAdapter _adapter;
        private readonly Dictionary<int, ScenePlaybackResourceState> _resources = [];
        private readonly Dictionary<AuraKey, ScenePlaybackAuraState> _activeAuras = [];
        private readonly Dictionary<ScenePlaybackTrack, TrackAccumulator> _tracks = [];
        private readonly Queue<ScenePlaybackTrackMarker> _recentMarkers = new(DefaultRecentMarkerCapacity);
        private SceneJournalSegment _segment;
        private ScenePlaybackTimeRange _timeRange;
        private JournalCursor _cursor;
        private long _targetTimestamp;
        private long _positionMilliseconds;
        private long _appliedEndOrdinal;
        private long _currentBatchOrdinal = -1;
        private long _completedBatchOrdinal = -1;

        public FrameProjector(Guid encounterId, SceneJournalSegment segment, ScenePlaybackTimeRange timeRange)
        {
            _encounterId = encounterId;
            _segment = segment;
            _timeRange = timeRange;
            _cursor = segment.CreateCursor();
            _appliedEndOrdinal = segment.StartObservationOrdinal;
            _applier = new DomainEventApplier(_entities, _boundary, _metadata, _combat);
            _adapter = new SceneCombatSnapshotAdapter(_entities, _combat, _boundary, _applier.BossFocus, _encounterId);
        }

        public SceneJournalSegment Segment => _segment;

        public ScenePlaybackTimeRange TimeRange => _timeRange;

        public ScenePlaybackFrame AdvanceTo(long positionMilliseconds, SceneJournalSegment segment, ScenePlaybackTimeRange timeRange)
        {
            _segment = segment;
            _timeRange = timeRange;
            _positionMilliseconds = ClampPosition(positionMilliseconds, _timeRange.DurationMilliseconds);
            _targetTimestamp = _timeRange.HasTimestamps
                ? _timeRange.StartTimestampMilliseconds + _positionMilliseconds
                : _positionMilliseconds;
            if (_cursor.NextObservationOrdinal < _segment.StartObservationOrdinal)
                _cursor = _segment.CreateCursor();
            ApplyEntries();
            return BuildFrame();
        }

        private ScenePlaybackFrame BuildFrame()
        {
            var snapshot = _adapter.CreateSnapshot();
            var totals = CreateTotals(snapshot);
            return new ScenePlaybackFrame
            {
                EncounterId = _encounterId,
                PositionMilliseconds = _positionMilliseconds,
                PositionTimestampMilliseconds = _timeRange.HasTimestamps ? _targetTimestamp : _positionMilliseconds,
                TimeRange = _timeRange,
                AppliedSegment = new SceneJournalSegment(_segment.Journal, _segment.StartObservationOrdinal, _appliedEndOrdinal, IsLiveGrowing: false),
                Snapshot = snapshot,
                CombatTotals = totals,
                Resources = CreateResourceSnapshot(),
                ActiveAuras = CreateAuraSnapshot(),
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
                var result = _segment.ReadEntries(_cursor, DefaultReadBatchSize, entries =>
                {
                    foreach (ref readonly var entry in entries)
                    {
                        var timestamp = ResolveTimestampMilliseconds(in entry);
                        if (_timeRange.HasTimestamps && timestamp > _targetTimestamp)
                        {
                            stoppedAtTarget = true;
                            return;
                        }

                        ApplyEntry(in entry, timestamp);
                        appliedAny = true;
                    }
                });

                if (result.Count == 0 || stoppedAtTarget || !appliedAny)
                    break;
            }

            TryCompleteCurrentBatchAtSegmentEnd();
        }

        private void ApplyEntry(in ObservedEventEnvelope entry, long timestamp)
        {
            var batchOrdinal = entry.Stamp.BatchOrdinal;
            if (_currentBatchOrdinal >= 0 && batchOrdinal != _currentBatchOrdinal)
                CompleteBatch(_currentBatchOrdinal);

            _currentBatchOrdinal = batchOrdinal;
            _applier.ApplyEntry(in entry);
            ApplyFrameTracks(in entry, timestamp);
            _appliedEndOrdinal = entry.Stamp.ObservationOrdinal + 1;
            _cursor = new JournalCursor(_appliedEndOrdinal);
        }

        private void CompleteBatch(long batchOrdinal)
        {
            if (batchOrdinal <= 0 || batchOrdinal <= _completedBatchOrdinal)
                return;

            _applier.CompleteBatch(batchOrdinal);
            _completedBatchOrdinal = batchOrdinal;
        }

        private void TryCompleteCurrentBatchAtSegmentEnd()
        {
            if (_currentBatchOrdinal <= 0 || _cursor.NextObservationOrdinal < _segment.CurrentEndObservationOrdinalExclusive)
                return;

            if (_segment.IsLiveGrowing && (_segment.Journal?.LastCompletedBatchOrdinal ?? -1) < _currentBatchOrdinal)
                return;

            CompleteBatch(_currentBatchOrdinal);
        }

        private void ApplyFrameTracks(in ObservedEventEnvelope entry, long timestamp)
        {
            var track = ResolveTrack(entry.Domain);
            ref var accumulator = ref CollectionsMarshal.GetValueRefOrAddDefault(_tracks, track, out var exists);
            if (!exists)
                accumulator = new TrackAccumulator(entry.Stamp.ObservationOrdinal);
            accumulator.Apply(entry.Stamp.ObservationOrdinal);
            AddRecentMarker(CreateMarker(in entry, track, timestamp));

            if (entry.Domain == ObservedEventDomain.Resource && entry.Resource is { } resource)
            {
                var maximumValue = ResolveResourceMaximum(in resource);
                _resources[resource.EntityId] = new ScenePlaybackResourceState(
                    resource.EntityId,
                    resource.CurrentValue,
                    maximumValue,
                    resource.Delta,
                    resource.ResourceKind,
                    timestamp,
                    entry.Stamp.ObservationOrdinal);
                return;
            }

            if (entry.Domain == ObservedEventDomain.Aura && entry.Aura is { } aura)
            {
                var key = new AuraKey(aura.TargetEntityId, aura.SequenceId, aura.SkillCode, aura.ChainId);
                if (IsAuraRemoval(in aura))
                {
                    _activeAuras.Remove(key);
                    return;
                }

                _activeAuras[key] = new ScenePlaybackAuraState(aura.SourceEntityId, aura.TargetEntityId, aura.SkillCode, aura.StackCount, aura.SequenceId, aura.ChainId, aura.ResultCode, aura.Mode, timestamp, entry.Stamp.ObservationOrdinal);
            }
        }

        private long? ResolveResourceMaximum(in ResourceObservation resource)
        {
            var maximumValue = resource.MaximumValue;
            if (_entities.TryGet(resource.EntityId, out var entity) && entity.MaxHp is int entityMaxHp)
                maximumValue = maximumValue.HasValue ? Math.Max(maximumValue.Value, entityMaxHp) : entityMaxHp;

            if (maximumValue.HasValue && resource.CurrentValue.HasValue)
                return Math.Max(maximumValue.Value, resource.CurrentValue.Value);

            return maximumValue;
        }

        private void AddRecentMarker(ScenePlaybackTrackMarker marker)
        {
            _recentMarkers.Enqueue(marker);
            while (_recentMarkers.Count > DefaultRecentMarkerCapacity)
                _recentMarkers.Dequeue();
        }

        private ScenePlaybackTrackMarker CreateMarker(in ObservedEventEnvelope entry, ScenePlaybackTrack track, long timestamp)
        {
            var skillCode = 0;
            var amount = 0L;
            long? currentValue = null;
            long? maximumValue = null;
            var resourceKind = 0;
            var resultCode = 0;
            if (entry.Combat is { } combat)
            {
                skillCode = combat.SkillCode;
                amount = combat.Damage;
            }
            else if (entry.Resource is { } resource)
            {
                currentValue = resource.CurrentValue;
                maximumValue = ResolveResourceMaximum(in resource);
                resourceKind = resource.ResourceKind;
                amount = resource.Delta ?? 0;
            }
            else if (entry.Aura is { } aura)
            {
                skillCode = aura.SkillCode;
                resultCode = aura.ResultCode;
            }

            return new ScenePlaybackTrackMarker(track, ResolvePositionMilliseconds(timestamp), timestamp, entry.Stamp.ObservationOrdinal, entry.SourceEntityId, entry.TargetEntityId, skillCode, amount, currentValue, maximumValue, resourceKind, resultCode);
        }

        private long ResolvePositionMilliseconds(long timestamp)
        {
            if (!_timeRange.HasTimestamps || timestamp <= 0)
                return Math.Max(0, timestamp);

            return Math.Max(0, timestamp - _timeRange.StartTimestampMilliseconds);
        }

        private static ScenePlaybackTrack ResolveTrack(ObservedEventDomain domain) => domain switch
        {
            ObservedEventDomain.Combat => ScenePlaybackTrack.Combat,
            ObservedEventDomain.Resource => ScenePlaybackTrack.Resource,
            ObservedEventDomain.Aura => ScenePlaybackTrack.Aura,
            ObservedEventDomain.Scene => ScenePlaybackTrack.Scene,
            ObservedEventDomain.State => ScenePlaybackTrack.State,
            ObservedEventDomain.Diagnostic => ScenePlaybackTrack.Diagnostic,
            ObservedEventDomain.Action => ScenePlaybackTrack.Action,
            _ => ScenePlaybackTrack.Other
        };

        private static bool IsAuraRemoval(in AuraObservation aura) => aura.Mode == 1 || aura.ResultCode != 0 && aura.StackCount <= 0;

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

            var elapsed = snapshot.EncounterTime > 0 ? snapshot.EncounterTime : Math.Max(0, _targetTimestamp - _timeRange.StartTimestampMilliseconds);
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

        private ScenePlaybackAuraState[] CreateAuraSnapshot()
        {
            if (_activeAuras.Count == 0)
                return [];

            var result = new ScenePlaybackAuraState[_activeAuras.Count];
            var index = 0;
            foreach (var state in _activeAuras.Values)
                result[index++] = state;
            Array.Sort(result, static (left, right) =>
            {
                var cmp = left.TargetEntityId.CompareTo(right.TargetEntityId);
                if (cmp != 0)
                    return cmp;
                cmp = left.SequenceId.CompareTo(right.SequenceId);
                return cmp != 0 ? cmp : left.SkillCode.CompareTo(right.SkillCode);
            });
            return result;
        }

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

            return _recentMarkers.ToArray();
        }
    }

    private readonly record struct AuraKey(int TargetEntityId, int SequenceId, int SkillCode, int ChainId);

    private struct TrackAccumulator
    {
        private long _firstOrdinal;
        private long _lastOrdinal;
        private int _count;

        public TrackAccumulator(long firstOrdinal)
        {
            _firstOrdinal = firstOrdinal;
            _lastOrdinal = firstOrdinal;
            _count = 0;
        }

        public void Apply(long ordinal)
        {
            _firstOrdinal = Math.Min(_firstOrdinal, ordinal);
            _lastOrdinal = Math.Max(_lastOrdinal, ordinal);
            _count++;
        }

        public readonly ScenePlaybackTrackWindow ToWindow(ScenePlaybackTrack track) => new(track, _firstOrdinal, _lastOrdinal + 1, _count);
    }
}
