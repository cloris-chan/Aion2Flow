using System.Globalization;
using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat;
using Cloris.Aion2Flow.Combat.NpcRuntime;
using Cloris.Aion2Flow.Scene.Stores;

namespace Cloris.Aion2Flow.Scene.Projection;

public sealed class SceneCombatSnapshotAdapter(EntityStore entities, CombatStore combat, MetadataStore metadata, BossFocusStore? bossFocus = null, CombatPairProjection? projection = null, Guid battleId = default)
{
    public DamageMeterSnapshot CreateSnapshot()
    {
        var pairs = projection ?? CombatPairProjection.FromCombatStore(combat);
        var snapshot = new DamageMeterSnapshot
        {
            BattleId = battleId == default ? Guid.NewGuid() : battleId,
            MapId = metadata.CurrentMapId,
            MapInstanceId = metadata.CurrentMapInstanceId
        };

        var targetId = ResolveTargetId(pairs);
        snapshot.TargetName = ResolveTargetName(targetId);
        snapshot.TargetObservation = BuildTargetObservation(targetId);

        var (start, end) = ResolveBattleWindow(pairs);
        var battleTime = end > start ? end - start : 0;
        snapshot.BattleStartTime = start;
        snapshot.BattleEndTime = end;
        snapshot.BattleTime = battleTime;

        foreach (var pair in pairs.Pairs.Values)
        {
            ApplyPair(snapshot, pair);
        }

        var totalDamage = 0L;
        foreach (var (id, data) in snapshot.Combatants)
        {
            data.CharacterClass = ResolveCharacterClass(id);
            if (data.CharacterClass is not null)
                totalDamage += data.DamageAmount;
        }

        foreach (var data in snapshot.Combatants.Values)
        {
            data.DamagePerSecond = battleTime > 0 ? (double)data.DamageAmount / battleTime * 1000 : 0;
            data.HealingPerSecond = battleTime > 0 ? (double)data.HealingAmount / battleTime * 1000 : 0;
            data.DamageContribution = totalDamage > 0 ? (double)data.DamageAmount / totalDamage : 0;
        }

        snapshot.Encounter = EvaluateEncounter(targetId, battleTime, snapshot.TargetObservation);
        return snapshot;
    }

    private void ApplyPair(DamageMeterSnapshot snapshot, DirectedPairSnapshot pair)
    {
        if (pair.Key.SourceId > 0)
        {
            var source = GetOrAdd(snapshot, pair.Key.SourceId);
            source.ApplySceneTotals(pair.TotalDamage, pair.TotalHealing, pair.TotalShield, pair.ShieldCount, pair.TotalShieldAbsorbed, pair.ShieldAbsorbedCount);
        }

        if (pair.Key.TargetId > 0)
            _ = GetOrAdd(snapshot, pair.Key.TargetId);
    }

    private CombatantMetrics GetOrAdd(DamageMeterSnapshot snapshot, int combatantId)
    {
        if (!snapshot.Combatants.TryGetValue(combatantId, out var metrics))
        {
            metrics = new CombatantMetrics(ResolveDisplayName(combatantId));
            snapshot.Combatants[combatantId] = metrics;
        }

        return metrics;
    }

    private int ResolveTargetId(CombatPairProjection pairs)
    {
        var targetId = 0;
        var damage = long.MinValue;

        foreach (var pair in pairs.Pairs.Values)
        {
            if (pair.Key.TargetId <= 0 || IsKnownSummon(pair.Key.TargetId))
                continue;

            if (pair.TotalDamage > damage)
            {
                damage = pair.TotalDamage;
                targetId = pair.Key.TargetId;
            }
        }

        if (targetId > 0)
            return targetId;

        if (bossFocus is not null && bossFocus.TryGetObservedBoss(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 10_000, out var boss))
            return boss.InstanceId;

        return 0;
    }

    private static (long Start, long End) ResolveBattleWindow(CombatPairProjection pairs)
    {
        var start = long.MaxValue;
        var end = long.MinValue;

        foreach (var pair in pairs.Pairs.Values)
        {
            if (pair.FirstObserved <= 0 || pair.LastObserved <= 0)
                continue;

            start = Math.Min(start, pair.FirstObserved);
            end = Math.Max(end, pair.LastObserved);
        }

        return start == long.MaxValue ? (0, 0) : (start, end);
    }

