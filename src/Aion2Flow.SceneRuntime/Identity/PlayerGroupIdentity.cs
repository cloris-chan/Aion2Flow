namespace Cloris.Aion2Flow.SceneRuntime.Identity;

public enum PlayerGroupKind : byte
{
    None = 0,
    Party = 1,
    Force = 2
}

public enum PlayerGroupRelation : byte
{
    Unknown = 0,
    PartyMember = 1,
    ForceMember = 2
}

public readonly record struct PlayerGroupMembership(PlayerGroupKind Kind, uint GroupId, byte SubPartyIndex, byte MemberSlotIndex)
{
    public bool IsKnown => Kind != PlayerGroupKind.None;

    public static PlayerGroupMembership Party(byte slotIndex) => new(PlayerGroupKind.Party, 0, 0, slotIndex);

    public static PlayerGroupMembership Force(uint groupId, byte subPartyIndex, byte memberSlotIndex) => new(PlayerGroupKind.Force, groupId, subPartyIndex, memberSlotIndex);
}
