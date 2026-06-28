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

    private static byte[] BuildFrame(int targetId, int sourceId, int layoutTag = 4, int flag = 0, int type = 2, int loop = 1)
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
        payload.AddRange([0x57, 0, 0, 0, 0, 0, 0, 0]);
        AppendVarInt(payload, 1);
        AppendVarInt(payload, 1395);
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