    private string ResolveTargetName(int targetId)
    {
        if (targetId <= 0 || !entities.TryGet(targetId, out var entity) || entity.NpcCode is not int npcCode)
            return string.Empty;

        if (CombatMetricsEngine.TryResolveNpcCatalogEntry(npcCode, out var catalogEntry) && !string.IsNullOrWhiteSpace(catalogEntry.Name))
            return catalogEntry.Name;

        return metadata.TryGetNpcName(npcCode, out var npcName) && !string.IsNullOrWhiteSpace(npcName) ? npcName : string.Empty;
    }

    private string ResolveDisplayName(int entityId)
    {
        if (metadata.TryGetDisplayName(entityId, out var displayName) && !string.IsNullOrWhiteSpace(displayName))
            return displayName;

        if (entities.TryGet(entityId, out var entity))
        {
            if (!string.IsNullOrWhiteSpace(entity.Nickname))
                return entity.Nickname;

            if (entity.NpcCode is int npcCode)
            {
                if (CombatMetricsEngine.TryResolveNpcCatalogEntry(npcCode, out var catalogEntry) && !string.IsNullOrWhiteSpace(catalogEntry.Name))
                    return catalogEntry.Name;

                if (metadata.TryGetNpcName(npcCode, out var npcName) && !string.IsNullOrWhiteSpace(npcName))
                    return npcName;

                return $"NPC-{npcCode}";
            }
        }

        return entityId.ToString(CultureInfo.InvariantCulture);
    }

    private CharacterClass? ResolveCharacterClass(int entityId)
    {
        if (entities.TryGet(entityId, out var entity) && (entity.NpcCode.HasValue || entity.Kind is NpcKind.Monster or NpcKind.Boss or NpcKind.Friendly or NpcKind.Summon))
            return null;

        return CharacterClass.None;
    }

    private bool IsKnownSummon(int entityId) =>
        entities.TryGet(entityId, out var entity) && (entity.OwnerEntityId.HasValue || entity.Kind == NpcKind.Summon);

    private NpcRuntimeObservation? BuildTargetObservation(int targetId)
    {
        if (targetId <= 0 || !entities.TryGet(targetId, out var entity))
            return null;

        var observation = new NpcRuntimeObservation
        {
            InstanceId = targetId,
            Value2136 = entity.Value2136,
            Sequence2136 = entity.Sequence2136,
            Value0140 = entity.Value0140,
            Value0240 = entity.Value0240,
            State4636Value0 = entity.State4636?.State0,
            State4636Value1 = entity.State4636?.State1,
            Sequence2C38 = entity.Latest2C38?.SequenceId,
            Result2C38 = entity.Latest2C38?.ResultCode,
            Hp = entity.CurrentHp,
            BattleToggledOn = entity.BattleActive
        };
        observation.PhaseHint = NpcRuntimeObservationInterpreter.InferPhaseHint(observation);
        return observation;
    }

    private static EncounterSummary EvaluateEncounter(int targetId, long battleTime, NpcRuntimeObservation? observation)
    {
        if (targetId <= 0)
        {
            return new EncounterSummary
            {
                TrackingTargetId = 0,
                PhaseHint = NpcRuntimePhaseHint.Unknown,
                IsActive = false,
                ShouldArchive = false,
                Reason = "no-target"
            };
        }

        return new EncounterSummary
        {
            TrackingTargetId = targetId,
            PhaseHint = observation?.PhaseHint ?? NpcRuntimePhaseHint.Unknown,
            IsActive = battleTime > 0 || observation?.BattleToggledOn == true || observation?.Hp.HasValue == true,
            ShouldArchive = false,
            Reason = battleTime > 0 ? "scene-combat" : observation?.BattleToggledOn == true ? "battle-toggle" : observation?.Hp.HasValue == true ? "hp-observed" : "insufficient-signal"
        };
    }
}
