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
    private readonly IScenePlaybackSource _source;
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
        var segment = _source.CreateTimelineSegment();
        var baseSnapshot = _source.CreateSnapshot();
        var timeRange = ResolveTimeRange(segment, baseSnapshot);
        var clamped = ClampPosition(positionMilliseconds, timeRange.DurationMilliseconds);
        var targetTimestamp = timeRange.HasTimestamps
            ? timeRange.StartTimestampMilliseconds + clamped
            : clamped;
        var projector = new FrameProjector(_source.EncounterId, segment, timeRange, targetTimestamp);
        var frame = projector.BuildFrame(clamped);
        _positionMilliseconds = clamped;
        _nextLoadedObservationOrdinal = frame.AppliedSegment.EndObservationOrdinalExclusive;
        return frame;
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
        private readonly SceneJournalSegment _segment;
        private readonly ScenePlaybackTimeRange _timeRange;
        private readonly long _targetTimestamp;
        private readonly EntityStore _entities = new();
        private readonly SceneBoundaryStore _boundary = new();
        private readonly RuntimeMetadataRegistry _metadata = new();
        private readonly CombatStore _combat = new();
        private readonly DomainEventApplier _applier;
        private readonly Dictionary<int, ScenePlaybackResourceState> _resources = [];
        private readonly Dictionary<AuraKey, ScenePlaybackAuraState> _activeAuras = [];
        private readonly Dictionary<ScenePlaybackTrack, TrackAccumulator> _tracks = [];
        private long _appliedEndOrdinal;

        public FrameProjector(Guid encounterId, SceneJournalSegment segment, ScenePlaybackTimeRange timeRange, long targetTimestamp)
        {
            _encounterId = encounterId;
            _segment = segment;
            _timeRange = timeRange;
            _targetTimestamp = targetTimestamp;
            _appliedEndOrdinal = segment.StartObservationOrdinal;
            _applier = new DomainEventApplier(_entities, _boundary, _metadata, _combat);
        }

        public ScenePlaybackFrame BuildFrame(long positionMilliseconds)
        {
            ApplyEntries();
            var adapter = new SceneCombatSnapshotAdapter(_entities, _combat, _boundary, _applier.BossFocus, _encounterId);
            var snapshot = adapter.CreateSnapshot();
            var totals = CreateTotals(snapshot);
            return new ScenePlaybackFrame
            {
                EncounterId = _encounterId,
                PositionMilliseconds = positionMilliseconds,
                PositionTimestampMilliseconds = _timeRange.HasTimestamps ? _targetTimestamp : positionMilliseconds,
                TimeRange = _timeRange,
                AppliedSegment = new SceneJournalSegment(_segment.Journal, _segment.StartObservationOrdinal, _appliedEndOrdinal, IsLiveGrowing: false),
                Snapshot = snapshot,
                CombatTotals = totals,
                Resources = CreateResourceSnapshot(),
                ActiveAuras = CreateAuraSnapshot(),
                Tracks = CreateTrackSnapshot()
            };
        }

        private void ApplyEntries()
        {
            var cursor = _segment.CreateCursor();
            long currentBatchOrdinal = -1;
            long completeBatchOrdinal = -1;
            while (true)
            {
                var shouldContinue = true;
                var result = _segment.ReadEntries(cursor, DefaultReadBatchSize, entries =>
                {
                    foreach (ref readonly var entry in entries)
                    {
                        var timestamp = ResolveTimestampMilliseconds(in entry);
                        if (_timeRange.HasTimestamps && timestamp > _targetTimestamp)
                        {
                            shouldContinue = false;
                            return;
                        }

                        if (currentBatchOrdinal >= 0 && entry.Stamp.BatchOrdinal != currentBatchOrdinal)
                            completeBatchOrdinal = currentBatchOrdinal;
                        currentBatchOrdinal = entry.Stamp.BatchOrdinal;
                        _applier.ApplyEntry(in entry);
                        ApplyFrameTracks(in entry, timestamp);
                        _appliedEndOrdinal = entry.Stamp.ObservationOrdinal + 1;
                    }
                });

                if (result.Count == 0 || !shouldContinue)
                    break;

                cursor = result.Cursor;
            }

            if (_appliedEndOrdinal >= _segment.CurrentEndObservationOrdinalExclusive)
                completeBatchOrdinal = currentBatchOrdinal;
            if (completeBatchOrdinal >= 0)
                _applier.CompleteBatch(completeBatchOrdinal);
        }

        private void ApplyFrameTracks(in ObservedEventEnvelope entry, long timestamp)
        {
            var track = ResolveTrack(entry.Domain);
            ref var accumulator = ref CollectionsMarshal.GetValueRefOrAddDefault(_tracks, track, out var exists);
            if (!exists)
                accumulator = new TrackAccumulator(entry.Stamp.ObservationOrdinal);
            accumulator.Apply(entry.Stamp.ObservationOrdinal);

            if (entry.Domain == ObservedEventDomain.Resource && entry.Resource is { } resource)
            {
                _resources[resource.EntityId] = new ScenePlaybackResourceState(
                    resource.EntityId,
                    resource.CurrentValue,
                    resource.MaximumValue,
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

                _activeAuras[key] = new ScenePlaybackAuraState(
                    aura.SourceEntityId,
                    aura.TargetEntityId,
                    aura.SkillCode,
                    aura.StackCount,
                    aura.SequenceId,
                    aura.ChainId,
                    aura.ResultCode,
                    aura.Mode,
                    timestamp,
                    entry.Stamp.ObservationOrdinal);
            }
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
    }

    private readonly record struct AuraKey(int TargetEntityId, int SequenceId, int SkillCode, int ChainId);

    private struct TrackAccumulator(long firstOrdinal)
    {
        private int _count = 0;

        public void Apply(long ordinal)
        {
            firstOrdinal = Math.Min(firstOrdinal, ordinal);
            firstOrdinal = Math.Max(firstOrdinal, ordinal);
            _count++;
        }

        public readonly ScenePlaybackTrackWindow ToWindow(ScenePlaybackTrack track) => new(track, firstOrdinal, firstOrdinal + 1, _count);
    }
}
