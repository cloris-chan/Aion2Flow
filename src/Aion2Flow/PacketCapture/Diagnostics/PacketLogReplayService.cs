using System.Globalization;
using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.PacketCapture.Protocol;
using Cloris.Aion2Flow.PacketCapture.Readers;
using Cloris.Aion2Flow.PacketCapture.Streams;

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
            ReplayLogKind.Raw => throw new NotSupportedException("Raw log replay is not supported yet. Use stream logs for whole-battle replay."),
            _ => throw new InvalidOperationException($"Unsupported replay log kind: {logKind}.")
        };
    }

    private static PacketLogReplayResult ReplayFrameLines(List<string> lines, string sourceName)
    {
        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var replayedEventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var skippedEventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        long frameOrdinal = 0;

        foreach (var line in lines)
        {
            if (!TryParseEntry(line, out var entry))
            {
                IncrementCount(skippedEventCounts, "<invalid>");
                continue;
            }

            frameOrdinal++;
            var batchOrdinal = entry.Timestamp.UtcDateTime.Ticks;
            if (TryReplayEntry(store, entry, frameOrdinal, batchOrdinal))
            {
                IncrementCount(replayedEventCounts, entry.EventName);
            }
            else
            {
                IncrementCount(skippedEventCounts, entry.EventName);
            }
        }

        store.FlushOrphanCompactHits();
        var snapshot = engine.CreateBattleSnapshot();
        var summaries = BuildCombatantSummaries(store, snapshot);

        return new PacketLogReplayResult(
            sourceName,
            lines.Count,
            replayedEventCounts.Values.Sum(),
            skippedEventCounts.Values.Sum(),
            snapshot,
            store.DeepClone(),
            summaries,
            replayedEventCounts,
            skippedEventCounts);
    }

    private static PacketLogReplayResult ReplayStreamLines(List<string> lines, string sourceName)
    {
        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        var replayedEventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var skippedEventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        using var inboundProcessor = new PacketStreamProcessor(store);

        foreach (var line in lines)
        {
            if (!TryParseStreamEntry(line, out var entry))
            {
                IncrementCount(skippedEventCounts, "<invalid>");
                continue;
            }

            if (string.Equals(entry.Direction, "outbound", StringComparison.OrdinalIgnoreCase))
            {
                IncrementCount(skippedEventCounts, "outbound-ignored");
                continue;
            }

            var parsed = inboundProcessor.AppendAndProcess(
                entry.Payload,
                entry.Connection,
                entry.Timestamp.ToUnixTimeMilliseconds());

            if (parsed)
            {
                IncrementCount(replayedEventCounts, entry.Direction);
            }
            else
            {
                IncrementCount(skippedEventCounts, entry.Direction);
            }
        }

        store.FlushPendingOutcomeSidecars();
        store.FlushOrphanCompactHits();
        var snapshot = engine.CreateBattleSnapshot();
        var summaries = BuildCombatantSummaries(store, snapshot);

        return new PacketLogReplayResult(
            sourceName,
            lines.Count,
            replayedEventCounts.Values.Sum(),
            skippedEventCounts.Values.Sum(),
            snapshot,
            store.DeepClone(),
            summaries,
            replayedEventCounts,
            skippedEventCounts);
    }

    private static List<PacketLogCombatantSummary> BuildCombatantSummaries(
        CombatMetricsStore store,
        DamageMeterSnapshot snapshot)
    {
        var summariesByCombatantId = new Dictionary<int, MutableCombatantSummary>();

        foreach (var context in CombatMetricsEngine.EnumerateBattlePackets(store, snapshot.BattleStartTime, snapshot.BattleEndTime))
        {
            var packet = context.Packet;
            var sourceId = context.SourceId;
            var targetId = context.TargetId;

            if (sourceId > 0)
            {
                EnsureSummary(summariesByCombatantId, sourceId, store, snapshot);
            }

            if (targetId > 0)
            {
                EnsureSummary(summariesByCombatantId, targetId, store, snapshot);
            }

            if (ContributesDamage(packet))
            {
                ApplyDamageSummary(summariesByCombatantId, sourceId, targetId, packet);
                continue;
            }

            if (ContributesHealing(packet))
            {
                ApplyHealingSummary(summariesByCombatantId, sourceId, targetId, packet);
                continue;
            }

            if (ContributesShield(packet))
            {
                ApplyShieldSummary(summariesByCombatantId, sourceId, targetId, packet);
            }
        }

        return summariesByCombatantId
            .OrderBy(static pair => pair.Key)
            .Select(static pair => pair.Value.ToSummary())
            .ToList();
    }

    private static MutableCombatantSummary EnsureSummary(
        Dictionary<int, MutableCombatantSummary> summariesByCombatantId,
        int combatantId,
        CombatMetricsStore store,
        DamageMeterSnapshot snapshot)
    {
        if (summariesByCombatantId.TryGetValue(combatantId, out var existing))
        {
            return existing;
        }

        var created = new MutableCombatantSummary(
            combatantId,
            CombatMetricsEngine.ResolveCombatantDisplayName(store, snapshot, combatantId));
        summariesByCombatantId[combatantId] = created;
        return created;
    }

    private static void ApplyDamageSummary(
        Dictionary<int, MutableCombatantSummary> summariesByCombatantId,
        int sourceId,
        int targetId,
        ParsedCombatPacket packet)
    {
        var hitContribution = Math.Max(0, packet.HitContribution);
        var attemptContribution = Math.Max(hitContribution, Math.Max(0, packet.AttemptContribution));
        var criticalContribution = packet.IsCritical ? hitContribution : 0;
        var evadeContribution = (packet.Modifiers & DamageModifiers.Evade) != 0 ? attemptContribution : 0;
        var invincibleContribution = (packet.Modifiers & DamageModifiers.Invincible) != 0 ? attemptContribution : 0;

        if (sourceId > 0 && summariesByCombatantId.TryGetValue(sourceId, out var source))
        {
            source.OutgoingDamage += packet.Damage;
            source.OutgoingHits += hitContribution;
            source.OutgoingAttempts += attemptContribution;
            source.OutgoingCriticals += criticalContribution;
            source.OutgoingEvades += evadeContribution;
            source.OutgoingInvincibles += invincibleContribution;
        }

        if (targetId > 0 && summariesByCombatantId.TryGetValue(targetId, out var target))
        {
            target.IncomingDamage += packet.Damage;
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
        ParsedCombatPacket packet)
    {
        if (sourceId > 0 && summariesByCombatantId.TryGetValue(sourceId, out var source))
        {
            source.OutgoingHealing += packet.Damage;
        }

        if (targetId > 0 && summariesByCombatantId.TryGetValue(targetId, out var target))
        {
            target.IncomingHealing += packet.Damage;
            if (packet.EffectTag == PacketEffectTag.RegenerationHealing)
            {
                target.RegenerationHealing += packet.Damage;
            }
        }
    }

    private static void ApplyShieldSummary(
        Dictionary<int, MutableCombatantSummary> summariesByCombatantId,
        int sourceId,
        int targetId,
        ParsedCombatPacket packet)
    {
        if (packet.EffectTag == PacketEffectTag.ShieldAbsorbed)
        {
            if (packet.Damage <= 0)
            {
                return;
            }

            if (sourceId > 0 && summariesByCombatantId.TryGetValue(sourceId, out var absorbSource))
            {
                absorbSource.OutgoingShieldAbsorbed += packet.Damage;
            }

            if (targetId > 0 && summariesByCombatantId.TryGetValue(targetId, out var absorbTarget))
            {
                absorbTarget.IncomingShieldAbsorbed += packet.Damage;
            }
            return;
        }

        if (sourceId > 0 && summariesByCombatantId.TryGetValue(sourceId, out var source))
        {
            source.OutgoingShield += packet.Damage;
        }

        if (targetId > 0 && summariesByCombatantId.TryGetValue(targetId, out var target))
        {
            target.IncomingShield += packet.Damage;
        }
    }

    private static bool ContributesDamage(ParsedCombatPacket packet)
    {
        if (packet.EventKind == CombatEventKind.Damage &&
            packet.ValueKind is CombatValueKind.Damage or CombatValueKind.PeriodicDamage or CombatValueKind.DrainDamage or CombatValueKind.Unknown &&
            (packet.AttemptContribution > 0 || (packet.Modifiers & (DamageModifiers.Evade | DamageModifiers.Invincible)) != 0))
        {
            return true;
        }

        return packet.ValueKind switch
        {
            CombatValueKind.Damage => packet.Damage > 0,
            CombatValueKind.PeriodicDamage => packet.Damage > 0,
            CombatValueKind.DrainDamage => packet.Damage > 0,
            CombatValueKind.Unknown => packet.EventKind == CombatEventKind.Damage && packet.Damage > 0,
            _ => false
        };
    }

    private static bool ContributesHealing(ParsedCombatPacket packet)
    {
        return packet.ValueKind switch
        {
            CombatValueKind.Healing => packet.Damage > 0,
            CombatValueKind.PeriodicHealing => packet.Damage > 0,
            CombatValueKind.DrainHealing => packet.Damage > 0,
            CombatValueKind.Shield => false,
            _ => packet.EventKind == CombatEventKind.Healing && packet.Damage > 0
        };
    }

    private static bool ContributesShield(ParsedCombatPacket packet)
        => packet.ValueKind == CombatValueKind.Shield && packet.Damage > 0;

    private static bool TryReplayEntry(CombatMetricsStore store, FrameReplayEntry entry, long frameOrdinal, long batchOrdinal)
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
            "sidecar-3538" => TryReplay3538(store, packet),
            "wrapped-8456" => TryReplay8456(store, packet),
            "state-0140" => TryReplay0140(store, packet),
            "state-2136" => TryReplay2136(store, packet),
            "state-0240" => TryReplay0240(store, packet),
            "state-4636" => TryReplay4636(store, packet),
            "state-4536" => TryReplay4536(store, packet),
            "state-4036" => TryReplayState4036(store, packet),
            "state-4136" => Packet4136Parser.TryParse(packet, out _),
            "state-1d37" => Packet1D37Parser.TryParse(packet, out _),
            "state-4936" => Packet4936Parser.TryParse(packet, out _),
            "aux-2a38" => TryReplay2A38(store, packet, timestamp, frameOrdinal, batchOrdinal),
            "aux-2b38" => Packet2B38Parser.TryParse(packet, out _),
            "aux-2c38" => TryReplay2C38(store, packet, timestamp, frameOrdinal, batchOrdinal),
            "nickname" => TryReplayNickname(store, packet),
            "remain-hp" => TryReplayRemainHp(store, packet),
            "battle-toggle" => TryReplayBattleToggle(store, packet),
            "summon" => TryReplaySummon(store, packet, entry.Metadata),
            "npc-spawn" => TryReplayNpcSpawn(store, packet, entry.Metadata),
            "recovery-path" => TryReplayRecoveryPath(store, packet, entry.Metadata),
            _ => false
        };
    }

    private static bool TryReplayDamage(CombatMetricsStore store, ReadOnlySpan<byte> packet, long timestamp, long frameOrdinal, long batchOrdinal)
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
                IsNormalized = true,
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

    private static bool ShouldStoreRegenerationHealing(CombatMetricsStore store, int targetId)
    {
        if (targetId <= 0)
        {
            return false;
        }

        if (store.SummonOwnerByInstance.ContainsKey(targetId))
        {
            return false;
        }

        return !store.TryGetNpcRuntimeState(targetId, out var state) || state.Kind != NpcKind.Summon;
    }

    private static bool TryReplayPeriodic(CombatMetricsStore store, ReadOnlySpan<byte> packet, long timestamp, long frameOrdinal, long batchOrdinal)
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

    private static bool TryReplayPeriodicLink(CombatMetricsStore store, ReadOnlySpan<byte> packet, long timestamp, long frameOrdinal, long batchOrdinal)
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

    private static bool TryReplayCompactValue(CombatMetricsStore store, ReadOnlySpan<byte> packet, long timestamp, long frameOrdinal, long batchOrdinal)
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

    private static bool TryReplayCompactOutcome(CombatMetricsStore store, ReadOnlySpan<byte> packet, long timestamp, long frameOrdinal, long batchOrdinal)
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

    private static bool TryReplayCompact0238(CombatMetricsStore store, ReadOnlySpan<byte> packet, long batchOrdinal)
    {
        if (!Packet0238CompactControlParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.RegisterCompactControl0238(parsed.SourceId, parsed.SkillCodeRaw, parsed.Marker, batchOrdinal);
        return true;
    }

    private static bool TryReplayCompact0638(CombatMetricsStore store, ReadOnlySpan<byte> packet, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        if (!Packet0638CompactControlParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.RegisterCompactControl0638(parsed.SourceId, parsed.SkillCodeRaw, parsed.Marker, timestamp, frameOrdinal, batchOrdinal);
        return true;
    }

    private static bool TryReplay3538(CombatMetricsStore store, ReadOnlySpan<byte> packet)
    {
        if (!Packet3538SidecarParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        return true;
    }

    private static bool TryReplay8456(CombatMetricsStore store, ReadOnlySpan<byte> packet)
    {
        if (!Packet8456EnvelopeParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        return true;
    }

    private static bool TryReplay0140(CombatMetricsStore store, ReadOnlySpan<byte> packet)
    {
        if (!Packet0140Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

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

    private static bool TryReplay2136(CombatMetricsStore store, ReadOnlySpan<byte> packet)
    {
        if (!Packet2136Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

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

    private static bool TryReplay0240(CombatMetricsStore store, ReadOnlySpan<byte> packet)
    {
        if (!Packet0240Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

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

    private static bool TryReplay4636(CombatMetricsStore store, ReadOnlySpan<byte> packet)
    {
        if (!Packet4636Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.AppendNpc4636State(parsed.SourceId, parsed.State0, parsed.State1);
        store.RememberNpcObservationSource(parsed.SourceId);
        return true;
    }

    private static bool TryReplay4536(CombatMetricsStore store, ReadOnlySpan<byte> packet)
    {
        if (!Packet4536Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.RememberNpcObservationSource(parsed.SourceId);
        return true;
    }

    private static bool TryReplay2A38(CombatMetricsStore store, ReadOnlySpan<byte> packet, long timestamp, long frameOrdinal, long batchOrdinal)
    {
        if (!Packet2A38Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.RegisterObservation2A38(parsed.SourceId, parsed.Mode, parsed.GroupCode, parsed.SequenceId, parsed.HeadValue, parsed.BuffCodeRaw, timestamp, frameOrdinal, batchOrdinal);
        return true;
    }

    private static bool TryReplay2C38(CombatMetricsStore store, ReadOnlySpan<byte> packet, long timestamp, long frameOrdinal, long batchOrdinal)
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

    private static bool TryReplayNickname(CombatMetricsStore store, ReadOnlySpan<byte> packet)
    {
        if (Packet3336NicknameParser.TryParse(packet, out var ownParsed))
        {
            store.AppendNickname(ownParsed.PlayerId, ownParsed.Nickname);
            return true;
        }

        if (Packet4436NicknameParser.TryParse(packet, out var otherParsed))
        {
            store.AppendNickname(otherParsed.PlayerId, otherParsed.Nickname);
            return true;
        }

        if (Packet048DNicknameParser.TryParse(packet, out var parsed))
        {
            store.AppendNickname(parsed.PlayerId, parsed.Nickname);
            return true;
        }

        return false;
    }

    private static bool TryReplayRemainHp(CombatMetricsStore store, ReadOnlySpan<byte> packet)
    {
        if (!Packet008DRemainHpParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.AppendNpcHp(parsed.NpcId, checked((int)parsed.Hp));
        return true;
    }

    private static bool TryReplayBattleToggle(CombatMetricsStore store, ReadOnlySpan<byte> packet)
    {
        if (!Packet218DBattleToggleParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.ToggleNpcBattle(parsed.NpcId);
        return true;
    }

    private static bool TryReplaySummon(CombatMetricsStore store, ReadOnlySpan<byte> packet, string metadata)
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

    private static bool TryReplayNpcSpawn(CombatMetricsStore store, ReadOnlySpan<byte> packet, string metadata)
    {
        if (TryParseNpcSpawnMetadata(metadata, out var entityId, out var npcCode))
        {
            if (npcCode > 0)
            {
                TryApplyNpcCatalog(store, entityId, npcCode);
            }

            return true;
        }

        if (!Packet4036CreateParser.TryParseNpcSpawn(packet, out var spawn))
        {
            return false;
        }

        if (spawn.NpcCode.HasValue)
        {
            TryApplyNpcCatalog(store, spawn.EntityId, spawn.NpcCode.Value);
        }

        return true;
    }

    private static bool TryReplayState4036(CombatMetricsStore store, ReadOnlySpan<byte> packet)
    {
        if (Packet4036CreateParser.TryParseNpcSpawn(packet, out var spawn) && spawn.NpcCode.HasValue)
        {
            TryApplyNpcCatalog(store, spawn.EntityId, spawn.NpcCode.Value, requireCatalogEntry: true);
        }

        if (Packet4036CreateParser.TryParseOwner(packet, out var entityId, out var ownerId))
        {
            store.AppendSummon(ownerId, entityId);
        }

        return Packet4036Parser.TryParse(packet, out _);
    }

    private static bool TryParseNpcSpawnMetadata(string metadata, out int entityId, out int npcCode)
    {
        entityId = 0;
        npcCode = 0;

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
        }

        return entityId > 0;
    }

    private static bool TryReplayRecoveryPath(CombatMetricsStore store, ReadOnlySpan<byte> packet, string metadata)
    {
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

            return true;
        }

        return false;
    }

    private static void TryApplyNpcCatalog(
        CombatMetricsStore store,
        int instanceId,
        int npcCode,
        bool requireCatalogEntry = false)
    {
        if (instanceId <= 0 || npcCode <= 0)
        {
            return;
        }

        var hasCatalogEntry = CombatMetricsEngine.TryResolveNpcCatalogEntry(npcCode, out var entry);
        if (requireCatalogEntry && !hasCatalogEntry)
        {
            return;
        }

        var lifecycleId = store.ResolveLifecycleId(instanceId);
        if (hasCatalogEntry &&
            store.TryGetNpcRuntimeState(lifecycleId, out var existing) &&
            existing.NpcCode is int existingCode &&
            existingCode != npcCode &&
            CombatMetricsEngine.TryResolveNpcCatalogEntry(existingCode, out _))
        {
            store.RebindInstanceLifecycle(instanceId);
        }

        store.AppendNpcCode(instanceId, npcCode);

        if (!hasCatalogEntry)
        {
            return;
        }
        store.AppendNpcName(npcCode, entry.Name);

        var kind = CombatMetricsEngine.ResolveNpcKind(entry.Kind);
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
        if (sourceName.Contains(".stream.", StringComparison.OrdinalIgnoreCase) ||
            sourceName.EndsWith("stream.log", StringComparison.OrdinalIgnoreCase))
        {
            return ReplayLogKind.Stream;
        }

        if (sourceName.Contains(".frame.", StringComparison.OrdinalIgnoreCase) ||
            sourceName.EndsWith("frame.log", StringComparison.OrdinalIgnoreCase))
        {
            return ReplayLogKind.Frame;
        }

        if (sourceName.Contains(".raw.", StringComparison.OrdinalIgnoreCase) ||
            sourceName.EndsWith("raw.log", StringComparison.OrdinalIgnoreCase))
        {
            return ReplayLogKind.Raw;
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
}

public sealed record PacketLogReplayResult(
    string SourceName,
    int TotalLines,
    int ReplayedLines,
    int SkippedLines,
    DamageMeterSnapshot Snapshot,
    CombatMetricsStore Store,
    IReadOnlyList<PacketLogCombatantSummary> Combatants,
    IReadOnlyDictionary<string, int> ReplayedEventCounts,
    IReadOnlyDictionary<string, int> SkippedEventCounts);

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
