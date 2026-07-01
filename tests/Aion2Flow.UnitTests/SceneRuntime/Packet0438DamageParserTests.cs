using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.UnitTests.SceneRuntime;

public sealed class Packet0438DamageParserTests
{
    [Fact]
    public void TryParse_RejectsInvalidEntityIds()
    {
        Assert.False(Packet0438DamageParser.TryParse(BuildFrame(targetId: -1, sourceId: 100), out _));
        Assert.False(Packet0438DamageParser.TryParse(BuildFrame(targetId: 200, sourceId: -1), out _));
    }

    [Fact]
    public void TryParse_RejectsInvalidControlFields()
    {
        Assert.False(Packet0438DamageParser.TryParse(BuildFrame(targetId: 200, sourceId: 100, layoutTag: 256), out _));
        Assert.False(Packet0438DamageParser.TryParse(BuildFrame(targetId: 200, sourceId: 100, flag: 256), out _));
        Assert.False(Packet0438DamageParser.TryParse(BuildFrame(targetId: 200, sourceId: 100, type: 256), out _));
        Assert.False(Packet0438DamageParser.TryParse(BuildFrame(targetId: 200, sourceId: 100, loop: -1), out _));
    }

    [Fact]
    public void TryParse_AcceptsLength32Frame()
    {
        var packet = Convert.FromHexString("200438C7240400C7246F011D008002570E220101000000F571906C0100");

        Assert.True(Packet0438DamageParser.TryParse(packet, out var parsed));
        Assert.Equal(4679, parsed.TargetId);
        Assert.Equal(4679, parsed.SourceId);
        Assert.Equal(1900911, parsed.BodySkillVariantRaw);
        Assert.Equal(128, parsed.Marker);
        Assert.Equal(2, parsed.Type);
    }

    [Fact]
    public void TryParse_UsesLoopAsDamageForCurrentDetailLayout()
    {
        var packet = Convert.FromHexString("270438DFDF011604B7324A85C600F003840001F3108C4D01000000828601A56E0100B902");

        Assert.True(Packet0438DamageParser.TryParse(packet, out var parsed));
        Assert.Equal(13010250, parsed.BodySkillVariantRaw);
        Assert.Equal(0x16, parsed.LayoutTag);
        Assert.Equal(3, parsed.Type);
        Assert.Equal(0, parsed.Unknown);
        Assert.Equal(14117, parsed.Damage);
        Assert.Equal(14117, parsed.Loop);
        Assert.Equal(DamageModifiers.Critical | DamageModifiers.Perfect | DamageModifiers.Back, parsed.Modifiers & (DamageModifiers.Critical | DamageModifiers.Perfect | DamageModifiers.Smite | DamageModifiers.Back | DamageModifiers.Front));
    }

    [Fact]
    public void TryParse_MapsCurrentDetailLayoutSmiteBackFlags()
    {
        var packet = Convert.FromHexString("240438DFDF010600B732C559D100F402080001C711C75101000000904EEB2F0100");

        Assert.True(Packet0438DamageParser.TryParse(packet, out var parsed));
        Assert.Equal(13720005, parsed.BodySkillVariantRaw);
        Assert.Equal(0x06, parsed.LayoutTag);
        Assert.Equal(2, parsed.Type);
        Assert.Equal(0, parsed.Unknown);
        Assert.Equal(6123, parsed.Damage);
        Assert.Equal(6123, parsed.Loop);
        Assert.Equal(DamageModifiers.Smite | DamageModifiers.Back, parsed.Modifiers & (DamageModifiers.Critical | DamageModifiers.Perfect | DamageModifiers.Smite | DamageModifiers.Back | DamageModifiers.Front));
    }

    [Fact]
    public void TryParse_MapsCurrentDetailLayoutFrontFlag()
    {
        var packet = BuildFrame(
            targetId: 200,
            sourceId: 100,
            layoutTag: 6,
            flag: 0,
            type: 3,
            detail: [0x0c, 0, 0x02, 0, 0, 0, 0, 0, 0, 0],
            unknown: 0,
            damage: 17154,
            loop: 4321);

        Assert.True(Packet0438DamageParser.TryParse(packet, out var parsed));
        Assert.Equal(4321, parsed.Damage);
        Assert.Equal(4321, parsed.Loop);
        Assert.Equal(DamageModifiers.Critical | DamageModifiers.Perfect | DamageModifiers.Smite | DamageModifiers.Front, parsed.Modifiers & (DamageModifiers.Critical | DamageModifiers.Perfect | DamageModifiers.Smite | DamageModifiers.Back | DamageModifiers.Front));
    }

