using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.UnitTests.SceneRuntime;

public sealed class Packet0538PeriodicValueParserTests
{
    [Fact]
    public void TryParse_BoundsTailToDeclaredFrameLength()
    {
        var frame = BuildFrame(targetId: 200, mode: 10, sourceId: 100, chainId: 6, bodyResourceEffectRef: 16_140_000, damage: 1395, tailSkillCode: 16_140_030);
        var packet = new List<byte>(frame);
        packet.AddRange([0x05, 0x38, 0x00, 0xAA, 0xBB, 0xCC, 0xDD]);

        Assert.True(Packet0538PeriodicValueParser.TryParse([.. packet], out var parsed));
        Assert.Equal(200, parsed.TargetId);
        Assert.Equal(10, parsed.Mode);
        Assert.Equal(100, parsed.SourceId);
        Assert.Equal(6, parsed.Unknown);
        Assert.Equal(16_140_000u, parsed.BodyResourceEffectRef.RawId);
        Assert.Equal(1395, parsed.Damage);
        Assert.Equal(4, parsed.TailLength);
        Assert.Equal(16_140_030, parsed.TailSkillCodeRaw);
        Assert.Equal(0, parsed.TailPrefixValue);
    }

    [Fact]
    public void TryParse_RejectsInvalidEntityIds()
    {
        Assert.False(Packet0538PeriodicValueParser.TryParse(BuildFrame(targetId: 0, mode: 10, sourceId: 100, chainId: 6, bodyResourceEffectRef: 16_140_000, damage: 1395, tailSkillCode: 16_140_030), out _));
        Assert.False(Packet0538PeriodicValueParser.TryParse(BuildFrame(targetId: 200, mode: 10, sourceId: 0, chainId: 6, bodyResourceEffectRef: 16_140_000, damage: 1395, tailSkillCode: 16_140_030), out _));
    }

    [Fact]
    public void TryParse_DoesNotTreatUnstructuredLongTailAsSkillIdentity()
    {
        var packet = BuildFrameWithTail(targetId: 1271559, mode: 56, sourceId: 1956, chainId: 11665, bodyResourceEffectRef: 87126207, damage: 101, [
            0x01, 0x02, 0x83, 0x80, 0x80, 0x80, 0x80, 0x01,
            0x11, 0x22, 0x33, 0x44, 0x04, 0x38, 0x99, 0xaa,
            0xbb, 0xcc, 0xdd, 0xee, 0xff
        ]);

        Assert.True(Packet0538PeriodicValueParser.TryParse(packet, out var parsed));
        Assert.Equal(21, parsed.TailLength);
        Assert.Equal(0, parsed.TailSkillCodeRaw);
        Assert.Equal(0, parsed.TailPrefixValue);
    }

    [Fact]
    public void TryParse_PreservesShortVarIntTailAsPrefixOnly()
    {
        var packet = Convert.FromHexString("150538E67C0390B70117EF3E2F0DA305DE02");

        Assert.True(Packet0538PeriodicValueParser.TryParse(packet, out var parsed));
        Assert.Equal(15974, parsed.TargetId);
        Assert.Equal(3, parsed.Mode);
        Assert.Equal(23440, parsed.SourceId);
        Assert.Equal(23, parsed.Unknown);
        Assert.Equal(221_200_111u, parsed.BodyResourceEffectRef.RawId);
        Assert.Equal(675, parsed.Damage);
        Assert.Equal(2, parsed.TailLength);
        Assert.Equal(0, parsed.TailSkillCodeRaw);
        Assert.Equal(350, parsed.TailPrefixValue);
    }

    private static byte[] BuildFrame(int targetId, int mode, int sourceId, int chainId, int bodyResourceEffectRef, int damage, int tailSkillCode)
    {
        var tail = new List<byte>(4);
        AppendUInt32Le(tail, tailSkillCode);
        return BuildFrameWithTail(targetId, mode, sourceId, chainId, bodyResourceEffectRef, damage, [.. tail]);
    }

    private static byte[] BuildFrameWithTail(int targetId, int mode, int sourceId, int chainId, int bodyResourceEffectRef, int damage, ReadOnlySpan<byte> tail)
    {
        var payload = new List<byte>
        {
            0x05,
            0x38
        };
        AppendVarInt(payload, targetId);
        AppendVarInt(payload, mode);
        AppendVarInt(payload, sourceId);
        AppendVarInt(payload, chainId);
        AppendUInt32Le(payload, bodyResourceEffectRef);
        AppendVarInt(payload, damage);
        payload.AddRange(tail);

        var frame = new List<byte>(payload.Count + 1);
        AppendVarInt(frame, payload.Count + 4);
        frame.AddRange(payload);
        return [.. frame];
    }

    private static void AppendVarInt(List<byte> buffer, int value)
    {
        var remaining = (uint)value;
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
