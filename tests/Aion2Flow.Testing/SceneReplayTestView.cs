using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests;

public static class SceneReplayTestView
{
    public static IReadOnlyList<SceneReplayPacket> Packets(PacketLogReplayResult replay) =>
        replay.SceneOwner.Combat.Events
            .Select(static e => new SceneReplayPacket(e.SourceId, e.TargetId, e.Observation, e.Contribution, e.ObservedAtMilliseconds))
            .ToArray();

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
            .Where(static entity => entity.OwnerKind == EntityOwnerKind.Summon && entity.OwnerEntityId.HasValue)
            .ToDictionary(static entity => entity.EntityId, static entity => entity.OwnerEntityId!.Value);

    public static int ResolveCombatantId(PacketLogReplayResult replay, int entityId) =>
        entityId > 0 && replay.SceneOwner.Entities.TryGet(entityId, out var entity) && entity.OwnerEntityId is int ownerId ? ownerId : entityId;

    public static bool TryGetNpcRuntimeState(PacketLogReplayResult replay, int entityId, out RuntimeNpcStateSnapshot state)
    {
        if (replay.SceneOwner.Entities.TryGet(entityId, out var entity))
        {
            replay.SceneOwner.EntityVitals.TryGet(entityId, out var vital);
            state = new RuntimeNpcStateSnapshot(entity.NpcCode, vital.EntityId > 0 ? vital.CurrentHp : null, vital.MaxHp, vital.EntityId > 0 ? vital.ObservedAtMilliseconds : null, entity.NpcCombatActive, entity.Kind, entity.Value2136, entity.Sequence2136, entity.Value0140, entity.Value0240, entity.State4636, entity.Latest2C38);
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

public readonly record struct SceneReplayPacket(
    int SourceId,
    int TargetId,
    CombatWireObservation Observation,
    CombatContribution Contribution,
    long Timestamp)
{
    public int SkillCode => Observation.SkillCode;
    public int BodySkillVariantRaw => Observation.BodySkillVariantRaw;
    public uint BodyCodeRaw => Observation.BodyCodeRaw;
    public ResourceEffectRef BodyResourceEffectRef => Observation.BodyResourceEffectRef;
    public long Damage => Observation.Damage;
    public int HitCount => Observation.HitCount;
    public int AttemptCount => Observation.AttemptCount;
    public long DetailRaw => Observation.DetailRaw;
    public ResourceEffectRef DetailResourceEffectRef => Observation.DetailResourceEffectRef;
    public int Marker => Observation.Marker;
    public int ChainId => Observation.ChainId;
    public int Type => Observation.Type;
    public int Flag => Observation.Flag;
    public int LayoutTag => Observation.LayoutTag;
    public int Loop => Observation.Loop;
    public int MultiHitCount => Observation.MultiHitCount;
    public int DrainHealAmount => Observation.DrainHealAmount;
    public int RegenerationAmount => Observation.RegenerationAmount;
    public DamageModifiers Modifiers => Observation.Modifiers;
    public CombatResourceKind ResourceKind => Observation.ResourceKind;
    public CombatWireOutcomeKind OutcomeKind => Observation.OutcomeKind;
    public PeriodicEffectRelation PeriodicRelation => Observation.PeriodicRelation;
    public int PeriodicMode => Observation.PeriodicMode;
    public int PeriodicTailSkillCodeRaw => Observation.PeriodicTailSkillCodeRaw;
    public int PeriodicTailPrefixValue => Observation.PeriodicTailPrefixValue;
    public int PeriodicTailLength => Observation.PeriodicTailLength;
    public CombatMetricKind Metric => Contribution.Metric;
    public CombatDeliveryKind Delivery => Contribution.Delivery;
    public long Amount => Contribution.Amount;
    public CombatResolutionTrace Resolution => Contribution.Resolution;
}
