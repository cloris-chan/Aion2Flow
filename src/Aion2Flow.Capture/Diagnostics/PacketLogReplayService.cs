using System.Diagnostics;
using System.Globalization;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Archive;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Runtime;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Capture.Diagnostics;

public sealed class PacketLogReplayService
{
    public static PacketLogReplayResult Replay(string path)
        => ReplayFile(path, CancellationToken.None, null, null);

    public static PacketLogReplayResult Replay(string path, ICombatOccurrenceObserver combatOccurrenceObserver)
    {
        ArgumentNullException.ThrowIfNull(combatOccurrenceObserver);
        return ReplayFile(path, CancellationToken.None, combatOccurrenceObserver, null);
    }

    public static PacketLogReplayResult Replay(string path, ISceneEventObserver sceneEventObserver)
    {
        ArgumentNullException.ThrowIfNull(sceneEventObserver);
        return ReplayFile(path, CancellationToken.None, sceneEventObserver, sceneEventObserver);
    }

    public static PacketLogReplayResult ReplayCancellable(string path, CancellationToken cancellationToken)
        => ReplayFile(path, cancellationToken, null, null);

    private static PacketLogReplayResult ReplayFile(string path, CancellationToken cancellationToken, ICombatOccurrenceObserver? combatOccurrenceObserver, IAuraLifecycleObserver? auraLifecycleObserver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var reader = File.OpenText(path);
        return ReplayCore(reader, path, cancellationToken, combatOccurrenceObserver, auraLifecycleObserver);
    }

    public static IReadOnlyList<PacketLogReplayResult> ReplayMany(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var results = new List<PacketLogReplayResult>();
        foreach (var path in paths)
            results.Add(Replay(path));

        return results;
    }

    public static PacketLogReplayResult Replay(TextReader reader, string sourceName)
        => ReplayCore(reader, sourceName, CancellationToken.None, null, null);

