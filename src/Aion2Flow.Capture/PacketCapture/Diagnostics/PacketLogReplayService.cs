using System.Diagnostics;
using System.Globalization;
using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Combat;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.PacketCapture.Protocol;
using Cloris.Aion2Flow.PacketCapture.Readers;
using Cloris.Aion2Flow.PacketCapture.Streams;
using Cloris.Aion2Flow.Scene;
using Cloris.Aion2Flow.Scene.Combat;
using Cloris.Aion2Flow.Scene.Journal;
using Cloris.Aion2Flow.Scene.Observation;
using Cloris.Aion2Flow.Scene.Projection;
using Cloris.Aion2Flow.Scene.Runtime;
using Cloris.Aion2Flow.Scene.Stores;

namespace Cloris.Aion2Flow.PacketCapture.Diagnostics;

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
                ReplayLogKind.Frame => ReplayFrameLines(ReadLines(reader), sourceName),
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
            ReplayLogKind.Frame => ReplayFrameLines(lines, sourceName),
            ReplayLogKind.Raw => throw new NotSupportedException("Raw log replay is not supported yet. Use stream logs for whole-encounter replay."),
            _ => throw new InvalidOperationException($"Unsupported replay log kind: {logKind}.")
        };
    }

    private static IEnumerable<string> ReadLines(TextReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static PacketLogReplayResult ReplayFrameLines(IEnumerable<string> lines, string sourceName)
    {
        using var sinkHolder = SceneSinkFactory.CreateForReplay();
        IRuntimeObservationSink sink = sinkHolder.Sink;
        var replayedEventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var skippedEventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        long frameOrdinal = 0;
        var totalLines = 0;
        var replayedLines = 0;
        var skippedLines = 0;
        var ingestStart = CaptureBaselineStart();

        foreach (var line in lines)
        {
            totalLines++;
            if (!TryParseEntry(line, out var entry))
            {
                IncrementCount(skippedEventCounts, "<invalid>");
                skippedLines++;
                continue;
            }

            frameOrdinal++;
            var batchOrdinal = entry.Timestamp.UtcDateTime.Ticks;
            if (TryReplayEntry(sink, entry, frameOrdinal, batchOrdinal))
            {
                IncrementCount(replayedEventCounts, entry.EventName);
                replayedLines++;
            }
            else
            {
                IncrementCount(skippedEventCounts, entry.EventName);
                skippedLines++;
            }
        }

        sink.CompleteBatch(long.MaxValue);
        var ingestCounter = CaptureBaselineCounter(ingestStart);

        var snapshotStart = CaptureBaselineStart();
        var snapshot = sinkHolder.Owner.CreateSnapshot();
        var snapshotCounter = CaptureBaselineCounter(snapshotStart);

        var summaryStart = CaptureBaselineStart();
        var summaries = BuildCombatantSummaries(sinkHolder.Owner.Combat, sinkHolder.Owner.Entities, sinkHolder.Owner.Metadata, snapshot);
        var summaryCounter = CaptureBaselineCounter(summaryStart);

        return new PacketLogReplayResult(
            sourceName,
            totalLines,
            replayedLines,
            skippedLines,
            snapshot,
            sinkHolder.Journal,
            sinkHolder.Owner,
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

    private static PacketLogReplayResult ReplayStreamLines(IEnumerable<string> lines, string sourceName)
    {
        using var sinkHolder = SceneSinkFactory.CreateForReplay();
        IRuntimeObservationSink sink = sinkHolder.Sink;
        var replayedEventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var skippedEventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        using var inboundProcessor = new PacketStreamProcessor(sink);
        var totalLines = 0;
        var replayedLines = 0;
        var skippedLines = 0;
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

            var parsed = inboundProcessor.AppendAndProcess(
                entry.Payload,
                entry.Connection,
                entry.Timestamp.ToUnixTimeMilliseconds());

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

        sink.CompleteBatch(long.MaxValue);
        var ingestCounter = CaptureBaselineCounter(ingestStart);

        var snapshotStart = CaptureBaselineStart();
        var snapshot = sinkHolder.Owner.CreateSnapshot();
        var snapshotCounter = CaptureBaselineCounter(snapshotStart);

        var summaryStart = CaptureBaselineStart();
        var summaries = BuildCombatantSummaries(sinkHolder.Owner.Combat, sinkHolder.Owner.Entities, sinkHolder.Owner.Metadata, snapshot);
        var summaryCounter = CaptureBaselineCounter(summaryStart);

        return new PacketLogReplayResult(
            sourceName,
            totalLines,
            replayedLines,
            skippedLines,
            snapshot,
            sinkHolder.Journal,
            sinkHolder.Owner,
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

    private static PacketLogReplayBaselineCounter CaptureBaselineCounter(BaselineStart start)
    {
        var elapsed = Stopwatch.GetElapsedTime(start.Timestamp);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - start.AllocatedBytes;
        return new PacketLogReplayBaselineCounter(elapsed, Math.Max(0, allocatedBytes));
    }

    private static List<PacketLogCombatantSummary> BuildCombatantSummaries(CombatStore combat, EntityStore entities, MetadataStore metadata, SceneCombatSnapshot snapshot)
    {
        var summariesByCombatantId = new Dictionary<int, MutableCombatantSummary>();

        foreach (var e in EnumerateSummaryEvents(combat, entities, snapshot))
        {
            var sourceId = ResolveCombatantId(entities, e.SourceId);
            var targetId = e.TargetId;

            if (sourceId > 0)
            {
                EnsureSummary(summariesByCombatantId, sourceId, entities, metadata);
            }

            if (targetId > 0)
            {
                EnsureSummary(summariesByCombatantId, targetId, entities, metadata);
            }

            var observation = e.Observation;
            if (e.ContributesDamage)
            {
                ApplyDamageSummary(summariesByCombatantId, sourceId, targetId, in observation);
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

        return summariesByCombatantId
            .OrderBy(static pair => pair.Key)
            .Select(static pair => pair.Value.ToSummary())
            .ToList();
    }

    private static IEnumerable<CombatEventRecord> EnumerateSummaryEvents(CombatStore combat, EntityStore entities, SceneCombatSnapshot snapshot)
    {
        if (snapshot.EncounterStartTime <= 0 || snapshot.EncounterEndTime < snapshot.EncounterStartTime)
            yield break;

        var relevant = new HashSet<int>();
        foreach (var e in combat.Events)
        {
            if (!IsWithinEncounterWindow(e, snapshot.EncounterStartTime, snapshot.EncounterEndTime) || IsSummonDamageTarget(entities, e))
                continue;

            var sourceId = ResolveCombatantId(entities, e.SourceId);
            relevant.Add(sourceId);
            if (e.TargetId > 0)
                relevant.Add(e.TargetId);

            yield return e;
        }

        if (relevant.Count == 0)
            yield break;

        foreach (var e in combat.Events)
        {
            if (IsWithinEncounterWindow(e, snapshot.EncounterStartTime, snapshot.EncounterEndTime) || IsSummonDamageTarget(entities, e))
                continue;

            var sourceId = ResolveCombatantId(entities, e.SourceId);
            if (!IsRelevantRecoveryEvent(e, sourceId, e.TargetId, relevant))
                continue;

            yield return e;
        }
    }

    private static MutableCombatantSummary EnsureSummary(
        Dictionary<int, MutableCombatantSummary> summariesByCombatantId,
        int combatantId,
        EntityStore entities,
        MetadataStore metadata)
    {
        if (summariesByCombatantId.TryGetValue(combatantId, out var existing))
        {
            return existing;
        }

        var created = new MutableCombatantSummary(
            combatantId,
            ResolveDisplayName(entities, metadata, combatantId));
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

    private static bool IsWithinEncounterWindow(CombatEventRecord e, long start, long end) =>
        e.ObservedAtMilliseconds >= start && e.ObservedAtMilliseconds <= end;

    private static bool IsSummonDamageTarget(EntityStore entities, CombatEventRecord e)
    {
        if (e.TargetId <= 0 || !e.ContributesDamage)
            return false;

        if (IsKnownSummon(entities, e.TargetId))
            return true;

        return ResolveCombatantId(entities, e.SourceId) == ResolveCombatantId(entities, e.TargetId);
    }

    private static bool IsKnownSummon(EntityStore entities, int entityId) =>
        entities.TryGet(entityId, out var entity) && (entity.OwnerEntityId.HasValue || entity.Kind == NpcKind.Summon);

    private static bool IsRelevantRecoveryEvent(CombatEventRecord e, int sourceId, int targetId, HashSet<int> relevant)
    {
        if (e.Observation.Damage <= 0 || (!relevant.Contains(sourceId) && !relevant.Contains(targetId)))
            return false;

        return e.Observation.EventKind is CombatEventKind.Healing or CombatEventKind.Support
               || e.Observation.ValueKind is CombatValueKind.Healing or CombatValueKind.PeriodicHealing or CombatValueKind.DrainHealing or CombatValueKind.Shield or CombatValueKind.Support;
    }

    private static string ResolveDisplayName(EntityStore entities, MetadataStore metadata, int entityId)
    {
        if (metadata.TryGetDisplayName(entityId, out var displayName) && !string.IsNullOrWhiteSpace(displayName))
            return displayName;

        if (entities.TryGet(entityId, out var entity))
        {
            if (!string.IsNullOrWhiteSpace(entity.Nickname))
                return entity.Nickname;

            if (entity.NpcCode is int npcCode)
            {
                if (CombatResourceRegistry.TryResolveNpcCatalogEntry(npcCode, out var catalogEntry) && !string.IsNullOrWhiteSpace(catalogEntry.Name))
                    return catalogEntry.Name;

                if (metadata.TryGetNpcName(npcCode, out var npcName) && !string.IsNullOrWhiteSpace(npcName))
                    return npcName;

                return $"NPC-{npcCode}";
            }
        }

        return entityId.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryReplayEntry(IRuntimeObservationSink store, FrameReplayEntry entry, long frameOrdinal, long batchOrdinal)
    {
        var timestamp = entry.Timestamp.ToUnixTimeMilliseconds();
        var packet = entry.Payload;

        return entry.EventName switch
        {
            "damage" => TryReplayDamage(store, packet, timestamp, frameOrdinal, batchOrdinal),
            "periodic" => TryReplayPeriodic(store, packet, timestamp, frameOrdinal, batchOrdinal),
            "periodic-link" => TryReplayPeriodicLink(store, packet, timestamp, frameOrdinal, batchOrdinal),
            "compact-value" => TryReplayCompactValue(store, packet, timestamp, frameOrdinal, batchOrdinal),
            "compact-outcome" => TryReplayCompactOutcome(store, packet, timestamp, frameOrdinal, batchOrdinal),
            "compact-0238" => TryReplayCompact0238(store, packet, batchOrdinal),
            "compact-0638" => TryReplayCompact0638(store, packet, timestamp, frameOrdinal, batchOrdinal),
            "sidecar-3538" => TryReplay3538(packet),
            "wrapped-8456" => TryReplay8456(store, packet),
            "state-0140" => TryReplay0140(store, packet),
            "state-2136" => TryReplay2136(store, packet),
            "map-2e92" => TryReplayMap2E92(store, packet),
            "state-0240" => TryReplay0240(store, packet),
            "state-4636" => TryReplay4636(store, packet),
            "state-4536" => TryReplay4536(store, packet),
            "state-4036" => TryReplayState4036(store, packet, timestamp),
            "state-4136" => Packet4136Parser.TryParse(packet, out _),
            "state-1d37" => Packet1D37Parser.TryParse(packet, out _),
            "state-4936" => Packet4936Parser.TryParse(packet, out _),
            "aux-2a38" => TryReplay2A38(store, packet, timestamp, frameOrdinal, batchOrdinal),
            "aux-2b38" => Packet2B38Parser.TryParse(packet, out _),
            "aux-2c38" => TryReplay2C38(store, packet, timestamp, frameOrdinal, batchOrdinal),
            "nickname" => TryReplayNickname(store, packet),
            "remain-hp" => TryReplayRemainHp(store, packet, timestamp),
            "entity-value-008d" => Packet008DRemainHpParser.TryParse(packet, out _),
            "battle-toggle" => TryReplayBattleToggle(store, packet, timestamp),
            "summon" => TryReplaySummon(store, packet, entry.Metadata),
            "npc-spawn" => TryReplayNpcSpawn(store, packet, entry.Metadata, timestamp),
            "frame-batch" => TryReplayFrameBatch(store, packet),
            "recovery-path" => TryReplayRecoveryPath(store, packet, timestamp),
            _ => false
        };
    }

    private static bool TryReplayFrameBatch(IRuntimeObservationSink store, ReadOnlySpan<byte> packet)
        => TryReplayNickname(store, packet);

    private static bool TryReplayDamage(IRuntimeObservationSink store, ReadOnlySpan<byte> packet, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        if (!TryParseDamagePacket(packet, out var parsed) || parsed.Damage <= 0)
        {
            return false;
        }

        var combatPacket = new ParsedCombatPacket
        {
            TargetId = parsed.TargetId,
            LayoutTag = parsed.LayoutTag,
            Flag = parsed.Flag,
            SourceId = parsed.SourceId,
            OriginalSkillCode = parsed.SkillCodeRaw,
            SkillCode = parsed.SkillCodeRaw,
            Marker = parsed.Marker,
            Type = parsed.Type,
            Modifiers = parsed.Modifiers,
            Unknown = parsed.Unknown,
            Damage = parsed.Damage,
            Loop = parsed.Loop,
            DrainHealAmount = parsed.DrainHealAmount,
            RegenerationAmount = parsed.RegenerationAmount,
            DetailRaw = parsed.DetailRaw,
            ResourceKind = parsed.ResourceKind,
            Timestamp = timestamp,
            FrameOrdinal = frameOrdinal,
            BatchOrdinal = batchOrdinal
        };

        if (parsed.TailMultiHitCount > 0)
        {
            combatPacket.MultiHitCount = parsed.TailMultiHitCount;
            combatPacket.Modifiers |= DamageModifiers.MultiHit;
        }

        store.AppendCombatPacket(combatPacket);

        if (parsed.RegenerationAmount > 0 && ShouldStoreRegenerationHealing(store, parsed.TargetId))
        {
            var regenPacket = new ParsedCombatPacket
            {
                TargetId = parsed.TargetId,
                SourceId = parsed.TargetId,
                OriginalSkillCode = parsed.SkillCodeRaw,
                SkillCode = parsed.SkillCodeRaw,
                Damage = parsed.RegenerationAmount,
                EventKind = CombatEventKind.Healing,
                ValueKind = CombatValueKind.Healing,
                Timestamp = timestamp,
                FrameOrdinal = frameOrdinal,
                BatchOrdinal = batchOrdinal
            };
            regenPacket.SetEffectTag(PacketEffectTag.RegenerationHealing);
            store.AppendCombatPacket(regenPacket);
        }

        if (ShouldStoreDrainHealing(parsed))
        {
            store.AppendCombatPacket(new ParsedCombatPacket
            {
                TargetId = parsed.SourceId,
                SourceId = parsed.SourceId,
                OriginalSkillCode = parsed.SkillCodeRaw,
                SkillCode = parsed.SkillCodeRaw,
                Damage = parsed.DrainHealAmount,
                DrainHealAmount = parsed.DrainHealAmount,
                Timestamp = timestamp,
                FrameOrdinal = frameOrdinal,
                BatchOrdinal = batchOrdinal
            });
        }

        return true;
    }

    private static bool ShouldStoreDrainHealing(Packet0438Damage parsed)
    {
        if (parsed.DrainHealAmount <= 0 || parsed.SourceId == parsed.TargetId)
        {
            return false;
        }

        return true;
    }

    private static bool ShouldStoreRegenerationHealing(IRuntimeObservationSink store, int targetId)
    {
        if (targetId <= 0)
        {
            return false;
        }

        if (store.HasSummonOwner(targetId))
        {
            return false;
        }

        return !store.TryGetNpcRuntimeState(targetId, out var state) || state.Kind != NpcKind.Summon;
    }

    private static bool TryReplayPeriodic(IRuntimeObservationSink store, ReadOnlySpan<byte> packet, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        if (!Packet0538PeriodicValueParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        if (parsed.IsLinkRecord)
        {
            store.RegisterPeriodicLink0538(
                parsed.TargetId,
                parsed.SourceId,
                parsed.LinkId,
                parsed.Unknown,
                parsed.TailRaw,
                timestamp,
                frameOrdinal,
                batchOrdinal);
            return true;
        }

        var combatPacket = new ParsedCombatPacket
        {
            TargetId = parsed.TargetId,
            SourceId = parsed.SourceId,
            OriginalSkillCode = parsed.SkillCodeRaw,
            SkillCode = parsed.LegacySkillCode,
            Unknown = parsed.Unknown,
            Damage = parsed.Damage,
            Timestamp = timestamp,
            FrameOrdinal = frameOrdinal,
            BatchOrdinal = batchOrdinal
        };
        combatPacket.SetPeriodicEffect(
            parsed.TargetId == parsed.SourceId ? PeriodicEffectRelation.Self : PeriodicEffectRelation.Target,
            parsed.Mode);

        store.AppendCombatPacket(combatPacket);
        return true;
    }

    private static bool TryReplayPeriodicLink(IRuntimeObservationSink store, ReadOnlySpan<byte> packet, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        if (!Packet0538PeriodicValueParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        if (!parsed.IsLinkRecord)
        {
            return false;
        }

        store.RegisterPeriodicLink0538(
            parsed.TargetId,
            parsed.SourceId,
            parsed.LinkId,
            parsed.Unknown,
            parsed.TailRaw,
            timestamp,
            frameOrdinal,
            batchOrdinal);
        return true;
    }

    private static bool TryReplayCompactValue(IRuntimeObservationSink store, ReadOnlySpan<byte> packet, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        if (!TryParseCompactValuePacket(packet, out var parsed))
        {
            return false;
        }

        store.RegisterCompactValue0438(
            parsed.TargetId,
            parsed.SourceId,
            parsed.SkillCodeRaw,
            parsed.Marker,
            parsed.LayoutTag,
            parsed.Type,
            parsed.Value,
            timestamp,
            frameOrdinal,
            batchOrdinal);
        return true;
    }

    private static bool TryReplayCompactOutcome(IRuntimeObservationSink store, ReadOnlySpan<byte> packet, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        if (!TryParseCompactOutcomePacket(packet, out var parsed))
        {
            return false;
        }

        store.RegisterCompactValue0438(
            parsed.TargetId,
            parsed.SourceId,
            parsed.SkillCodeRaw,
            parsed.Marker,
            parsed.LayoutTag,
            parsed.Type,
            timestamp,
            frameOrdinal,
            batchOrdinal);
        return true;
    }

    private static bool TryReplayCompact0238(IRuntimeObservationSink store, ReadOnlySpan<byte> packet, long batchOrdinal)
    {
        if (!Packet0238CompactControlParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.RegisterCompactControl0238(parsed.SourceId, parsed.SkillCodeRaw, parsed.Marker, batchOrdinal);
        return true;
    }

    private static bool TryReplayCompact0638(IRuntimeObservationSink store, ReadOnlySpan<byte> packet, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        if (!Packet0638CompactControlParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.RegisterCompactControl0638(parsed.SourceId, parsed.SkillCodeRaw, parsed.Marker, timestamp, frameOrdinal, batchOrdinal);
        return true;
    }

    private static bool TryReplay3538(ReadOnlySpan<byte> packet)
        => Packet3538SidecarParser.TryParse(packet, out _);

    private static bool TryReplay8456(IRuntimeObservationSink store, ReadOnlySpan<byte> packet)
    {
        if (!Packet8456EnvelopeParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        return true;
    }

    private static bool TryReplay0140(IRuntimeObservationSink store, ReadOnlySpan<byte> packet)
    {
        if (!Packet0140Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        StageDestinationMapFromSceneState(store, parsed.Value0);

        var targetId = store.ResolveNpcObservationSource();
        if (targetId > 0)
        {
            store.AppendNpc0140Value(targetId, parsed.Value0);
            if (parsed.Value0 <= int.MaxValue)
            {
                TryApplyNpcCatalog(store, targetId, (int)parsed.Value0, requireCatalogEntry: true);
            }
        }

        return true;
    }

    private static bool TryReplay2136(IRuntimeObservationSink store, ReadOnlySpan<byte> packet)
    {
        if (!Packet2136Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        StageDestinationMapFromSceneState(store, parsed.Value0);

        var targetId = store.ResolveNpcObservationSource();
        if (targetId > 0)
        {
            store.AppendNpc2136State(targetId, parsed.Sequence, parsed.Value0);
            if (parsed.Value0 <= int.MaxValue)
            {
                TryApplyNpcCatalog(store, targetId, (int)parsed.Value0, requireCatalogEntry: true);
            }
        }

        return true;
    }

    private static bool TryReplayMap2E92(IRuntimeObservationSink store, ReadOnlySpan<byte> packet)
    {
        if (!Packet2E92Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.StageDestinationMapInstance(parsed.InstanceId);
        return true;
    }

    private static bool TryReplay0240(IRuntimeObservationSink store, ReadOnlySpan<byte> packet)
    {
        if (!Packet0240Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        StageDestinationMapFromSceneState(store, parsed.Value0);

        var targetId = store.ResolveNpcObservationSource();
        if (targetId > 0)
        {
            store.AppendNpc0240Value(targetId, parsed.Value0);
            if (parsed.Value0 <= int.MaxValue)
            {
                TryApplyNpcCatalog(store, targetId, (int)parsed.Value0, requireCatalogEntry: true);
            }
        }

        return true;
    }

    private static void StageDestinationMapFromSceneState(IRuntimeObservationSink store, uint value)
    {
        if (IsSceneStateMapId(value))
        {
            store.StageDestinationMap(value);
        }
    }

    private static bool IsSceneStateMapId(uint value)
        => SceneMapIdClassifier.IsSceneStateMapId(value);

    private static bool TryReplay4636(IRuntimeObservationSink store, ReadOnlySpan<byte> packet)
    {
        if (!Packet4636Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.AppendNpc4636State(parsed.SourceId, parsed.State0, parsed.State1);
        store.RememberNpcObservationSource(parsed.SourceId);
        return true;
    }

    private static bool TryReplay4536(IRuntimeObservationSink store, ReadOnlySpan<byte> packet)
    {
        if (!Packet4536Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.RememberNpcObservationSource(parsed.SourceId);
        return true;
    }

    private static bool TryReplay2A38(IRuntimeObservationSink store, ReadOnlySpan<byte> packet, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        if (!Packet2A38Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.RegisterObservation2A38(parsed.SourceId, parsed.Mode, parsed.GroupCode, parsed.SequenceId, parsed.HeadValue, parsed.BuffCodeRaw, timestamp, frameOrdinal, batchOrdinal);
        return true;
    }

    private static bool TryReplay2C38(IRuntimeObservationSink store, ReadOnlySpan<byte> packet, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        if (!Packet2C38Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.RegisterObservation2C38(
            parsed.SourceId,
            parsed.Mode,
            parsed.SequenceId,
            parsed.ResultCode,
            parsed.TailSourceId,
            parsed.TailSkillCodeRaw,
            timestamp,
            frameOrdinal,
            batchOrdinal);
        return true;
    }

    private static bool TryReplayNickname(IRuntimeObservationSink store, ReadOnlySpan<byte> packet)
    {
        if (Packet3336NicknameParser.TryParse(packet, out var ownParsed))
        {
            store.AppendNickname(ownParsed.PlayerId, ownParsed.Nickname, ownParsed.OriginServerId);
            store.MarkSceneArrival();
            return true;
        }

        if (Packet4436NicknameParser.TryParse(packet, out var otherParsed))
        {
            store.AppendNickname(otherParsed.PlayerId, otherParsed.Nickname, otherParsed.OriginServerId);
            return true;
        }

        if (Packet048DNicknameParser.TryParse(packet, out var parsed))
        {
            store.AppendNickname(parsed.PlayerId, parsed.Nickname, parsed.OriginServerId);
            return true;
        }

        if (Packet0994NicknameParser.TryParse(packet, out var rosterParsed))
        {
            store.AppendNickname(rosterParsed.PlayerId, rosterParsed.Nickname);
            return true;
        }

        return false;
    }

    private static bool TryReplayRemainHp(IRuntimeObservationSink store, ReadOnlySpan<byte> packet, long timestamp)
    {
        if (!Packet008DRemainHpParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        if (Packet008DRemainHpParser.IsHealthValue(parsed))
        {
            store.AppendNpcHp(parsed.NpcId, checked((int)parsed.Hp), timestamp);
        }

        return true;
    }

    private static bool TryReplayBattleToggle(IRuntimeObservationSink store, ReadOnlySpan<byte> packet, long timestamp)
    {
        if (!Packet218DBattleToggleParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        if (parsed.IsActive is bool isActive)
        {
            store.SetNpcBattle(parsed.NpcId, isActive, timestamp);
        }
        else
        {
            store.ToggleNpcBattle(parsed.NpcId);
        }

        return true;
    }

    private static bool TryReplaySummon(IRuntimeObservationSink store, ReadOnlySpan<byte> packet, string metadata)
    {
        if (TryParseSummonMetadata(metadata, out var ownerId, out var summonId, out var npcCode))
        {
            if (npcCode > 0)
            {
                TryApplyNpcCatalog(store, summonId, npcCode);
            }

            store.AppendNpcKind(summonId, NpcKind.Summon);
            store.AppendSummon(ownerId, summonId);
            return true;
        }

        if (!Packet4036CreateParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        if (parsed.NpcCode.HasValue)
        {
            TryApplyNpcCatalog(store, parsed.SummonId, parsed.NpcCode.Value);
        }

        store.AppendNpcKind(parsed.SummonId, NpcKind.Summon);
        store.AppendSummon(parsed.OwnerId, parsed.SummonId);
        return true;
    }

    private static bool TryParseSummonMetadata(string metadata, out int ownerId, out int summonId, out int npcCode)
    {
        ownerId = 0;
        summonId = 0;
        npcCode = 0;

        if (string.IsNullOrEmpty(metadata))
        {
            return false;
        }

        foreach (var segment in metadata.Split('|'))
        {
            if (segment.StartsWith("owner=", StringComparison.Ordinal) &&
                int.TryParse(segment.AsSpan("owner=".Length), CultureInfo.InvariantCulture, out var o))
            {
                ownerId = o;
            }
            else if (segment.StartsWith("summon=", StringComparison.Ordinal) &&
                     int.TryParse(segment.AsSpan("summon=".Length), CultureInfo.InvariantCulture, out var s))
            {
                summonId = s;
            }
            else if (segment.StartsWith("npcCode=", StringComparison.Ordinal) &&
                     int.TryParse(segment.AsSpan("npcCode=".Length), CultureInfo.InvariantCulture, out var m))
            {
                npcCode = m;
            }
        }

        return ownerId > 0 && summonId > 0;
    }

    private static bool TryReplayNpcSpawn(IRuntimeObservationSink store, ReadOnlySpan<byte> packet, string metadata, long timestamp)
    {
        if (Packet4036CreateParser.TryParseNpcSpawn(packet, out var spawn))
        {
            if (spawn.NpcCode.HasValue)
            {
                TryApplyNpcCatalog(store, spawn.EntityId, spawn.NpcCode.Value);
            }

            if (spawn.CurrentHp is int currentHp && spawn.MaxHp is int maxHp)
            {
                store.AppendNpcHp(spawn.EntityId, currentHp, maxHp, timestamp);
            }

            return true;
        }

        if (!TryParseNpcSpawnMetadata(metadata, out var entityId, out var npcCode, out var metadataCurrentHp, out var metadataMaxHp))
        {
            return false;
        }

        if (npcCode > 0)
        {
            TryApplyNpcCatalog(store, entityId, npcCode);
        }

        if (metadataCurrentHp is int parsedCurrentHp &&
            metadataMaxHp is int parsedMaxHp &&
            parsedMaxHp >= parsedCurrentHp)
        {
            store.AppendNpcHp(entityId, parsedCurrentHp, parsedMaxHp, timestamp);
        }

        return true;
    }

    private static bool TryReplayState4036(IRuntimeObservationSink store, ReadOnlySpan<byte> packet, long timestamp)
    {
        if (Packet4036CreateParser.TryParseNpcSpawn(packet, out var spawn) && spawn.NpcCode.HasValue)
        {
            TryApplyNpcCatalog(store, spawn.EntityId, spawn.NpcCode.Value, requireCatalogEntry: true);
            if (spawn.CurrentHp is int currentHp && spawn.MaxHp is int maxHp)
            {
                store.AppendNpcHp(spawn.EntityId, currentHp, maxHp, timestamp);
            }
        }

        if (Packet4036CreateParser.TryParseOwner(packet, out var entityId, out var ownerId))
        {
            store.AppendSummon(ownerId, entityId);
        }

        return Packet4036Parser.TryParse(packet, out _);
    }

    private static bool TryParseNpcSpawnMetadata(string metadata, out int entityId, out int npcCode, out int? currentHp, out int? maxHp)
    {
        entityId = 0;
        npcCode = 0;
        currentHp = null;
        maxHp = null;

        if (string.IsNullOrEmpty(metadata))
        {
            return false;
        }

        foreach (var segment in metadata.Split('|'))
        {
            if (segment.StartsWith("entity=", StringComparison.Ordinal) &&
                int.TryParse(segment.AsSpan("entity=".Length), CultureInfo.InvariantCulture, out var e))
            {
                entityId = e;
            }
            else if (segment.StartsWith("npcCode=", StringComparison.Ordinal) &&
                     int.TryParse(segment.AsSpan("npcCode=".Length), CultureInfo.InvariantCulture, out var m))
            {
                npcCode = m;
            }
            else if (segment.StartsWith("currentHp=", StringComparison.Ordinal) &&
                     int.TryParse(segment.AsSpan("currentHp=".Length), CultureInfo.InvariantCulture, out var hp))
            {
                currentHp = hp;
            }
            else if (segment.StartsWith("maxHp=", StringComparison.Ordinal) &&
                     int.TryParse(segment.AsSpan("maxHp=".Length), CultureInfo.InvariantCulture, out var mhp))
            {
                maxHp = mhp;
            }
        }

        return entityId > 0;
    }

    private static bool TryReplayRecoveryPath(IRuntimeObservationSink store, ReadOnlySpan<byte> packet, long timestamp)
    {
        if (TryReplayNickname(store, packet)) return true;

        if (Packet4036CreateParser.TryParse(packet, out var summon))
        {
            store.AppendSummon(summon.OwnerId, summon.SummonId);
            if (summon.NpcCode.HasValue)
            {
                TryApplyNpcCatalog(store, summon.SummonId, summon.NpcCode.Value);
            }

            return true;
        }

        if (Packet4036CreateParser.TryParseNpcSpawn(packet, out var spawn))
        {
            if (spawn.NpcCode.HasValue)
            {
                TryApplyNpcCatalog(store, spawn.EntityId, spawn.NpcCode.Value);
            }

            if (spawn.CurrentHp is int currentHp && spawn.MaxHp is int maxHp)
            {
                store.AppendNpcHp(spawn.EntityId, currentHp, maxHp, timestamp);
            }

            return true;
        }

        if (Packet3538SidecarParser.TryParse(packet, out _))
        {
            return true;
        }

        if (Packet218DBattleToggleParser.TryParse(packet, out var battleToggle))
        {
            if (battleToggle.IsActive is bool isActive)
            {
                store.SetNpcBattle(battleToggle.NpcId, isActive, timestamp);
            }
            else
            {
                store.ToggleNpcBattle(battleToggle.NpcId);
            }

            return true;
        }

        return false;
    }

    private static void TryApplyNpcCatalog(
        IRuntimeObservationSink store,
        int instanceId,
        int npcCode,
        bool requireCatalogEntry = false)
    {
        if (instanceId <= 0 || npcCode <= 0)
        {
            return;
        }

        var hasCatalogEntry = CombatResourceRegistry.TryResolveNpcCatalogEntry(npcCode, out var entry);
        if (requireCatalogEntry && !hasCatalogEntry)
        {
            return;
        }

        var lifecycleId = store.ResolveLifecycleId(instanceId);
        if (hasCatalogEntry &&
            store.TryGetNpcRuntimeState(lifecycleId, out var existing) &&
            existing.NpcCode is int existingCode &&
            existingCode != npcCode &&
            CombatResourceRegistry.TryResolveNpcCatalogEntry(existingCode, out _))
        {
            store.RebindInstanceLifecycle(instanceId);
        }

        store.AppendNpcCode(instanceId, npcCode);

        if (!hasCatalogEntry)
        {
            return;
        }
        store.AppendNpcName(npcCode, entry.Name);

        var kind = CombatResourceRegistry.ResolveNpcKind(entry.Kind);
        if (kind != NpcKind.Unknown && kind != NpcKind.Summon)
        {
            store.AppendNpcKind(instanceId, kind);
        }
    }

    private static bool TryParseEntry(string line, out FrameReplayEntry entry)
    {
        entry = default;
        if (!TryReadLineSegments(line, out var timestampText, out var eventName, out var connectionText, out var dataText, out var metadata))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(
                timestampText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(eventName))
        {
            return false;
        }

        if (!TryParseConnection(connectionText, out var connection))
        {
            return false;
        }

        try
        {
            entry = new FrameReplayEntry(
                timestamp,
                eventName,
                connection,
                Convert.FromHexString(dataText),
                metadata);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
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
                return ReplayLogKind.Frame;
            }

            return thirdSegment.Contains(':')
                ? ReplayLogKind.Stream
                : ReplayLogKind.Raw;
        }

        return ReplayLogKind.Frame;
    }

    private static bool TryDetectLogKindFromSourceName(string sourceName, out ReplayLogKind logKind)
    {
        if (sourceName.Contains(".stream.", StringComparison.OrdinalIgnoreCase) ||
            sourceName.EndsWith("stream.log", StringComparison.OrdinalIgnoreCase))
        {
            logKind = ReplayLogKind.Stream;
            return true;
        }

        if (sourceName.Contains(".frame.", StringComparison.OrdinalIgnoreCase) ||
            sourceName.EndsWith("frame.log", StringComparison.OrdinalIgnoreCase))
        {
            logKind = ReplayLogKind.Frame;
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

    private static bool TryParseDamagePacket(ReadOnlySpan<byte> packet, out Packet0438Damage parsed)
    {
        if (Packet0438DamageParser.TryParse(packet, out parsed))
        {
            return true;
        }

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _))
        {
            return false;
        }

        return Packet0438DamageParser.TryParsePayload(packet[reader.Offset..], out parsed, out _);
    }

    private static bool TryParseCompactValuePacket(ReadOnlySpan<byte> packet, out Packet0438CompactValue parsed)
    {
        if (Packet0438CompactValueParser.TryParse(packet, out parsed))
        {
            return true;
        }

        parsed = default;
        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out var length) || length <= 3 || reader.Remaining < 2)
        {
            return false;
        }

        if (packet[reader.Offset] != 0x04 || packet[reader.Offset + 1] != 0x38)
        {
            return false;
        }

        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var targetId)) return false;
        if (!reader.TryReadVarInt(out var layoutTag)) return false;
        if (!reader.TryReadVarInt(out var flag)) return false;
        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (targetId <= 0 || sourceId <= 0 || layoutTag != 0 || reader.Remaining < 5) return false;
        if (!reader.TryReadUInt32Le(out var skillCodeRaw)) return false;
        if (!reader.TryReadByte(out var marker)) return false;
        if (!reader.TryReadVarInt(out var type)) return false;
        if (!reader.TryReadVarInt(out var unknown)) return false;
        if (!reader.TryReadVarInt(out var value)) return false;
        if (!reader.TryReadVarInt(out var loop)) return false;

        var tailLength = reader.Remaining;
        var tailRaw = 0;
        if (tailLength >= 4)
        {
            var tail = packet[reader.Offset..];
            tailRaw = tail[0]
                | (tail[1] << 8)
                | (tail[2] << 16)
                | (tail[3] << 24);
        }

        parsed = new Packet0438CompactValue(
            targetId,
            layoutTag,
            flag,
            sourceId,
            skillCodeRaw,
            marker,
            type,
            unknown,
            value,
            loop,
            tailLength,
            tailRaw);
        return true;
    }

    private static bool TryParseCompactOutcomePacket(ReadOnlySpan<byte> packet, out Packet0438CompactOutcome parsed)
    {
        if (Packet0438CompactOutcomeParser.TryParse(packet, out parsed))
        {
            return true;
        }

        parsed = default;
        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out var length) || length <= 3 || reader.Remaining < 2)
        {
            return false;
        }

        if (packet[reader.Offset] != 0x04 || packet[reader.Offset + 1] != 0x38)
        {
            return false;
        }

        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var targetId)) return false;
        if (!reader.TryReadVarInt(out var layoutTag)) return false;
        if (!reader.TryReadVarInt(out var flag)) return false;
        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (targetId <= 0 || sourceId <= 0 || layoutTag != 2 || reader.Remaining < 5) return false;
        if (!reader.TryReadUInt32Le(out var skillCodeRaw)) return false;
        if (!reader.TryReadByte(out var marker)) return false;
        if (!reader.TryReadVarInt(out var type)) return false;

        parsed = new Packet0438CompactOutcome(
            targetId,
            layoutTag,
            flag,
            sourceId,
            skillCodeRaw,
            marker,
            type,
            reader.Remaining);
        return true;
    }

    private readonly record struct FrameReplayEntry(
        DateTimeOffset Timestamp,
        string EventName,
        TcpConnection Connection,
        byte[] Payload,
        string Metadata);

    private readonly record struct StreamReplayEntry(
        DateTimeOffset Timestamp,
        string Direction,
        TcpConnection Connection,
        byte[] Payload);

    private enum ReplayLogKind
    {
        Frame,
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
