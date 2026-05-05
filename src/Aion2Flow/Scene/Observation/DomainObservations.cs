namespace Cloris.Aion2Flow.Scene.Observation;

public readonly record struct RawPacketReference(ushort Opcode, int PayloadLength, long CaptureSequence, long TimestampMilliseconds);

public readonly record struct CombatObservation(int SkillCode, long Damage, int HitCount, int AttemptCount, long DetailRaw);

public readonly record struct StateObservation(int EntityId, int StateCode, int Value0, int Value1, long DetailRaw);

public readonly record struct SceneObservation(int MapId, int MapInstanceId, int Value0, int Value1, string? DiagnosticKey);

public readonly record struct ResourceObservation(int EntityId, long? CurrentValue, long? MaximumValue, long? Delta, int ResourceKind);

public readonly record struct AuraObservation(int SourceEntityId, int TargetEntityId, int SkillCode, int StackCount, int SequenceId, int ChainId);
