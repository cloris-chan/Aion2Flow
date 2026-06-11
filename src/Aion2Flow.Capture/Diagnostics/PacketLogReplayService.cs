using System.Diagnostics;
using System.Globalization;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Runtime;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Capture.Diagnostics;

public sealed class PacketLogReplayService
{
    public static PacketLogReplayResult Replay(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var reader = File.OpenText(path);
        return Replay(reader, path);
    }

    public static IReadOnlyList<PacketLogReplayResult> ReplayMany(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var results = new List<PacketLogReplayResult>();
        foreach (var path in paths)
        {
            results.Add(Replay(path));
        }

        return results;
    }

    public static PacketLogReplayResult Replay(TextReader reader, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        if (TryDetectLogKindFromSourceName(sourceName, out var sourceLogKind))
        {
            return sourceLogKind switch
            {
                ReplayLogKind.Stream => ReplayStreamLines(ReadLines(reader), sourceName),
                ReplayLogKind.Raw => throw new NotSupportedException("Raw log replay is not supported yet. Use stream logs for whole-encounter replay."),
                _ => throw new InvalidOperationException($"Unsupported replay log kind: {sourceLogKind}.")
            };
        }

        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        var logKind = DetectLogKind(lines, sourceName);
        return logKind switch
        {
            ReplayLogKind.Stream => ReplayStreamLines(lines, sourceName),
            ReplayLogKind.Raw => throw new NotSupportedException("Raw log replay is not supported yet. Use stream logs for whole-encounter replay."),
            _ => throw new NotSupportedException("Only stream log replay is supported. Raw logs are not supported yet.")
        };
    }

