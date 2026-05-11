using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public enum PeriodicEffectRelation : byte
{
    None,
    Self,
    Target
}

public enum PacketEffectTag : byte
{
    None,
    ActiveDodgeEvade,
    CompactEvade,
    PeriodicLinkInvincible,
    Aux2C38Invincible,
    RegenerationHealing,
    ShieldGrant,
    ShieldAbsorbed
}

public struct ParsedCombatPacket
{
    public bool IsNormalized { get; set; }
    public int SourceId { get; set; }
    public int TargetId { get; set; }
    public int Flag { get; set; }
    public int Damage { get; set; }
    public int OriginalSkillCode { get; set; }
    public int SkillCode { get; set; }
    public int BaseSkillCode { get; set; }
    public int ChargeStage { get; set; }
    public int SpecializationMask { get; set; }
    public int Marker { get; set; }
    public int Type { get; set; }
    public int Unknown { get; set; }
    public int LayoutTag { get; set; }
    public int Loop { get; set; }
    public int HitContribution { get; set; } = 1;
    public int AttemptContribution { get; set; } = 1;
    public int MultiHitCount { get; set; }
    public int DrainHealAmount { get; set; }
    public int RegenerationAmount { get; set; }
    public long DetailRaw { get; set; }
    public CombatResourceKind ResourceKind { get; set; } = CombatResourceKind.Unknown;
    public long FrameOrdinal { get; set; }
    public long BatchOrdinal { get; set; }
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public DamageModifiers Modifiers { get; set; }
    public CombatEventKind EventKind { get; set; } = CombatEventKind.Damage;
    public CombatValueKind ValueKind { get; set; } = CombatValueKind.Unknown;
    public PeriodicEffectRelation PeriodicRelation { get; private set; }
    public int PeriodicMode { get; private set; }
    public PacketEffectTag EffectTag { get; private set; }
    public readonly bool IsCritical => (Modifiers & DamageModifiers.Critical) != 0;
    public readonly bool IsPeriodicEffect => PeriodicRelation != PeriodicEffectRelation.None;
    public readonly bool IsPeriodicSelfEffect => PeriodicRelation == PeriodicEffectRelation.Self;
    public readonly bool IsPeriodicTargetEffect => PeriodicRelation == PeriodicEffectRelation.Target;
    public readonly bool IsPeriodicTargetInitialEffect => IsPeriodicTargetEffect && PeriodicMode == 1;
    public readonly SkillVariantInfo SkillVariant => new(OriginalSkillCode, SkillCode, BaseSkillCode, ChargeStage, SpecializationMask);

    public ParsedCombatPacket()
    {
    }

    public readonly bool IsPeriodicSelfMode(int mode) => IsPeriodicSelfEffect && PeriodicMode == mode;

    public readonly bool IsPeriodicTargetMode(int mode) => IsPeriodicTargetEffect && PeriodicMode == mode;

    public void SetPeriodicEffect(PeriodicEffectRelation relation, int mode)
    {
        PeriodicRelation = relation;
        PeriodicMode = relation == PeriodicEffectRelation.None ? 0 : Math.Max(mode, 0);
        EffectTag = PacketEffectTag.None;
    }

    public void SetEffectTag(PacketEffectTag effectTag)
    {
        EffectTag = effectTag;
        if (effectTag is PacketEffectTag.None or PacketEffectTag.ShieldGrant or PacketEffectTag.ShieldAbsorbed)
        {
            return;
        }

        PeriodicRelation = PeriodicEffectRelation.None;
        PeriodicMode = 0;
    }

    public readonly CombatObservation ToObservation() => new()
    {
        SkillCode = SkillCode,
        OriginalSkillCode = OriginalSkillCode,
        BaseSkillCode = BaseSkillCode,
        Damage = Damage,
        HitCount = HitContribution,
        AttemptCount = AttemptContribution,
        DetailRaw = DetailRaw,
        Marker = Marker,
        Type = Type,
        Flag = Flag,
        LayoutTag = LayoutTag,
        Loop = Loop,
        MultiHitCount = MultiHitCount,
        DrainHealAmount = DrainHealAmount,
        RegenerationAmount = RegenerationAmount,
        Modifiers = Modifiers,
        ResourceKind = ResourceKind,
        EventKind = EventKind,
        ValueKind = ValueKind,
        EffectTag = EffectTag,
        PeriodicRelation = PeriodicRelation,
        PeriodicMode = PeriodicMode,
        ChainId = Unknown
    };

    public static ParsedCombatPacket FromObservation(int sourceId, int targetId, in CombatObservation observation, long timestamp = 0, long frameOrdinal = 0, long batchOrdinal = 0) => new()
    {
        SourceId = sourceId,
        TargetId = targetId,
        SkillCode = observation.SkillCode,
        OriginalSkillCode = observation.OriginalSkillCode,
        BaseSkillCode = observation.BaseSkillCode,
        Damage = checked((int)observation.Damage),
        HitContribution = observation.HitCount,
        AttemptContribution = observation.AttemptCount,
        DetailRaw = observation.DetailRaw,
        Marker = observation.Marker,
        Type = observation.Type,
        Flag = observation.Flag,
        LayoutTag = observation.LayoutTag,
        Loop = observation.Loop,
        MultiHitCount = observation.MultiHitCount,
        DrainHealAmount = observation.DrainHealAmount,
        RegenerationAmount = observation.RegenerationAmount,
        Modifiers = observation.Modifiers,
        ResourceKind = observation.ResourceKind,
        EventKind = observation.EventKind,
        ValueKind = observation.ValueKind,
        PeriodicRelation = observation.PeriodicRelation,
        PeriodicMode = observation.PeriodicMode,
        EffectTag = observation.EffectTag,
        Unknown = observation.ChainId,
        Timestamp = timestamp,
        FrameOrdinal = frameOrdinal,
        BatchOrdinal = batchOrdinal
    };

    internal readonly string FormatEffectLabel()
    {
        if (IsPeriodicEffect)
        {
            return FormatPeriodicEffectLabel(PeriodicRelation, PeriodicMode);
        }

        return EffectTag == PacketEffectTag.None
            ? string.Empty
            : FormatEffectTagLabel(EffectTag);
    }

    private static string FormatPeriodicEffectLabel(PeriodicEffectRelation relation, int mode)
    {
        if (relation == PeriodicEffectRelation.None)
        {
            return string.Empty;
        }

        if (relation == PeriodicEffectRelation.Self)
        {
            return mode switch
            {
                1 => "periodic-self-initial",
                3 => "periodic-self-tick",
                _ => $"periodic-self-mode-{mode}"
            };
        }

        return mode switch
        {
            1 => "periodic-target-initial",
            2 => "periodic-target-tick",
            3 => "periodic-target-tick",
            _ => $"periodic-target-mode-{mode}"
        };
    }

    private static string FormatEffectTagLabel(PacketEffectTag effectTag)
    {
        return effectTag switch
        {
            PacketEffectTag.ActiveDodgeEvade => "active-dodge-evade",
            PacketEffectTag.CompactEvade => "compact-evade",
            PacketEffectTag.PeriodicLinkInvincible => "periodic-link-invincible",
            PacketEffectTag.Aux2C38Invincible => "aux-2c38-invincible",
            _ => string.Empty
        };
    }
}
