using System.Buffers.Binary;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketEmbeddedCombatGrammarTests
{
    [Theory]
    [InlineData(4, 0, 2)]
    [InlineData(54, 0, 3)]
    [InlineData(118, 4, 2)]
    public void Current_Embedded_0438_Control_Shapes_Are_Accepted(int layoutTag, int flag, int type)
        => Assert.True(PacketCombatHandler.IsCurrentEmbedded0438DamageShape(layoutTag, flag, type));

    [Theory]
    [InlineData(7, 6, 1)]
    [InlineData(222, 71, 33)]
    [InlineData(6, 0, 5)]
    [InlineData(4, 17, 112)]
    public void Random_Unknown_Payload_0438_Control_Shapes_Are_Rejected(int layoutTag, int flag, int type)
        => Assert.False(PacketCombatHandler.IsCurrentEmbedded0438DamageShape(layoutTag, flag, type));

    [Theory]
    [InlineData(1, 0, 0, DamageModifiers.None)]
    [InlineData(0, 1, 0, DamageModifiers.None)]
    [InlineData(0, 0, 1, DamageModifiers.None)]
    [InlineData(0, 0, 0, DamageModifiers.Evade)]
    public void Current_Embedded_0438_Quantified_Or_Outcome_Values_Are_Accepted(int damage, int drain, int regeneration, DamageModifiers modifiers)
        => Assert.True(PacketCombatHandler.IsCurrentEmbedded0438CombatValue(damage, drain, regeneration, modifiers));

    [Fact]
    public void Empty_Embedded_0438_Value_Is_Rejected()
        => Assert.False(PacketCombatHandler.IsCurrentEmbedded0438CombatValue(0, 0, 0, DamageModifiers.None));

    [Fact]
    public void Embedded_0438_Preserves_Invalid_High_Bit_Body_Code_As_Grammar_Evidence()
    {
        Assert.True(Packet0438DamageParser.TryParsePayload(CreateDamagePayload(0), out var zeroBody, out _));
        Assert.True(zeroBody.HasCurrentBodySkillVariant);

        Assert.True(Packet0438DamageParser.TryParsePayload(CreateDamagePayload(uint.MaxValue), out var highBitBody, out _));
        Assert.False(highBitBody.HasCurrentBodySkillVariant);
        Assert.Equal(0, highBitBody.BodySkillVariantRaw);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(15)]
    [InlineData(48)]
    [InlineData(74)]
    public void Current_Embedded_0538_Modes_Are_Accepted(int mode)
        => Assert.True(PacketCombatHandler.IsCurrentEmbedded0538ValueShape(mode));

    [Theory]
    [InlineData(122)]
    [InlineData(196)]
    [InlineData(7840)]
    public void Random_Unknown_Payload_0538_Modes_Are_Rejected(int mode)
        => Assert.False(PacketCombatHandler.IsCurrentEmbedded0538ValueShape(mode));

    [Fact]
    public void Embedded_0538_Tail_Requires_A_Complete_Current_Value_Form()
    {
        Assert.True(Packet0538PeriodicValueParser.TryParsePayload(CreatePeriodicPayload(11, []), out var emptyTail));
        Assert.True(emptyTail.HasRecognizedTail);

        Span<byte> validTail = stackalloc byte[9];
        Assert.True(PacketTransportCodec.TryWriteVarInt(1189, validTail, out var prefixLength));
        BinaryPrimitives.WriteInt32LittleEndian(validTail[prefixLength..], 17_090_150);
        Assert.True(Packet0538PeriodicValueParser.TryParsePayload(CreatePeriodicPayload(11, validTail[..(prefixLength + 4)]), out var encodedTail));
        Assert.True(encodedTail.HasRecognizedTail);

        Assert.True(Packet0538PeriodicValueParser.TryParsePayload(CreatePeriodicPayload(11, new byte[9]), out var malformedTail));
        Assert.False(malformedTail.HasRecognizedTail);

        Span<byte> outOfRangeSkill = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(outOfRangeSkill, uint.MaxValue);
        Assert.True(Packet0538PeriodicValueParser.TryParsePayload(CreatePeriodicPayload(11, outOfRangeSkill), out var invalidSkillTail));
        Assert.False(invalidSkillTail.HasRecognizedTail);
    }

    private static byte[] CreatePeriodicPayload(int mode, ReadOnlySpan<byte> tail)
    {
        Span<byte> payload = stackalloc byte[64];
        var offset = 0;
        payload[offset++] = 0x05;
        payload[offset++] = 0x38;
        WriteVarInt(100, payload, ref offset);
        WriteVarInt(mode, payload, ref offset);
        WriteVarInt(200, payload, ref offset);
        WriteVarInt(300, payload, ref offset);
        BinaryPrimitives.WriteUInt32LittleEndian(payload[offset..], 170_901_501);
        offset += 4;
        WriteVarInt(400, payload, ref offset);
        tail.CopyTo(payload[offset..]);
        offset += tail.Length;
        return payload[..offset].ToArray();
    }

    private static byte[] CreateDamagePayload(uint bodyCode)
    {
        Span<byte> payload = stackalloc byte[64];
        var offset = 0;
        payload[offset++] = 0x04;
        payload[offset++] = 0x38;
        WriteVarInt(100, payload, ref offset);
        WriteVarInt(4, payload, ref offset);
        WriteVarInt(0, payload, ref offset);
        WriteVarInt(200, payload, ref offset);
        BinaryPrimitives.WriteUInt32LittleEndian(payload[offset..], bodyCode);
        offset += 4;
        payload[offset++] = 0;
        WriteVarInt(2, payload, ref offset);
        payload.Slice(offset, 8).Clear();
        offset += 8;
        WriteVarInt(1, payload, ref offset);
        WriteVarInt(10, payload, ref offset);
        WriteVarInt(1, payload, ref offset);
        return payload[..offset].ToArray();
    }

    private static void WriteVarInt(int value, Span<byte> destination, ref int offset)
    {
        Assert.True(PacketTransportCodec.TryWriteVarInt(value, destination[offset..], out var written));
        offset += written;
    }
}