    private static IEnumerable<string> ReadLines(TextReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static PacketLogReplayResult ReplayStreamLines(IEnumerable<string> lines, string sourceName)
    {
        var journal = new ObservedEventJournal(lines is ICollection<string> collection ? ResolveJournalCapacity(collection.Count) : 16_384);
        var sceneId = Guid.NewGuid();
        var sceneStarted = DateTimeOffset.UtcNow;
        var clock = new SceneRuntimeClock(sceneStarted.ToUnixTimeMilliseconds());
        var metadataRegistry = new RuntimeMetadataRegistry();
        var owner = new SceneReadModelOwner(journal, sceneId, sceneStarted, metadataRegistry);
        long nextBatchOrdinal = 0;
        var replayedEventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var skippedEventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var inboundProcessors = new Dictionary<TcpConnection, ReplayConnectionProcessor>();
        var totalLines = 0;
        var replayedLines = 0;
        var skippedLines = 0;
        var lastParsedConnection = default(TcpConnection);
        var hasLastParsedConnection = false;
        var hasSceneStarted = false;
        var ingestStart = CaptureBaselineStart();

        foreach (var line in lines)
        {
            totalLines++;
            if (!TryParseStreamEntry(line, out var entry))
            {
                IncrementCount(skippedEventCounts, "<invalid>");
                skippedLines++;
                continue;
            }

            if (string.Equals(entry.Direction, "outbound", StringComparison.OrdinalIgnoreCase))
            {
                IncrementCount(skippedEventCounts, "outbound-ignored");
                skippedLines++;
                continue;
            }

            if (!hasSceneStarted)
            {
                sceneStarted = entry.Timestamp;
                clock.Reset(sceneStarted);
                owner.ResetCombat(sceneId, clock.NextObservationOrdinal, sceneStarted);
                hasSceneStarted = true;
            }

            if (!inboundProcessors.TryGetValue(entry.Connection, out var inboundProcessor))
            {
                var connectionSink = new JournalingRuntimeObservationSink(
                    journal,
                    clock,
                    () => sceneId,
                    () => Interlocked.Increment(ref nextBatchOrdinal));
                inboundProcessor = new ReplayConnectionProcessor(
                    connectionSink,
                    new PacketStreamProcessor(connectionSink));
                inboundProcessors[entry.Connection] = inboundProcessor;
            }

            var parsed = inboundProcessor.Processor.AppendAndProcess(
                entry.Payload,
                entry.Connection,
                entry.Timestamp.ToUnixTimeMilliseconds());

            if (parsed)
            {
                var connection = entry.Connection;
                if (hasLastParsedConnection && !lastParsedConnection.IsSameConnection(in connection, out _))
                {
                    var source = new PacketObservationSource(entry.Timestamp.ToUnixTimeMilliseconds(), 0, 0, 0, entry.Payload.Length, 0, default);
                    inboundProcessor.Sink.MarkSceneTransportBoundary(in source);
                }

                lastParsedConnection = connection;
                hasLastParsedConnection = true;
                IncrementCount(replayedEventCounts, entry.Direction);
                replayedLines++;
            }
            else
            {
                IncrementCount(skippedEventCounts, entry.Direction);
                skippedLines++;
            }
        }

        journal.CompleteBatch(long.MaxValue);
        foreach (var processor in inboundProcessors.Values)
            processor.Processor.Dispose();

        var ingestCounter = CaptureBaselineCounter(ingestStart);

        var snapshotStart = CaptureBaselineStart();
        var snapshot = owner.CreateSnapshot();
        var snapshotCounter = CaptureBaselineCounter(snapshotStart);

        var summaryStart = CaptureBaselineStart();
        var summaries = owner.ReadLocked((entities, _, metadataRegistry, combat) => BuildCombatantSummaries(combat, entities, metadataRegistry, snapshot));
        var summaryCounter = CaptureBaselineCounter(summaryStart);

        return new PacketLogReplayResult(
            sourceName,
            totalLines,
            replayedLines,
            skippedLines,
            snapshot,
            journal,
            owner,
            summaries,
            replayedEventCounts,
            skippedEventCounts)
        {
            BaselineCounters = new PacketLogReplayBaselineCounters(
                ingestCounter,
                snapshotCounter,
                summaryCounter),
        };
    }

    private static BaselineStart CaptureBaselineStart()
        => new(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());

    private static int ResolveJournalCapacity(int lineCount) =>
        lineCount <= 0 ? 0 : lineCount <= int.MaxValue / 2 ? lineCount * 2 : int.MaxValue;

    private static PacketLogReplayBaselineCounter CaptureBaselineCounter(BaselineStart start)
    {
        var elapsed = Stopwatch.GetElapsedTime(start.Timestamp);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - start.AllocatedBytes;
        return new PacketLogReplayBaselineCounter(elapsed, Math.Max(0, allocatedBytes));
    }

    private static List<PacketLogCombatantSummary> BuildCombatantSummaries(CombatStore combat, EntityStore entities, RuntimeMetadataRegistry metadataRegistry, SceneCombatSnapshot snapshot)
    {
        var summariesByCombatantId = new Dictionary<int, MutableCombatantSummary>();

        foreach (ref readonly var e in EnumerateSummaryEvents(combat, entities, snapshot))
        {
            var sourceId = ResolveCombatantId(entities, e.SourceId);
            var targetId = e.TargetId;

            if (sourceId > 0)
            {
                EnsureSummary(summariesByCombatantId, sourceId, entities, metadataRegistry);
            }

            if (targetId > 0)
            {
                EnsureSummary(summariesByCombatantId, targetId, entities, metadataRegistry);
            }

            var observation = e.Observation;
            if (e.ContributesDamage)
            {
                if (!IsSummonDamageTarget(entities, in e))
                {
                    ApplyDamageSummary(summariesByCombatantId, sourceId, targetId, in observation);
                }

                continue;
            }

            if (e.ContributesHealing)
            {
                ApplyHealingSummary(summariesByCombatantId, sourceId, targetId, in observation);
                continue;
            }

            if (e.ContributesShieldGrant || e.ContributesShieldAbsorbed)
            {
                ApplyShieldSummary(summariesByCombatantId, sourceId, targetId, in observation);
            }
        }

        return [.. summariesByCombatantId.OrderBy(static pair => pair.Key).Select(static pair => pair.Value.ToSummary())];
    }

    private static CombatEventSpan EnumerateSummaryEvents(CombatStore combat, EntityStore entities, SceneCombatSnapshot snapshot)
    {
        if (snapshot.EncounterEndTime < snapshot.EncounterStartTime)
            return new CombatEventSpan([], []);

        var events = combat.EventSpan;
        var indices = new List<int>();
        var relevant = new HashSet<int>();
        for (var i = 0; i < events.Length; i++)
        {
            ref readonly var e = ref events[i];
            if (!IsWithinEncounterWindow(in e, snapshot.EncounterStartTime, snapshot.EncounterEndTime))
                continue;

            var sourceId = ResolveCombatantId(entities, e.SourceId);
            relevant.Add(sourceId);
            if (e.TargetId > 0)
                relevant.Add(e.TargetId);

            indices.Add(i);
        }

        if (relevant.Count == 0)
            return new CombatEventSpan(events, indices);

        for (var i = 0; i < events.Length; i++)
        {
            ref readonly var e = ref events[i];
            if (IsWithinEncounterWindow(in e, snapshot.EncounterStartTime, snapshot.EncounterEndTime))
                continue;

            var sourceId = ResolveCombatantId(entities, e.SourceId);
            if (!IsRelevantRecoveryEvent(in e, sourceId, e.TargetId, relevant))
                continue;

            indices.Add(i);
        }

        return new CombatEventSpan(events, indices);
    }

    private readonly ref struct CombatEventSpan
    {
        private readonly ReadOnlySpan<CombatEventRecord> _events;
        private readonly List<int> _indices;

        public CombatEventSpan(ReadOnlySpan<CombatEventRecord> events, List<int> indices)
        {
            _events = events;
            _indices = indices;
        }

        public readonly Enumerator GetEnumerator() => new(_events, _indices ?? []);

        public ref struct Enumerator
        {
            private readonly ReadOnlySpan<CombatEventRecord> _events;
            private List<int>.Enumerator _indices;

            public Enumerator(ReadOnlySpan<CombatEventRecord> events, List<int> indices)
            {
                _events = events;
                _indices = indices.GetEnumerator();
            }

            public readonly ref readonly CombatEventRecord Current => ref _events[_indices.Current];

            public bool MoveNext() => _indices.MoveNext();
        }
    }

    private static MutableCombatantSummary EnsureSummary(
        Dictionary<int, MutableCombatantSummary> summariesByCombatantId,
        int combatantId,
        EntityStore entities,
        RuntimeMetadataRegistry metadataRegistry)
    {
        if (summariesByCombatantId.TryGetValue(combatantId, out var existing))
        {
            return existing;
        }

        var created = new MutableCombatantSummary(
            combatantId,
            ResolveDisplayName(entities, metadataRegistry, combatantId));
        summariesByCombatantId[combatantId] = created;
        return created;
    }

    private static void ApplyDamageSummary(
        Dictionary<int, MutableCombatantSummary> summariesByCombatantId,
        int sourceId,
        int targetId,
        in CombatObservation observation)
    {
        var hitContribution = Math.Max(0, observation.HitCount);
        var attemptContribution = Math.Max(hitContribution, Math.Max(0, observation.AttemptCount));
        var criticalContribution = (observation.Modifiers & DamageModifiers.Critical) != 0 ? hitContribution : 0;
        var evadeContribution = (observation.Modifiers & DamageModifiers.Evade) != 0 ? attemptContribution : 0;
        var invincibleContribution = (observation.Modifiers & DamageModifiers.Invincible) != 0 ? attemptContribution : 0;

        if (sourceId > 0 && summariesByCombatantId.TryGetValue(sourceId, out var source))
        {
            source.OutgoingDamage += observation.Damage;
            source.OutgoingHits += hitContribution;
            source.OutgoingAttempts += attemptContribution;
            source.OutgoingCriticals += criticalContribution;
            source.OutgoingEvades += evadeContribution;
            source.OutgoingInvincibles += invincibleContribution;
        }

        if (targetId > 0 && summariesByCombatantId.TryGetValue(targetId, out var target))
        {
            target.IncomingDamage += observation.Damage;
            target.IncomingHits += hitContribution;
            target.IncomingAttempts += attemptContribution;
            target.IncomingCriticals += criticalContribution;
            target.IncomingEvades += evadeContribution;
            target.IncomingInvincibles += invincibleContribution;
        }
    }

    private static void ApplyHealingSummary(
        Dictionary<int, MutableCombatantSummary> summariesByCombatantId,
        int sourceId,
        int targetId,
        in CombatObservation observation)
    {
        if (sourceId > 0 && summariesByCombatantId.TryGetValue(sourceId, out var source))
        {
            source.OutgoingHealing += observation.Damage;
        }

        if (targetId > 0 && summariesByCombatantId.TryGetValue(targetId, out var target))
        {
            target.IncomingHealing += observation.Damage;
            if (observation.EffectTag == PacketEffectTag.RegenerationHealing)
            {
                target.RegenerationHealing += observation.Damage;
            }
        }
    }

    private static void ApplyShieldSummary(
        Dictionary<int, MutableCombatantSummary> summariesByCombatantId,
        int sourceId,
        int targetId,
        in CombatObservation observation)
    {
        if (observation.EffectTag == PacketEffectTag.ShieldAbsorbed)
        {
            if (observation.Damage <= 0)
            {
                return;
            }

            if (sourceId > 0 && summariesByCombatantId.TryGetValue(sourceId, out var absorbSource))
            {
                absorbSource.OutgoingShieldAbsorbed += observation.Damage;
            }

            if (targetId > 0 && summariesByCombatantId.TryGetValue(targetId, out var absorbTarget))
            {
                absorbTarget.IncomingShieldAbsorbed += observation.Damage;
            }
            return;
        }

        if (sourceId > 0 && summariesByCombatantId.TryGetValue(sourceId, out var source))
        {
            source.OutgoingShield += observation.Damage;
        }

        if (targetId > 0 && summariesByCombatantId.TryGetValue(targetId, out var target))
        {
            target.IncomingShield += observation.Damage;
        }
    }

    private static int ResolveCombatantId(EntityStore entities, int entityId)
    {
        if (entityId <= 0)
            return entityId;

        return entities.TryGet(entityId, out var entity) && entity.OwnerEntityId is int ownerId ? ownerId : entityId;
    }

    private static bool IsWithinEncounterWindow(in CombatEventRecord e, long start, long end) =>
        e.ObservedAtMilliseconds >= start && e.ObservedAtMilliseconds <= end;

    private static bool IsSummonDamageTarget(EntityStore entities, in CombatEventRecord e)
    {
        if (e.TargetId <= 0 || !e.ContributesDamage)
            return false;

        if (IsKnownSummon(entities, e.TargetId))
            return true;

        return ResolveCombatantId(entities, e.SourceId) == ResolveCombatantId(entities, e.TargetId);
    }

    private static bool IsKnownSummon(EntityStore entities, int entityId) =>
        entities.TryGet(entityId, out var entity) && (entity.OwnerEntityId.HasValue || entity.Kind == NpcKind.Summon);

    private static bool IsRelevantRecoveryEvent(in CombatEventRecord e, int sourceId, int targetId, HashSet<int> relevant)
    {
        if (e.Observation.Damage <= 0 || (!relevant.Contains(sourceId) && !relevant.Contains(targetId)))
            return false;

        return e.Observation.EventKind is CombatEventKind.Healing or CombatEventKind.Support
               || e.Observation.ValueKind is CombatValueKind.Healing or CombatValueKind.PeriodicHealing or CombatValueKind.DrainHealing or CombatValueKind.Shield or CombatValueKind.Support;
    }

    private static string ResolveDisplayName(EntityStore entities, RuntimeMetadataRegistry metadataRegistry, int entityId)
    {
        if (metadataRegistry.TryGetPcMetadata(entityId, out var pc) && pc.HasNickname)
        {
            return pc.Nickname;
        }

        if (metadataRegistry.TryGetNpcCode(entityId, out var npcCode) &&
            CombatResourceRegistry.TryResolveNpcCatalogEntry(npcCode, out var catalogEntry) &&
            !string.IsNullOrWhiteSpace(catalogEntry.Name))
        {
            return catalogEntry.Name;
        }

        if (entities.TryGet(entityId, out var entity))
        {
            if (entity.IsPlayer && !string.IsNullOrWhiteSpace(entity.Nickname))
            {
                return entity.Nickname;
            }

            if (entity.NpcCode is int entityNpcCode &&
                CombatResourceRegistry.TryResolveNpcCatalogEntry(entityNpcCode, out var entityCatalogEntry) &&
                !string.IsNullOrWhiteSpace(entityCatalogEntry.Name))
            {
                return entityCatalogEntry.Name;
            }
        }

        return entityId.ToString(CultureInfo.InvariantCulture);
    }


    private static ReplayLogKind DetectLogKind(IReadOnlyList<string> lines, string sourceName)
    {
        if (TryDetectLogKindFromSourceName(sourceName, out var sourceLogKind))
        {
            return sourceLogKind;
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!TryReadLineSegments(line, out _, out var secondSegment, out var thirdSegment, out _, out _))
            {
                continue;
            }

            if (!secondSegment.StartsWith("dir=", StringComparison.Ordinal))
            {
                return ReplayLogKind.Unknown;
            }

            return thirdSegment.Contains(':')
                ? ReplayLogKind.Stream
                : ReplayLogKind.Raw;
        }

        return ReplayLogKind.Unknown;
    }