    private static PacketLogReplayResult ReplayCore(TextReader reader, string sourceName, CancellationToken cancellationToken, ICombatOccurrenceObserver? combatOccurrenceObserver, IAuraLifecycleObserver? auraLifecycleObserver)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        return ReplayStreamLines(
            ReadLines(reader, cancellationToken),
            sourceName,
            cancellationToken,
            combatOccurrenceObserver,
            auraLifecycleObserver);
    }

    private static IEnumerable<string> ReadLines(TextReader reader, CancellationToken cancellationToken)
    {
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return line;
        }
    }

    private static PacketLogReplayResult ReplayStreamLines(IEnumerable<string> lines, string sourceName, CancellationToken cancellationToken, ICombatOccurrenceObserver? combatOccurrenceObserver, IAuraLifecycleObserver? auraLifecycleObserver)
    {
        var sceneStarted = DateTimeOffset.UtcNow;
        var replayTimeProvider = new ReplayTimeProvider(sceneStarted);
        var scene = new SceneLiveReadModel(
            sceneStarted,
            replayTimeProvider,
            combatOccurrenceObserver,
            auraLifecycleObserver);
        var journal = scene.Journal;
        var sinkFactory = SceneSinkFactory.CreateForLive(scene);
        var replayedEventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var skippedEventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        using var inboundProcessors = new ReplayTransportProcessorSet(
            () => new ReplayConnectionProcessor(sinkFactory));
        var totalLines = 0;
        var replayedLines = 0;
        var skippedLines = 0;
        var hasSceneStarted = false;
        var ingestStart = CaptureBaselineStart();

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalLines++;
            if (!TryParseStreamEntry(line, out var entry))
            {
                IncrementCount(skippedEventCounts, "<invalid>");
                skippedLines++;
                continue;
            }

            replayTimeProvider.SetUtcNow(entry.Timestamp);
            if (string.Equals(entry.Direction, "outbound", StringComparison.OrdinalIgnoreCase))
            {
                IncrementCount(skippedEventCounts, "outbound-ignored");
                skippedLines++;
                continue;
            }

            if (!hasSceneStarted)
            {
                sceneStarted = entry.Timestamp;
                scene.Reset(sceneStarted);
                hasSceneStarted = true;
            }

            var replayConnection = new ReplayConnectionKey(entry.Connection, entry.ConnectionOrdinal);
            if (!inboundProcessors.TryGetOrCreate(in replayConnection, out var inboundProcessor))
            {
                IncrementCount(skippedEventCounts, "stale-transport-attempt");
                skippedLines++;
                continue;
            }

            var result = inboundProcessor.Classify(entry.Payload);
            if (result.Kind != TcpConnectionStartKind.Game)
            {
                if (result.Kind == TcpConnectionStartKind.NonGame)
                {
                    inboundProcessors.Reject(in replayConnection, inboundProcessor);
                }

                IncrementCount(skippedEventCounts, result.Kind == TcpConnectionStartKind.Pending ? "connection-start-pending" : "non-game-connection");
                skippedLines++;
                continue;
            }

            bool parsed;
            try
            {
                var acceptedPayload = result.ResolveAcceptedPayload(entry.Payload);
                inboundProcessor.MarkGameStream();
                var needsActivation = !inboundProcessors.IsActive(in replayConnection);
                if (needsActivation && replayConnection.ConnectionOrdinal > 0)
                {
                    var source = new PacketObservationSource(
                        entry.Timestamp.ToUnixTimeMilliseconds(),
                        0,
                        0,
                        acceptedPayload.Length,
                        0,
                        default);
                    if (!inboundProcessors.TryActivate(in replayConnection, inboundProcessor, in source))
                    {
                        IncrementCount(skippedEventCounts, "stale-transport-attempt");
                        skippedLines++;
                        continue;
                    }
                }

                if (needsActivation && replayConnection.ConnectionOrdinal == 0)
                {
                    parsed = inboundProcessor.ProbeAndBuffer(
                        acceptedPayload,
                        entry.Connection,
                        entry.Timestamp.ToUnixTimeMilliseconds());
                    if (parsed)
                    {
                        var source = new PacketObservationSource(
                            entry.Timestamp.ToUnixTimeMilliseconds(),
                            0,
                            0,
                            acceptedPayload.Length,
                            0,
                            default);
                        if (!inboundProcessors.TryActivate(in replayConnection, inboundProcessor, in source))
                            throw new InvalidOperationException("The parsed ordinal-less replay connection could not become active.");
                    }
                }
                else
                {
                    parsed = inboundProcessor.AppendActive(
                        acceptedPayload,
                        entry.Connection,
                        entry.Timestamp.ToUnixTimeMilliseconds());
                }
            }
            finally
            {
                result.Return();
            }

            if (parsed)
            {
                IncrementCount(replayedEventCounts, entry.Direction);
                replayedLines++;
            }
            else
            {
                IncrementCount(skippedEventCounts, entry.Direction);
                skippedLines++;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        journal.CompleteFlush(long.MaxValue);

        var ingestCounter = CaptureBaselineCounter(ingestStart);

        var snapshotStart = CaptureBaselineStart();
        var owner = scene.Owner;
        var snapshot = owner.CreateSnapshot();
        var snapshotCounter = CaptureBaselineCounter(snapshotStart);

        var summaryStart = CaptureBaselineStart();
        var summaries = owner.ReadLocked((entities, _, metadataRegistry, combat, mechanics, resources, adapter) => BuildCombatantSummaries(combat, mechanics, resources, entities, metadataRegistry, adapter, snapshot));
        var summaryCounter = CaptureBaselineCounter(summaryStart);
        var mapTransitionArchives = new List<SceneArchivePayload>();
        while (scene.TryDequeueMapTransition(out var mapTransitionArchive))
        {
            if (mapTransitionArchive is not null)
                mapTransitionArchives.Add(mapTransitionArchive);
        }

        return new PacketLogReplayResult(
            sourceName,
            totalLines,
            replayedLines,
            skippedLines,
            snapshot,
            journal,
            owner,
            summaries,
            owner.Resources.Events,
            replayedEventCounts,
            skippedEventCounts)
        {
            BaselineCounters = new PacketLogReplayBaselineCounters(
                ingestCounter,
                snapshotCounter,
                summaryCounter),
            MapTransitionArchives = mapTransitionArchives
        };
    }

    private static BaselineStart CaptureBaselineStart()
        => new(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());

    private static PacketLogReplayBaselineCounter CaptureBaselineCounter(BaselineStart start)
    {
        var elapsed = Stopwatch.GetElapsedTime(start.Timestamp);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - start.AllocatedBytes;
        return new PacketLogReplayBaselineCounter(elapsed, Math.Max(0, allocatedBytes));
    }

    internal static List<PacketLogCombatantSummary> BuildCombatantSummaries(
        CombatStore combat,
        MechanicStore mechanics,
        ResourceStore resources,
        EntityStore entities,
        RuntimeMetadataRegistry metadataRegistry,
        SceneCombatSnapshotAdapter adapter,
        SceneCombatSnapshot snapshot)
    {
        var summariesByCombatantId = new Dictionary<int, MutableCombatantSummary>();

        foreach (ref readonly var e in EnumerateSummaryEvents(combat, adapter, snapshot))
        {
            var sourceId = adapter.ResolveDetailCombatantId(e.SourceId);
            var targetId = e.TargetId;

            if (sourceId > 0)
            {
                EnsureSummary(summariesByCombatantId, sourceId, entities, metadataRegistry);
            }

            if (targetId > 0)
            {
                EnsureSummary(summariesByCombatantId, targetId, entities, metadataRegistry);
            }

            var contribution = e.Contribution;
            if (e.ContributesDamage)
            {
                if (!IsSummonDamageTarget(adapter, in e))
                {
                    ApplyDamageSummary(summariesByCombatantId, sourceId, targetId, in contribution);
                }

                continue;
            }

            if (e.ContributesHealing)
            {
                ApplyHealingSummary(summariesByCombatantId, sourceId, targetId, in contribution);
                continue;
            }

            if (e.ContributesShieldGrant || e.ContributesShieldAbsorbed)
            {
                ApplyShieldSummary(summariesByCombatantId, sourceId, targetId, in contribution);
            }
        }

        var mechanicEvents = mechanics.Events;
        for (var i = 0; i < mechanicEvents.Count; i++)
        {
            var e = mechanicEvents[i];
            if (!IsWithinEncounterWindow(e.ObservedAtMilliseconds, snapshot.EncounterStartTime, snapshot.EncounterEndTime) ||
                adapter.IsSummonDamageTarget(e.SourceId, e.TargetId, 0))
            {
                continue;
            }

            var sourceId = adapter.ResolveDetailCombatantId(e.SourceId);
            var targetId = e.TargetId;
            if (sourceId > 0)
                EnsureSummary(summariesByCombatantId, sourceId, entities, metadataRegistry);
            if (targetId > 0)
                EnsureSummary(summariesByCombatantId, targetId, entities, metadataRegistry);

            var mechanic = e.Mechanic;
            ApplyMechanicSummary(summariesByCombatantId, sourceId, targetId, in mechanic);
        }

        var resourceEvents = resources.Events;
        for (var i = 0; i < resourceEvents.Count; i++)
        {
            var e = resourceEvents[i];
            if (!IsWithinEncounterWindow(e.ObservedAtMilliseconds, snapshot.EncounterStartTime, snapshot.EncounterEndTime))
                continue;

            var sourceId = adapter.ResolveDetailCombatantId(e.SourceId);
            var targetId = e.TargetId;
            if (sourceId > 0)
                EnsureSummary(summariesByCombatantId, sourceId, entities, metadataRegistry);
            if (targetId > 0)
                EnsureSummary(summariesByCombatantId, targetId, entities, metadataRegistry);
        }

        return [.. summariesByCombatantId.OrderBy(static pair => pair.Key).Select(static pair => pair.Value.ToSummary())];
    }

    private static CombatEventSpan EnumerateSummaryEvents(CombatStore combat, SceneCombatSnapshotAdapter adapter, SceneCombatSnapshot snapshot)
    {
        if (snapshot.EncounterEndTime < snapshot.EncounterStartTime)
            return new CombatEventSpan(default, []);

        var events = combat.EventSpan;
        var indices = new List<int>();
        var relevant = new HashSet<int>();
        for (var i = 0; i < events.Length; i++)
        {
            ref readonly var e = ref events[i];
            if (!IsWithinEncounterWindow(in e, snapshot.EncounterStartTime, snapshot.EncounterEndTime))
                continue;

            var sourceId = adapter.ResolveDetailCombatantId(e.SourceId);
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

            var sourceId = adapter.ResolveDetailCombatantId(e.SourceId);
            if (!IsRelevantRecoveryEvent(in e, sourceId, e.TargetId, relevant))
                continue;

            indices.Add(i);
        }

        return new CombatEventSpan(events, indices);
    }

    private readonly ref struct CombatEventSpan
    {
        private readonly CombatEventRange _events;
        private readonly List<int> _indices;

        public CombatEventSpan(CombatEventRange events, List<int> indices)
        {
            _events = events;
            _indices = indices;
        }

        public readonly Enumerator GetEnumerator() => new(_events, _indices ?? []);

        public ref struct Enumerator
        {
            private readonly CombatEventRange _events;
            private List<int>.Enumerator _indices;

            public Enumerator(CombatEventRange events, List<int> indices)
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
        in CombatContribution contribution)
    {
        if (sourceId > 0 && summariesByCombatantId.TryGetValue(sourceId, out var source))
            source.OutgoingDamage += contribution.Amount;

        if (targetId > 0 && summariesByCombatantId.TryGetValue(targetId, out var target))
            target.IncomingDamage += contribution.Amount;
    }

    private static void ApplyMechanicSummary(
        Dictionary<int, MutableCombatantSummary> summariesByCombatantId,
        int sourceId,
        int targetId,
        in CombatMechanicOccurrence mechanic)
    {
        var criticalContribution = (mechanic.Modifiers & DamageModifiers.Critical) != 0 ? mechanic.HitCount : 0;
        if (sourceId > 0 && summariesByCombatantId.TryGetValue(sourceId, out var source))
        {
            source.OutgoingHits += mechanic.HitCount;
            source.OutgoingAttempts += mechanic.AttemptCount;
            source.OutgoingCriticals += criticalContribution;
            source.OutgoingEvades += mechanic.EvadeCount;
            source.OutgoingInvincibles += mechanic.InvincibleCount;
        }

        if (targetId > 0 && summariesByCombatantId.TryGetValue(targetId, out var target))
        {
            target.IncomingHits += mechanic.HitCount;
            target.IncomingAttempts += mechanic.AttemptCount;
            target.IncomingCriticals += criticalContribution;
            target.IncomingEvades += mechanic.EvadeCount;
            target.IncomingInvincibles += mechanic.InvincibleCount;
        }
    }

    private static void ApplyHealingSummary(
        Dictionary<int, MutableCombatantSummary> summariesByCombatantId,
        int sourceId,
        int targetId,
        in CombatContribution contribution)
    {
        if (sourceId > 0 && summariesByCombatantId.TryGetValue(sourceId, out var source))
        {
            source.OutgoingHealing += contribution.Amount;
        }

        if (targetId > 0 && summariesByCombatantId.TryGetValue(targetId, out var target))
        {
            target.IncomingHealing += contribution.Amount;
            if (contribution.Delivery == CombatDeliveryKind.Regeneration)
            {
                target.RegenerationHealing += contribution.Amount;
            }
        }
    }

    private static void ApplyShieldSummary(
        Dictionary<int, MutableCombatantSummary> summariesByCombatantId,
        int sourceId,
        int targetId,
        in CombatContribution contribution)
    {
        if (contribution.Metric == CombatMetricKind.ShieldAbsorbed)
        {
            if (contribution.Amount <= 0)
            {
                return;
            }

            if (sourceId > 0 && summariesByCombatantId.TryGetValue(sourceId, out var absorbSource))
            {
                absorbSource.OutgoingShieldAbsorbed += contribution.Amount;
            }

            if (targetId > 0 && summariesByCombatantId.TryGetValue(targetId, out var absorbTarget))
            {
                absorbTarget.IncomingShieldAbsorbed += contribution.Amount;
            }
            return;
        }

        if (sourceId > 0 && summariesByCombatantId.TryGetValue(sourceId, out var source))
        {
            source.OutgoingShield += contribution.Amount;
        }

        if (targetId > 0 && summariesByCombatantId.TryGetValue(targetId, out var target))
        {
            target.IncomingShield += contribution.Amount;
        }
    }

    private static bool IsWithinEncounterWindow(in CombatEventRecord e, long start, long end) =>
        IsWithinEncounterWindow(e.ObservedAtMilliseconds, start, end);

    private static bool IsWithinEncounterWindow(long observedAtMilliseconds, long start, long end) =>
        observedAtMilliseconds >= start && observedAtMilliseconds <= end;

    private static bool IsSummonDamageTarget(SceneCombatSnapshotAdapter adapter, in CombatEventRecord e)
    {
        if (e.TargetId <= 0 || !e.ContributesDamage)
            return false;

        return adapter.IsSummonDamageTarget(e.SourceId, e.TargetId, e.Contribution.Amount);
    }

    private static bool IsRelevantRecoveryEvent(in CombatEventRecord e, int sourceId, int targetId, HashSet<int> relevant)
    {
        if (e.Contribution.Amount <= 0 || (!relevant.Contains(sourceId) && !relevant.Contains(targetId)))
            return false;

        return e.Contribution.Metric is CombatMetricKind.Healing or CombatMetricKind.ShieldGranted or CombatMetricKind.ShieldAbsorbed;
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

    private static bool TryParseStreamEntry(string line, out StreamReplayEntry entry)
    {
        entry = default;
        if (!TryReadLineSegments(
                line,
                out var timestampText,
                out var directionSegment,
                out var connectionSegment,
                out var dataText,
                out var metadata) ||
            !TryParseConnectionOrdinal(metadata, out var connectionOrdinal) ||
            !TryParsePayloadLength(metadata, out var payloadLength))
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
            var payload = Convert.FromHexString(dataText);
            if (payload.Length != payloadLength)
            {
                return false;
            }

            entry = new StreamReplayEntry(
                timestamp,
                directionSegment["dir=".Length..],
                connection,
                connectionOrdinal,
                payload);
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

    private static bool TryParseConnectionOrdinal(string metadata, out long connectionOrdinal)
    {
        const string marker = "attempt=";
        connectionOrdinal = 0;
        var markerIndex = metadata.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return true;
        }

        if (markerIndex != 0 && metadata[markerIndex - 1] != '|')
        {
            return false;
        }

        var valueStart = markerIndex + marker.Length;
        var valueEnd = metadata.IndexOf('|', valueStart);
        var value = valueEnd < 0
            ? metadata.AsSpan(valueStart)
            : metadata.AsSpan(valueStart, valueEnd - valueStart);
        return long.TryParse(
                   value,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out connectionOrdinal) &&
               connectionOrdinal >= 0;
    }

    private static bool TryParsePayloadLength(string metadata, out int payloadLength)
    {
        const string marker = "len=";
        payloadLength = 0;
        var markerIndex = metadata.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0 || (markerIndex != 0 && metadata[markerIndex - 1] != '|'))
        {
            return false;
        }

        var valueStart = markerIndex + marker.Length;
        var valueEnd = metadata.IndexOf('|', valueStart);
        var value = valueEnd < 0
            ? metadata.AsSpan(valueStart)
            : metadata.AsSpan(valueStart, valueEnd - valueStart);
        return int.TryParse(
                   value,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out payloadLength) &&
               payloadLength >= 0;
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
        long ConnectionOrdinal,
        byte[] Payload);

    private readonly record struct ReplayConnectionKey(TcpConnection Connection, long ConnectionOrdinal);

    private sealed class ReplayTransportProcessorSet(Func<ReplayConnectionProcessor> processorFactory) : IDisposable
    {
        private readonly Dictionary<ReplayConnectionKey, ReplayConnectionProcessor> _candidateProcessors = [];
        private readonly HashSet<ReplayConnectionKey> _retiredConnections = [];
        private ReplayConnectionProcessor? _activeProcessor;
        private ReplayConnectionKey _activeConnection;
        private long _highestActiveConnectionOrdinal;
        private long _nextUseOrdinal;
        private bool _hasActiveConnection;

        public bool IsActive(in ReplayConnectionKey connection) =>
            _hasActiveConnection && _activeConnection == connection;

        public bool TryGetOrCreate(
            in ReplayConnectionKey connection,
            out ReplayConnectionProcessor processor)
        {
            if (IsActive(in connection))
            {
                processor = _activeProcessor!;
                processor.LastUseOrdinal = ++_nextUseOrdinal;
                return true;
            }

            if (IsStale(in connection))
            {
                processor = null!;
                return false;
            }

            if (_candidateProcessors.TryGetValue(connection, out processor!))
            {
                processor.LastUseOrdinal = ++_nextUseOrdinal;
                return true;
            }

            if (_candidateProcessors.Count >= CaptureBufferLimits.CandidateStreamCountLimit)
            {
                EvictLeastRecentlyUsedCandidate();
            }

            processor = processorFactory();
            processor.LastUseOrdinal = ++_nextUseOrdinal;
            _candidateProcessors.Add(connection, processor);
            return true;
        }

        public bool TryActivate(
            in ReplayConnectionKey connection,
            ReplayConnectionProcessor processor,
            in PacketObservationSource source)
        {
            if (IsActive(in connection))
            {
                return ReferenceEquals(_activeProcessor, processor);
            }

            if (IsStale(in connection) ||
                !_candidateProcessors.TryGetValue(connection, out var candidate) ||
                !ReferenceEquals(candidate, processor))
            {
                return false;
            }

            var previousProcessor = _activeProcessor;
            var previousConnection = _activeConnection;
            if (!processor.Activate(in source, markTransportStreamActivated: _hasActiveConnection))
                return false;

            _candidateProcessors.Remove(connection);
            _activeConnection = connection;
            _activeProcessor = processor;
            _hasActiveConnection = true;
            if (connection.ConnectionOrdinal > _highestActiveConnectionOrdinal)
            {
                _highestActiveConnectionOrdinal = connection.ConnectionOrdinal;
            }

            if (previousProcessor is not null)
            {
                _retiredConnections.Add(previousConnection);
                previousProcessor.Dispose();
            }

            DisposeStaleCandidates();
            return true;
        }

        public void Reject(
            in ReplayConnectionKey connection,
            ReplayConnectionProcessor processor)
        {
            if (!_candidateProcessors.TryGetValue(connection, out var candidate) ||
                !ReferenceEquals(candidate, processor))
            {
                return;
            }

            _candidateProcessors.Remove(connection);
            _retiredConnections.Add(connection);
            candidate.Dispose();
        }

        public void Dispose()
        {
            _activeProcessor?.Dispose();
            _activeProcessor = null;
            _hasActiveConnection = false;

            foreach (var processor in _candidateProcessors.Values)
            {
                processor.Dispose();
            }

            _candidateProcessors.Clear();
            _retiredConnections.Clear();
        }

        private bool IsStale(in ReplayConnectionKey connection)
        {
            if (_retiredConnections.Contains(connection))
            {
                return true;
            }

            if (_highestActiveConnectionOrdinal <= 0)
            {
                return false;
            }

            return connection.ConnectionOrdinal <= 0 ||
                   connection.ConnectionOrdinal <= _highestActiveConnectionOrdinal;
        }

        private void EvictLeastRecentlyUsedCandidate()
        {
            var oldestConnection = default(ReplayConnectionKey);
            ReplayConnectionProcessor? oldestProcessor = null;
            var oldestUseOrdinal = long.MaxValue;
            foreach (var candidate in _candidateProcessors)
            {
                if (candidate.Value.LastUseOrdinal >= oldestUseOrdinal)
                {
                    continue;
                }

                oldestConnection = candidate.Key;
                oldestProcessor = candidate.Value;
                oldestUseOrdinal = candidate.Value.LastUseOrdinal;
            }

            if (oldestProcessor is null)
            {
                return;
            }

            _candidateProcessors.Remove(oldestConnection);
            oldestProcessor.Dispose();
        }

        private void DisposeStaleCandidates()
        {
            List<ReplayConnectionKey>? staleConnections = null;
            foreach (var connection in _candidateProcessors.Keys)
            {
                if (!IsStale(in connection))
                {
                    continue;
                }

                staleConnections ??= [];
                staleConnections.Add(connection);
            }

            if (staleConnections is null)
            {
                return;
            }

            foreach (var connection in staleConnections)
            {
                if (_candidateProcessors.Remove(connection, out var processor))
                {
                    processor.Dispose();
                }
            }
        }
    }

    private readonly record struct ReplayAcceptedPayload(
        byte[] Payload,
        TcpConnection Connection,
        long TimestampMilliseconds);

    private sealed class ReplayConnectionProcessor : IDisposable
    {
        private readonly TcpConnectionStartClassifier _classifier = new();
        private readonly Func<IRuntimeObservationSink> _liveSinkFactory;
        private readonly List<ReplayAcceptedPayload> _pendingPayloads = [];
        private PacketStreamProcessor? _probeProcessor;
        private PacketStreamProcessor? _activeProcessor;

        public ReplayConnectionProcessor(Func<IRuntimeObservationSink> liveSinkFactory)
        {
            _liveSinkFactory = liveSinkFactory;
        }

        public long LastUseOrdinal { get; set; }

        public TcpConnectionStartResult Classify(ReadOnlySpan<byte> payload) => _classifier.Classify(payload);

        public void MarkGameStream()
        {
            _classifier.MarkGameStream();
        }

        public bool ProbeAndBuffer(
            ReadOnlySpan<byte> payload,
            TcpConnection connection,
            long timestampMilliseconds)
        {
            if (_activeProcessor is not null)
                throw new InvalidOperationException("An active replay connection cannot accept probe payloads.");

            var bufferedPayload = payload.ToArray();
            _pendingPayloads.Add(new ReplayAcceptedPayload(
                bufferedPayload,
                connection,
                timestampMilliseconds));
            _probeProcessor ??= CreateProbeProcessor();
            return _probeProcessor.AppendAndProcess(
                bufferedPayload,
                connection,
                timestampMilliseconds);
        }

        public bool Activate(
            in PacketObservationSource source,
            bool markTransportStreamActivated)
        {
            if (_activeProcessor is not null)
                return true;

            var sink = _liveSinkFactory();
            _activeProcessor = new PacketStreamProcessor(sink);
            if (markTransportStreamActivated)
                sink.MarkTransportStreamActivated(in source);

            var parsed = _pendingPayloads.Count == 0;
            foreach (var pending in _pendingPayloads)
            {
                parsed |= _activeProcessor.AppendAndProcess(
                    pending.Payload,
                    pending.Connection,
                    pending.TimestampMilliseconds);
            }

            _pendingPayloads.Clear();
            _probeProcessor?.Dispose();
            _probeProcessor = null;
            return parsed;
        }

        public bool AppendActive(
            ReadOnlySpan<byte> payload,
            TcpConnection connection,
            long timestampMilliseconds)
        {
            if (_activeProcessor is null)
                throw new InvalidOperationException("The replay connection has not been activated.");

            return _activeProcessor.AppendAndProcess(
                payload,
                connection,
                timestampMilliseconds);
        }

        public void Dispose()
        {
            _probeProcessor?.Dispose();
            _probeProcessor = null;
            _activeProcessor?.Dispose();
            _activeProcessor = null;
            _pendingPayloads.Clear();
        }

        private static PacketStreamProcessor CreateProbeProcessor()
        {
            var journal = new ObservedEventJournal();
            var sink = new JournalingRuntimeObservationSink(
                journal,
                new SceneRuntimeClock(0),
                Guid.NewGuid());
            return new PacketStreamProcessor(sink);
        }
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

    private sealed class ReplayTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value.ToUniversalTime();
    }
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
    IReadOnlyList<CombatResourceEventRecord> ResourceEvents,
    IReadOnlyDictionary<string, int> ReplayedEventCounts,
    IReadOnlyDictionary<string, int> SkippedEventCounts)
{
    public PacketLogReplayBaselineCounters BaselineCounters { get; init; } = PacketLogReplayBaselineCounters.Empty;

    public IReadOnlyList<SceneArchivePayload> MapTransitionArchives { get; init; } = [];
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
