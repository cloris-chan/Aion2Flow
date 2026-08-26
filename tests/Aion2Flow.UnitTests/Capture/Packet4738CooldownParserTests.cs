using System.Buffers.Binary;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class Packet4738CooldownParserTests
{
    [Fact]
    public void SingleEntryBatch_ParsesRowBaseSkillAndRemainingMilliseconds()
    {
        var body = new byte[] { 1, 0x10, 0x59, 0xC8, 0x00, 0xB2, 0x32 };
        var packet = BuildPacket(body);

        Assert.True(Packet4738CooldownParser.TryParse(packet, out var batch));
        Assert.Equal(1, batch.Count);
        Assert.True(batch.TryRead(out var parsed));
        Assert.Equal(13_130_000, parsed.RowBaseSkillId);
        Assert.Equal(6_450, parsed.RemainingMilliseconds);
        Assert.False(batch.TryRead(out _));
    }

    [Fact]
    public void MultipleEntryBatch_ParsesEveryEntryInOrder()
    {
        var body = new byte[]
        {
            3,
            0x10, 0x59, 0xC8, 0x00, 0xB2, 0x32,
            0x01, 0x00, 0x00, 0x00, 0x00,
            0x02, 0x00, 0x00, 0x00, 0xAC, 0x02
        };

        Assert.True(Packet4738CooldownParser.TryParse(BuildPacket(body), out var batch));
        Assert.Equal(3, batch.Count);
        Assert.True(batch.TryRead(out var first));
        Assert.Equal(new Packet4738Cooldown(13_130_000, 6_450), first);
        Assert.True(batch.TryRead(out var second));
        Assert.Equal(new Packet4738Cooldown(1, 0), second);
        Assert.True(batch.TryRead(out var third));
        Assert.Equal(new Packet4738Cooldown(2, 300), third);
        Assert.False(batch.TryRead(out _));
    }

    [Fact]
    public void LargeSnapshotBatch_ParsesMultiByteDeclaredLength()
    {
        const int count = 26;
        var body = new byte[1 + count * 5];
        body[0] = count;
        for (var entryIndex = 0; entryIndex < count; entryIndex++)
        {
            var offset = 1 + entryIndex * 5;
            var rowBaseSkillId = 11_000_000 + entryIndex * 10_000;
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset, 4), rowBaseSkillId);
            body[offset + 4] = 0;
        }

        Assert.True(Packet4738CooldownParser.TryParse(BuildPacket(body), out var batch));
        Assert.Equal(count, batch.Count);
        for (var entryIndex = 0; entryIndex < count; entryIndex++)
        {
            Assert.True(batch.TryRead(out var entry));
            Assert.Equal(11_000_000 + entryIndex * 10_000, entry.RowBaseSkillId);
            Assert.Equal(0, entry.RemainingMilliseconds);
        }

        Assert.False(batch.TryRead(out _));
    }

    [Fact]
    public void ZeroEntryBatch_IsRejected()
    {
        Assert.False(Packet4738CooldownParser.TryParse(BuildPacket([0]), out _));
    }

    [Fact]
    public void CountExceedingAvailableEntries_IsRejected()
    {
        var body = new byte[] { 2, 0x10, 0x59, 0xC8, 0x00, 0xB2, 0x32 };

        Assert.False(Packet4738CooldownParser.TryParse(BuildPacket(body), out _));
    }

    [Fact]
    public void EntriesBeyondDeclaredCount_AreRejected()
    {
        var body = new byte[]
        {
            1,
            0x10, 0x59, 0xC8, 0x00, 0xB2, 0x32,
            0x01, 0x00, 0x00, 0x00, 0x00
        };

        Assert.False(Packet4738CooldownParser.TryParse(BuildPacket(body), out _));
    }

    [Fact]
    public void NonMinimalRemainingMillisecondsVarInt_IsRejected()
    {
        var body = new byte[] { 1, 0x10, 0x59, 0xC8, 0x00, 0xB2, 0xB2, 0x00 };

        Assert.False(Packet4738CooldownParser.TryParse(BuildPacket(body), out _));
    }

    [Fact]
    public void NonMinimalDeclaredLengthVarInt_IsRejected()
    {
        var body = new byte[] { 1, 0x10, 0x59, 0xC8, 0x00, 0x01 };
        var packet = new byte[body.Length + 4];
        packet[0] = (byte)((body.Length + 6) | 0x80);
        packet[1] = 0;
        packet[2] = 0x47;
        packet[3] = 0x38;
        body.CopyTo(packet.AsSpan(4));

        Assert.False(Packet4738CooldownParser.TryParse(packet, out _));
    }

    [Fact]
    public void DeclaredLengthMismatch_IsRejected()
    {
        var body = new byte[] { 1, 0x10, 0x59, 0xC8, 0x00, 0x01 };
        var packet = BuildPacket(body);
        packet[0]++;

        Assert.False(Packet4738CooldownParser.TryParse(packet, out _));
    }

    private static byte[] BuildPacket(ReadOnlySpan<byte> body)
    {
        Span<byte> prefix = stackalloc byte[5];
        Assert.True(PacketTransportCodec.TryWriteVarInt(body.Length + 6, prefix, out var prefixLength));
        var packet = new byte[prefixLength + sizeof(ushort) + body.Length];
        prefix[..prefixLength].CopyTo(packet);
        packet[prefixLength] = 0x47;
        packet[prefixLength + 1] = 0x38;
        body.CopyTo(packet.AsSpan(prefixLength + sizeof(ushort)));
        return packet;
    }
}
