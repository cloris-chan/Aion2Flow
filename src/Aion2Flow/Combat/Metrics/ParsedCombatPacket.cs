using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Resources;

namespace Cloris.Aion2Flow.Combat.Metrics;

public sealed class ParsedCombatPacket
{
    public bool IsNormalized { get; set; }
    public int SourceId { get; set; }
    public int TargetId { get; set; }
    public string EffectFamily { get; set; } = string.Empty;
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
    public long FrameOrdinal { get; set; }
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public DamageModifiers Modifiers { get; set; }
    public CombatEventKind EventKind { get; set; } = CombatEventKind.Damage;
    public CombatValueKind ValueKind { get; set; } = CombatValueKind.Unknown;
    public SkillKind SkillKind { get; set; } = SkillKind.Unknown;
    public SkillSemantics SkillSemantics { get; set; } = SkillSemantics.None;
    public bool IsCritical => (Modifiers & DamageModifiers.Critical) != 0;
    public bool IsPeriodicEffect => EffectFamily.StartsWith("periodic-", StringComparison.Ordinal);
    public SkillVariantInfo SkillVariant => new(OriginalSkillCode, SkillCode, BaseSkillCode, ChargeStage, SpecializationMask);

    public ParsedCombatPacket DeepClone()
    {
        return new ParsedCombatPacket
        {
            SourceId = SourceId,
            TargetId = TargetId,
            EffectFamily = EffectFamily,
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
            FrameOrdinal = FrameOrdinal,
            Timestamp = Timestamp,
            Id = Id,
            Modifiers = Modifiers,
            EventKind = EventKind,
            ValueKind = ValueKind,
            SkillKind = SkillKind,
            SkillSemantics = SkillSemantics,
            IsNormalized = IsNormalized
        };
    }
}
