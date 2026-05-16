using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;

namespace Cloris.Aion2Flow.SceneRuntime.Observation;


public readonly record struct RawPacketReference(ushort Opcode, int PayloadLength, long CaptureSequence, long TimestampMilliseconds);

public readonly record struct CombatObservation
{
    public int SkillCode { get; init; }
    public int OriginalSkillCode { get; init; }
    public int BaseSkillCode { get; init; }
    public long Damage { get; init; }
    public int HitCount { get; init; }
    public int AttemptCount { get; init; }
    public long DetailRaw { get; init; }
    public int Marker { get; init; }
    public int Type { get; init; }
    public int Flag { get; init; }
    public int LayoutTag { get; init; }
    public int Loop { get; init; }
    public int MultiHitCount { get; init; }
    public int DrainHealAmount { get; init; }
    public int RegenerationAmount { get; init; }
    public DamageModifiers Modifiers { get; init; }
    public CombatResourceKind ResourceKind { get; init; }
    public CombatEventKind EventKind { get; init; }
    public CombatValueKind ValueKind { get; init; }
    public PacketEffectTag EffectTag { get; init; }
    public PeriodicEffectRelation PeriodicRelation { get; init; }
    public int PeriodicMode { get; init; }
    public int ChainId { get; init; }
}

public readonly record struct StateObservation(
    int EntityId,
    int StateCode,
    int Value0,
    int Value1,
    long DetailRaw,
    string? Text,
    int? OriginServerId = null,
    Faction Faction = Faction.Unknown);

public readonly record struct SceneObservation(uint MapId, uint MapInstanceId, int Value0, int Value1, string? DiagnosticKey);

public readonly record struct ResourceObservation(int EntityId, long? CurrentValue, long? MaximumValue, long? Delta, int ResourceKind);

public readonly record struct AuraObservation(int SourceEntityId, int TargetEntityId, int SkillCode, int StackCount, int SequenceId, int ChainId, int ResultCode, int Mode);