    private static bool TryDetectLogKindFromSourceName(string sourceName, out ReplayLogKind logKind)
    {
        if (sourceName.Contains(".stream.", StringComparison.OrdinalIgnoreCase) ||
            sourceName.EndsWith("stream.log", StringComparison.OrdinalIgnoreCase))
        {
            logKind = ReplayLogKind.Stream;
            return true;
        }

        if (sourceName.Contains(".raw.", StringComparison.OrdinalIgnoreCase) ||
            sourceName.EndsWith("raw.log", StringComparison.OrdinalIgnoreCase))
        {
            logKind = ReplayLogKind.Raw;
            return true;
        }

        logKind = default;
        return false;
    }

    private static bool TryParseStreamEntry(string line, out StreamReplayEntry entry)
    {
        entry = default;
        if (!TryReadLineSegments(line, out var timestampText, out var directionSegment, out var connectionSegment, out var dataText, out _))
        {
            return false;
        }

        if (!directionSegment.StartsWith("dir=", StringComparison.Ordinal) ||
            !DateTimeOffset.TryParse(
                timestampText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp) ||
            !TryParseConnection(connectionSegment, out var connection))
        {
            return false;
        }

        try
        {
            entry = new StreamReplayEntry(
                timestamp,
                directionSegment["dir=".Length..],
                connection,
                Convert.FromHexString(dataText));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryReadLineSegments(
        string line,
        out string timestampText,
        out string secondSegment,
        out string thirdSegment,
        out string dataText,
        out string metadata)
    {
        timestampText = string.Empty;
        secondSegment = string.Empty;
        thirdSegment = string.Empty;
        dataText = string.Empty;
        metadata = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var firstSeparator = line.IndexOf('|');
        if (firstSeparator <= 0)
        {
            return false;
        }

        var secondSeparator = line.IndexOf('|', firstSeparator + 1);
        if (secondSeparator <= firstSeparator + 1)
        {
            return false;
        }

        var thirdSeparator = line.IndexOf('|', secondSeparator + 1);
        if (thirdSeparator <= secondSeparator + 1)
        {
            return false;
        }

        var dataSeparator = line.LastIndexOf("|data=", StringComparison.Ordinal);
        if (dataSeparator <= thirdSeparator)
        {
            return false;
        }

        timestampText = line[..firstSeparator];
        secondSegment = line[(firstSeparator + 1)..secondSeparator];
        thirdSegment = line[(secondSeparator + 1)..thirdSeparator];
        dataText = line[(dataSeparator + 6)..];
        metadata = thirdSeparator + 1 < dataSeparator
            ? line[(thirdSeparator + 1)..dataSeparator]
            : string.Empty;
        return true;
    }

    private static bool TryParseConnection(string text, out TcpConnection connection)
    {
        connection = default;
        var arrowIndex = text.IndexOf("->", StringComparison.Ordinal);
        if (arrowIndex <= 0 || arrowIndex >= text.Length - 2)
        {
            return false;
        }

        if (!TryParseEndpoint(text[..arrowIndex], out var sourceAddress, out var sourcePort) ||
            !TryParseEndpoint(text[(arrowIndex + 2)..], out var destinationAddress, out var destinationPort))
        {
            return false;
        }

        connection = new TcpConnection(sourceAddress, destinationAddress, sourcePort, destinationPort);
        return true;
    }

    private static bool TryParseEndpoint(string text, out uint address, out ushort port)
    {
        address = 0;
        port = 0;

        var separatorIndex = text.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= text.Length - 1)
        {
            return false;
        }

        return uint.TryParse(text[..separatorIndex], NumberStyles.None, CultureInfo.InvariantCulture, out address) &&
               ushort.TryParse(text[(separatorIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port);
    }

    private static void IncrementCount(Dictionary<string, int> counts, string key)
    {
        counts[key] = counts.TryGetValue(key, out var current) ? current + 1 : 1;
    }


    private readonly record struct StreamReplayEntry(
        DateTimeOffset Timestamp,
        string Direction,
        TcpConnection Connection,
        byte[] Payload);

    private readonly record struct ReplayConnectionProcessor(
        IRuntimeObservationSink Sink,
        PacketStreamProcessor Processor);

    private enum ReplayLogKind
    {
        Unknown,
        Stream,
        Raw
    }

    private sealed class MutableCombatantSummary(int combatantId, string displayName)
    {
        public int CombatantId { get; } = combatantId;
        public string DisplayName { get; } = displayName;
        public long OutgoingDamage { get; set; }
        public long IncomingDamage { get; set; }
        public long OutgoingHealing { get; set; }
        public long IncomingHealing { get; set; }
        public long OutgoingShield { get; set; }
        public long IncomingShield { get; set; }
        public long OutgoingShieldAbsorbed { get; set; }
        public long IncomingShieldAbsorbed { get; set; }
        public long RegenerationHealing { get; set; }
        public int OutgoingHits { get; set; }
        public int IncomingHits { get; set; }
        public int OutgoingAttempts { get; set; }
        public int IncomingAttempts { get; set; }
        public int OutgoingCriticals { get; set; }
        public int IncomingCriticals { get; set; }
        public int OutgoingEvades { get; set; }
        public int IncomingEvades { get; set; }
        public int OutgoingInvincibles { get; set; }
        public int IncomingInvincibles { get; set; }

        public PacketLogCombatantSummary ToSummary()
        {
            return new PacketLogCombatantSummary(
                CombatantId,
                DisplayName,
                OutgoingDamage,
                IncomingDamage,
                OutgoingHealing,
                IncomingHealing,
                OutgoingShield,
                IncomingShield,
                OutgoingShieldAbsorbed,
                IncomingShieldAbsorbed,
                RegenerationHealing,
                OutgoingHits,
                IncomingHits,
                OutgoingAttempts,
                IncomingAttempts,
                OutgoingCriticals,
                IncomingCriticals,
                OutgoingEvades,
                IncomingEvades,
                OutgoingInvincibles,
                IncomingInvincibles);
        }
    }

    private readonly record struct BaselineStart(long Timestamp, long AllocatedBytes);
}

public sealed record PacketLogReplayResult(
    string SourceName,
    int TotalLines,
    int ReplayedLines,
    int SkippedLines,
    SceneCombatSnapshot Snapshot,
    ObservedEventJournal SceneJournal,
    SceneReadModelOwner SceneOwner,
    IReadOnlyList<PacketLogCombatantSummary> Combatants,
    IReadOnlyDictionary<string, int> ReplayedEventCounts,
    IReadOnlyDictionary<string, int> SkippedEventCounts)
{
    public PacketLogReplayBaselineCounters BaselineCounters { get; init; } = PacketLogReplayBaselineCounters.Empty;
}

public sealed record PacketLogReplayBaselineCounters(
    PacketLogReplayBaselineCounter ReplayIngest,
    PacketLogReplayBaselineCounter SnapshotCreation,
    PacketLogReplayBaselineCounter CombatantSummaryCreation)
{
    public static PacketLogReplayBaselineCounters Empty { get; } = new(
        PacketLogReplayBaselineCounter.Empty,
        PacketLogReplayBaselineCounter.Empty,
        PacketLogReplayBaselineCounter.Empty);
}

public readonly record struct PacketLogReplayBaselineCounter(TimeSpan Elapsed, long AllocatedBytes)
{
    public static PacketLogReplayBaselineCounter Empty { get; } = new(TimeSpan.Zero, 0);
}

public sealed record PacketLogCombatantSummary(
    int CombatantId,
    string DisplayName,
    long OutgoingDamage,
    long IncomingDamage,
    long OutgoingHealing,
    long IncomingHealing,
    long OutgoingShield,
    long IncomingShield,
    long OutgoingShieldAbsorbed,
    long IncomingShieldAbsorbed,
    long RegenerationHealing,
    int OutgoingHits,
    int IncomingHits,
    int OutgoingAttempts,
    int IncomingAttempts,
    int OutgoingCriticals,
    int IncomingCriticals,
    int OutgoingEvades,
    int IncomingEvades,
    int OutgoingInvincibles,
    int IncomingInvincibles);
