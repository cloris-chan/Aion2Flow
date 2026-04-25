using Cloris.Aion2Flow.Combat.Classification;

namespace Cloris.Aion2Flow.Combat.Metrics;

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
    Aux2C38Invincible
}

public sealed class ParsedCombatPacket
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
    public bool HasAuthoritativeMultiHitCount { get; set; }
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
    public bool IsCritical => (Modifiers & DamageModifiers.Critical) != 0;
    public bool IsPeriodicEffect => PeriodicRelation != PeriodicEffectRelation.None;
    public bool IsPeriodicSelfEffect => PeriodicRelation == PeriodicEffectRelation.Self;
    public bool IsPeriodicTargetEffect => PeriodicRelation == PeriodicEffectRelation.Target;
    public bool IsPeriodicTargetInitialEffect => IsPeriodicTargetEffect && PeriodicMode == 1;
    public SkillVariantInfo SkillVariant => new(OriginalSkillCode, SkillCode, BaseSkillCode, ChargeStage, SpecializationMask);

    public bool IsPeriodicSelfMode(int mode) => IsPeriodicSelfEffect && PeriodicMode == mode;

    public bool IsPeriodicTargetMode(int mode) => IsPeriodicTargetEffect && PeriodicMode == mode;

    public void SetPeriodicEffect(PeriodicEffectRelation relation, int mode)
    {
        PeriodicRelation = relation;
        PeriodicMode = relation == PeriodicEffectRelation.None ? 0 : Math.Max(mode, 0);
        EffectTag = PacketEffectTag.None;
    }

    public void SetEffectTag(PacketEffectTag effectTag)
    {
        EffectTag = effectTag;
        if (effectTag != PacketEffectTag.None)
        {
            PeriodicRelation = PeriodicEffectRelation.None;
            PeriodicMode = 0;
        }
    }

    public ParsedCombatPacket DeepClone()
    {
        var clone = new ParsedCombatPacket
        {
            SourceId = SourceId,
            TargetId = TargetId,
            Flag = Flag,
            Damage = Damage,
            OriginalSkillCode = OriginalSkillCode,
            SkillCode = SkillCode,
            BaseSkillCode = BaseSkillCode,
            ChargeStage = ChargeStage,
            SpecializationMask = SpecializationMask,
            Marker = Marker,
            Type = Type,
            Unknown = Unknown,
            LayoutTag = LayoutTag,
            Loop = Loop,
            HitContribution = HitContribution,
            AttemptContribution = AttemptContribution,
            MultiHitCount = MultiHitCount,
            HasAuthoritativeMultiHitCount = HasAuthoritativeMultiHitCount,
            DrainHealAmount = DrainHealAmount,
            RegenerationAmount = RegenerationAmount,
            DetailRaw = DetailRaw,
            ResourceKind = ResourceKind,
            FrameOrdinal = FrameOrdinal,
            BatchOrdinal = BatchOrdinal,
            Timestamp = Timestamp,
            Id = Id,
            Modifiers = Modifiers,
            EventKind = EventKind,
            ValueKind = ValueKind,
            IsNormalized = IsNormalized
        };

        if (IsPeriodicEffect)
        {
            clone.SetPeriodicEffect(PeriodicRelation, PeriodicMode);
        }

        if (EffectTag != PacketEffectTag.None)
        {
            clone.SetEffectTag(EffectTag);
        }

        return clone;
    }

    internal string FormatEffectLabel()
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
