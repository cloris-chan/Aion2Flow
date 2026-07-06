using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.SceneRuntime.Observation;

public enum PacketStructureKind : byte
{
    None,
    TransportPacket,
    NestedTransportPacket,
    CompressedPayload,
    FrameBatchEntry,
    PacketContainerEntry,
    UnknownFramePayload,
    RecoveryPayload,
    EmbeddedFrame,
    RecoveredFrame
}

public readonly record struct PacketStructureReference(PacketStructureKind Kind, int ScopeId, int ParentScopeId, int Depth, int SiblingIndex, int Offset, int Length, int BodyOffset, int BodyLength);

public readonly record struct PacketStructurePath(PacketStructureReference Root, PacketStructureReference Level1, PacketStructureReference Level2, PacketStructureReference Level3, PacketStructureReference Leaf, int Depth)
{
    public bool IsEmpty => Depth == 0 || Leaf.Kind == PacketStructureKind.None;

    public PacketStructureReference Parent => Depth switch
    {
        <= 1 => default,
        2 => Root,
        3 => Level1,
        4 => Level2,
        _ => Level3.Kind == PacketStructureKind.None ? Level2 : Level3
    };

    public static PacketStructurePath FromLeaf(PacketStructureReference leaf) => leaf.Kind == PacketStructureKind.None ? default : new PacketStructurePath(default, default, default, default, leaf, Math.Max(1, leaf.Depth));

    public PacketStructurePath Push(PacketStructureReference next)
    {
        if (next.Kind == PacketStructureKind.None)
            return this;

        return Depth switch
        {
            0 => new PacketStructurePath(next, default, default, default, next, next.Depth),
            1 => this with { Level1 = next, Leaf = next, Depth = next.Depth },
            2 => this with { Level2 = next, Leaf = next, Depth = next.Depth },
            3 => this with { Level3 = next, Leaf = next, Depth = next.Depth },
            _ => this with { Leaf = next, Depth = next.Depth }
        };
    }
}

public readonly record struct RawPacketReference
{
    public ushort Opcode { get; init; }
    public int PayloadLength { get; init; }
    public long CaptureSequence { get; init; }
    public PacketStructurePath StructurePath { get; init; }
    public PacketStructureReference Structure => StructurePath.Leaf;

    public RawPacketReference(ushort Opcode, int PayloadLength, long CaptureSequence)
        : this(Opcode, PayloadLength, CaptureSequence, default(PacketStructurePath))
    {
    }

    public RawPacketReference(ushort Opcode, int PayloadLength, long CaptureSequence, PacketStructureReference Structure)
        : this(Opcode, PayloadLength, CaptureSequence, PacketStructurePath.FromLeaf(Structure))
    {
    }

    public RawPacketReference(ushort Opcode, int PayloadLength, long CaptureSequence, PacketStructurePath StructurePath)
    {
        this.Opcode = Opcode;
        this.PayloadLength = PayloadLength;
        this.CaptureSequence = CaptureSequence;
        this.StructurePath = StructurePath;
    }
}

public readonly record struct CombatObservation
{
    public int SkillCode { get; init; }
    public int BodySkillVariantRaw { get; init; }
    public uint BodyCodeRaw { get; init; }
    public ResourceEffectRef BodyResourceEffectRef { get; init; }
    public long Damage { get; init; }
    public int HitCount { get; init; }
    public int AttemptCount { get; init; }
    public long DetailRaw { get; init; }
    public ResourceEffectRef DetailResourceEffectRef { get; init; }
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
    public int PeriodicTailSkillCodeRaw { get; init; }
    public int PeriodicTailPrefixValue { get; init; }
    public int PeriodicTailLength { get; init; }
    public int ChainId { get; init; }
}

public readonly record struct StateObservation(int EntityId, int StateCode, long Value0, long Value1, long DetailRaw, string? Text, Faction Faction = Faction.Unknown, CharacterClass? CharacterClass = null, bool IsLocalPlayer = false, int? OriginServerId = null, string? LegionName = null, PlayerGroupMembership GroupMembership = default);

public readonly record struct SceneObservation(uint MapId, uint MapInstanceId, int Value0, int Value1, string? DiagnosticKey);

public readonly record struct ResourceObservation(int EntityId, long? CurrentValue, long? MaximumValue, long? Delta, int ResourceKind);

public enum AuraObservationKind : byte
{
    Open,
    Result
}

public readonly record struct AuraResultRecord(int StateCode, int InstanceSequenceId, int ResultCode, int DetailEntityId, uint DetailValue0, uint DetailValue1);

public readonly record struct AuraObservation
{
    public AuraObservationKind Kind { get; init; }
    public int EntityId { get; init; }
    public int InstanceSequenceId { get; init; }
    public ResourceEffectRef BuffResourceEffectRef { get; init; }
    public int StackCount { get; init; }
    public int ResultCode { get; init; }
    public int OpenMode { get; init; }
    public int ResultCount { get; init; }
    public int ResultIndex { get; init; }
    public int GroupCode { get; init; }
    public uint HeadCode { get; init; }
    public ushort HeadValue { get; init; }
    public ulong HeadMiddleRaw { get; init; }
    public uint TimelineValue { get; init; }
    public uint StableValue { get; init; }
    public int EchoSourceEntityId { get; init; }
    public int StateCode { get; init; }
    public int ResultDetailEntityId { get; init; }
    public uint ResultDetailValue0 { get; init; }
    public uint ResultDetailValue1 { get; init; }
    public int TailLength { get; init; }
    public ulong TailLow64 { get; init; }
    public ulong TailHigh64 { get; init; }
}

public readonly record struct ActionObservation
{
    public int SourceEntityId { get; init; }
    public int SourceEntityIdCopy { get; init; }
    public int Phase { get; init; }
    public int InstanceSequenceId { get; init; }
    public ResourceEffectRef ActionResourceEffectRef { get; init; }
    public int SequenceValue { get; init; }
    public int StateValue { get; init; }
    public int DetailValue { get; init; }
    public int TailLength { get; init; }
}