    [Fact]
    public void TryParse_MapsCurrentDetailLayoutDefensiveFlags()
    {
        var packet = BuildFrame(
            targetId: 200,
            sourceId: 100,
            layoutTag: 6,
            flag: 0,
            type: 2,
            detail: [0x13, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            unknown: 0,
            damage: 17154,
            loop: 4321);

        Assert.True(Packet0438DamageParser.TryParse(packet, out var parsed));
        Assert.Equal(4321, parsed.Damage);
        Assert.Equal(DamageModifiers.Parry | DamageModifiers.Block | DamageModifiers.Endurance, parsed.Modifiers & (DamageModifiers.Parry | DamageModifiers.Block | DamageModifiers.Endurance));
    }

    [Fact]
    public void TryParse_UsesTailDamageForCurrentDetailLayoutDefensiveDamageResult()
    {
        var packet = Convert.FromHexString("2A0438DD104600EAF401807618001A02228102020B4A8E0901000000904EE30C01EF3E2F0D0100");

        Assert.True(Packet0438DamageParser.TryParse(packet, out var parsed));
        Assert.Equal(1_603_200, parsed.BodySkillVariantRaw);
        Assert.Equal(0x46, parsed.LayoutTag);
        Assert.Equal(0, parsed.Unknown);
        Assert.Equal(1_635, parsed.Damage);
        Assert.Equal(10_000, parsed.Loop);
        Assert.Equal(257, parsed.RegenerationAmount);
        Assert.Equal(DamageModifiers.Block | DamageModifiers.Regeneration | DamageModifiers.Front, parsed.Modifiers & (DamageModifiers.Parry | DamageModifiers.Block | DamageModifiers.Endurance | DamageModifiers.Regeneration | DamageModifiers.Front | DamageModifiers.Back));
    }

    [Fact]
    public void TryParse_UsesShiftedDirectionForCurrentDetailLayoutRegenerationResult()
    {
        var packet = Convert.FromHexString("250438DD100600EAF4018B7618001D0220B80702F34D8E0901000000904E99250100");

        Assert.True(Packet0438DamageParser.TryParse(packet, out var parsed));
        Assert.Equal(1_603_211, parsed.BodySkillVariantRaw);
        Assert.Equal(0x06, parsed.LayoutTag);
        Assert.Equal(0, parsed.Unknown);
        Assert.Equal(4_761, parsed.Damage);
        Assert.Equal(10_000, parsed.Loop);
        Assert.Equal(952, parsed.RegenerationAmount);
        Assert.Equal(DamageModifiers.Regeneration | DamageModifiers.Front, parsed.Modifiers & (DamageModifiers.Regeneration | DamageModifiers.Front | DamageModifiers.Back));
    }

    [Fact]
    public void TryParse_KeepsDamageFieldForLegacyDetailLayout()
    {
        var packet = BuildFrame(targetId: 200, sourceId: 100, layoutTag: 4, loop: 777);

        Assert.True(Packet0438DamageParser.TryParse(packet, out var parsed));
        Assert.Equal(1395, parsed.Damage);
        Assert.Equal(777, parsed.Loop);
    }

    private static byte[] BuildFrame(int targetId, int sourceId, int layoutTag = 4, int flag = 0, int type = 2, int loop = 1, int unknown = 1)
        => BuildFrame(targetId, sourceId, layoutTag, flag, type, [0x57, 0, 0, 0, 0, 0, 0, 0], unknown, 1395, loop);

    private static byte[] BuildFrame(int targetId, int sourceId, int layoutTag, int flag, int type, byte[] detail, int unknown, int damage, int loop)
    {
        var payload = new List<byte>
        {
            0x04,
            0x38
        };
        AppendVarInt(payload, targetId);
        AppendVarInt(payload, layoutTag);
        AppendVarInt(payload, flag);
        AppendVarInt(payload, sourceId);
        AppendUInt32Le(payload, 16_140_000);
        payload.Add(1);
        AppendVarInt(payload, type);
        payload.AddRange(detail);
        AppendVarInt(payload, unknown);
        AppendVarInt(payload, damage);
        AppendVarInt(payload, loop);

        var frame = new List<byte>(payload.Count + 1);
        AppendVarInt(frame, payload.Count + 4);
        frame.AddRange(payload);
        return [.. frame];
    }

    private static void AppendVarInt(List<byte> buffer, int value)
    {
        var remaining = unchecked((uint)value);
        while (remaining > 0x7F)
        {
            buffer.Add((byte)((remaining & 0x7F) | 0x80));
            remaining >>= 7;
        }

        buffer.Add((byte)remaining);
    }

    private static void AppendUInt32Le(List<byte> buffer, int value)
    {
        var raw = unchecked((uint)value);
        buffer.Add((byte)raw);
        buffer.Add((byte)(raw >> 8));
        buffer.Add((byte)(raw >> 16));
        buffer.Add((byte)(raw >> 24));
    }
}
