using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.Tests;

public static class SceneReplayTestView
{
    public static IReadOnlyList<SceneReplayPacket> Packets(PacketLogReplayResult replay) =>
        replay.SceneOwner.Combat.Events.Select(static e =>
        {
            var observation = e.Observation;
            return new SceneReplayPacket
            {
                SourceId = e.SourceId,
                TargetId = e.TargetId,
                SkillCode = observation.SkillCode,
                BodySkillVariantRaw = observation.BodySkillVariantRaw,
                BodyResourceEffectRef = observation.BodyResourceEffectRef,
                Damage = observation.Damage,
                HitContribution = observation.HitCount,
                AttemptContribution = observation.AttemptCount,
                DetailRaw = observation.DetailRaw,
                DetailResourceEffectRef = observation.DetailResourceEffectRef,
                Marker = observation.Marker,
                Unknown = observation.ChainId,
                Type = observation.Type,
                Flag = observation.Flag,
                LayoutTag = observation.LayoutTag,
                Loop = observation.Loop,
                MultiHitCount = observation.MultiHitCount,
                DrainHealAmount = observation.DrainHealAmount,
                RegenerationAmount = observation.RegenerationAmount,
                Modifiers = observation.Modifiers,
                EventKind = observation.EventKind,
                ValueKind = observation.ValueKind,
                EffectTag = observation.EffectTag,
                PeriodicRelation = observation.PeriodicRelation,
                PeriodicMode = observation.PeriodicMode,
                PeriodicTailSkillCodeRaw = observation.PeriodicTailSkillCodeRaw,
                PeriodicTailPrefixValue = observation.PeriodicTailPrefixValue,
                PeriodicTailLength = observation.PeriodicTailLength,
                Timestamp = e.ObservedAtMilliseconds,
                ContributesDamage = e.ContributesDamage,
                ContributesHealing = e.ContributesHealing,
                ContributesShieldGrant = e.ContributesShieldGrant,
                ContributesShieldAbsorbed = e.ContributesShieldAbsorbed
            };
        }).ToArray();

    public static Dictionary<int, List<SceneReplayPacket>> BySource(PacketLogReplayResult replay) =>
        Packets(replay)
            .GroupBy(static packet => packet.SourceId)
            .ToDictionary(static group => group.Key, static group => group.ToList());

    public static Dictionary<int, List<SceneReplayPacket>> ByTarget(PacketLogReplayResult replay) =>
        Packets(replay)
            .GroupBy(static packet => packet.TargetId)
            .ToDictionary(static group => group.Key, static group => group.ToList());

    public static Dictionary<int, int> SummonOwnerByInstance(PacketLogReplayResult replay) =>
        replay.SceneOwner.Entities.Entities.Values
            .Where(static entity => entity.OwnerEntityId.HasValue)
            .ToDictionary(static entity => entity.EntityId, static entity => entity.OwnerEntityId!.Value);

    public static int ResolveCombatantId(PacketLogReplayResult replay, int entityId) =>
        entityId > 0 && replay.SceneOwner.Entities.TryGet(entityId, out var entity) && entity.OwnerEntityId is int ownerId ? ownerId : entityId;

    public static bool TryGetNpcRuntimeState(PacketLogReplayResult replay, int entityId, out RuntimeNpcStateSnapshot state)
    {
        if (replay.SceneOwner.Entities.TryGet(entityId, out var entity))
        {
            state = new RuntimeNpcStateSnapshot(entity.NpcCode, entity.CurrentHp, entity.MaxHp, null, entity.NpcCombatActive, entity.Kind, entity.Value2136, entity.Sequence2136, entity.Value0140, entity.Value0240, entity.State4636, entity.Latest2C38);
            return true;
        }

        state = default;
        return false;
    }

    public static string ResolveDisplayName(PacketLogReplayResult replay, int entityId)
    {
        if (replay.SceneOwner.MetadataRegistry.TryGetPcMetadata(entityId, out var pc) && pc.HasNickname)
            return pc.Nickname;

        if (replay.SceneOwner.MetadataRegistry.TryGetNpcCode(entityId, out var npcCode) &&
            CombatResourceRegistry.TryResolveNpcCatalogEntry(npcCode, out var catalogEntry) &&
            !string.IsNullOrWhiteSpace(catalogEntry.Name))
        {
            return catalogEntry.Name;
        }

        return entityId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

public readonly record struct SceneReplayPacket
{
    public int SourceId { get; init; }
    public int TargetId { get; init; }
    public int SkillCode { get; init; }
    public int BodySkillVariantRaw { get; init; }
    public ResourceEffectRef BodyResourceEffectRef { get; init; }
    public long Damage { get; init; }
    public int HitContribution { get; init; }
    public int AttemptContribution { get; init; }
    public long DetailRaw { get; init; }
    public ResourceEffectRef DetailResourceEffectRef { get; init; }
    public int Marker { get; init; }
    public int Unknown { get; init; }
    public int Type { get; init; }
    public int Flag { get; init; }
    public int LayoutTag { get; init; }
    public int Loop { get; init; }
    public int MultiHitCount { get; init; }
    public int DrainHealAmount { get; init; }
    public int RegenerationAmount { get; init; }
    public DamageModifiers Modifiers { get; init; }
    public CombatEventKind EventKind { get; init; }
    public CombatValueKind ValueKind { get; init; }
    public PacketEffectTag EffectTag { get; init; }
    public PeriodicEffectRelation PeriodicRelation { get; init; }
    public int PeriodicMode { get; init; }
    public int PeriodicTailSkillCodeRaw { get; init; }
    public int PeriodicTailPrefixValue { get; init; }
    public int PeriodicTailLength { get; init; }
    public long Timestamp { get; init; }
    public bool ContributesDamage { get; init; }
    public bool ContributesHealing { get; init; }
    public bool ContributesShieldGrant { get; init; }
    public bool ContributesShieldAbsorbed { get; init; }
}
